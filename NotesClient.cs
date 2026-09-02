using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace QuickOneNote;

/// <summary>A note in the Desktop Notes app, as returned by GET /api/notes.</summary>
public sealed record NoteRef(string? Id, string Title);

/// <summary>
/// Client for the Desktop Notes "Capture API" (see CAPTURE_API.md): pushes text and/or a PNG
/// screenshot into the Notes inbox (POST /api/inbox), and lists existing notes (GET /api/notes)
/// so the user can pick one to append to. Raw <see cref="HttpClient"/>, no NuGet.
/// </summary>
public sealed class NotesClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public const string DefaultBaseUrl = "https://macross.no-ip.info";

    private readonly string _baseUrl;
    private readonly string _token;

    public NotesClient(string? baseUrl, string token)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
        _token = token;
    }

    /// <summary>
    /// Queue one capture. Targeting: <paramref name="noteId"/> wins if it still exists, else
    /// <paramref name="title"/> appends-or-creates. Sends text (if any) then the PNG (if any).
    /// </summary>
    public async Task SendAsync(string? title, string? noteId, string? text, byte[]? png)
    {
        var payload = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(noteId)) payload["noteId"] = noteId!;
        if (!string.IsNullOrWhiteSpace(title)) payload["title"] = title!;
        if (!string.IsNullOrEmpty(text)) payload["text"] = text!;
        if (png is { Length: > 0 }) payload["image"] = Convert.ToBase64String(png);
        if (payload.Count == 0) return;

        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/inbox");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var res = await Http.SendAsync(req).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}: {Trim(body)}");
        }
    }

    /// <summary>List existing notes (id + title) so the user can append to one.</summary>
    public async Task<List<NoteRef>> ListNotesAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/api/notes");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var res = await Http.SendAsync(req).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        string json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ParseNotes(json);
    }

    /// <summary>Cheap connectivity check (no auth): GET /api/health.</summary>
    public async Task<bool> HealthAsync()
    {
        try
        {
            using var res = await Http.GetAsync(_baseUrl + "/api/health").ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // The /api/notes shape isn't formally documented, so parse defensively: accept either a bare
    // array or an object wrapping one (notes/items/data/results), and match id/title fields
    // case-insensitively.
    private static List<NoteRef> ParseNotes(string json)
    {
        var result = new List<NoteRef>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            JsonElement array = default;
            if (root.ValueKind == JsonValueKind.Array)
                array = root;
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "notes", "items", "data", "results" })
                    if (root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Array) { array = e; break; }
            }
            if (array.ValueKind != JsonValueKind.Array) return result;

            foreach (var el in array.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                string? id = Prop(el, "id", "noteId", "guid");
                string? title = Prop(el, "title", "name");
                if (id == null && title == null) continue;
                result.Add(new NoteRef(id, string.IsNullOrWhiteSpace(title) ? "(untitled)" : title!));
            }
        }
        catch
        {
            // Unexpected shape — return whatever we parsed (possibly empty).
        }
        return result;
    }

    private static string? Prop(JsonElement obj, params string[] names)
    {
        foreach (var p in obj.EnumerateObject())
            foreach (var n in names)
                if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        return null;
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;
}
