using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>The text and/or image pulled from a selection.</summary>
public sealed record CapturedContent(string? Text, byte[]? PngImage)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && (PngImage is null || PngImage.Length == 0);
}

/// <summary>
/// Grabs the current selection by simulating Ctrl+C, reading the clipboard, then
/// restoring the user's previous clipboard contents. All clipboard access must happen
/// on an STA thread (see <see cref="CaptureRunner"/>).
/// </summary>
public static class ClipboardCapture
{
    /// <summary>Copy the active selection and return it, leaving the clipboard as we found it.</summary>
    public static CapturedContent CaptureSelection()
    {
        IDataObject? snapshot = Snapshot();
        uint before = NativeMethods.GetClipboardSequenceNumber();

        NativeMethods.SendCopy();

        // Wait (up to ~1s) for the target app to actually place data on the clipboard.
        bool copied = false;
        for (int i = 0; i < 40; i++)
        {
            if (NativeMethods.GetClipboardSequenceNumber() != before)
            {
                copied = true;
                break;
            }
            System.Threading.Thread.Sleep(25);
        }

        // Only use the clipboard if our Ctrl+C actually produced *new* content. Otherwise the
        // copy failed (e.g. nothing selected, or a floating window stole focus) and reading the
        // clipboard would append whatever stale text/image happened to be sitting there.
        var captured = copied ? ReadClipboard() : new CapturedContent(null, null);

        Restore(snapshot);
        return captured;
    }

    /// <summary>
    /// Read whatever is already on the clipboard WITHOUT simulating a copy. Best for images and
    /// screenshots: the user copies an image (Win+Shift+S, or right-click → Copy image) and then
    /// triggers this, so nothing overwrites it.
    /// </summary>
    public static CapturedContent CaptureClipboard() => ReadClipboard();

    /// <summary>
    /// Capture the single monitor that currently has focus (the one holding the foreground
    /// window) as a PNG. On a multi-monitor setup this grabs only the active screen.
    /// </summary>
    public static CapturedContent CaptureFocusedScreen()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        var screen = (fg != IntPtr.Zero ? Screen.FromHandle(fg) : Screen.PrimaryScreen) ?? Screen.PrimaryScreen!;
        using var bmp = NativeMethods.CaptureScreen(screen.Bounds);
        return new CapturedContent(null, ToPng(bmp));
    }

    /// <summary>Read text/image directly from a file (used by the Explorer right-click entry).</summary>
    public static CapturedContent CaptureFromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        string[] imageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        if (Array.IndexOf(imageExts, ext) >= 0)
        {
            using var img = Image.FromFile(path);
            return new CapturedContent(null, ToPng(img));
        }

        // Treat everything else as text.
        var text = File.ReadAllText(path);
        return new CapturedContent(text, null);
    }

    private static CapturedContent ReadClipboard()
    {
        string? text = null;
        byte[]? png = null;

        Retry(() =>
        {
            if (Clipboard.ContainsText())
                text = Clipboard.GetText();
        });

        Retry(() =>
        {
            if (Clipboard.ContainsImage())
            {
                using var img = Clipboard.GetImage();
                if (img != null)
                    png = ToPng(img);
            }
        });

        return new CapturedContent(string.IsNullOrWhiteSpace(text) ? null : text, png);
    }

    private static IDataObject? Snapshot()
    {
        try
        {
            var copy = new DataObject();
            bool any = false;

            Retry(() =>
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    copy.SetText(Clipboard.GetText(TextDataFormat.UnicodeText), TextDataFormat.UnicodeText);
                    any = true;
                }
                if (Clipboard.ContainsText(TextDataFormat.Html))
                {
                    copy.SetText(Clipboard.GetText(TextDataFormat.Html), TextDataFormat.Html);
                    any = true;
                }
                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null) { copy.SetImage(img); any = true; }
                }
                if (Clipboard.ContainsFileDropList())
                {
                    copy.SetFileDropList(Clipboard.GetFileDropList());
                    any = true;
                }
            });

            return any ? copy : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Restore(IDataObject? snapshot)
    {
        try
        {
            if (snapshot != null)
                Retry(() => Clipboard.SetDataObject(snapshot, copy: true));
        }
        catch
        {
            // If restore fails the worst case is the captured content stays on the clipboard.
        }
    }

    private static byte[] ToPng(Image image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>The clipboard is a shared resource and can be briefly locked by other apps.</summary>
    private static void Retry(Action action, int attempts = 5)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { action(); return; }
            catch when (i < attempts - 1) { System.Threading.Thread.Sleep(40); }
        }
    }
}
