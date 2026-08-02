using System.Drawing.Imaging;
using System.IO;

namespace QuickOneNote;

/// <summary>
/// Auto-saves captured screenshots to the user's Pictures\Screenshots folder, the same place
/// the Windows Snipping Tool uses. Filenames follow the "Screenshot yyyy-MM-dd HHmmss.png"
/// pattern, with a numeric suffix to avoid overwriting when two shots land in the same second.
/// </summary>
internal static class ScreenshotSaver
{
    /// <summary>The folder screenshots are written to (created on demand).</summary>
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// <summary>Save raw PNG bytes. Returns the file path, or null on failure.</summary>
    public static string? Save(byte[] png)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
            string path = Path.Combine(Folder, $"Screenshot {stamp}.png");
            for (int n = 1; File.Exists(path); n++)
                path = Path.Combine(Folder, $"Screenshot {stamp} ({n}).png");
            File.WriteAllBytes(path, png);
            return path;
        }
        catch
        {
            // Saving to disk is a convenience — never let it break the actual capture/send.
            return null;
        }
    }

    /// <summary>Save a bitmap as PNG. Returns the file path, or null on failure.</summary>
    public static string? Save(Bitmap image)
    {
        try
        {
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            return Save(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
