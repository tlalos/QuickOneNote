using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// The running tray application: owns the notify icon, the global hotkey, and orchestrates
/// each capture (copy selection -> read clipboard -> append to OneNote) on an STA worker
/// thread so the UI stays responsive.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly HotkeyManager _hotkeys;        // selection hotkey (simulates Ctrl+C) — text
    private readonly HotkeyManager _clipHotkeys;    // clipboard hotkey (no copy) — images/screenshots
    private readonly HotkeyManager _screenHotkeys;  // screenshot hotkey — capture whole screen
    private readonly HotkeyManager _snipHotkeys;    // snip hotkey — draw a region to capture
    private readonly HotkeyManager _seriesHotkeys;  // series hotkey — start/finish a batch
    private readonly Control _marshal = new();   // hidden control used to marshal back to the UI thread
    private AppSettings _settings;
    private int _busy; // 0 = idle, 1 = capture in progress

    private SnipEditorForm? _editor;   // the single open snip-editor window
    private bool _snipping;            // guards against re-entrant snips

    // Screenshot-series state (all touched on the UI thread only).
    private bool _seriesActive;
    private readonly List<Bitmap> _seriesShots = new();
    private ToolStripMenuItem _startSeriesItem = null!;
    private ToolStripMenuItem _finishSeriesItem = null!;
    private ToolStripMenuItem _cancelSeriesItem = null!;

    public TrayApplicationContext()
    {
        _marshal.CreateControl();
        _settings = AppSettings.Load();

        _tray = new NotifyIcon
        {
            Icon = IconFactory.CreateNoteIcon(),
            Visible = true,
            Text = "QuickOneNote",
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => CaptureSelectionNow();

        _hotkeys = new HotkeyManager();
        _hotkeys.HotkeyPressed += (_, _) => CaptureSelectionNow();

        _clipHotkeys = new HotkeyManager();
        _clipHotkeys.HotkeyPressed += (_, _) => CaptureClipboardNow();

        _screenHotkeys = new HotkeyManager();
        _screenHotkeys.HotkeyPressed += (_, _) => CaptureScreenNow();

        _snipHotkeys = new HotkeyManager();
        _snipHotkeys.HotkeyPressed += (_, _) => StartSnip();

        _seriesHotkeys = new HotkeyManager();
        _seriesHotkeys.HotkeyPressed += (_, _) => ToggleSeries();

        ApplyHotkeys(announce: false);

        if (!_settings.IsConfigured)
        {
            ShowInfo("Welcome! Open Settings to choose a OneNote section and hotkeys.");
            OpenSettings();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Add selection to OneNote (text)", null, (_, _) => CaptureSelectionNow());
        menu.Items.Add("Add clipboard to OneNote (image/screenshot)", null, (_, _) => CaptureClipboardNow());
        menu.Items.Add("Capture full screen to OneNote", null, (_, _) => CaptureScreenNow());
        menu.Items.Add("Snip a region…", null, (_, _) => StartSnip());
        menu.Items.Add(new ToolStripSeparator());
        _startSeriesItem = new ToolStripMenuItem("Start screenshot series", null, (_, _) => StartSeries());
        _finishSeriesItem = new ToolStripMenuItem("Finish series…", null, (_, _) => FinishSeries());
        _cancelSeriesItem = new ToolStripMenuItem("Cancel series", null, (_, _) => CancelSeries());
        menu.Items.Add(_startSeriesItem);
        menu.Items.Add(_finishSeriesItem);
        menu.Items.Add(_cancelSeriesItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        menu.Opening += (_, _) => UpdateSeriesMenu();
        return menu;
    }

    private void UpdateSeriesMenu()
    {
        _startSeriesItem.Visible = !_seriesActive;
        _finishSeriesItem.Visible = _seriesActive;
        _cancelSeriesItem.Visible = _seriesActive;
        _finishSeriesItem.Text = $"Finish series ({_seriesShots.Count})…";
    }

    private void ApplyHotkeys(bool announce)
    {
        bool ok1 = _hotkeys.Register(_settings.Hotkey);
        bool ok2 = _clipHotkeys.Register(_settings.ClipboardHotkey);
        bool ok3 = _screenHotkeys.Register(_settings.ScreenshotHotkey);
        bool ok4 = _snipHotkeys.Register(_settings.SnipHotkey);
        bool ok5 = _seriesHotkeys.Register(_settings.SeriesHotkey);

        // A blank hotkey is intentionally disabled (not registered) — never report it as a failure.
        var failed = new List<string>();
        if (_settings.Hotkey.IsValid && !ok1) failed.Add($"selection hotkey {_settings.Hotkey.Display}");
        if (_settings.ClipboardHotkey.IsValid && !ok2) failed.Add($"clipboard hotkey {_settings.ClipboardHotkey.Display}");
        if (_settings.ScreenshotHotkey.IsValid && !ok3) failed.Add($"screenshot hotkey {_settings.ScreenshotHotkey.Display}");
        if (_settings.SnipHotkey.IsValid && !ok4) failed.Add($"snip hotkey {_settings.SnipHotkey.Display}");
        if (_settings.SeriesHotkey.IsValid && !ok5) failed.Add($"series hotkey {_settings.SeriesHotkey.Display}");

        if (failed.Count > 0)
            ShowWarning("Could not register " + string.Join(" and ", failed) +
                        " — it's in use by another app. Pick a different one in Settings.");
        else if (announce)
            ShowInfo($"Hotkeys set: {_settings.Hotkey.Display} = selection/text, " +
                     $"{_settings.ClipboardHotkey.Display} = clipboard/image.");

        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        // NotifyIcon.Text is capped at 63 characters.
        string text = $"QuickOneNote — {_settings.Hotkey.Display}=text, {_settings.ClipboardHotkey.Display}=image";
        _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    // ----- Capture pipeline -----

    /// <summary>Copy the current selection (simulated Ctrl+C) and send it — best for text.</summary>
    private void CaptureSelectionNow() =>
        RunCapture(ClipboardCapture.CaptureSelection, "Nothing was selected to capture.");

    /// <summary>Send whatever is on the clipboard without copying — best for images/screenshots.</summary>
    private void CaptureClipboardNow() =>
        RunCapture(ClipboardCapture.CaptureClipboard,
            "The clipboard is empty. Copy an image first (e.g. press Win+Shift+S for a screenshot), then try again.");

    /// <summary>Capture the focused monitor and send it — one keypress, no copy needed.</summary>
    private void CaptureScreenNow() =>
        RunCapture(ClipboardCapture.CaptureFocusedScreen, "Could not capture the screen.", "a full-screen screenshot");

    /// <summary>Draw a region on screen, annotate it, then Copy/Save/Send it to OneNote.</summary>
    private void StartSnip()
    {
        if (!_settings.IsConfigured)
        {
            ShowInfo("Choose a OneNote section in Settings first.");
            OpenSettings();
            return;
        }

        if (_snipping) return;   // ignore re-entrant triggers while the overlay is up
        _snipping = true;
        var old = _editor;
        try
        {
            // Hide any open editor and let the desktop repaint so it isn't captured in the shot.
            if (old is { IsDisposed: false })
            {
                old.Hide();
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
                Application.DoEvents();
            }

            Bitmap crop;
            using (var overlay = new SnipOverlayForm())
            {
                if (overlay.ShowDialog() != DialogResult.OK || overlay.Result is null)
                {
                    if (old is { IsDisposed: false }) old.Show();   // cancelled — restore the editor
                    return;
                }
                crop = overlay.Result;
            }

            // Snip succeeded — the previous editor is replaced.
            if (old is { IsDisposed: false }) { _editor = null; old.Close(); }

            // During a series, snips are collected raw and annotated later in the review window.
            if (_seriesActive)
            {
                _seriesShots.Add(crop);
                ShowInfo($"Added to series ({_seriesShots.Count}). Snip more, or finish with {_settings.SeriesHotkey.Display}.");
                return;
            }

            // Put the snip on the clipboard immediately, like the Windows Snipping Tool.
            try { Clipboard.SetImage(crop); } catch { /* clipboard busy */ }

            // The editor takes ownership of the bitmap and disposes it when it closes.
            var editor = new SnipEditorForm(crop, _seriesActive);
            _editor = editor;
            editor.FormClosed += (_, _) => { if (ReferenceEquals(_editor, editor)) _editor = null; };
            editor.SendRequested += png =>
                RunCapture(() => new CapturedContent(null, png), "The snip was empty.", "a snip");
            editor.SendWithTitleRequested += (title, png) =>
                RunSeriesSend(title, new List<SeriesItem> { new(null, png) });
            editor.SendTextRequested += text =>
                RunCapture(() => new CapturedContent(text, null), "No text to send.", "text");
            editor.ReselectRequested += () => StartSnip();   // re-snip: StartSnip hides/replaces this editor
            editor.Show();
            editor.Activate();
        }
        finally
        {
            _snipping = false;
        }
    }

    // ----- Screenshot series -----

    private void ToggleSeries()
    {
        if (_seriesActive) FinishSeries();
        else StartSeries();
    }

    private void StartSeries()
    {
        if (_seriesActive) return;
        if (!_settings.IsConfigured)
        {
            ShowInfo("Choose a OneNote section in Settings first.");
            OpenSettings();
            return;
        }
        _seriesActive = true;
        ClearSeriesShots();
        ShowInfo($"Screenshot series started. Snip regions with {_settings.SnipHotkey.Display}; " +
                 $"finish with {_settings.SeriesHotkey.Display} (or the tray menu).");
    }

    private void FinishSeries()
    {
        if (!_seriesActive) return;
        _seriesActive = false;

        if (_seriesShots.Count == 0)
        {
            ShowInfo("Series ended — no screenshots were taken.");
            return;
        }

        // Hand ownership of the bitmaps to the review window.
        var shots = new List<Bitmap>(_seriesShots);
        _seriesShots.Clear();

        var review = new SeriesReviewForm(shots);
        review.SubmitRequested += (title, items) => RunSeriesSend(title, items);
        review.Show();
        review.Activate();
    }

    private void CancelSeries()
    {
        if (!_seriesActive) return;
        _seriesActive = false;
        ClearSeriesShots();
        ShowInfo("Series cancelled.");
    }

    private void ClearSeriesShots()
    {
        foreach (var b in _seriesShots) b.Dispose();
        _seriesShots.Clear();
    }

    private void RunSeriesSend(string title, IReadOnlyList<SeriesItem> items)
    {
        if (items.Count == 0) return;
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            ShowWarning("Busy sending — try again in a moment.");
            return;
        }

        string what = items.Count == 1 ? "a titled note" : $"a series of {items.Count}";
        var settings = _settings;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                Report(success: true, $"Sending {what} to OneNote…");
                OneNoteBackends.For(settings).AppendSeries(settings, title, items);
                Report(success: true, $"Added {what} to OneNote.");
            }
            catch (Exception ex)
            {
                Report(success: false, "Couldn't add to OneNote: " + ex.Message);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _busy, 0);
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void RunCapture(Func<CapturedContent> grab, string emptyMessage, string? label = null)
    {
        if (!_settings.IsConfigured)
        {
            ShowInfo("Choose a OneNote section in Settings first.");
            OpenSettings();
            return;
        }

        // Ignore re-entrancy if a capture is already running.
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        var settings = _settings; // capture reference for the worker
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var content = grab();
                if (content.IsEmpty)
                {
                    Report(success: false, emptyMessage);
                    return;
                }

                string what = label ?? Describe(content);
                Report(success: true, $"Sending {what} to OneNote…");

                OneNoteBackends.For(settings).Append(settings, content);

                Report(success: true, $"Added {what} to OneNote.");
            }
            catch (Exception ex)
            {
                Report(success: false, "Couldn't add to OneNote: " + ex.Message);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _busy, 0);
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private static string Describe(CapturedContent c)
    {
        bool hasText = !string.IsNullOrWhiteSpace(c.Text);
        bool hasImage = c.PngImage is { Length: > 0 };
        if (hasText && hasImage) return "text and an image";
        if (hasImage) return "an image";
        return "text";
    }

    private void Report(bool success, string message)
    {
        if (_marshal.IsDisposed) return;
        try
        {
            _marshal.BeginInvoke(new Action(() =>
            {
                if (success) ShowInfo(message);
                else ShowWarning(message);
            }));
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (InvalidOperationException) { /* handle not created */ }
    }

    // ----- Settings -----

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _settings = form.Result;
            _settings.Save();
            ApplyHotkeys(announce: true);
        }
    }

    // ----- Notifications -----

    private void ShowInfo(string message) => Balloon(message, ToolTipIcon.Info);
    private void ShowWarning(string message) => Balloon(message, ToolTipIcon.Warning);

    private void Balloon(string message, ToolTipIcon icon)
    {
        if (!_settings.ShowNotifications && icon == ToolTipIcon.Info) return;
        _tray.BalloonTipTitle = "QuickOneNote";
        _tray.BalloonTipText = message;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(2500);
    }

    private void ExitApp()
    {
        _tray.Visible = false;
        Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeys.Dispose();
            _clipHotkeys.Dispose();
            _screenHotkeys.Dispose();
            _snipHotkeys.Dispose();
            _seriesHotkeys.Dispose();
            ClearSeriesShots();
            _tray.Dispose();
            _marshal.Dispose();
        }
        base.Dispose(disposing);
    }
}
