using System.Drawing.Imaging;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Full-screen (all monitors) overlay that freezes the screen, dims it, and lets the user drag
/// a rectangle to select a region. <see cref="Result"/> holds the cropped bitmap on OK.
/// </summary>
public sealed class SnipOverlayForm : Form
{
    private readonly Bitmap _frozen;      // screenshot of the whole virtual desktop
    private readonly Rectangle _virtual;  // virtual-screen bounds (origin may be negative)
    private readonly ToolStrip _bar;      // floating top toolbar (like the Snipping Tool)
    private Point _start;
    private Rectangle _selection;
    private bool _dragging;
    private bool _picking;                // eyedropper mode
    private Point? _hoverPoint;           // live preview position while picking
    private string? _pickedHex;
    private Color _pickedColor;
    private Point _pickedAt;

    /// <summary>The selected region as an independent bitmap, or null if cancelled.</summary>
    public Bitmap? Result { get; private set; }

    public SnipOverlayForm()
    {
        _virtual = SystemInformation.VirtualScreen;
        // BitBlt with CAPTUREBLT so GPU-composited windows (cmd / PowerShell / Terminal), including
        // over a remote-desktop viewer, are captured correctly instead of black/scrambled.
        _frozen = NativeMethods.CaptureScreen(_virtual);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtual;                 // cover every monitor
        AutoScaleMode = AutoScaleMode.None; // 1:1 pixel mapping to the frozen bitmap
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        BackColor = Color.Black;
        KeyPreview = true;

        _bar = BuildBar();
        Controls.Add(_bar);
    }

    private ToolStrip BuildBar()
    {
        var bar = new ToolStrip
        {
            Dock = DockStyle.None,   // float as a compact bar, not a full-width docked strip
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            Renderer = new SnipUi.FlatRenderer(),
            BackColor = Color.FromArgb(249, 249, 249),
            GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = true,
            ImageScalingSize = new Size(24, 24),
            Padding = new Padding(8, 5, 8, 5),
            Cursor = Cursors.Default,   // normal arrow over the toolbar (not the crosshair)
        };

        var shot = SnipUi.GlyphButton(SnipUi.GlyphCamera, "Capture the screen");
        shot.Click += (_, _) => CaptureScreenshot();

        var pick = SnipUi.GlyphButton(SnipUi.GlyphColorPicker, "Pick a colour — then click anywhere (copies the hex)");
        pick.Click += (_, _) => { _picking = true; Cursor = CursorFactory.Eyedropper; };

        var close = SnipUi.GlyphButton(SnipUi.GlyphClose, "Cancel (Esc)");
        close.ForeColor = Color.FromArgb(180, 40, 40);
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        bar.Items.Add(shot);
        bar.Items.Add(pick);
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(close);
        return bar;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();

        // Float the toolbar near the top-centre of the primary monitor.
        var pb = Screen.PrimaryScreen?.Bounds ?? _virtual;
        int bx = (pb.X - _virtual.X) + (pb.Width - _bar.Width) / 2;
        int by = (pb.Y - _virtual.Y) + 16;
        _bar.Location = new Point(bx, by);
    }

    private void CaptureScreenshot()
    {
        // Whole primary monitor, cut from the already-captured frozen image.
        var pb = Screen.PrimaryScreen?.Bounds ?? _virtual;
        var src = new Rectangle(pb.X - _virtual.X, pb.Y - _virtual.Y, pb.Width, pb.Height);
        var bmp = new Bitmap(pb.Width, pb.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.DrawImage(_frozen, new Rectangle(0, 0, pb.Width, pb.Height), src, GraphicsUnit.Pixel);
        Result = bmp;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void PickColorAt(Point p)
    {
        int x = Math.Clamp(p.X, 0, _frozen.Width - 1);
        int y = Math.Clamp(p.Y, 0, _frozen.Height - 1);
        _pickedColor = _frozen.GetPixel(x, y);
        _pickedHex = $"#{_pickedColor.R:X2}{_pickedColor.G:X2}{_pickedColor.B:X2}";
        _pickedAt = p;
        try { Clipboard.SetText(_pickedHex); } catch { }
        _picking = false;
        _hoverPoint = null;
        Cursor = Cursors.Cross;   // back to selection mode
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Eyedropper mode: the next click samples a colour instead of starting a selection.
        if (_picking && e.Button == MouseButtons.Left)
        {
            PickColorAt(e.Location);
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _start = e.Location;
            _selection = new Rectangle(e.Location, Size.Empty);
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_picking)
        {
            // Live colour preview follows the cursor; repaint only around it (not the whole screen).
            InvalidatePreview(_hoverPoint);
            _hoverPoint = e.Location;
            InvalidatePreview(_hoverPoint);
            return;
        }
        if (_dragging)
        {
            _selection = Normalize(_start, e.Location);
            Invalidate();
        }
    }

    private void InvalidatePreview(Point? p)
    {
        if (p is { } pt)
            Invalidate(new Rectangle(pt.X - 230, pt.Y - 80, 460, 160));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _selection = Normalize(_start, e.Location);

        if (_selection.Width >= 3 && _selection.Height >= 3)
        {
            // Copy the region out of the FROZEN bitmap so the dim veil/border are never included.
            var crop = new Bitmap(_selection.Width, _selection.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(crop))
                g.DrawImage(_frozen, new Rectangle(0, 0, _selection.Width, _selection.Height), _selection, GraphicsUnit.Pixel);
            Result = crop;
            DialogResult = DialogResult.OK;
        }
        else
        {
            DialogResult = DialogResult.Cancel;
        }
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_frozen, 0, 0);

        using (var veil = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
            g.FillRectangle(veil, ClientRectangle);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            // Un-dim the selected region and outline it.
            g.DrawImage(_frozen, _selection, _selection, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(255, 0, 120, 215), 2);
            g.DrawRectangle(pen, _selection.X, _selection.Y, Math.Max(0, _selection.Width - 1), Math.Max(0, _selection.Height - 1));

            using var font = new Font("Segoe UI", 9);
            using var back = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            string label = $"{_selection.Width} × {_selection.Height}";
            var sz = g.MeasureString(label, font);
            float lx = _selection.X;
            float ly = _selection.Y - sz.Height - 4;
            if (ly < 0) ly = _selection.Y + 4;
            g.FillRectangle(back, lx, ly, sz.Width + 6, sz.Height + 2);
            g.DrawString(label, font, Brushes.White, lx + 3, ly + 1);
        }

        // Live colour readout that follows the cursor while the eyedropper is active.
        if (_picking && _hoverPoint is { } hp)
        {
            int hx = Math.Clamp(hp.X, 0, _frozen.Width - 1);
            int hy = Math.Clamp(hp.Y, 0, _frozen.Height - 1);
            var c = _frozen.GetPixel(hx, hy);
            DrawColorReadout(g, hp, c, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
        }
        // Last picked colour (after a click).
        else if (_pickedHex != null)
        {
            DrawColorReadout(g, _pickedAt, _pickedColor, _pickedHex + "  (copied)");
        }
    }

    private void DrawColorReadout(Graphics g, Point at, Color color, string text)
    {
        using var f = new Font("Segoe UI", 10, FontStyle.Bold);
        var sz = g.MeasureString(text, f);
        float bw = sz.Width + 44, bh = Math.Max(sz.Height + 12, 30);
        float bx = at.X + 18, by = at.Y + 18;
        if (bx + bw > ClientSize.Width) bx = at.X - bw - 18;
        if (by + bh > ClientSize.Height) by = at.Y - bh - 18;

        using var back = new SolidBrush(Color.FromArgb(235, 30, 30, 30));
        g.FillRectangle(back, bx, by, bw, bh);
        using var sw = new SolidBrush(color);
        float box = 18, boxY = by + (bh - box) / 2;
        g.FillRectangle(sw, bx + 8, boxY, box, box);
        g.DrawRectangle(Pens.White, bx + 8, boxY, box, box);
        g.DrawString(text, f, Brushes.White, bx + 34, by + (bh - sz.Height) / 2);
    }

    private static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    protected override void Dispose(bool disposing)
    {
        if (disposing) _frozen.Dispose();
        base.Dispose(disposing);
    }
}
