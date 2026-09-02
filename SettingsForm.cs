using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Lets the user choose a storage backend (local OneNote or cloud/Graph), a target section,
/// capture mode, and hotkeys. English UI.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _working;

    private readonly RadioButton _backendLocal = new();
    private readonly RadioButton _backendCloud = new();
    private readonly TextBox _clientId = new();
    private readonly Button _signIn = new();
    private readonly Label _signInStatus = new();

    private readonly ListBox _sections = new();
    private readonly Button _refresh = new();
    private readonly Label _status = new();
    private readonly ComboBox _mode = new();
    private readonly TextBox _hotkeyBox = new();
    private readonly TextBox _clipHotkeyBox = new();
    private readonly TextBox _screenshotHotkeyBox = new();
    private readonly TextBox _snipHotkeyBox = new();
    private readonly TextBox _seriesHotkeyBox = new();
    private readonly ToolTip _tips = new();
    private readonly CheckBox _notifications = new();
    private readonly CheckBox _autostart = new();
    private readonly CheckBox _saveShots = new();
    private readonly TextBox _notesUrl = new();
    private readonly TextBox _notesToken = new();

    private HotkeyConfig _pendingHotkey;
    private HotkeyConfig _pendingClipHotkey;
    private HotkeyConfig _pendingScreenHotkey;
    private HotkeyConfig _pendingSnipHotkey;
    private HotkeyConfig _pendingSeriesHotkey;

    private enum HotkeyKind { Selection, Clipboard, Screenshot, Snip, Series }

    /// <summary>The updated settings, valid only after the dialog returns <see cref="DialogResult.OK"/>.</summary>
    public AppSettings Result => _working;

    public SettingsForm(AppSettings current)
    {
        _working = new AppSettings
        {
            Backend = current.Backend,
            GraphClientId = current.GraphClientId,
            SectionId = current.SectionId,
            SectionName = current.SectionName,
            Mode = current.Mode,
            Hotkey = new HotkeyConfig { Modifiers = current.Hotkey.Modifiers, VirtualKey = current.Hotkey.VirtualKey },
            ClipboardHotkey = new HotkeyConfig { Modifiers = current.ClipboardHotkey.Modifiers, VirtualKey = current.ClipboardHotkey.VirtualKey },
            ScreenshotHotkey = new HotkeyConfig { Modifiers = current.ScreenshotHotkey.Modifiers, VirtualKey = current.ScreenshotHotkey.VirtualKey },
            SnipHotkey = new HotkeyConfig { Modifiers = current.SnipHotkey.Modifiers, VirtualKey = current.SnipHotkey.VirtualKey },
            SeriesHotkey = new HotkeyConfig { Modifiers = current.SeriesHotkey.Modifiers, VirtualKey = current.SeriesHotkey.VirtualKey },
            ShowNotifications = current.ShowNotifications,
            SaveScreenshots = current.SaveScreenshots,
            NotesApiBaseUrl = current.NotesApiBaseUrl,
            NotesApiTokenProtected = current.NotesApiTokenProtected,
        };
        _pendingHotkey = _working.Hotkey;
        _pendingClipHotkey = _working.ClipboardHotkey;
        _pendingScreenHotkey = _working.ScreenshotHotkey;
        _pendingSnipHotkey = _working.SnipHotkey;
        _pendingSeriesHotkey = _working.SeriesHotkey;

        BuildLayout();
        LoadModeChoices();

        _clientId.Text = _working.GraphClientId ?? "";
        _notifications.Checked = _working.ShowNotifications;
        _saveShots.Checked = _working.SaveScreenshots;
        _autostart.Checked = Startup.IsEnabled();
        _notesUrl.Text = _working.NotesApiBaseUrl ?? "";
        _notesToken.Text = _working.NotesApiToken ?? "";
        _hotkeyBox.Text = _working.Hotkey.Display;
        _clipHotkeyBox.Text = _working.ClipboardHotkey.Display;
        _screenshotHotkeyBox.Text = _working.ScreenshotHotkey.Display;
        _snipHotkeyBox.Text = _working.SnipHotkey.Display;
        _seriesHotkeyBox.Text = _working.SeriesHotkey.Display;
        _backendLocal.Checked = _working.Backend == BackendKind.Local;
        _backendCloud.Checked = _working.Backend == BackendKind.Cloud;
        UpdateBackendUi();

        Shown += (_, _) => LoadSections();
    }

    private void BuildLayout()
    {
        Text = "QuickOneNote — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 992);
        // On smaller or DPI-scaled displays the tall dialog may exceed the work area; let it scroll
        // rather than clipping the buttons at the bottom.
        AutoScroll = true;
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (Height > work.Height) Height = work.Height - 20;
        Icon = IconFactory.CreateNoteIcon();

        int x = 16, w = 428;

        var lblStorage = new Label { Text = "Where to save notes:", Location = new Point(x, 12), AutoSize = true };
        _backendLocal.Text = "Local OneNote (installed on this PC)";
        _backendLocal.Location = new Point(x, 34);
        _backendLocal.AutoSize = true;
        _backendLocal.CheckedChanged += (_, _) => { if (_backendLocal.Checked) OnBackendChanged(); };

        _backendCloud.Text = "Cloud — Microsoft account (no OneNote install needed)";
        _backendCloud.Location = new Point(x, 58);
        _backendCloud.AutoSize = true;
        _backendCloud.CheckedChanged += (_, _) => { if (_backendCloud.Checked) OnBackendChanged(); };

        var lblClient = new Label { Text = "Azure app Client ID:", Location = new Point(x, 88), AutoSize = true };
        _clientId.Location = new Point(x, 110);
        _clientId.Size = new Size(w - 110, 26);
        _clientId.PlaceholderText = "00000000-0000-0000-0000-000000000000";
        _signIn.Text = "Sign in…";
        _signIn.Location = new Point(x + w - 100, 109);
        _signIn.Size = new Size(100, 28);
        _signIn.Click += (_, _) => SignIn();

        _signInStatus.Location = new Point(x, 140);
        _signInStatus.Size = new Size(w, 18);
        _signInStatus.ForeColor = SystemColors.GrayText;

        var lblSection = new Label { Text = "Target section:", Location = new Point(x, 166), AutoSize = true };
        _sections.Location = new Point(x, 188);
        _sections.Size = new Size(w, 150);
        _sections.DisplayMember = nameof(SectionInfo.DisplayPath);
        _sections.IntegralHeight = false;

        _refresh.Text = "Refresh";
        _refresh.Location = new Point(x, 344);
        _refresh.Size = new Size(90, 28);
        _refresh.Click += (_, _) => LoadSections();

        _status.Location = new Point(x + 100, 350);
        _status.Size = new Size(w - 100, 18);
        _status.ForeColor = SystemColors.GrayText;

        var lblMode = new Label { Text = "When capturing:", Location = new Point(x, 384), AutoSize = true };
        _mode.Location = new Point(x, 406);
        _mode.Size = new Size(w, 26);
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;

        var lblHotkey = new Label { Text = "Selection hotkey — for text (click box, press a combo):", Location = new Point(x, 444), AutoSize = true };
        _hotkeyBox.Location = new Point(x, 466);
        _hotkeyBox.Size = new Size(w, 26);
        _hotkeyBox.ReadOnly = true;
        _hotkeyBox.Cursor = Cursors.Hand;
        _hotkeyBox.BackColor = Color.White;
        _hotkeyBox.KeyDown += (_, e) => HandleHotkeyKey(e, HotkeyKind.Selection);

        var lblClipHotkey = new Label { Text = "Clipboard hotkey — for images/screenshots:", Location = new Point(x, 498), AutoSize = true };
        _clipHotkeyBox.Location = new Point(x, 520);
        _clipHotkeyBox.Size = new Size(w, 26);
        _clipHotkeyBox.ReadOnly = true;
        _clipHotkeyBox.Cursor = Cursors.Hand;
        _clipHotkeyBox.BackColor = Color.White;
        _clipHotkeyBox.KeyDown += (_, e) => HandleHotkeyKey(e, HotkeyKind.Clipboard);

        var lblScreenHotkey = new Label { Text = "Screenshot hotkey — capture the whole screen:", Location = new Point(x, 552), AutoSize = true };
        _screenshotHotkeyBox.Location = new Point(x, 574);
        _screenshotHotkeyBox.Size = new Size(w, 26);
        _screenshotHotkeyBox.ReadOnly = true;
        _screenshotHotkeyBox.Cursor = Cursors.Hand;
        _screenshotHotkeyBox.BackColor = Color.White;
        _screenshotHotkeyBox.KeyDown += (_, e) => HandleHotkeyKey(e, HotkeyKind.Screenshot);

        var lblSnipHotkey = new Label { Text = "Snip hotkey — draw a region on screen:", Location = new Point(x, 606), AutoSize = true };
        _snipHotkeyBox.Location = new Point(x, 628);
        _snipHotkeyBox.Size = new Size(w, 26);
        _snipHotkeyBox.ReadOnly = true;
        _snipHotkeyBox.Cursor = Cursors.Hand;
        _snipHotkeyBox.BackColor = Color.White;
        _snipHotkeyBox.KeyDown += (_, e) => HandleHotkeyKey(e, HotkeyKind.Snip);

        var lblSeriesHotkey = new Label { Text = "Series hotkey — start/finish a screenshot series:", Location = new Point(x, 660), AutoSize = true };
        _seriesHotkeyBox.Location = new Point(x, 682);
        _seriesHotkeyBox.Size = new Size(w, 26);
        _seriesHotkeyBox.ReadOnly = true;
        _seriesHotkeyBox.Cursor = Cursors.Hand;
        _seriesHotkeyBox.BackColor = Color.White;
        _seriesHotkeyBox.KeyDown += (_, e) => HandleHotkeyKey(e, HotkeyKind.Series);

        _notifications.Text = "Show a notification after each capture";
        _notifications.Location = new Point(x, 714);
        _notifications.AutoSize = true;

        _saveShots.Text = @"Save screenshots to Pictures\Screenshots";
        _saveShots.Location = new Point(x, 740);
        _saveShots.AutoSize = true;

        _autostart.Text = "Start with Windows (run at login)";
        _autostart.Location = new Point(x, 766);
        _autostart.AutoSize = true;

        var lblNotes = new Label
        {
            Text = "Desktop Notes (Capture API) — optional second target",
            Location = new Point(x, 800),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
        };
        var lblNotesToken = new Label { Text = "API token (from the Notes app → Settings):", Location = new Point(x, 826), AutoSize = true };
        _notesToken.Location = new Point(x, 848);
        _notesToken.Width = w;
        _notesToken.UseSystemPasswordChar = true;
        var lblNotesUrl = new Label { Text = "Server URL (leave blank for the default):", Location = new Point(x, 880), AutoSize = true };
        _notesUrl.Location = new Point(x, 902);
        _notesUrl.Width = w;
        _notesUrl.PlaceholderText = NotesClient.DefaultBaseUrl;

        var ok = new Button { Text = "Save", Location = new Point(w - 74 + x, 946), Size = new Size(90, 30) };
        ok.Click += Ok_Click;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(w - 170 + x, 946), Size = new Size(90, 30) };

        Controls.AddRange(new Control[]
        {
            lblStorage, _backendLocal, _backendCloud,
            lblClient, _clientId, _signIn, _signInStatus,
            lblSection, _sections, _refresh, _status,
            lblMode, _mode,
            lblHotkey, _hotkeyBox, lblClipHotkey, _clipHotkeyBox, lblScreenHotkey, _screenshotHotkeyBox,
            lblSnipHotkey, _snipHotkeyBox, lblSeriesHotkey, _seriesHotkeyBox,
            _notifications, _saveShots, _autostart,
            lblNotes, lblNotesToken, _notesToken, lblNotesUrl, _notesUrl, ok, cancel,
        });

        const string hotkeyTip = "Click and press a key combination (must include Ctrl, Shift, or Alt). Press Delete to clear and disable this shortcut.";
        foreach (var b in new[] { _hotkeyBox, _clipHotkeyBox, _screenshotHotkeyBox, _snipHotkeyBox, _seriesHotkeyBox })
            _tips.SetToolTip(b, hotkeyTip);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void LoadModeChoices()
    {
        _mode.Items.Clear();
        _mode.Items.Add("Append to today's page");
        _mode.Items.Add("Create a new page each time");
        _mode.SelectedIndex = _working.Mode == CaptureMode.TodaysPage ? 0 : 1;
    }

    // ----- Backend selection -----

    private BackendKind CurrentBackend => _backendCloud.Checked ? BackendKind.Cloud : BackendKind.Local;

    private void OnBackendChanged()
    {
        UpdateBackendUi();
        _sections.Items.Clear();   // section IDs differ between backends
        _status.Text = "";
    }

    private void UpdateBackendUi()
    {
        bool cloud = CurrentBackend == BackendKind.Cloud;
        _clientId.Enabled = cloud;
        _signIn.Enabled = cloud;
        _signInStatus.Text = cloud
            ? (GraphAuth.HasSavedSession ? "Signed in." : "Not signed in.")
            : "";
    }

    private void SignIn()
    {
        if (string.IsNullOrWhiteSpace(_clientId.Text))
        {
            MessageBox.Show("Enter your Azure app Client ID first.", "QuickOneNote",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var auth = new GraphAuth(_clientId.Text.Trim());
        using var dlg = new DeviceCodeForm(auth);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _signInStatus.Text = "Signed in.";
            _working.GraphClientId = _clientId.Text.Trim();
            LoadSections();
        }
    }

    // ----- Sections -----

    private AppSettings ProbeSettings() => new()
    {
        Backend = CurrentBackend,
        GraphClientId = _clientId.Text.Trim(),
        SectionId = _working.SectionId,
    };

    private void LoadSections()
    {
        _status.Text = "Loading sections…";
        _refresh.Enabled = false;
        _sections.Enabled = false;
        Application.DoEvents();

        try
        {
            var backend = OneNoteBackends.For(ProbeSettings());
            var sections = backend.GetSections();
            _sections.Items.Clear();
            foreach (var s in sections)
                _sections.Items.Add(s);

            if (!string.IsNullOrEmpty(_working.SectionId))
            {
                for (int i = 0; i < _sections.Items.Count; i++)
                {
                    if (_sections.Items[i] is SectionInfo si && si.Id == _working.SectionId)
                    {
                        _sections.SelectedIndex = i;
                        break;
                    }
                }
            }

            _status.Text = sections.Count == 0
                ? (CurrentBackend == BackendKind.Cloud
                    ? "No sections found for this account."
                    : "No sections found. Is OneNote running with a notebook open?")
                : $"{sections.Count} section(s) found.";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not load sections.";
            string extra = CurrentBackend == BackendKind.Cloud
                ? "\n\nFor Cloud: enter your Client ID and click 'Sign in…' first."
                : "\n\nFor Local: this needs the desktop OneNote app (COM automation).";
            MessageBox.Show("Could not load your OneNote sections." + extra + "\n\nDetails: " + ex.Message,
                "QuickOneNote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refresh.Enabled = true;
            _sections.Enabled = true;
        }
    }

    // ----- Hotkeys -----

    private void HandleHotkeyKey(KeyEventArgs e, HotkeyKind kind)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var box = kind switch
        {
            HotkeyKind.Clipboard => _clipHotkeyBox,
            HotkeyKind.Screenshot => _screenshotHotkeyBox,
            HotkeyKind.Snip => _snipHotkeyBox,
            HotkeyKind.Series => _seriesHotkeyBox,
            _ => _hotkeyBox,
        };

        // Delete / Backspace clears the hotkey — the action is then disabled (no global shortcut).
        if (e.KeyCode is Keys.Delete or Keys.Back)
        {
            AssignPending(kind, HotkeyConfig.None);
            box.Text = HotkeyConfig.None.Display; // "(none)"
            return;
        }

        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        var candidate = HotkeyConfig.FromKeys(e.KeyData);
        if (!candidate.IsValid)
        {
            box.Text = "Please include Ctrl, Shift, or Alt (or Delete to clear)";
            return;
        }

        AssignPending(kind, candidate);
        box.Text = candidate.Display;
    }

    private void AssignPending(HotkeyKind kind, HotkeyConfig c)
    {
        switch (kind)
        {
            case HotkeyKind.Clipboard: _pendingClipHotkey = c; break;
            case HotkeyKind.Screenshot: _pendingScreenHotkey = c; break;
            case HotkeyKind.Snip: _pendingSnipHotkey = c; break;
            case HotkeyKind.Series: _pendingSeriesHotkey = c; break;
            default: _pendingHotkey = c; break;
        }
    }

    private void Ok_Click(object? sender, EventArgs e)
    {
        if (_sections.SelectedItem is not SectionInfo section)
        {
            MessageBox.Show("Please select a section.", "QuickOneNote",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var all = new[] { _pendingHotkey, _pendingClipHotkey, _pendingScreenHotkey, _pendingSnipHotkey, _pendingSeriesHotkey };
        // A blank hotkey means "disabled" and is allowed; only non-blank ones must be valid + unique.
        if (all.Any(h => !h.IsValid && !h.IsEmpty))
        {
            MessageBox.Show("Please set valid hotkeys (each must include Ctrl, Shift, or Alt) or clear them.", "QuickOneNote",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
                if (!all[i].IsEmpty && Same(all[i], all[j]))
                {
                    MessageBox.Show("The assigned hotkeys must all be different.", "QuickOneNote",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

        _working.Backend = CurrentBackend;
        _working.GraphClientId = string.IsNullOrWhiteSpace(_clientId.Text) ? null : _clientId.Text.Trim();
        _working.SectionId = section.Id;
        _working.SectionName = section.DisplayPath;
        _working.Mode = _mode.SelectedIndex == 0 ? CaptureMode.TodaysPage : CaptureMode.NewPageEachTime;
        _working.Hotkey = _pendingHotkey;
        _working.ClipboardHotkey = _pendingClipHotkey;
        _working.ScreenshotHotkey = _pendingScreenHotkey;
        _working.SnipHotkey = _pendingSnipHotkey;
        _working.SeriesHotkey = _pendingSeriesHotkey;
        _working.ShowNotifications = _notifications.Checked;
        _working.SaveScreenshots = _saveShots.Checked;
        _working.NotesApiBaseUrl = string.IsNullOrWhiteSpace(_notesUrl.Text) ? null : _notesUrl.Text.Trim();
        _working.NotesApiToken = string.IsNullOrWhiteSpace(_notesToken.Text) ? null : _notesToken.Text.Trim();

        // Apply the run-at-login registry entry immediately.
        Startup.Set(_autostart.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool Same(HotkeyConfig a, HotkeyConfig b) =>
        a.Modifiers == b.Modifiers && a.VirtualKey == b.VirtualKey;
}
