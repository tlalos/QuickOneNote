using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace QuickOneNote;

/// <summary>GDI-drawn 24×24 icons for the annotation tools (consistent, no icon-font reliance).</summary>
internal static class ToolIcons
{
    private static readonly Color Ink = Color.FromArgb(60, 60, 60);
    private static readonly Color Accent = Color.FromArgb(0, 120, 215);

    private const int IconSize = 28;

    private static (Bitmap b, Graphics g) New()
    {
        var b = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Icons are authored in a 24-unit box; enlarge and centre them in the larger canvas so
        // they visually match the Fluent glyph buttons (undo/redo/copy/save).
        g.TranslateTransform(IconSize / 2f, IconSize / 2f);
        g.ScaleTransform(1.4f, 1.4f);
        g.TranslateTransform(-12f, -12f);
        return (b, g);
    }

    public static Bitmap Select()
    {
        var (b, g) = New();
        using (g)
        {
            var pts = new PointF[] { new(6, 4), new(6, 19), new(10, 15), new(13, 21), new(15, 20), new(12, 14), new(17, 14) };
            using var br = new SolidBrush(Ink);
            g.FillPolygon(br, pts);
            using var pen = new Pen(Color.White, 1);
            g.DrawPolygon(pen, pts);
        }
        return b;
    }

    public static Bitmap Pen()
    {
        var (b, g) = New();
        using (g)
        {
            using var body = new Pen(Ink, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(body, 7, 17, 16, 8);
            using var tip = new Pen(Accent, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(tip, 16, 8, 18, 6);
            using var nib = new SolidBrush(Ink);
            g.FillPolygon(nib, new PointF[] { new(5, 19), new(8, 16), new(6.5f, 17.5f) });
        }
        return b;
    }

    public static Bitmap Highlighter()
    {
        var (b, g) = New();
        using (g)
        {
            using var hl = new Pen(Color.FromArgb(150, 255, 213, 0), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(hl, 5, 18, 17, 7);
            using var body = new Pen(Ink, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(body, 13, 11, 18, 6);
        }
        return b;
    }

    public static Bitmap Rectangle()
    {
        var (b, g) = New();
        using (g) { using var pen = new Pen(Ink, 2); g.DrawRectangle(pen, 4, 6, 16, 12); }
        return b;
    }

    public static Bitmap Ellipse()
    {
        var (b, g) = New();
        using (g) { using var pen = new Pen(Ink, 2); g.DrawEllipse(pen, 4, 5, 16, 14); }
        return b;
    }

    public static Bitmap Line()
    {
        var (b, g) = New();
        using (g) { using var pen = new Pen(Ink, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }; g.DrawLine(pen, 5, 19, 19, 5); }
        return b;
    }

    public static Bitmap Arrow()
    {
        var (b, g) = New();
        using (g)
        {
            using var pen = new Pen(Ink, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, 5, 19, 17, 7);
            using var br = new SolidBrush(Ink);
            g.FillPolygon(br, new PointF[] { new(19, 5), new(11, 7), new(17, 13) });
        }
        return b;
    }

    public static Bitmap Text()
    {
        var (b, g) = New();
        using (g)
        {
            using var f = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            using var br = new SolidBrush(Ink);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("A", f, br, new RectangleF(0, 0, 24, 24), sf);
        }
        return b;
    }

    public static Bitmap Blur()
    {
        var (b, g) = New();
        using (g)
        {
            var cols = new[] { Color.FromArgb(150, 150, 150), Color.FromArgb(90, 90, 90), Color.FromArgb(190, 190, 190), Color.FromArgb(120, 120, 120) };
            int k = 0;
            for (int y = 5; y < 19; y += 5)
                for (int x = 5; x < 19; x += 5)
                {
                    using var br = new SolidBrush(cols[k++ % cols.Length]);
                    g.FillRectangle(br, x, y, 4, 4);
                }
        }
        return b;
    }

    public static Bitmap Step()
    {
        var (b, g) = New();
        using (g)
        {
            using var br = new SolidBrush(Accent);
            g.FillEllipse(br, 4, 4, 16, 16);
            using var f = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
            using var tb = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("1", f, tb, new RectangleF(4, 4, 16, 16), sf);
        }
        return b;
    }

    private static readonly Color Purple = Color.FromArgb(124, 58, 237);

    public static Bitmap Beautify()
    {
        var (b, g) = New();
        using (g)
        {
            using var br = new SolidBrush(Purple);
            Sparkle(g, br, 13, 10, 7);
            Sparkle(g, br, 6, 18, 4);
            Sparkle(g, br, 19, 18, 3);
        }
        return b;
    }

    private static void Sparkle(Graphics g, Brush br, float cx, float cy, float r)
    {
        float k = r * 0.30f;
        g.FillPolygon(br, new PointF[]
        {
            new(cx, cy - r), new(cx + k, cy - k), new(cx + r, cy), new(cx + k, cy + k),
            new(cx, cy + r), new(cx - k, cy + k), new(cx - r, cy), new(cx - k, cy - k),
        });
    }

    public static Bitmap Ocr()
    {
        var (b, g) = New();
        using (g)
        {
            using var pen = new Pen(Ink, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new PointF[] { new(4, 8), new(4, 4), new(8, 4) });
            g.DrawLines(pen, new PointF[] { new(16, 4), new(20, 4), new(20, 8) });
            g.DrawLines(pen, new PointF[] { new(20, 16), new(20, 20), new(16, 20) });
            g.DrawLines(pen, new PointF[] { new(8, 20), new(4, 20), new(4, 16) });
            using var f = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
            using var tb = new SolidBrush(Ink);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("A", f, tb, new RectangleF(0, 0, 24, 24), sf);
        }
        return b;
    }

    public static Bitmap Title()
    {
        var (b, g) = New();
        using (g)
        {
            using var f = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
            using var br = new SolidBrush(Purple);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("T", f, br, new RectangleF(-2, -1, 22, 26), sf);
            using var pen = new Pen(Purple, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, 19, 13, 19, 20);
            g.DrawLine(pen, 15.5f, 16.5f, 22.5f, 16.5f);
        }
        return b;
    }

    public static Bitmap Palette()
    {
        var (b, g) = New();
        using (g)
        {
            using var pen = new Pen(Ink, 1.6f);
            g.DrawEllipse(pen, 3, 3, 18, 18);
            var cols = new[] { Color.Red, Color.Orange, Color.Gold, Color.LimeGreen, Color.DodgerBlue, Color.MediumPurple };
            for (int i = 0; i < cols.Length; i++)
            {
                using var br = new SolidBrush(cols[i]);
                double a = i / (double)cols.Length * Math.PI * 2;
                g.FillEllipse(br, (float)(12 + 6 * Math.Cos(a) - 2), (float)(12 + 6 * Math.Sin(a) - 2), 4, 4);
            }
        }
        return b;
    }
}
