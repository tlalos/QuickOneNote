using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>Where notes are stored.</summary>
public enum BackendKind
{
    /// <summary>Local desktop OneNote via COM (requires OneNote installed).</summary>
    Local = 0,

    /// <summary>Cloud OneNote via Microsoft Graph (requires sign-in; no OneNote install).</summary>
    Cloud = 1,
}

/// <summary>How a captured snippet is placed into OneNote.</summary>
public enum CaptureMode
{
    /// <summary>Create a brand new page for every capture.</summary>
    NewPageEachTime = 0,

    /// <summary>Append everything to a single page named after today's date.</summary>
    TodaysPage = 1,
}

/// <summary>A global hotkey expressed as Win32 modifier flags + a virtual-key code.</summary>
public sealed class HotkeyConfig
{
    // Win32 RegisterHotKey modifier flags.
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }

    // Defaults use Ctrl+Shift+O / Ctrl+Shift+I. We avoid Ctrl+Shift+N (taken by Chrome
    // Incognito and OneNote Quick Note) and avoid Ctrl+Alt combos (they collide with AltGr
    // on non-US keyboard layouts such as Greek).
    public static HotkeyConfig Default => new()
    {
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        VirtualKey = (uint)Keys.O,
    };

    /// <summary>Default hotkey for sending the current clipboard (images/screenshots).</summary>
    public static HotkeyConfig DefaultClipboard => new()
    {
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        VirtualKey = (uint)Keys.I,
    };

    /// <summary>Default hotkey for capturing the whole screen and sending it.</summary>
    public static HotkeyConfig DefaultScreenshot => new()
    {
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        VirtualKey = (uint)Keys.S,
    };

    /// <summary>Default hotkey for the region snip tool.</summary>
    public static HotkeyConfig DefaultSnip => new()
    {
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        VirtualKey = (uint)Keys.G,
    };

    /// <summary>Default hotkey to start/finish a screenshot series.</summary>
    public static HotkeyConfig DefaultSeries => new()
    {
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        VirtualKey = (uint)Keys.B,
    };

    /// <summary>Build a config from a WinForms key combination (used by the settings capture box).</summary>
    public static HotkeyConfig FromKeys(Keys keyData)
    {
        uint mods = 0;
        if ((keyData & Keys.Control) == Keys.Control) mods |= MOD_CONTROL;
        if ((keyData & Keys.Shift) == Keys.Shift) mods |= MOD_SHIFT;
        if ((keyData & Keys.Alt) == Keys.Alt) mods |= MOD_ALT;
        return new HotkeyConfig { Modifiers = mods, VirtualKey = (uint)(keyData & Keys.KeyCode) };
    }

    /// <summary>An empty (disabled) hotkey — not registered with the system.</summary>
    public static HotkeyConfig None => new() { Modifiers = 0, VirtualKey = 0 };

    [JsonIgnore]
    public bool IsValid => VirtualKey != 0 && Modifiers != 0;

    /// <summary>True when the hotkey is blank (the action is disabled / no global shortcut).</summary>
    [JsonIgnore]
    public bool IsEmpty => VirtualKey == 0 && Modifiers == 0;

    /// <summary>Human-readable combo, e.g. "Ctrl + Shift + N", or "(none)" when disabled.</summary>
    [JsonIgnore]
    public string Display
    {
        get
        {
            if (IsEmpty) return "(none)";
            var parts = new List<string>();
            if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
            var key = (Keys)VirtualKey;
            parts.Add(key == Keys.None ? "?" : key.ToString());
            return string.Join(" + ", parts);
        }
    }
}

/// <summary>Persisted user settings, stored as JSON under %APPDATA%\QuickOneNote.</summary>
public sealed class AppSettings
{
    public BackendKind Backend { get; set; } = BackendKind.Local;

    /// <summary>Azure app registration (client) ID — required for the Cloud backend.</summary>
    public string? GraphClientId { get; set; }

    public string? SectionId { get; set; }
    public string? SectionName { get; set; }
    public CaptureMode Mode { get; set; } = CaptureMode.TodaysPage;

    /// <summary>Hotkey that copies the current selection (Ctrl+C) — best for text.</summary>
    public HotkeyConfig Hotkey { get; set; } = HotkeyConfig.Default;

    /// <summary>Hotkey that sends the clipboard as-is (no copy) — best for images/screenshots.</summary>
    public HotkeyConfig ClipboardHotkey { get; set; } = HotkeyConfig.DefaultClipboard;

    /// <summary>Hotkey that captures the whole screen and sends it.</summary>
    public HotkeyConfig ScreenshotHotkey { get; set; } = HotkeyConfig.DefaultScreenshot;

    /// <summary>Hotkey that opens the region snip tool.</summary>
    public HotkeyConfig SnipHotkey { get; set; } = HotkeyConfig.DefaultSnip;

    /// <summary>Hotkey that starts/finishes a screenshot series.</summary>
    public HotkeyConfig SeriesHotkey { get; set; } = HotkeyConfig.DefaultSeries;

    public bool ShowNotifications { get; set; } = true;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrEmpty(SectionId);

    private static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickOneNote");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    loaded.Hotkey ??= HotkeyConfig.Default;
                    loaded.ClipboardHotkey ??= HotkeyConfig.DefaultClipboard;
                    loaded.ScreenshotHotkey ??= HotkeyConfig.DefaultScreenshot;
                    loaded.SnipHotkey ??= HotkeyConfig.DefaultSnip;
                    loaded.SeriesHotkey ??= HotkeyConfig.DefaultSeries;
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt/unreadable settings fall back to defaults rather than crashing the app.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
