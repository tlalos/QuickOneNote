using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Builds and manages the annotation tool buttons for a <see cref="SnipCanvas"/>. Shapes and
/// colours are grouped into dropdowns to keep the toolbar compact. Shared by the snip editor
/// and the series review window.
/// </summary>
internal sealed class DrawingToolbar
{
    private static readonly (string Name, Color C)[] Palette =
    {
        ("Yellow", Color.Yellow), ("Red", Color.Red), ("Green", Color.LimeGreen),
        ("Blue", Color.DodgerBlue), ("Black", Color.Black),
    };

    private readonly SnipCanvas _canvas;
    private readonly List<(SnipCanvas.ToolKind tool, ToolStripButton btn)> _tools = new();
    private ToolStripSplitButton _shapesBtn = null!;
    private ToolStripDropDownButton _colorBtn = null!;
    private SnipCanvas.ToolKind _currentShape = SnipCanvas.ToolKind.Rectangle;

    public DrawingToolbar(SnipCanvas canvas) => _canvas = canvas;

    public void AddTo(ToolStrip ts)
    {
        AddTool(ts, SnipCanvas.ToolKind.Select, ToolIcons.Select(), "Select / move (Delete removes)");
        ts.Items.Add(new ToolStripSeparator());
        AddTool(ts, SnipCanvas.ToolKind.Pen, ToolIcons.Pen(), "Pen");
        AddTool(ts, SnipCanvas.ToolKind.Highlighter, ToolIcons.Highlighter(), "Highlighter");
        AddShapes(ts);
        AddTool(ts, SnipCanvas.ToolKind.Text, ToolIcons.Text(), "Text");
        AddTool(ts, SnipCanvas.ToolKind.Blur, ToolIcons.Blur(), "Blur / redact");
        AddTool(ts, SnipCanvas.ToolKind.Step, ToolIcons.Step(), "Numbered step");
        ts.Items.Add(new ToolStripSeparator());
        AddColors(ts);
        ts.Items.Add(BuildThickness());
    }

    // ----- Simple tool buttons -----

    private void AddTool(ToolStrip ts, SnipCanvas.ToolKind t, Image img, string tip)
    {
        var b = new ToolStripButton
        {
            Image = img,
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = tip,
            AutoSize = false,
            Size = new Size(SnipUi.ButtonW, SnipUi.ButtonH),
            ImageAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(1, 0, 1, 0),
        };
        b.Click += (_, _) => SelectTool(t);
        _tools.Add((t, b));
        ts.Items.Add(b);
    }

    public void SelectTool(SnipCanvas.ToolKind t)
    {
        _canvas.Tool = t;
        foreach (var (tt, bb) in _tools) bb.Checked = tt == t;
    }

    // ----- Shapes dropdown -----

    private void AddShapes(ToolStrip ts)
    {
        _shapesBtn = new ToolStripSplitButton
        {
            Image = ShapeIcon(_currentShape),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            AutoSize = false,
            Size = new Size(SnipUi.ButtonW + 14, SnipUi.ButtonH),
            ToolTipText = "Shapes — click to use, arrow to choose (rectangle / ellipse / line / arrow)",
        };
        _shapesBtn.ButtonClick += (_, _) => SelectTool(_currentShape);
        foreach (var (k, name) in new[]
        {
            (SnipCanvas.ToolKind.Rectangle, "Rectangle"),
            (SnipCanvas.ToolKind.Ellipse, "Ellipse"),
            (SnipCanvas.ToolKind.Line, "Line"),
            (SnipCanvas.ToolKind.Arrow, "Arrow"),
        })
        {
            var mi = new ToolStripMenuItem(name) { Image = ShapeIcon(k) };
            mi.Click += (_, _) => { _currentShape = k; _shapesBtn.Image = ShapeIcon(k); SelectTool(k); };
            _shapesBtn.DropDownItems.Add(mi);
        }
        ts.Items.Add(_shapesBtn);
    }

    private static Image ShapeIcon(SnipCanvas.ToolKind k) => k switch
    {
        SnipCanvas.ToolKind.Rectangle => ToolIcons.Rectangle(),
        SnipCanvas.ToolKind.Ellipse => ToolIcons.Ellipse(),
        SnipCanvas.ToolKind.Line => ToolIcons.Line(),
        _ => ToolIcons.Arrow(),
    };

    // ----- Colours dropdown -----

    private void AddColors(ToolStrip ts)
    {
        _colorBtn = new ToolStripDropDownButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = SnipUi.Swatch(Color.Yellow),
            AutoSize = false,
            Size = new Size(SnipUi.ButtonW + 14, SnipUi.ButtonH),
            ToolTipText = "Colour",
        };
        foreach (var (name, c) in Palette)
        {
            var mi = new ToolStripMenuItem(name) { Image = SnipUi.Swatch(c) };
            mi.Click += (_, _) => ApplyColor(c);
            _colorBtn.DropDownItems.Add(mi);
        }
        _colorBtn.DropDownItems.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("Custom colour…") { Image = ToolIcons.Palette() };
        custom.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _canvas.Color, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK) ApplyColor(dlg.Color);
        };
        _colorBtn.DropDownItems.Add(custom);
        ts.Items.Add(_colorBtn);
    }

    public void SelectColor(Color c) => ApplyColor(c);

    private void ApplyColor(Color c)
    {
        _canvas.Color = c;
        _colorBtn.Image = SnipUi.Swatch(c);
    }

    // ----- Thickness -----

    private ToolStripDropDownButton BuildThickness()
    {
        var dd = new ToolStripDropDownButton("Size")
        {
            AutoSize = false,
            Size = new Size(58, SnipUi.ButtonH),
            ToolTipText = "Stroke / shape thickness",
        };
        foreach (var (label, w) in new[] { ("Thin (2)", 2f), ("Medium (4)", 4f), ("Bold (6)", 6f), ("Heavy (10)", 10f) })
        {
            var mi = new ToolStripMenuItem(label);
            mi.Click += (_, _) =>
            {
                _canvas.StrokeWidth = w;
                foreach (ToolStripMenuItem item in dd.DropDownItems) item.Checked = item == mi;
            };
            if (Math.Abs(w - _canvas.StrokeWidth) < 0.1f) mi.Checked = true;
            dd.DropDownItems.Add(mi);
        }
        return dd;
    }
}
