using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace QuickOneNote;

/// <summary>
/// OAuth2 for personal Microsoft accounts using the device-code flow (no external library).
/// The user visits a URL and enters a short code; we poll for the token, then persist the
/// refresh token (DPAPI-encrypted) so future launches sign in silently.
/// </summary>
public sealed class GraphAuth
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    private const string Scope = "Notes.ReadWrite offline_access openid profile";

    private static readonly HttpClient Http = new();
    // Serialize token acquisition so overlapping operations never fire two refreshes at once.
    // Personal-account refresh tokens are single-use, and racing them triggers AADSTS50196.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string _clientId;

    public GraphAuth(string clientId) => _clientId = clientId;

    private static string TokenFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickOneNote", "graph_token.bin");

    public static bool HasSavedSession => File.Exists(TokenFile);

    public static void SignOut()
    {
        try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch { }
    }

    /// <summary>Details the user needs to complete device-code sign-in.</summary>
    public sealed record DeviceCode(string UserCode, string VerificationUri, string DeviceCodeValue, int Interval, string Message);

    public async Task<DeviceCode> StartDeviceCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(_clientId))
            throw new InvalidOperationException("No Client ID set. Enter your Azure app Client ID in Settings.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = Scope,
        });
        var res = await Http.PostAsync($"{Authority}/devicecode", form);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException("Sign-in start failed: " + Describe(root));

        return new DeviceCode(
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.GetProperty("device_code").GetString()!,
            root.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5,
            root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "");
    }

    /// <summary>Poll until the user completes sign-in. Returns true on success.</summary>
    public async Task<bool> PollForTokenAsync(DeviceCode dc, CancellationToken ct)
    {
        int interval = Math.Max(dc.Interval, 1);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = _clientId,
                ["device_code"] = dc.DeviceCodeValue,
            });
            var res = await Http.PostAsync($"{Authority}/token", form, ct);
            var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement;

            if (res.IsSuccessStatusCode)
            {
                SaveFromResponse(root, existingRefresh: null);
                return true;
            }

            string? err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            if (err == "authorization_pending") continue;
            if (err == "slow_down") { interval += 5; continue; }
            return false; // declined / expired / bad code
        }
        return false;
    }

    /// <summary>Get a valid access token, reusing the cached one and refreshing only when needed.</summary>
    public async Task<string> GetAccessTokenAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var cache = LoadCache();
            if (cache == null)
                throw new InvalidOperationException("Not signed in. Open Settings and sign in to your Microsoft account.");

            // Reuse a still-valid access token so we don't hit Azure on every operation.
            if (!string.IsNullOrEmpty(cache.AccessToken) && DateTime.UtcNow < cache.ExpiresUtc)
                return cache.AccessToken!;

            if (string.IsNullOrEmpty(cache.RefreshToken))
                throw new InvalidOperationException("Not signed in. Open Settings and sign in again.");

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _clientId,
                ["refresh_token"] = cache.RefreshToken!,
                ["scope"] = Scope,
            });
            var res = await Http.PostAsync($"{Authority}/token", form);
            var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            if (!res.IsSuccessStatusCode)
            {
                string err = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                // Only forget the session when the refresh token is genuinely dead.
                if (err == "invalid_grant") SignOut();
                throw new InvalidOperationException("Session expired — please sign in again. " + Describe(root));
            }
            return SaveFromResponse(root, cache.RefreshToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Persist access token + expiry + refresh token from a token response; returns the access token.</summary>
    private static string SaveFromResponse(JsonElement token, string? existingRefresh)
    {
        string access = token.GetProperty("access_token").GetString()!;
        int expires = token.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600;
        var expiry = DateTime.UtcNow.AddSeconds(expires - 120);
        string refresh = token.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { Length: > 0 } r
            ? r
            : existingRefresh ?? "";
        SaveCache(new TokenCacheData(refresh, access, expiry));
        return access;
    }

    private sealed record TokenCacheData(string RefreshToken, string? AccessToken, DateTime ExpiresUtc);

    private sealed class TokenCacheDto
    {
        public string? rt { get; set; }
        public string? at { get; set; }
        public long exp { get; set; }
    }

    private static TokenCacheData? LoadCache()
    {
        try
        {
            if (!File.Exists(TokenFile)) return null;
            string text = Encoding.UTF8.GetString(DpapiProtect.Unprotect(File.ReadAllBytes(TokenFile)));
            if (text.StartsWith("{"))
            {
                var dto = JsonSerializer.Deserialize<TokenCacheDto>(text);
                return dto?.rt is { Length: > 0 }
                    ? new TokenCacheData(dto.rt, dto.at, DateTimeOffset.FromUnixTimeSeconds(dto.exp).UtcDateTime)
                    : null;
            }
            // Legacy format: the file was just the raw refresh token string.
            return new TokenCacheData(text, null, DateTime.MinValue);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(TokenCacheData c)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
        var dto = new TokenCacheDto
        {
            rt = c.RefreshToken,
            at = c.AccessToken,
            exp = new DateTimeOffset(c.ExpiresUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
        };
        File.WriteAllBytes(TokenFile, DpapiProtect.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto))));
    }

    private static string Describe(JsonElement root) =>
        root.TryGetProperty("error_description", out var d) ? d.GetString() ?? "" :
        root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "unknown error";
}
