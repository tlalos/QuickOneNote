using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Shows a single snip and lets the user annotate it (pen / highlighter), zoom (Ctrl+wheel),
/// then Copy, Save, Send to OneNote, or re-snip a new region.
/// </summary>
public sealed class SnipEditorForm : Form
{
    private readonly Bitmap _image;
    private readonly SnipCanvas _canvas;
    private readonly bool _seriesActive;
    private DrawingToolbar _toolbar = null!;
    private ToolStrip _toolsStrip = null!;
    private ToolStripLabel _zoomLabel = null!;

    /// <summary>Raised when the user clicks "Send to OneNote"; carries the rendered PNG.</summary>
    public event Action<byte[]>? SendRequested;

    /// <summary>Raised when the user sends this snip with a title (a new titled section).</summary>
    public event Action<string, byte[]>? SendWithTitleRequested;

    /// <summary>Raised when the user wants to draw a new region (re-snip).</summary>
    public event Action? ReselectRequested;

    /// <summary>Raised when the user OCRs the snip and sends the recognised text to OneNote.</summary>
    public event Action<string>? SendTextRequested;

    /// <summary>Raised when the user sends the snip to Desktop Notes; carries (title, noteId, append, png).</summary>
    public event Action<string?, string?, bool, byte[]>? SendToNotesRequested;

    /// <summary>Raised when the user sends OCR text to Desktop Notes; carries (title, noteId, append, text).</summary>
    public event Action<string?, string?, bool, string>? SendTextToNotesRequested;

    /// <summary>Raised when the Notes menu is used but no token is configured yet.</summary>
    public event Action? ConfigureNotesRequested;

    /// <summary>True when a Desktop Notes token is configured (enables the Notes menu).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool NotesConfigured { get; set; }

    /// <summary>Supplies the list of existing Notes (called on a background thread).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<IReadOnlyList<NoteRef>>? NotesLister { get; set; }

    private IReadOnlyList<NoteRef>? _notesCache;

    public SnipEditorForm(Bitmap image, bool seriesActive = false)
    {
        _image = image;
        _seriesActive = seriesActive;
        _canvas = new SnipCanvas(image);
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "QuickOneNote — Snip";
        Icon = IconFactory.CreateNoteIcon();
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new Size(560, 360);
        BackColor = Color.FromArgb(249, 249, 249);

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        int w = Math.Min(_canvas.ImageSize.Width + 120, work.Width - 120);
        int h = Math.Min(_canvas.ImageSize.Height + 160, work.Height - 120);
        ClientSize = new Size(Math.Max(w, 620), Math.Max(h, 420));

        var tools = _toolsStrip = SnipUi.MakeToolStrip();

        // Re-snip (new selection) sits first.
        var resnip = SnipUi.GlyphButton(SnipUi.GlyphResnip, "New selection (re-snip)");
        resnip.Click += (_, _) => ReselectRequested?.Invoke();
        tools.Items.Add(resnip);
        tools.Items.Add(new ToolStripSeparator());

        _toolbar = new DrawingToolbar(_canvas);
        _toolbar.AddTo(tools);
        tools.Items.Add(new ToolStripSeparator());

        var undo = SnipUi.GlyphButton(SnipUi.GlyphUndo, "Undo (Ctrl+Z)");
        undo.Click += (_, _) => _canvas.Undo();
        tools.Items.Add(undo);
        var redo = SnipUi.GlyphButton(SnipUi.GlyphRedo, "Redo (Ctrl+Y)");
        redo.Click += (_, _) => _canvas.Redo();
        tools.Items.Add(redo);

        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(BuildBeautifyMenu());
        tools.Items.Add(BuildOcrMenu());

        // Right-aligned action group (right-to-left add order -> visually copy save send zoom).
        _zoomLabel = new ToolStripLabel("100%")
        {
            IsLink = true,
            Alignment = ToolStripItemAlignment.Right,
            ToolTipText = "Click to reset zoom (Ctrl+scroll to zoom)",
            LinkColor = Color.FromArgb(90, 90, 90),
        };
        _zoomLabel.Click += (_, _) => _canvas.ResetZoom();
        tools.Items.Add(_zoomLabel);

        var send = SnipUi.GlyphButton(SnipUi.GlyphSend, "Send to OneNote");
        send.Alignment = ToolStripItemAlignment.Right;
        send.ForeColor = Color.FromArgb(124, 58, 237);
        send.Click += (_, _) => DoSend();
        tools.Items.Add(send);

        tools.Items.Add(BuildNotesButton());

        var titled = new ToolStripButton
        {
            Image = ToolIcons.Title(),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Alignment = ToolStripItemAlignment.Right,
            AutoSize = false,
            Size = new Size(SnipUi.ButtonW, SnipUi.ButtonH),
            Enabled = !_seriesActive,
            ToolTipText = _seriesActive
                ? "Send with title — disabled while a screenshot series is active"
                : "Send to OneNote with a title (starts a new titled section)",
            Margin = new Padding(2, 0, 2, 0),
        };
        titled.Click += (_, _) => DoSendWithTitle();
        tools.Items.Add(titled);

        var save = SnipUi.GlyphButton(SnipUi.GlyphSave, "Save as PNG…");
        save.Alignment = ToolStripItemAlignment.Right;
        save.Click += (_, _) => DoSave();
        tools.Items.Add(save);

        var copy = SnipUi.GlyphButton(SnipUi.GlyphCopy, "Copy to clipboard");
        copy.Alignment = ToolStripItemAlignment.Right;
        copy.Click += (_, _) => DoCopy();
        tools.Items.Add(copy);

        _canvas.Dock = DockStyle.Fill;
        _canvas.ZoomChanged += () => _zoomLabel.Text = $"{Math.Round(_canvas.Zoom * 100)}%";

        Controls.Add(_canvas);
        Controls.Add(tools);

        _toolbar.SelectTool(SnipCanvas.ToolKind.Highlighter);
        _toolbar.SelectColor(Color.Yellow);

        // Keep the clipboard in sync with edits: every committed change re-copies the composited
        // image, so pasting elsewhere always reflects the shapes/steps/effects currently on screen.
        _canvas.Changed += CopyCompositeToClipboard;

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { _canvas.Undo(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Y) { _canvas.Redo(); e.Handled = true; }
        };

        // Warm the existing-notes list in the background so the Notes dropdown opens instantly.
        Shown += (_, _) => PrefetchNotes();
    }

    // ----- Desktop Notes -----

    private ToolStripDropDownButton BuildNotesButton()
    {
        var b = new ToolStripDropDownButton
        {
            Text = SnipUi.GlyphNotes,
            Font = new Font(SnipUi.IconFont, 16f),
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Alignment = ToolStripItemAlignment.Right,
            AutoSize = false,
            Size = new Size(SnipUi.ButtonW + 16, SnipUi.ButtonH),
            ForeColor = Color.FromArgb(16, 122, 90),
            ToolTipText = "Send to Desktop Notes",
        };
        // The button face uses a large icon font for the glyph; the menu must use a normal UI font
        // (otherwise the menu text inherits the icon font and renders huge/clipped).
        b.DropDown.Font = new Font("Segoe UI", 9.75f);
        b.DropDownOpening += (_, _) => PopulateNotesMenu(b);
        return b;
    }

    private void PrefetchNotes()
    {
        if (!NotesConfigured || NotesLister is not { } lister) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            try { var list = lister(); if (!IsDisposed) _notesCache = list; }
            catch { _notesCache = Array.Empty<NoteRef>(); }
        });
    }

    private void PopulateNotesMenu(ToolStripDropDownButton b)
    {
        b.DropDownItems.Clear();

        if (!NotesConfigured)
        {
            var setup = new ToolStripMenuItem("Set up Desktop Notes in Settings…");
            setup.Click += (_, _) => ConfigureNotesRequested?.Invoke();
            b.DropDownItems.Add(setup);
            return;
        }

        // The screenshot image goes to the chosen note.
        AddNoteTargets(b.DropDownItems, SendToNotes);

        // A parallel submenu sends the OCR-recognised text instead of the image.
        b.DropDownItems.Add(new ToolStripSeparator());
        var ocr = new ToolStripMenuItem("Recognised text (OCR)");
        AddNoteTargets(ocr.DropDownItems, SendOcrToNotes);
        b.DropDownItems.Add(ocr);
    }

    /// <summary>Add Daily / New / existing-note items that call <paramref name="onPick"/>(title, noteId, append).</summary>
    private void AddNoteTargets(ToolStripItemCollection items, Action<string?, string?, bool> onPick)
    {
        var daily = new ToolStripMenuItem($"Daily note ({DateTime.Now:yyyy-MM-dd})");
        daily.Click += (_, _) => onPick(DateTime.Now.ToString("yyyy-MM-dd"), null, true);
        items.Add(daily);

        var newNote = new ToolStripMenuItem("New note with title…");
        newNote.Click += (_, _) => { var t = PromptForTitle("Title for the new note:", ""); if (!string.IsNullOrWhiteSpace(t)) onPick(t!.Trim(), null, false); };
        items.Add(newNote);

        items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem("Append to an existing note:") { Enabled = false });

        if (_notesCache == null)
        {
            items.Add(new ToolStripMenuItem("Loading… (reopen in a moment)") { Enabled = false });
            PrefetchNotes();
        }
        else if (_notesCache.Count == 0)
        {
            items.Add(new ToolStripMenuItem("No existing notes found") { Enabled = false });
        }
        else
        {
            foreach (var note in _notesCache.Take(40))
            {
                string title = note.Title;
                string? id = note.Id;
                var mi = new ToolStripMenuItem(title);
                mi.Click += (_, _) => onPick(title, id, true);
                items.Add(mi);
            }
        }
    }

    private void SendToNotes(string? title, string? noteId, bool append)
    {
        using var ms = new MemoryStream();
        using (var bmp = _canvas.Render())
            bmp.Save(ms, ImageFormat.Png);
        SendToNotesRequested?.Invoke(title, noteId, append, ms.ToArray());
        Close();
    }

    private async void SendOcrToNotes(string? title, string? noteId, bool append)
    {
        string? text = await RecognizeCurrentAsync();
        if (string.IsNullOrWhiteSpace(text)) return;   // RecognizeCurrentAsync already reported why
        SendTextToNotesRequested?.Invoke(title, noteId, append, text!);
        Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Make sure the window is at least wide enough to show every toolbar item.
        int need = _toolsStrip.GetPreferredSize(new Size(int.MaxValue, _toolsStrip.Height)).Width + 24;
        int chrome = Width - ClientSize.Width;      // window borders
        int minWidth = need + chrome;
        if (Width < minWidth) Width = minWidth;
        MinimumSize = new Size(Math.Max(MinimumSize.Width, minWidth), MinimumSize.Height);
    }

    // ----- Beautify menu -----

    private ToolStripDropDownButton BuildBeautifyMenu()
    {
        var b = _canvas.Beautify;
        void Changed() => _canvas.BeautifyChanged();

        var dd = new ToolStripDropDownButton
        {
            Image = ToolIcons.Beautify(),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            AutoSize = false,
            Size = new Size(SnipUi.ButtonW + 14, SnipUi.ButtonH),
            ToolTipText = "Beautify — background, rounded corners, shadow",
        };

        var enabled = new ToolStripMenuItem("Enabled") { CheckOnClick = true, Checked = b.Enabled };
        enabled.CheckedChanged += (_, _) => { b.Enabled = enabled.Checked; Changed(); };
        dd.DropDownItems.Add(enabled);
        dd.DropDownItems.Add(new ToolStripSeparator());

        var bg = new ToolStripMenuItem("Background");
        foreach (var (name, c1, c2) in BeautifySettings.Gradients)
        {
            var mi = new ToolStripMenuItem(name) { Image = GradientSwatch(c1, c2) };
            mi.Click += (_, _) => { b.Kind = BackgroundKind.Gradient; b.Color1 = c1; b.Color2 = c2; b.Enabled = true; enabled.Checked = true; Changed(); };
            bg.DropDownItems.Add(mi);
        }
        bg.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (name, c) in BeautifySettings.Solids)
        {
            var mi = new ToolStripMenuItem(name) { Image = SolidSwatch(c) };
            mi.Click += (_, _) => { b.Kind = BackgroundKind.Solid; b.Color1 = c; b.Enabled = true; enabled.Checked = true; Changed(); };
            bg.DropDownItems.Add(mi);
        }
        var custom = new ToolStripMenuItem("Custom colour…");
        custom.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = b.Color1, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK) { b.Kind = BackgroundKind.Solid; b.Color1 = dlg.Color; b.Enabled = true; enabled.Checked = true; Changed(); }
        };
        bg.DropDownItems.Add(custom);
        dd.DropDownItems.Add(bg);

        var shadow = new ToolStripMenuItem("Drop shadow") { CheckOnClick = true, Checked = b.Shadow };
        shadow.CheckedChanged += (_, _) => { b.Shadow = shadow.Checked; Changed(); };
        dd.DropDownItems.Add(shadow);

        dd.DropDownItems.Add(RadioMenu("Corners", new[] { ("None", 0), ("Small", 10), ("Medium", 20), ("Large", 34) }, () => b.CornerRadius, v => { b.CornerRadius = v; Changed(); }));
        dd.DropDownItems.Add(RadioMenu("Padding", new[] { ("Small", 28), ("Medium", 56), ("Large", 96) }, () => b.Padding, v => { b.Padding = v; Changed(); }));

        var asp = new ToolStripMenuItem("Size");
        foreach (var (name, ap) in new[] { ("Auto", AspectPreset.Auto), ("16:9", AspectPreset.R16x9), ("Square", AspectPreset.Square), ("Story (9:16)", AspectPreset.Story) })
        {
            var mi = new ToolStripMenuItem(name) { Checked = b.Aspect == ap };
            mi.Click += (_, _) => { b.Aspect = ap; foreach (ToolStripMenuItem m in asp.DropDownItems) m.Checked = m == mi; Changed(); };
            asp.DropDownItems.Add(mi);
        }
        dd.DropDownItems.Add(asp);
        return dd;
    }

    private static ToolStripMenuItem RadioMenu(string title, (string label, int val)[] options, Func<int> get, Action<int> set)
    {
        var parent = new ToolStripMenuItem(title);
        foreach (var (label, val) in options)
        {
            var mi = new ToolStripMenuItem(label) { Checked = get() == val };
            mi.Click += (_, _) => { set(val); foreach (ToolStripMenuItem m in parent.DropDownItems) m.Checked = m == mi; };
            parent.DropDownItems.Add(mi);
        }
        return parent;
    }

    private static Bitmap GradientSwatch(Color c1, Color c2)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        using var br = new LinearGradientBrush(new Rectangle(0, 0, 16, 16), c1, c2, LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(br, 0, 0, 16, 16);
        return bmp;
    }

    private static Bitmap SolidSwatch(Color c)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        using var br = new SolidBrush(c);
        g.FillRectangle(br, 0, 0, 16, 16);
        g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
        return bmp;
    }

    // ----- OCR -----

    private ToolStripDropDownButton BuildOcrMenu()
    {
        var dd = new ToolStripDropDownButton
        {
            Image = ToolIcons.Ocr(),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            AutoSize = false,
            Size = new Size(SnipUi.ButtonW + 14, SnipUi.ButtonH),
            ToolTipText = "Recognise text (OCR) — copy or send to OneNote",
        };
        var copy = new ToolStripMenuItem("Copy text to clipboard");
        copy.Click += (_, _) => DoOcr(send: false);
        var send = new ToolStripMenuItem("Send text to OneNote");
        send.Click += (_, _) => DoOcr(send: true);
        dd.DropDownItems.Add(copy);
        dd.DropDownItems.Add(send);
        return dd;
    }

    private async void DoOcr(bool send)
    {
        string? text = await RecognizeCurrentAsync();
        if (text == null) return;

        if (send)
        {
            SendTextRequested?.Invoke(text);
            Close();
        }
        else
        {
            try { Clipboard.SetText(text); } catch { }
            MessageBox.Show(this, "Recognised text copied to the clipboard.", "QuickOneNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>OCR the raw screenshot (no annotations). Returns null and shows a message on failure/empty.</summary>
    private async Task<string?> RecognizeCurrentAsync()
    {
        byte[] png;
        using (var ms = new MemoryStream())
        {
            _image.Save(ms, ImageFormat.Png);   // OCR the raw screenshot, not annotations/beautify
            png = ms.ToArray();
        }

        try
        {
            UseWaitCursor = true;
            string text = await OcrService.RecognizeAsync(png);
            UseWaitCursor = false;

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "No text was found in the image.", "QuickOneNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            return text;
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            MessageBox.Show(this, "OCR failed: " + ex.Message, "QuickOneNote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    // ----- Actions -----

    private void DoCopy() => CopyCompositeToClipboard();

    /// <summary>Put the current composited image (screenshot + all edits) on the clipboard.</summary>
    private void CopyCompositeToClipboard()
    {
        try
        {
            using var bmp = _canvas.Render();
            Clipboard.SetImage(bmp);
        }
        catch { /* clipboard may be briefly locked by another app */ }
    }

    private void DoSave()
    {
        using var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "snip.png" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            using var bmp = _canvas.Render();
            bmp.Save(dlg.FileName, ImageFormat.Png);
        }
    }

    private void DoSend()
    {
        using var ms = new MemoryStream();
        using (var bmp = _canvas.Render())
            bmp.Save(ms, ImageFormat.Png);
        SendRequested?.Invoke(ms.ToArray());
        Close();
    }

    private void DoSendWithTitle()
    {
        string? title = PromptForTitle();
        if (title == null) return; // cancelled

        using var ms = new MemoryStream();
        using (var bmp = _canvas.Render())
            bmp.Save(ms, ImageFormat.Png);
        SendWithTitleRequested?.Invoke(title, ms.ToArray());
        Close();
    }

    private string? PromptForTitle(string label = "Title for this note (starts a new titled section):", string? initial = null)
    {
        using var dlg = new Form
        {
            Text = "Send with title",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(380, 128),
            Icon = IconFactory.CreateNoteIcon(),
        };
        var lbl = new Label { Text = label, Location = new Point(12, 14), AutoSize = true };
        var tb = new TextBox
        {
            Location = new Point(12, 40),
            Width = 356,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Font = new Font("Segoe UI", 10f),
            Text = initial ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        var ok = new Button { Text = "Send", DialogResult = DialogResult.OK, Size = new Size(84, 30), Location = new Point(196, 84), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(84, 30), Location = new Point(284, 84), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        dlg.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Shown += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return dlg.ShowDialog(this) == DialogResult.OK ? tb.Text.Trim() : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}
