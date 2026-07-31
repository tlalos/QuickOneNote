using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>Shared toolbar look &amp; feel for the snip editor and series review windows.</summary>
internal static class SnipUi
{
    // Segoe Fluent Icons / MDL2 Assets glyph code points.
    public const string GlyphPen = "";        // Edit
    public const string GlyphHighlight = "";  // Highlight
    public const string GlyphUndo = "";       // Undo
    public const string GlyphResnip = "";     // Crop
    public const string GlyphCopy = "";       // Copy
    public const string GlyphSave = "";       // Save
    public const string GlyphSend = "";       // Send
    public const string GlyphPrev = "";       // ChevronLeft
    public const string GlyphNext = "";       // ChevronRight
    public const string GlyphDelete = "";     // Delete

    // Defined by code point (avoids invisible-glyph literals): camera, eyedropper, close, redo.
    public static readonly string GlyphCamera = ((char)0xE722).ToString();
    public static readonly string GlyphColorPicker = ((char)0xEF3C).ToString();
    public static readonly string GlyphClose = ((char)0xE8BB).ToString();
    public static readonly string GlyphRedo = ((char)0xE7A6).ToString();

    public static readonly string IconFont = PickIconFont();

    // Editor/review toolbar sizing (bumped up for larger, easier-to-hit icons).
    public const int BarHeight = 56;
    public const int ButtonW = 46;
    public const int ButtonH = 44;
    public const int SwatchSize = 28;

    public static ToolStrip MakeToolStrip() => new()
    {
        GripStyle = ToolStripGripStyle.Hidden,
        Renderer = new FlatRenderer(),
        BackColor = Color.FromArgb(249, 249, 249),
        Padding = new Padding(10, 6, 10, 6),
        AutoSize = false,
        Height = BarHeight,
        ImageScalingSize = new Size(28, 28),
        Font = new Font("Segoe UI", 10f),
    };

    public static ToolStripButton GlyphButton(string glyph, string tip) => new()
    {
        Text = glyph,
        Font = new Font(IconFont, 17f),
        DisplayStyle = ToolStripItemDisplayStyle.Text,
        ToolTipText = tip,
        AutoSize = false,
        Size = new Size(ButtonW, ButtonH),
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(2, 0, 2, 0),
    };

    public static Bitmap Swatch(Color c)
    {
        int s = SwatchSize;
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 3, 3, s - 6, s - 6);
        using var pen = new Pen(Color.FromArgb(120, 120, 120));
        g.DrawEllipse(pen, 3, 3, s - 6, s - 6);
        return bmp;
    }

    /// <summary>A color swatch toolbar button sized to match the larger toolbar.</summary>
    public static ToolStripButton ColorButton(Color c) => new()
    {
        Image = Swatch(c),
        DisplayStyle = ToolStripItemDisplayStyle.Image,
        ToolTipText = c.Name,
        AutoSize = false,
        Size = new Size(ButtonW - 6, ButtonH),
        ImageAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(2, 0, 2, 0),
    };

    private static string PickIconFont()
    {
        try
        {
            using var installed = new InstalledFontCollection();
            var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                if (names.Contains(name)) return name;
        }
        catch { }
        return "Segoe UI Symbol";
    }

    /// <summary>Flat, Windows-11-ish toolbar rendering: light background, rounded hover/checked.</summary>
    public sealed class FlatRenderer : ToolStripProfessionalRenderer
    {
        public FlatRenderer() => RoundedEdges = false;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            => e.Graphics.Clear(Color.FromArgb(249, 249, 249));

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripButton b) { base.OnRenderButtonBackground(e); return; }
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(2, 2, e.Item.Width - 5, e.Item.Height - 5);

            Color? fill = null, border = null;
            if (b.Checked) { fill = Color.FromArgb(222, 238, 252); border = Color.FromArgb(0, 120, 215); }
            else if (e.Item.Selected || b.Pressed) { fill = Color.FromArgb(236, 236, 236); }

            if (fill != null)
            {
                using var path = Rounded(r, 6);
                using var br = new SolidBrush(fill.Value);
                g.FillPath(br, path);
                if (border != null)
                {
                    using var pen = new Pen(border.Value);
                    g.DrawPath(pen, path);
                }
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            int x = e.Item.Width / 2;
            using var pen = new Pen(Color.FromArgb(224, 224, 224));
            g.DrawLine(pen, x, 8, x, e.Item.Height - 8);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
