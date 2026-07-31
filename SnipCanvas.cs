using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Image annotation surface shared by the snip editor and series review. Supports pen,
/// highlighter, shapes (rectangle/ellipse/line/arrow), text, blur/redact, numbered steps, and
/// select/move — plus Ctrl+wheel zoom. The image and annotation list are owned by the caller.
/// </summary>
internal sealed class SnipCanvas : Control
{
    public enum ToolKind { Pen, Highlighter, Rectangle, Ellipse, Line, Arrow, Text, Blur, Step, Select }

    private const int Pad = 28;
    private const float TextSizePx = 20f;

    private Bitmap _image;
    private List<Annotation> _items;
    private readonly List<List<Annotation>> _undo = new();   // snapshots (deep clones)
    private readonly List<List<Annotation>> _redo = new();
    private Annotation? _current;      // in-progress drag annotation
    private Annotation? _selected;     // selected annotation (Select tool)
    private PointF _dragLast;
    private bool _dragging;
    private bool _movePushed;          // an undo snapshot was taken for the current move

    private TextBox? _textBox;         // inline text editor
    private TextAnnotation? _textAnno;

    private float _zoom = 1f;
    private bool _initZoom;
    private int _stepCounter;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ToolKind Tool { get; set; } = ToolKind.Highlighter;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Color { get; set; } = Color.Yellow;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float StrokeWidth { get; set; } = 4f;

    public float Zoom => _zoom;
    public Size ImageSize => _image.Size;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? ZoomChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BeautifySettings Beautify { get; } = new();

    /// <summary>Call after changing Beautify settings so the view re-fits and repaints.</summary>
    public void BeautifyChanged() { _initZoom = false; _zoom = 1f; ZoomChanged?.Invoke(); Invalidate(); }

    public SnipCanvas(Bitmap image, List<Annotation>? items = null)
    {
        _image = image;
        _items = items ?? new List<Annotation>();
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Color.FromArgb(243, 243, 243);
    }

    /// <summary>Swap to a different image + annotation list (used when navigating a series).</summary>
    public void Load(Bitmap image, List<Annotation> items)
    {
        CommitText();
        _image = image;
        _items = items;
        _undo.Clear();
        _redo.Clear();
        _movePushed = false;
        _current = null;
        _selected = null;
        _stepCounter = items.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max();
        _zoom = 1f;
        _initZoom = false;
        ZoomChanged?.Invoke();
        Invalidate();
    }

    // ----- Coordinate mapping -----

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (CanFocus) Focus();
    }

    /// <summary>The overall "poster" size and where the screenshot sits inside it (for the beautifier).</summary>
    private (Size poster, PointF imgOffset) Composition()
    {
        if (!Beautify.Enabled) return (_image.Size, PointF.Empty);

        int pad = Beautify.Padding;
        int cw = _image.Width + 2 * pad, ch = _image.Height + 2 * pad;
        int pw = cw, ph = ch;

        float? aspect = Beautify.Aspect switch
        {
            AspectPreset.R16x9 => 16f / 9f,
            AspectPreset.Square => 1f,
            AspectPreset.Story => 9f / 16f,
            _ => null,
        };
        if (aspect is float a)
        {
            if (cw / (float)ch > a) { pw = cw; ph = (int)Math.Ceiling(cw / a); }
            else { ph = ch; pw = (int)Math.Ceiling(ch * a); }
        }
        return (new Size(pw, ph), new PointF((pw - _image.Width) / 2f, (ph - _image.Height) / 2f));
    }

    private (float ox, float oy, float z) ComputeLayout()
    {
        var ps = Composition().poster;
        float z = _zoom;
        float w = ps.Width * z, h = ps.Height * z;
        return ((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, z);
    }

    private PointF ToImage(Point p)
    {
        var (ox, oy, z) = ComputeLayout();
        var off = Composition().imgOffset;
        return new PointF((p.X - ox) / z - off.X, (p.Y - oy) / z - off.Y);
    }

    private Point ToClient(PointF img)
    {
        var (ox, oy, z) = ComputeLayout();
        var off = Composition().imgOffset;
        return new Point((int)((img.X + off.X) * z + ox), (int)((img.Y + off.Y) * z + oy));
    }

    // ----- Mouse -----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        CommitText();

        var p = ToImage(e.Location);
        switch (Tool)
        {
            case ToolKind.Pen:
            case ToolKind.Highlighter:
                var st = new StrokeAnnotation
                {
                    Color = Tool == ToolKind.Highlighter ? Color.FromArgb(110, Color) : Color,
                    Width = Tool == ToolKind.Highlighter ? Math.Max(14f, StrokeWidth * 4f) : StrokeWidth,
                };
                st.Points.Add(p);
                Begin(st);
                break;

            case ToolKind.Rectangle:
            case ToolKind.Ellipse:
            case ToolKind.Line:
            case ToolKind.Arrow:
                Begin(new ShapeAnnotation { Kind = ToShapeKind(Tool), Color = Color, Width = StrokeWidth, Start = p, End = p });
                break;

            case ToolKind.Blur:
                Begin(new BlurAnnotation { Start = p, End = p });
                break;

            case ToolKind.Step:
                Add(new StepAnnotation { Center = p, Number = ++_stepCounter, Color = Color });
                break;

            case ToolKind.Text:
                BeginTextEdit(p);
                break;

            case ToolKind.Select:
                _selected = _items.AsEnumerable().Reverse().FirstOrDefault(a => a.HitTest(p));
                _dragging = _selected != null;
                _movePushed = false;
                _dragLast = p;
                Invalidate();
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if ((e.Button & MouseButtons.Left) == 0) return;
        var p = ToImage(e.Location);

        switch (_current)
        {
            case StrokeAnnotation s: s.Points.Add(p); Invalidate(); return;
            case ShapeAnnotation sh: sh.End = p; Invalidate(); return;
            case BlurAnnotation b: b.End = p; Invalidate(); return;
        }

        if (Tool == ToolKind.Select && _dragging && _selected != null)
        {
            if (!_movePushed) { PushUndo(); _movePushed = true; }   // snapshot before the first move
            _selected.Move(p.X - _dragLast.X, p.Y - _dragLast.Y);
            _dragLast = p;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;

        // Discard degenerate drags (a click with no drag) for shapes/blur — and its paired snapshot.
        bool discarded = false;
        if (_current is ShapeAnnotation sh && Near(sh.Start, sh.End)) { _items.Remove(sh); discarded = true; }
        else if (_current is BlurAnnotation b && Near(b.Start, b.End)) { _items.Remove(b); discarded = true; }
        if (discarded && _undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
        _current = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; return; }
        if (e.KeyCode is Keys.Delete or Keys.Back && _selected != null && _textBox == null)
        {
            PushUndo();
            _items.Remove(_selected);
            _selected = null;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            float f = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            _zoom = Math.Clamp(_zoom * f, 0.1f, 8f);
            ZoomChanged?.Invoke();
            Invalidate();
        }
        else base.OnMouseWheel(e);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    private static bool Near(PointF a, PointF b) => Math.Abs(a.X - b.X) < 3 && Math.Abs(a.Y - b.Y) < 3;
    private static ShapeKind ToShapeKind(ToolKind t) => t switch
    {
        ToolKind.Rectangle => ShapeKind.Rectangle,
        ToolKind.Ellipse => ShapeKind.Ellipse,
        ToolKind.Line => ShapeKind.Line,
        _ => ShapeKind.Arrow,
    };

    private void Begin(Annotation a) { PushUndo(); _items.Add(a); _current = a; Invalidate(); }
    private void Add(Annotation a) { PushUndo(); _items.Add(a); Invalidate(); }

    private void PushUndo()
    {
        _undo.Add(_items.Select(a => a.Clone()).ToList());
        if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear();
    }

    // ----- Inline text editing -----

    private void BeginTextEdit(PointF img)
    {
        _textAnno = new TextAnnotation { Location = img, Color = Color, FontSize = TextSizePx };
        _textBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", TextSizePx * _zoom, GraphicsUnit.Pixel),
            ForeColor = Color,
            AutoSize = false,
            Width = 220,
            Height = (int)(TextSizePx * _zoom * 1.7f),
            Location = ToClient(img),
        };
        _textBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitText(); } else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelText(); } };
        _textBox.LostFocus += (_, _) => CommitText();
        Controls.Add(_textBox);
        _textBox.Focus();
    }

    private void CommitText()
    {
        if (_textBox == null || _textAnno == null) return;
        var tb = _textBox;
        var anno = _textAnno;
        _textBox = null;
        _textAnno = null;

        string text = tb.Text;
        Controls.Remove(tb);
        tb.Dispose();

        if (!string.IsNullOrWhiteSpace(text))
        {
            anno.Text = text;
            using (var g = CreateGraphics())
            using (var f = new Font("Segoe UI", anno.FontSize, GraphicsUnit.Pixel))
                anno.Measured = g.MeasureString(text, f);
            Add(anno);
        }
    }

    private void CancelText()
    {
        if (_textBox == null) return;
        Controls.Remove(_textBox);
        _textBox.Dispose();
        _textBox = null;
        _textAnno = null;
    }

    // ----- Undo / redo / zoom -----

    public void Undo() { CommitText(); Restore(_undo, _redo); }
    public void Redo() { CommitText(); Restore(_redo, _undo); }

    /// <summary>Pop a snapshot from <paramref name="from"/> into the live list, saving the current
    /// state onto <paramref name="to"/>. Restores in place so the caller's list reference stays valid.</summary>
    private void Restore(List<List<Annotation>> from, List<List<Annotation>> to)
    {
        if (from.Count == 0) return;
        to.Add(_items.Select(a => a.Clone()).ToList());
        var snapshot = from[^1];
        from.RemoveAt(from.Count - 1);
        _items.Clear();
        _items.AddRange(snapshot);
        _current = null;
        _selected = null;
        Invalidate();
    }

    public void ResetZoom() { _zoom = 1f; ZoomChanged?.Invoke(); Invalidate(); }

    private void EnsureInitialZoom()
    {
        if (_initZoom || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _initZoom = true;
        var ps = Composition().poster;
        float availW = Math.Max(1, ClientSize.Width - 2 * Pad);
        float availH = Math.Max(1, ClientSize.Height - 2 * Pad);
        float fit = Math.Min(availW / ps.Width, availH / ps.Height);
        if (fit < 1f) { _zoom = Math.Max(fit, 0.1f); ZoomChanged?.Invoke(); }
    }

    // ----- Paint -----

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureInitialZoom();
        var g = e.Graphics;
        var (ox, oy, z) = ComputeLayout();
        var ps = Composition().poster;
        float w = ps.Width * z, h = ps.Height * z;

        // Editor chrome: a soft shadow around the whole poster (not part of the export).
        for (int s = 7; s >= 1; s--)
        {
            using var sh = new SolidBrush(Color.FromArgb(7, 0, 0, 0));
            g.FillRectangle(sh, ox - s, oy - s + 3, w + 2 * s, h + 2 * s);
        }

        g.InterpolationMode = z < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var state = g.Save();
        g.TranslateTransform(ox, oy);
        g.ScaleTransform(z, z);
        DrawPoster(g);
        DrawSelection(g);
        DrawBlurOutline(g);
        g.Restore(state);

        using var border = new Pen(Color.FromArgb(205, 205, 205));
        g.DrawRectangle(border, ox, oy, w - 1, h - 1);
    }

    /// <summary>Draw the whole composition in poster coordinates (used by preview and export).</summary>
    private void DrawPoster(Graphics g)
    {
        var (poster, off) = Composition();
        var imgRect = new RectangleF(off.X, off.Y, _image.Width, _image.Height);

        if (Beautify.Enabled)
        {
            FillBackground(g, new RectangleF(0, 0, poster.Width, poster.Height));
            if (Beautify.Shadow) DrawImageShadow(g, imgRect, Beautify.CornerRadius);
        }
        else
        {
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, imgRect);
        }

        var save = g.Save();
        if (Beautify.Enabled && Beautify.CornerRadius > 0)
        {
            using var path = RoundedPath(imgRect, Beautify.CornerRadius);
            g.SetClip(path, CombineMode.Intersect);
        }
        g.DrawImage(_image, imgRect);
        g.TranslateTransform(off.X, off.Y);
        foreach (var a in _items)
            a.Draw(g, _image);
        g.Restore(save);
    }

    private void DrawSelection(Graphics g)
    {
        if (_selected == null || Tool != ToolKind.Select) return;
        var off = Composition().imgOffset;
        var b = _selected.Bounds;
        using var sel = new Pen(Color.FromArgb(0, 120, 215), 1.5f / _zoom) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(sel, b.X + off.X, b.Y + off.Y, b.Width, b.Height);
    }

    /// <summary>While dragging the blur tool, show a clear rectangle of the area being redacted.</summary>
    private void DrawBlurOutline(Graphics g)
    {
        if (_current is not BlurAnnotation b) return;
        var off = Composition().imgOffset;
        var r = RectangleF.FromLTRB(
            Math.Min(b.Start.X, b.End.X) + off.X, Math.Min(b.Start.Y, b.End.Y) + off.Y,
            Math.Max(b.Start.X, b.End.X) + off.X, Math.Max(b.Start.Y, b.End.Y) + off.Y);
        if (r.Width < 1 || r.Height < 1) return;

        using (var fill = new SolidBrush(Color.FromArgb(40, 0, 120, 215)))
            g.FillRectangle(fill, r.X, r.Y, r.Width, r.Height);
        using var pen = new Pen(Color.FromArgb(0, 120, 215), 1.5f / _zoom) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    private void FillBackground(Graphics g, RectangleF r)
    {
        if (Beautify.Kind == BackgroundKind.Gradient)
        {
            using var br = new LinearGradientBrush(r, Beautify.Color1, Beautify.Color2, LinearGradientMode.ForwardDiagonal);
            g.FillRectangle(br, r);
        }
        else
        {
            using var br = new SolidBrush(Beautify.Color1);
            g.FillRectangle(br, r);
        }
    }

    private static void DrawImageShadow(Graphics g, RectangleF imgRect, int radius)
    {
        for (int i = 12; i >= 1; i--)
        {
            var rr = RectangleF.Inflate(imgRect, i, i);
            rr.Offset(0, i * 0.55f);
            using var path = RoundedPath(rr, radius + i);
            using var br = new SolidBrush(Color.FromArgb(9, 0, 0, 0));
            g.FillPath(br, path);
        }
    }

    private static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public Bitmap Render()
    {
        if (!Beautify.Enabled) return RenderImage(_image, _items);
        var poster = Composition().poster;
        var bmp = new Bitmap(poster.Width, poster.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        DrawPoster(g);
        return bmp;
    }

    /// <summary>Composite an image with its annotations into a new bitmap (full resolution).</summary>
    public static Bitmap RenderImage(Bitmap image, IEnumerable<Annotation> items)
    {
        var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImageUnscaled(image, 0, 0);
        foreach (var a in items)
            a.Draw(g, image);
        return bmp;
    }
}
