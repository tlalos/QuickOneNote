using System.Text;

namespace QuickOneNote;

/// <summary>
/// Builds Markdown snippets for the Desktop Notes Capture API (see CAPTURE_API.md, Formatting).
/// The API renders <c>text</c> as Markdown, including callouts: a first line <c>&gt; [!type] title</c>
/// followed by <c>&gt; body</c> lines. Callout types: info/note, success/check, warning/caution, tip/hint.
/// Non-ASCII glyphs use \u escapes so the source can't be corrupted by editor re-encoding.
/// </summary>
internal static class NotesFormat
{
    private const string MidDot = "\u00B7";      // middle dot separator
    private const string EmDash = "\u2014";      // em dash
    // Braille blank: not treated as whitespace, so the Markdown parser keeps it as an empty
    // paragraph (a plain / non-breaking space gets trimmed and collapsed).
    private const string BlankLine = "\u2800";

    /// <summary>Build a callout block: <c>&gt; [!kind] title</c> then one <c>&gt; </c> line per body entry.</summary>
    public static string Callout(string kind, string title, params string[] body)
    {
        var sb = new StringBuilder();
        sb.Append("> [!").Append(kind).Append("] ").Append(title);
        foreach (var line in body)
            sb.Append('\n').Append("> ").Append(line);
        return sb.ToString();
    }

    /// <summary>Fixed info callout that precedes a single screenshot sent to Desktop Notes.</summary>
    public static string ScreenshotCallout() =>
        Callout("info", "Screenshot",
            $"Captured with QuickOneNote {MidDot} " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

    /// <summary>Info callout that precedes OCR text sent to Desktop Notes.</summary>
    public static string OcrCallout() =>
        Callout("info", "Recognized text (OCR)",
            $"QuickOneNote {MidDot} " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

    /// <summary>
    /// Prepend blank spacing so an entry appended to an existing note is separated from earlier
    /// content. Uses Braille-blank lines (each renders as an empty paragraph) because the Markdown
    /// parser trims plain/non-breaking-space lines away. No-op when creating a fresh note.
    /// </summary>
    public static string Prepend(bool separate, string text) =>
        separate ? $"{BlankLine}\n{BlankLine}\n{text}" : text;

    /// <summary>Info callout used as the header of a screenshot series.</summary>
    public static string SeriesHeaderCallout(string title, int shotCount)
    {
        string subtitle = $"Screenshot series {EmDash} {shotCount} shot{(shotCount == 1 ? "" : "s")} {MidDot} "
            + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        return Callout("info", string.IsNullOrWhiteSpace(title) ? "Screenshot series" : title, subtitle);
    }
}
