using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Reviews a captured series: a title for the set, left/right navigation through each shot,
/// per-shot caption + pen/highlighter annotation, delete, and submit. On submit raises
/// <see cref="SubmitRequested"/> with the title and each shot's caption + rendered PNG.
/// </summary>
public sealed class SeriesReviewForm : Form
{
    private sealed class Item
    {
        public Bitmap Image;
        public List<Annotation> Strokes = new();
        public string Caption = "";
        public Item(Bitmap image) => Image = image;
    }

    private readonly List<Item> _items;
    private int _index;

    private SnipCanvas _canvas = null!;
    private TextBox _title = null!;
    private TextBox _caption = null!;
    private Label _counter = null!;
    private Button _prev = null!, _next = null!;
    private DrawingToolbar _toolbar = null!;
    private ToolStripLabel _zoomLabel = null!;

    /// <summary>Raised on Submit with (title, items). Each png already has annotations baked in.</summary>
    public event Action<string, IReadOnlyList<SeriesItem>>? SubmitRequested;

    public SeriesReviewForm(IEnumerable<Bitmap> shots)
    {
        _items = shots.Select(b => new Item(b)).ToList();
        BuildUi();
        LoadCurrent();
    }

    private void BuildUi()
    {
        Text = "QuickOneNote — Review series";
        Icon = IconFactory.CreateNoteIcon();
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(249, 249, 249);
        MinimumSize = new Size(720, 600);

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        ClientSize = new Size(Math.Min(work.Width - 160, 900), Math.Min(work.Height - 160, 740));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); // title
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // tools
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // canvas
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); // caption
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // buttons
        Controls.Add(root);

        // Row 0 — title (label docked left, textbox fills the rest)
        var titlePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 8) };
        _title = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f) };
        _title.PlaceholderText = "Title for this series (added in bold)";
        _title.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var titleLbl = new Label { Text = "Title:", Dock = DockStyle.Left, Width = 48, TextAlign = ContentAlignment.MiddleLeft };
        titlePanel.Controls.Add(_title);    // fill first
        titlePanel.Controls.Add(titleLbl);  // dock left last so it claims the edge
        root.Controls.Add(titlePanel, 0, 0);

        // Row 2 — canvas (created first so the toolbar can bind to it)
        _canvas = new SnipCanvas(_items[0].Image, _items[0].Strokes) { Dock = DockStyle.Fill };
        _canvas.ZoomChanged += () => _zoomLabel.Text = $"{Math.Round(_canvas.Zoom * 100)}%";

        // Row 1 — drawing toolbar (shared tools)
        var tools = SnipUi.MakeToolStrip();
        tools.Dock = DockStyle.Fill;
        _toolbar = new DrawingToolbar(_canvas);
        _toolbar.AddTo(tools);
        tools.Items.Add(new ToolStripSeparator());
        var undo = SnipUi.GlyphButton(SnipUi.GlyphUndo, "Undo (Ctrl+Z)");
        undo.Click += (_, _) => _canvas.Undo();
        tools.Items.Add(undo);
        var redo = SnipUi.GlyphButton(SnipUi.GlyphRedo, "Redo (Ctrl+Y)");
        redo.Click += (_, _) => _canvas.Redo();
        tools.Items.Add(redo);
        _zoomLabel = new ToolStripLabel("100%") { IsLink = true, Alignment = ToolStripItemAlignment.Right, LinkColor = Color.FromArgb(90, 90, 90), ToolTipText = "Click to reset zoom (Ctrl+scroll to zoom)" };
        _zoomLabel.Click += (_, _) => _canvas.ResetZoom();
        tools.Items.Add(_zoomLabel);

        root.Controls.Add(tools, 0, 1);
        root.Controls.Add(_canvas, 0, 2);

        // Row 3 — caption
        var capPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 6) };
        var capLbl = new Label { Text = "Caption for this screenshot (shown above it):", AutoSize = true, Dock = DockStyle.Top };
        _caption = new TextBox { Dock = DockStyle.Fill, Multiline = true, Font = new Font("Segoe UI", 9.5f) };
        capPanel.Controls.Add(_caption);
        capPanel.Controls.Add(capLbl);
        root.Controls.Add(capPanel, 0, 3);

        // Row 4 — buttons
        var bottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 8) };

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        _prev = new Button { Text = "‹ Prev", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 32, Padding = new Padding(6, 0, 6, 0), Margin = new Padding(0, 0, 4, 0) };
        _prev.Click += (_, _) => Navigate(_index - 1);
        _counter = new Label { Text = "1 / 1", AutoSize = false, Width = 64, Height = 32, TextAlign = ContentAlignment.MiddleCenter };
        _next = new Button { Text = "Next ›", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 32, Padding = new Padding(6, 0, 6, 0), Margin = new Padding(4, 0, 0, 0) };
        _next.Click += (_, _) => Navigate(_index + 1);
        left.Controls.Add(_prev);
        left.Controls.Add(_counter);
        left.Controls.Add(_next);

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
        var submit = new Button { Text = "Submit to OneNote", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 34, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(12, 0, 12, 0), Margin = new Padding(6, 0, 0, 0) };
        submit.Click += (_, _) => DoSubmit();
        var cancel = new Button { Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 34, DialogResult = DialogResult.Cancel, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(6, 0, 0, 0) };
        var delete = new Button { Text = "Delete shot", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 34, Padding = new Padding(8, 0, 8, 0), Margin = new Padding(6, 0, 0, 0) };
        delete.Click += (_, _) => DeleteCurrent();
        right.Controls.Add(submit);
        right.Controls.Add(cancel);
        right.Controls.Add(delete);

        bottom.Controls.Add(left);
        bottom.Controls.Add(right);
        root.Controls.Add(bottom, 0, 4);

        CancelButton = cancel;
        _toolbar.SelectTool(SnipCanvas.ToolKind.Highlighter);
        _toolbar.SelectColor(Color.Yellow);
    }

    // ----- Navigation -----

    private void Navigate(int to)
    {
        if (to < 0 || to >= _items.Count) return;
        _items[_index].Caption = _caption.Text;
        _index = to;
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        var item = _items[_index];
        _caption.Text = item.Caption;
        _canvas.Load(item.Image, item.Strokes);
        _counter.Text = $"{_index + 1} / {_items.Count}";
        _prev.Enabled = _index > 0;
        _next.Enabled = _index < _items.Count - 1;
    }

    private void DeleteCurrent()
    {
        if (_items.Count <= 1)
        {
            // Deleting the last shot cancels the whole review.
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }
        _items[_index].Image.Dispose();
        _items.RemoveAt(_index);
        if (_index >= _items.Count) _index = _items.Count - 1;
        LoadCurrent();
    }

    // ----- Tools -----

    // ----- Submit -----

    private void DoSubmit()
    {
        _items[_index].Caption = _caption.Text;

        var payload = new List<SeriesItem>(_items.Count);
        foreach (var item in _items)
        {
            using var rendered = SnipCanvas.RenderImage(item.Image, item.Strokes);
            using var ms = new MemoryStream();
            rendered.Save(ms, ImageFormat.Png);
            payload.Add(new SeriesItem(string.IsNullOrWhiteSpace(item.Caption) ? null : item.Caption.Trim(), ms.ToArray()));
        }

        SubmitRequested?.Invoke(_title.Text.Trim(), payload);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var item in _items)
                item.Image.Dispose();
        base.Dispose(disposing);
    }
}
