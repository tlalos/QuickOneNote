namespace QuickOneNote;

/// <summary>One screenshot in a series: its rendered (annotated) PNG and an optional caption.</summary>
public sealed record SeriesItem(string? Caption, byte[] Png);

/// <summary>
/// A place notes can be sent to. Implemented by the local desktop OneNote (COM) and the
/// cloud Microsoft Graph API. Both expose the same operations the app needs.
/// </summary>
public interface IOneNoteBackend
{
    /// <summary>List target sections as "Notebook / Group / Section".</summary>
    List<SectionInfo> GetSections();

    /// <summary>Append captured content according to the chosen mode.</summary>
    void Append(AppSettings settings, CapturedContent content);

    /// <summary>
    /// Append a titled series: a bold title, then for each item its caption line (if any)
    /// above the image.
    /// </summary>
    void AppendSeries(AppSettings settings, string title, IReadOnlyList<SeriesItem> items);
}

/// <summary>Chooses the backend implementation based on settings.</summary>
public static class OneNoteBackends
{
    public static IOneNoteBackend For(AppSettings settings) => settings.Backend switch
    {
        BackendKind.Cloud => new GraphOneNoteBackend(new GraphAuth(settings.GraphClientId ?? "")),
        _ => new OneNoteClient(),
    };
}
