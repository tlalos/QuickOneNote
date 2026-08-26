using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickOneNote;

/// <summary>Thin P/Invoke layer for global hotkeys and synthesized keystrokes.</summary>
internal static class NativeMethods
{
    // ----- Screen capture (BitBlt with CAPTUREBLT) -----
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    private const uint SRCCOPY = 0x00CC0020;
    // Includes layered/overlay windows and forces DWM to hand over the real composited pixels.
    // Without it, GPU-composited windows (cmd / PowerShell / Windows Terminal), especially over a
    // remote-desktop viewer, come back black or scrambled.
    private const uint CAPTUREBLT = 0x40000000;

    /// <summary>
    /// Capture a region of the screen using GDI BitBlt with the CAPTUREBLT flag. More robust than
    /// <see cref="System.Drawing.Graphics.CopyFromScreen(int,int,int,int,Size)"/> for hardware-
    /// accelerated windows and remote sessions.
    /// </summary>
    public static Bitmap CaptureScreen(Rectangle area)
    {
        var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr dst = g.GetHdc();
            IntPtr src = GetDC(IntPtr.Zero);
            try
            {
                bool ok = BitBlt(dst, 0, 0, area.Width, area.Height, src, area.X, area.Y, SRCCOPY | CAPTUREBLT);
                if (!ok)
                {
                    // Fall back to a plain blit if CAPTUREBLT is unsupported for this surface.
                    g.ReleaseHdc(dst);
                    g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
                    return bmp;
                }
            }
            finally
            {
                if (src != IntPtr.Zero) ReleaseDC(IntPtr.Zero, src);
                try { g.ReleaseHdc(dst); } catch { /* already released on the fallback path */ }
            }
        }

        // A plain framebuffer read (even with CAPTUREBLT) can't reach the pixels of a
        // GPU-composited console over a remote-desktop session — they come back scrambled. Ask the
        // console window itself to paint into a DC via PrintWindow(PW_RENDERFULLCONTENT) and lay
        // that over the affected region.
        OverlayForegroundConsole(bmp, area);
        return bmp;
    }

    // ----- Console overlay (PrintWindow) -----
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect([In] ref RECT lprc, uint dwFlags);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>The HMONITOR whose display area most closely contains the given screen rectangle.</summary>
    public static IntPtr MonitorFromRect(Rectangle area)
    {
        var r = new RECT { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
        return MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    // Window classes that render their text through the GPU (Direct2D/DirectComposition) and so
    // capture as scrambled/black via a framebuffer blit in a remote session.
    private static readonly string[] ConsoleClasses =
    {
        "ConsoleWindowClass",             // cmd.exe / powershell.exe (conhost)
        "CASCADIA_HOSTING_WINDOW_CLASS",  // Windows Terminal
        "PseudoConsoleWindow",
    };

    private static string ClassOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int n = GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString(0, n);
    }

    /// <summary>
    /// If the foreground window is a console/terminal that intersects <paramref name="area"/>,
    /// re-render its client area with PrintWindow and paint it over the captured bitmap. Only the
    /// foreground window is touched, so windows in front of it are never overwritten. Best-effort:
    /// any failure leaves the original capture untouched.
    /// </summary>
    private static void OverlayForegroundConsole(Bitmap target, Rectangle area)
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd)) return;
            if (Array.IndexOf(ConsoleClasses, ClassOf(hWnd)) < 0) return;
            if (!GetWindowRect(hWnd, out RECT wr)) return;

            int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
            if (ww <= 0 || wh <= 0) return;

            // Client area in screen coordinates — we overlay only the text area, leaving the
            // (correctly captured) title bar and border from the base blit alone.
            if (!GetClientRect(hWnd, out RECT cr)) return;
            var origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hWnd, ref origin)) return;
            var client = new Rectangle(origin.X, origin.Y, cr.Right - cr.Left, cr.Bottom - cr.Top);
            if (client.Width <= 0 || client.Height <= 0 || !client.IntersectsWith(area)) return;

            using var shot = new Bitmap(ww, wh, PixelFormat.Format32bppArgb);
            bool ok;
            using (var wg = Graphics.FromImage(shot))
            {
                IntPtr hdc = wg.GetHdc();
                ok = PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                wg.ReleaseHdc(hdc);
            }
            if (!ok) return;

            using var g = Graphics.FromImage(target);
            // Source sub-rect within the window shot that corresponds to the client area.
            var srcClient = new Rectangle(client.Left - wr.Left, client.Top - wr.Top, client.Width, client.Height);
            var dst = new Rectangle(client.Left - area.X, client.Top - area.Y, client.Width, client.Height);
            g.DrawImage(shot, dst, srcClient, GraphicsUnit.Pixel);
        }
        catch
        {
            // Overlay is a best-effort enhancement; never let it break the capture.
        }
    }

    // ----- Global hotkey -----
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int WM_HOTKEY = 0x0312;

    // ----- Clipboard change detection -----
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    // ----- Synthesized input (SendInput) -----
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // Virtual-key codes we need.
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_MENU = 0x12;   // Alt
    public const ushort VK_C = 0x43;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // Excludes a window from screen capture (BitBlt/PrintWindow/DWM) — Win10 2004+.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public const uint WDA_NONE = 0x0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    private const uint MAPVK_VK_TO_VSC = 0;
    private const ushort VK_A = 0x41;

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                // Include the hardware scan code. Chromium/WebView2-based apps (e.g. the new
                // Outlook, Teams, VS Code) ignore synthetic keystrokes that have no scan code.
                wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
            },
        },
    };

    /// <summary>
    /// Copy the current selection in the foreground app to the clipboard.
    /// The user is typically still holding the hotkey's Ctrl/Shift when this runs, which would
    /// turn our Ctrl+C into Ctrl+Shift+C. So we first WAIT (briefly) for those modifiers to be
    /// physically released, then force them up as a fallback, then send a clean Ctrl+C.
    /// </summary>
    public static void SendCopy()
    {
        // Wait up to ~500ms for the user to let go of the hotkey modifiers.
        for (int i = 0; i < 50 && (IsDown(VK_CONTROL) || IsDown(VK_SHIFT) || IsDown(VK_MENU)); i++)
            System.Threading.Thread.Sleep(10);

        // Fallback: force the modifiers up in case they're still held.
        var release = new[]
        {
            Key(VK_CONTROL, up: true),
            Key(VK_SHIFT, up: true),
            Key(VK_MENU, up: true),
        };
        SendInput((uint)release.Length, release, Marshal.SizeOf<INPUT>());

        System.Threading.Thread.Sleep(40);

        var copy = new[]
        {
            Key(VK_CONTROL, up: false),
            Key(VK_C, up: false),
            Key(VK_C, up: true),
            Key(VK_CONTROL, up: true),
        };
        SendInput((uint)copy.Length, copy, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Send Ctrl+A (select all) to the foreground window. Used by the copy self-test.</summary>
    public static void SendSelectAll()
    {
        var seq = new[]
        {
            Key(VK_CONTROL, up: false),
            Key(VK_A, up: false),
            Key(VK_A, up: true),
            Key(VK_CONTROL, up: true),
        };
        SendInput((uint)seq.Length, seq, Marshal.SizeOf<INPUT>());
    }
}
