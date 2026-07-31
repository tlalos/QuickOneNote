using System.Runtime.InteropServices;

namespace QuickOneNote;

/// <summary>Thin P/Invoke layer for global hotkeys and synthesized keystrokes.</summary>
internal static class NativeMethods
{
    // ----- Global hotkey -----
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int WM_HOTKEY = 0x0312;

    // ----- Clipboard change detection -----
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    // ----- Synthesized input (SendInput) -----
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // Virtual-key codes we need.
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_MENU = 0x12;   // Alt
    public const ushort VK_C = 0x43;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    private const uint MAPVK_VK_TO_VSC = 0;
    private const ushort VK_A = 0x41;

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                // Include the hardware scan code. Chromium/WebView2-based apps (e.g. the new
                // Outlook, Teams, VS Code) ignore synthetic keystrokes that have no scan code.
                wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
            },
        },
    };

    /// <summary>
    /// Copy the current selection in the foreground app to the clipboard.
    /// The user is typically still holding the hotkey's Ctrl/Shift when this runs, which would
    /// turn our Ctrl+C into Ctrl+Shift+C. So we first WAIT (briefly) for those modifiers to be
    /// physically released, then force them up as a fallback, then send a clean Ctrl+C.
    /// </summary>
    public static void SendCopy()
    {
        // Wait up to ~500ms for the user to let go of the hotkey modifiers.
        for (int i = 0; i < 50 && (IsDown(VK_CONTROL) || IsDown(VK_SHIFT) || IsDown(VK_MENU)); i++)
            System.Threading.Thread.Sleep(10);

        // Fallback: force the modifiers up in case they're still held.
        var release = new[]
        {
            Key(VK_CONTROL, up: true),
            Key(VK_SHIFT, up: true),
            Key(VK_MENU, up: true),
        };
        SendInput((uint)release.Length, release, Marshal.SizeOf<INPUT>());

        System.Threading.Thread.Sleep(40);

        var copy = new[]
        {
            Key(VK_CONTROL, up: false),
            Key(VK_C, up: false),
            Key(VK_C, up: true),
            Key(VK_CONTROL, up: true),
        };
        SendInput((uint)copy.Length, copy, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Send Ctrl+A (select all) to the foreground window. Used by the copy self-test.</summary>
    public static void SendSelectAll()
    {
        var seq = new[]
        {
            Key(VK_CONTROL, up: false),
            Key(VK_A, up: false),
            Key(VK_A, up: true),
            Key(VK_CONTROL, up: true),
        };
        SendInput((uint)seq.Length, seq, Marshal.SizeOf<INPUT>());
    }
}
