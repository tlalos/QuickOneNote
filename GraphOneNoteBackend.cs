using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace QuickOneNote;

/// <summary>
/// Cloud OneNote via the Microsoft Graph REST API. Requires a signed-in Microsoft account
/// (see <see cref="GraphAuth"/>). No local OneNote install needed.
/// </summary>
public sealed class GraphOneNoteBackend : IOneNoteBackend
{
    private const string Base = "https://graph.microsoft.com/v1.0/me/onenote";
    // A small blank line used to visually separate entries on a page.
    private const string Spacer = "<p><span style=\"font-size:6pt\">&#160;</span></p>";
    // The OneNote Graph API is slow, especially for image uploads — give it room.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private readonly GraphAuth _auth;

    public GraphOneNoteBackend(GraphAuth auth) => _auth = auth;

    // ----- Sections -----

    // Bridge async->sync via Task.Run so the awaited continuations never try to resume on a
    // caller's UI thread (which is blocked here) — that would deadlock. Task.Run has no
    // SynchronizationContext, so continuations run on the thread pool.
    public List<SectionInfo> GetSections() => Task.Run(GetSectionsAsync).GetAwaiter().GetResult();

    private async Task<List<SectionInfo>> GetSectionsAsync()
    {
        string url = $"{Base}/sections?$select=id,displayName&$expand=parentNotebook($select=displayName),parentSectionGroup($select=displayName)&$top=100";
        string body = await SendWithRetryAsync(() => AuthorizedRequest(HttpMethod.Get, url), "list sections");
        var root = JsonDocument.Parse(body).RootElement;
        var list = new List<SectionInfo>();
        foreach (var s in root.GetProperty("value").EnumerateArray())
        {
            string id = s.GetProperty("id").GetString()!;
            string name = s.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "Section" : "Section";
            string notebook = NestedName(s, "parentNotebook");
            string group = NestedName(s, "parentSectionGroup");

            string path = notebook.Length == 0 ? name
                : group.Length == 0 ? $"{notebook} / {name}"
                : $"{notebook} / {group} / {name}";
            list.Add(new SectionInfo(id, path));
        }
        list.Sort((a, b) => string.Compare(a.DisplayPath, b.DisplayPath, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static string NestedName(JsonElement parent, string prop) =>
        parent.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Object &&
        p.TryGetProperty("displayName", out var n) ? n.GetString() ?? "" : "";

    // ----- Append -----

    public void Append(AppSettings settings, CapturedContent content) =>
        Task.Run(() => AppendAsync(settings, content)).GetAwaiter().GetResult();

    private async Task AppendAsync(AppSettings settings, CapturedContent content)
    {
        if (string.IsNullOrEmpty(settings.SectionId))
            throw new InvalidOperationException("No OneNote section is selected. Open Settings first.");
        if (content.IsEmpty)
            throw new InvalidOperationException("Nothing was captured.");

        if (settings.Mode == CaptureMode.TodaysPage)
        {
            string title = DateTime.Now.ToString("yyyy-MM-dd");
            var images = new List<(string, byte[])>();
            string inner = BuildHtml(content, includeTimestamp: true, images);
            string? pageId = await FindPageByTitleAsync(settings.SectionId!, title);
            if (pageId == null)
                await CreatePageAsync(settings.SectionId!, title, inner, images);
            else
                await AppendToPageAsync(pageId, inner, images);
        }
        else
        {
            string title = "Quick note — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var images = new List<(string, byte[])>();
            string inner = BuildHtml(content, includeTimestamp: false, images);
            await CreatePageAsync(settings.SectionId!, title, inner, images);
        }
    }

    public void AppendSeries(AppSettings settings, string title, IReadOnlyList<SeriesItem> items) =>
        Task.Run(() => AppendSeriesAsync(settings, title, items)).GetAwaiter().GetResult();

    private async Task AppendSeriesAsync(AppSettings settings, string title, IReadOnlyList<SeriesItem> items)
    {
        if (string.IsNullOrEmpty(settings.SectionId))
            throw new InvalidOperationException("No OneNote section is selected. Open Settings first.");
        if (items.Count == 0)
            throw new InvalidOperationException("The series is empty.");

        var images = new List<(string, byte[])>();
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            // A divider line + a bold title mark the start of a new section.
            sb.Append("<p><span style=\"color:#BFC4CC\">").Append(new string('─', 42)).Append("</span></p>");
            sb.Append("<p><b>").Append(Enc(title)).Append("</b></p>");
        }

        foreach (var item in items)
        {
            // Separate the title/previous screenshot from this one with a blank line.
            if (sb.Length > 0) sb.Append(Spacer);

            if (!string.IsNullOrWhiteSpace(item.Caption))
                foreach (var line in item.Caption!.Replace("\r\n", "\n").Split('\n'))
                    sb.Append("<p>").Append(Enc(line)).Append("</p>");

            if (item.Png is { Length: > 0 })
            {
                string name = "img" + (images.Count + 1);
                images.Add((name, item.Png));
                sb.Append($"<img src=\"name:{name}\" />");
            }
        }

        string inner = sb.ToString();
        if (settings.Mode == CaptureMode.TodaysPage)
        {
            string pageTitle = DateTime.Now.ToString("yyyy-MM-dd");
            string? pageId = await FindPageByTitleAsync(settings.SectionId!, pageTitle);
            if (pageId == null)
                await CreatePageAsync(settings.SectionId!, pageTitle, inner, images);
            else
                await AppendToPageAsync(pageId, inner, images);
        }
        else
        {
            await CreatePageAsync(settings.SectionId!, title.Length > 0 ? title : "Screenshot series", inner, images);
        }
    }

    private async Task<string?> FindPageByTitleAsync(string sectionId, string title)
    {
        string url = $"{Base}/sections/{sectionId}/pages?$select=id,title&$top=20&$filter=title eq '{Uri.EscapeDataString(title)}'";
        string body = await SendWithRetryAsync(() => AuthorizedRequest(HttpMethod.Get, url), "find today's page");
        var root = JsonDocument.Parse(body).RootElement;
        foreach (var p in root.GetProperty("value").EnumerateArray())
            return p.GetProperty("id").GetString();
        return null;
    }

    private async Task CreatePageAsync(string sectionId, string title, string inner, List<(string Name, byte[] Bytes)> images)
    {
        string html =
            "<!DOCTYPE html><html><head>" +
            $"<title>{Enc(title)}</title>" +
            "<meta name=\"created\" content=\"" + DateTime.Now.ToString("o") + "\" />" +
            $"</head><body>{inner}</body></html>";

        async Task<HttpRequestMessage> Build()
        {
            var req = await AuthorizedRequest(HttpMethod.Post, $"{Base}/sections/{sectionId}/pages");
            if (images.Count == 0)
            {
                req.Content = new StringContent(html, Encoding.UTF8, "application/xhtml+xml");
            }
            else
            {
                var multipart = new MultipartFormDataContent("QoNBoundary");
                multipart.Add(new StringContent(html, Encoding.UTF8, "text/html"), "Presentation");
                foreach (var (name, bytes) in images)
                    multipart.Add(PngPart(bytes), name);
                req.Content = multipart;
            }
            return req;
        }

        await SendWithRetryAsync(Build, "create page");
    }

    private async Task AppendToPageAsync(string pageId, string inner, List<(string Name, byte[] Bytes)> images)
    {
        // OneNote PATCH command: append HTML to the page body. A leading blank line separates
        // this note from whatever is already on the page.
        string commands = JsonSerializer.Serialize(new[]
        {
            new { target = "body", action = "append", content = Spacer + inner },
        });

        async Task<HttpRequestMessage> Build()
        {
            var req = await AuthorizedRequest(new HttpMethod("PATCH"), $"{Base}/pages/{pageId}/content");
            if (images.Count == 0)
            {
                req.Content = new StringContent(commands, Encoding.UTF8, "application/json");
            }
            else
            {
                var multipart = new MultipartFormDataContent("QoNBoundary");
                multipart.Add(new StringContent(commands, Encoding.UTF8, "application/json"), "commands");
                foreach (var (name, bytes) in images)
                    multipart.Add(PngPart(bytes), name);
                req.Content = multipart;
            }
            return req;
        }

        await SendWithRetryAsync(Build, "append to page");
    }

    // ----- HTML building -----

    private static string BuildHtml(CapturedContent content, bool includeTimestamp, List<(string, byte[])> images)
    {
        var sb = new StringBuilder();
        string prefix = includeTimestamp ? $"[{DateTime.Now:HH:mm}] " : "";
        bool hasText = !string.IsNullOrWhiteSpace(content.Text);

        if (hasText)
        {
            var lines = content.Text!.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = (i == 0 ? prefix : "") + lines[i];
                sb.Append("<p>").Append(Enc(line)).Append("</p>");
            }
        }

        if (content.PngImage is { Length: > 0 })
        {
            if (!hasText && includeTimestamp)
                sb.Append("<p>").Append(Enc(prefix.TrimEnd())).Append("</p>");
            string name = "img" + (images.Count + 1);
            images.Add((name, content.PngImage));
            sb.Append($"<img src=\"name:{name}\" />");
        }

        return sb.ToString();
    }

    private static ByteArrayContent PngPart(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return part;
    }

    private static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);

    // ----- HTTP helpers -----

    private async Task<HttpRequestMessage> AuthorizedRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.GetAccessTokenAsync());
        return req;
    }

    /// <summary>
    /// Send a request (rebuilt fresh each attempt) and return the response body, retrying
    /// transient Graph failures (429/500/502/503/504 and timeouts) with backoff. The OneNote
    /// service frequently returns 504 Gateway Timeout on page creation, so this is essential.
    /// </summary>
    private static async Task<string> SendWithRetryAsync(Func<Task<HttpRequestMessage>> build, string what)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage? res = null;
            try
            {
                using var req = await build();
                res = await Http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                    return await res.Content.ReadAsStringAsync();

                int code = (int)res.StatusCode;
                bool transient = code is 429 or 500 or 502 or 503 or 504;
                if (transient && attempt < maxAttempts)
                {
                    await Task.Delay(RetryDelay(res, attempt));
                    continue;
                }
                await ThrowFromResponse(res, what);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
            finally
            {
                res?.Dispose();
            }
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage res, int attempt)
    {
        if (res.Headers.RetryAfter?.Delta is { } delta) return delta;
        // Exponential-ish backoff: 3s, 6s, 12s…
        return TimeSpan.FromSeconds(Math.Min(30, 3 * Math.Pow(2, attempt - 1)));
    }

    private static async Task ThrowFromResponse(HttpResponseMessage res, string what)
    {
        string body = await res.Content.ReadAsStringAsync();
        string detail = body;
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            if (root.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                detail = m.GetString() ?? body;
        }
        catch { /* keep raw body */ }

        int code = (int)res.StatusCode;
        string hint = code is 429 or 500 or 502 or 503 or 504
            ? " OneNote's servers are busy — please try again in a moment."
            : "";
        throw new InvalidOperationException($"Graph API error during {what} ({code}): {detail}{hint}");
    }
}
