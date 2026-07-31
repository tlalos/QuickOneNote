using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Registers a single global hotkey and raises <see cref="HotkeyPressed"/> on the UI thread
/// whenever it fires. Uses a hidden message-only window to receive WM_HOTKEY.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB00B;

    private readonly MessageWindow _window;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public HotkeyManager()
    {
        _window = new MessageWindow();
        _window.HotkeyPressed += (_, _) => HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The window handle other code can pass to Win32 calls if needed.</summary>
    public IntPtr Handle => _window.Handle;

    /// <summary>
    /// (Re)register the given hotkey. Returns false if Windows refused it
    /// (e.g. another app already owns that combination).
    /// </summary>
    public bool Register(HotkeyConfig hotkey)
    {
        Unregister();
        if (hotkey is null || !hotkey.IsValid)
            return false;

        _registered = NativeMethods.RegisterHotKey(
            _window.Handle,
            HotkeyId,
            hotkey.Modifiers | HotkeyConfig.MOD_NOREPEAT,
            hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _window.DestroyHandle();
    }

    /// <summary>Invisible native window whose only job is to catch WM_HOTKEY.</summary>
    private sealed class MessageWindow : NativeWindow
    {
        public event EventHandler? HotkeyPressed;

        public MessageWindow()
        {
            // A message-only window (HWND_MESSAGE) never appears on screen.
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            base.WndProc(ref m);
        }
    }
}
