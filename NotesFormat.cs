using System.Text;

namespace QuickOneNote;

/// <summary>
/// Builds Markdown snippets for the Desktop Notes Capture API (see CAPTURE_API.md → Formatting).
/// The API renders <c>text</c> as Markdown, including callouts: a first line <c>&gt; [!type] title</c>
/// followed by <c>&gt; body</c> lines. Callout types: info/note, success/check, warning/caution, tip/hint.
/// </summary>
internal static class NotesFormat
{
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
            "Captured with QuickOneNote · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

    /// <summary>Info callout used as the header of a screenshot series.</summary>
    public static string SeriesHeaderCallout(string title, int shotCount)
    {
        string subtitle = $"Screenshot series — {shotCount} shot{(shotCount == 1 ? "" : "s")} · "
            + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        return Callout("info", string.IsNullOrWhiteSpace(title) ? "Screenshot series" : title, subtitle);
    }
}
