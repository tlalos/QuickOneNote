using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickOneNote;

/// <summary>
/// Writes the app's purple-"N" mark to a multi-resolution <c>.ico</c> file using classic 32-bit
/// BMP/DIB frames (read by the .NET SDK icon embedder, Inno Setup, the Windows shell, and GDI+
/// alike — unlike PNG-compressed frames, which some of those can't decode). Used to give the exe,
/// Start-menu shortcut, and installer a real icon. Invoked via the hidden
/// <c>--exporticon &lt;path&gt;</c> verb during packaging.
/// </summary>
internal static class IconExport
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var frames = Sizes.Select(s =>
        {
            using var bmp = Draw(s);
            return DibFrame(bmp);
        }).ToArray();

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write((short)0);              // reserved
        bw.Write((short)1);              // type: icon
        bw.Write((short)Sizes.Length);   // image count

        int offset = 6 + 16 * Sizes.Length;
        for (int i = 0; i < Sizes.Length; i++)
        {
            int s = Sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s));   // width  (0 == 256)
            bw.Write((byte)(s >= 256 ? 0 : s));   // height
            bw.Write((byte)0);                    // palette
            bw.Write((byte)0);                    // reserved
            bw.Write((short)1);                   // colour planes
            bw.Write((short)32);                  // bits per pixel
            bw.Write(frames[i].Length);           // image data size
            bw.Write(offset);                     // image data offset
            offset += frames[i].Length;
        }
        foreach (var f in frames) bw.Write(f);
    }

    /// <summary>A 32bpp DIB icon image: BITMAPINFOHEADER + bottom-up BGRA pixels + a zeroed AND mask.</summary>
    private static byte[] DibFrame(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = (w + 31) / 32 * 4;      // 1bpp mask rows padded to 4 bytes
        int maskSize = maskStride * h;
        int pixSize = w * h * 4;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER (40 bytes). biHeight is doubled to cover the colour image + AND mask.
        bw.Write(40);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((short)1);            // planes
        bw.Write((short)32);           // bpp
        bw.Write(0);                   // BI_RGB
        bw.Write(pixSize + maskSize);  // biSizeImage
        bw.Write(0); bw.Write(0);      // resolution
        bw.Write(0); bw.Write(0);      // palette

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = h - 1; y >= 0; y--)     // DIB is bottom-up
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w * 4);
                bw.Write(row, 0, w * 4);
            }
        }
        finally { bmp.UnlockBits(data); }

        bw.Write(new byte[maskSize]);            // AND mask all 0 → use the alpha channel
        return ms.ToArray();
    }

    private static Bitmap Draw(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        float pad = s * 0.0625f;
        var rect = new RectangleF(pad, pad, s - 2 * pad, s - 2 * pad);
        using (var bg = new SolidBrush(Color.FromArgb(124, 58, 237)))   // purple
        using (var path = Rounded(rect, s * 0.22f))
            g.FillPath(bg, path);

        using var font = new Font("Segoe UI", s * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fg = new SolidBrush(Color.White);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("N", font, fg, rect, sf);
        return bmp;
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
