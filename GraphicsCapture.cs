using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace QuickOneNote;

/// <summary>
/// Screen capture via the Windows.Graphics.Capture (WGC) API — the same path the Snipping Tool
/// uses. Unlike a GDI <c>BitBlt</c>, it reads the DWM-composited output, so it correctly captures
/// GPU-rendered surfaces such as a RustDesk/AnyDesk/RDP viewer window, hardware-accelerated
/// browsers, video players, and games (a plain blit returns those black or scrambled).
///
/// The whole area is assembled from per-monitor captures. If WGC is unavailable or any monitor
/// fails, the caller-visible <see cref="CaptureArea"/> falls back to the GDI blit.
/// </summary>
internal static class GraphicsCapture
{
    /// <summary>
    /// Capture a rectangle of the virtual desktop. Tries WGC per intersecting monitor and
    /// composites the result; on any failure falls back entirely to the GDI blit so a capture is
    /// always produced.
    /// </summary>
    public static Bitmap CaptureArea(Rectangle area)
    {
        try
        {
            bool supported = GraphicsCaptureSession.IsSupported();
            CaptureLog.Write($"CaptureArea {area.Width}x{area.Height} at ({area.X},{area.Y}); WGC supported={supported}; monitors={Screen.AllScreens.Length}");
            if (!supported)
            {
                CaptureLog.Write("-> FALLBACK to GDI blit (WGC not supported)");
                return NativeMethods.CaptureScreen(area);
            }

            var monitors = new List<(Rectangle bounds, Bitmap shot)>();
            foreach (var screen in Screen.AllScreens)
            {
                if (Rectangle.Intersect(screen.Bounds, area).IsEmpty) continue;
                IntPtr hMon = NativeMethods.MonitorFromRect(screen.Bounds);
                Bitmap? shot = CaptureMonitor(hMon);
                if (shot == null)
                {
                    CaptureLog.Write($"-> FALLBACK to GDI blit (WGC failed for monitor {screen.Bounds.Width}x{screen.Bounds.Height})");
                    foreach (var (_, s) in monitors) s.Dispose();
                    return NativeMethods.CaptureScreen(area);   // mixed results — use one consistent source
                }
                CaptureLog.Write($"   monitor {screen.Bounds.Width}x{screen.Bounds.Height} -> WGC shot {shot.Width}x{shot.Height} OK");
                monitors.Add((screen.Bounds, shot));
            }

            if (monitors.Count == 0)
            {
                CaptureLog.Write("-> FALLBACK to GDI blit (no intersecting monitors)");
                return NativeMethods.CaptureScreen(area);
            }
            CaptureLog.Write($"-> WGC composite OK from {monitors.Count} monitor(s)");

            var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                foreach (var (bounds, shot) in monitors)
                {
                    // Draw the whole monitor image into its place in the composite (scaling to the
                    // monitor's logical bounds if WGC returned a different pixel size).
                    var dst = new Rectangle(bounds.X - area.X, bounds.Y - area.Y, bounds.Width, bounds.Height);
                    g.DrawImage(shot, dst, new Rectangle(0, 0, shot.Width, shot.Height), GraphicsUnit.Pixel);
                    shot.Dispose();
                }
            }
            return bmp;
        }
        catch (Exception ex)
        {
            CaptureLog.Write("-> FALLBACK to GDI blit (exception): " + ex.Message);
            return NativeMethods.CaptureScreen(area);
        }
    }

    /// <summary>Capture a single monitor via WGC on a dedicated MTA thread. Null on any failure.</summary>
    private static Bitmap? CaptureMonitor(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero) { CaptureLog.Write("   CaptureMonitor: null HMONITOR"); return null; }

        Bitmap? result = null;
        var t = new Thread(() =>
        {
            try { result = CaptureMonitorCore(hMonitor); }
            catch (Exception ex) { CaptureLog.Write("   CaptureMonitorCore threw: " + ex.Message); result = null; }
        });
        t.SetApartmentState(ApartmentState.MTA);
        t.IsBackground = true;
        t.Start();
        // A one-shot capture completes in a few frames; cap the wait so a stuck GPU never hangs us.
        if (!t.Join(TimeSpan.FromSeconds(3)))
        {
            CaptureLog.Write("   CaptureMonitor: timed out after 3s");
            return null;
        }
        return result;
    }

    private static Bitmap? CaptureMonitorCore(IntPtr hMonitor)
    {
        RoInitialize(1);   // RO_INIT_MULTITHREADED (S_FALSE if already initialised — ignore)

        IntPtr d3dDevice = IntPtr.Zero, dxgiDevice = IntPtr.Zero, context = IntPtr.Zero, inspectable = IntPtr.Zero;
        try
        {
            const int D3D_DRIVER_TYPE_HARDWARE = 1;
            const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
            const uint D3D11_SDK_VERSION = 7;

            int hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
                out d3dDevice, out _, out context);
            if (hr < 0)
            {
                // Retry with the WARP software renderer (headless / no-GPU sessions).
                const int D3D_DRIVER_TYPE_WARP = 5;
                hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
                    out d3dDevice, out _, out context);
                if (hr < 0) { CaptureLog.Write($"   D3D11CreateDevice failed hr=0x{hr:X8}"); return null; }
            }

            var iidDxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            if (Marshal.QueryInterface(d3dDevice, in iidDxgiDevice, out dxgiDevice) < 0) return null;
            if (CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable) < 0) return null;

            IDirect3DDevice device = WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);

            GraphicsCaptureItem item = CreateItemForMonitor(hMonitor);
            if (item == null) return null;
            SizeInt32 size = item.Size;
            if (size.Width <= 0 || size.Height <= 0) return null;

            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            using var session = pool.CreateCaptureSession(item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                try { session.IsCursorCaptureEnabled = false; } catch { /* keep default */ }
            }
            session.StartCapture();

            Direct3D11CaptureFrame? frame = null;
            for (int i = 0; i < 300 && frame == null; i++)
            {
                frame = pool.TryGetNextFrame();
                if (frame == null) Thread.Sleep(5);
            }
            if (frame == null) { CaptureLog.Write("   TryGetNextFrame: no frame within timeout"); return null; }

            using (frame)
            {
                SoftwareBitmap sb = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface)
                    .AsTask().GetAwaiter().GetResult();
                using (sb)
                    return ToBitmap(sb);
            }
        }
        finally
        {
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
            if (d3dDevice != IntPtr.Zero) Marshal.Release(d3dDevice);
            // `inspectable` ownership passes to the projected IDirect3DDevice; leave it to the runtime.
        }
    }

    private static void TrySet(Action set) { try { set(); } catch { /* property gated by Windows build */ } }

    private static Bitmap ToBitmap(SoftwareBitmap source)
    {
        SoftwareBitmap sb = source;
        bool converted = false;
        if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sb.BitmapAlphaMode != BitmapAlphaMode.Straight)
        {
            sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
            converted = true;
        }

        int w = sb.PixelWidth, h = sb.PixelHeight, bytes = w * h * 4;
        var buffer = new Windows.Storage.Streams.Buffer((uint)bytes);
        sb.CopyToBuffer(buffer);
        var pixels = new byte[bytes];
        using (var reader = DataReader.FromBuffer(buffer))
            reader.ReadBytes(pixels);
        // Force fully opaque — screen captures have no meaningful alpha, and a stray 0 would render
        // transparent once composited/saved.
        for (int i = 3; i < bytes; i += 4) pixels[i] = 255;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, data.Scan0, bytes); }   // 32bpp stride == 4*w, no padding
        finally { bmp.UnlockBits(data); }

        if (converted) sb.Dispose();
        return bmp;
    }

    // ----- WinRT / D3D interop -----

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, in Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, in Guid iid);
    }

    private static GraphicsCaptureItem CreateItemForMonitor(IntPtr hMonitor)
    {
        const string classId = "Windows.Graphics.Capture.GraphicsCaptureItem";
        IntPtr hstr = IntPtr.Zero, factoryPtr = IntPtr.Zero;
        try
        {
            // .NET's built-in HSTRING P/Invoke marshaling isn't supported, so build the HSTRING
            // by hand and pass it as an IntPtr.
            int hr = WindowsCreateString(classId, classId.Length, out hstr);
            if (hr < 0) throw new COMException("WindowsCreateString failed", hr);

            var interopIid = typeof(IGraphicsCaptureItemInterop).GUID;
            hr = RoGetActivationFactory(hstr, in interopIid, out factoryPtr);
            if (hr < 0) throw new COMException("RoGetActivationFactory failed", hr);

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");   // IID of IGraphicsCaptureItem
            IntPtr itemPtr = interop.CreateForMonitor(hMonitor, in itemIid);
            try { return GraphicsCaptureItem.FromAbi(itemPtr); }
            finally { if (itemPtr != IntPtr.Zero) Marshal.Release(itemPtr); }
        }
        finally
        {
            if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
            if (hstr != IntPtr.Zero) WindowsDeleteString(hstr);
        }
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr software,
        uint flags, IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
        out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);
}
