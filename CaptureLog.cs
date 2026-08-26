using System.IO;

namespace QuickOneNote;

/// <summary>
/// Tiny append-only diagnostic log for the screen-capture path, written to
/// %APPDATA%\QuickOneNote\capture.log. Used to tell whether a capture went through
/// Windows.Graphics.Capture or fell back to the GDI blit, and why.
/// </summary>
internal static class CaptureLog
{
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickOneNote", "capture.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                // Trim the file if it grows past ~500 lines so it never balloons.
                if (File.Exists(Path))
                {
                    var lines = File.ReadAllLines(Path);
                    if (lines.Length > 500)
                        File.WriteAllLines(Path, lines[^300..]);
                }
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never disrupt a capture.
        }
    }
}
