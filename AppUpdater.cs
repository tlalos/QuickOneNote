using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text.Json;

namespace QuickOneNote;

/// <summary>A newer release found on GitHub (or Available=false when up to date).</summary>
public sealed record ReleaseInfo(bool Available, string Version, string AssetUrl, string AssetName, string Notes);

/// <summary>
/// Discovers and downloads app updates from the repo's GitHub Releases. Pure I/O + JSON parsing.
/// Works with public repos (no token) and private repos (fine-grained PAT with contents:read — the
/// asset must then be fetched by its API url with an octet-stream Accept). No NuGet.
/// </summary>
public static class AppUpdater
{
    private const string ApiBase = "https://api.github.com";

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("QuickOneNote-Updater");   // GitHub requires a UA
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        c.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return c;
    }

    private static void Auth(HttpRequestMessage req, string token)
    {
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Find the highest release in owner/repo newer than <paramref name="currentVersion"/> that
    /// carries a <paramref name="assetPrefix"/>*.zip asset.
    /// </summary>
    public static async Task<ReleaseInfo> CheckLatestAsync(string repo, string token, bool includePrerelease,
        string assetPrefix, string currentVersion, CancellationToken ct = default)
    {
        using var http = NewClient(TimeSpan.FromSeconds(30));
        string bestVer = currentVersion, bestUrl = "", bestName = "", bestNotes = "";
        bool found = false;

        var r = repo.Trim().Trim('/');
        if (r.Length == 0) return new ReleaseInfo(false, currentVersion, "", "", "");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/repos/{r}/releases?per_page=30");
        Auth(req, token);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return new ReleaseInfo(false, currentVersion, "", "", "");
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return new ReleaseInfo(false, currentVersion, "", "", "");
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            bool pre = rel.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            if (pre && !includePrerelease) continue;
            string tag = rel.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            string ver = NormalizeTag(tag);
            if (ver.Length == 0 || CompareVersions(ver, bestVer) <= 0) continue;

            var (asset, aname) = FindAsset(rel, assetPrefix);
            if (asset == null) continue;

            bestVer = ver; bestUrl = asset; bestName = aname; found = true;
            bestNotes = rel.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";
        }
        return new ReleaseInfo(found, bestVer, bestUrl, bestName, bestNotes);
    }

    private static (string? url, string name) FindAsset(JsonElement rel, string assetPrefix)
    {
        if (!rel.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, "");
        foreach (var a in assets.EnumerateArray())
        {
            string name = a.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            if (name.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && a.TryGetProperty("url", out var u))                        // API url, not browser_download_url
                return (u.GetString(), name);
        }
        return (null, "");
    }

    /// <summary>Download a release asset (private repos require the API asset URL + octet-stream Accept).</summary>
    public static async Task DownloadAssetAsync(string assetApiUrl, string token, string destFile,
        Action<long, long>? onProgress = null, CancellationToken ct = default)
    {
        using var http = NewClient(TimeSpan.FromMinutes(10));
        using var req = new HttpRequestMessage(HttpMethod.Get, assetApiUrl);
        Auth(req, token);
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd("application/octet-stream");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destFile))!);
        await using var fs = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[1 << 20];
        long done = 0; int read;
        while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            onProgress?.Invoke(done, total);
        }
    }

    public static string NormalizeTag(string tag) => (tag ?? "").Trim().TrimStart('v', 'V');

    /// <summary>Compare dotted numeric versions; pre-release suffixes ignored. Returns &lt;0, 0, &gt;0.</summary>
    public static int CompareVersions(string a, string b)
    {
        int[] pa = Parse(a), pb = Parse(b);
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;

        static int[] Parse(string v)
        {
            v = (v ?? "").Trim();
            int cut = v.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0) v = v.Substring(0, cut);
            var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var outp = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out outp[i]);
            return outp;
        }
    }
}
