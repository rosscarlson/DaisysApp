using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>A problem worth showing as it is (wrong password, no client, rate limited…).</summary>
public sealed class IRacingException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// iRacing's Data API (members-ng.iracing.com/data). Signs in with OAuth's "password limited" grant
/// (oauth.iracing.com: your email and password and your OAuth client's id and secret, the secrets masked as iRacing
/// asks: Base64(SHA-256(secret + the lower-case id))), then keeps going with the refresh token, which it saves (each
/// is used once). Each data call answers with a link to the data (on S3), which is fetched in turn; searches answer
/// with chunks, each fetched. Waits when the rate limit runs out.
/// </summary>
public sealed class IRacingApi
{
    private const string TokenUrl = "https://oauth.iracing.com/oauth2/token";
    private const string DataUrl = "https://members-ng.iracing.com/data";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly IRacingSettings settings;
    private string? accessToken;
    private DateTime accessExpires;

    /// <summary>A note on what it's doing (waiting for the rate limit…).</summary>
    public event Action<string>? Status;

    public IRacingApi(IRacingSettings settings) => this.settings = settings;

    /// <summary>Forgets the access token (the account or client changed).</summary>
    public void SignOut() => accessToken = null;

    /// <summary>Base64(SHA-256(secret + the trimmed, lower-case id)): how iRacing wants a password or client secret sent.</summary>
    public static string Mask(string secret, string id) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + id.Trim().ToLowerInvariant())));

    // ---------------------------------------------------------------- signing in

    public async Task SignInAsync(CancellationToken cancel, bool fresh = false)
    {
        if (!fresh && accessToken != null && DateTime.UtcNow < accessExpires) return;
        if (!settings.HasCredentials) throw new IRacingException(T("Fill in your iRacing email, password, client ID and client secret in Settings → Gaming → iRacing."));

        string refresh = settings.RefreshToken;
        if (!fresh && refresh.Length > 0 && DateTime.UtcNow < settings.RefreshTokenExpires)
        {
            try
            {
                await TokenAsync(new()
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = settings.ClientId.Trim(),
                    ["client_secret"] = Mask(settings.ClientSecret, settings.ClientId),
                    ["refresh_token"] = refresh,
                }, cancel);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // refresh tokens can be revoked early: sign in from scratch
                Log.Here.Info("iRacing: the saved sign-in didn't work, signing in again (" + ex.Message + ")");
                settings.ForgetSignIn();
            }
        }

        await TokenAsync(new()
        {
            ["grant_type"] = "password_limited",
            ["client_id"] = settings.ClientId.Trim(),
            ["client_secret"] = Mask(settings.ClientSecret, settings.ClientId),
            ["username"] = settings.Email.Trim(),
            ["password"] = Mask(settings.Password, settings.Email),
            ["scope"] = "iracing.auth",
        }, cancel);
        Log.Here.Info("iRacing: signed in as " + settings.Email.Trim());
    }

    private async Task TokenAsync(Dictionary<string, string> form, CancellationToken cancel)
    {
        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancel);
        string body = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
        {
            string error = "", about = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                error = Str(doc.RootElement, "error") ?? "";
                about = Str(doc.RootElement, "error_description") ?? "";
            }
            catch { }
            Log.Here.Warn($"iRacing: sign-in refused ({(int)response.StatusCode} {error}: {about})");
            if (response.Headers.RetryAfter?.Delta is { } wait)
                throw new IRacingException(F("iRacing has had too many sign-ins: try again in {0:0} minutes.", Math.Ceiling(wait.TotalMinutes)));
            throw new IRacingException(error switch
            {
                "invalid_client" => T("iRacing didn't accept the client ID or secret. Check them in Settings → Gaming → iRacing."),
                "invalid_grant" or "access_denied" => T("iRacing didn't accept the email or password (or this account isn't on the client's list of users)."),
                "unauthorized_client" => T("This client can't sign in this way, or it has signed in too often: try again later."),
                _ => F("iRacing refused the sign-in: {0}", about.Length > 0 ? about : error.Length > 0 ? error : ((int)response.StatusCode).ToString()),
            });
        }
        using var token = JsonDocument.Parse(body);
        var r = token.RootElement;
        accessToken = Str(r, "access_token") ?? throw new IRacingException(T("iRacing's sign-in answer had no token."));
        accessExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, Num(r, "expires_in") ?? 600) - 60);
        if (Str(r, "refresh_token") is { Length: > 0 } refresh)
        {
            settings.RefreshToken = refresh;
            settings.RefreshTokenExpires = DateTime.UtcNow.AddSeconds((Num(r, "refresh_token_expires_in") ?? 3600) - 60);
        }
        settings.Save();
    }

    // ---------------------------------------------------------------- data

    /// <summary>A data call (e.g. "/member/info"), its link followed: the data's JSON.</summary>
    public async Task<JsonElement> GetAsync(string path, CancellationToken cancel)
    {
        for (int attempt = 0; ; attempt++)
        {
            await SignInAsync(cancel);
            using var request = new HttpRequestMessage(HttpMethod.Get, DataUrl + path);
            request.Headers.Authorization = new("Bearer", accessToken);
            using var response = await Http.SendAsync(request, cancel);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                accessToken = null;
                continue;
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable) && attempt < 4)
            {
                await WaitForLimitAsync(response, cancel, force: true);
                continue;
            }
            string body = await response.Content.ReadAsStringAsync(cancel);
            if (!response.IsSuccessStatusCode)
            {
                Log.Here.Warn($"iRacing: {path} → {(int)response.StatusCode} {Shorten(body)}");
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new IRacingException(T("iRacing didn't accept the sign-in for its data. Check the settings."));
                throw new IRacingException(F("iRacing's data service answered {0} for {1}.", (int)response.StatusCode, path.Split('?')[0]));
            }
            await WaitForLimitAsync(response, cancel);
            var root = Parse(body);
            if (root.ValueKind == JsonValueKind.Object && Str(root, "link") is { Length: > 0 } link)
                return Parse(await Http.GetStringAsync(link, cancel));
            return root;
        }
    }

    /// <summary>A search: its rows, from every chunk (or the rows it gave straight away).</summary>
    public async Task<List<JsonElement>> SearchAsync(string path, CancellationToken cancel)
    {
        var result = await GetAsync(path, cancel);
        var rows = new List<JsonElement>();
        var data = result.TryGetProperty("data", out var d) ? d : result;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("chunk_info", out var chunks) && chunks.ValueKind == JsonValueKind.Object)
        {
            string baseUrl = Str(chunks, "base_download_url") ?? "";
            if (chunks.TryGetProperty("chunk_file_names", out var names) && names.ValueKind == JsonValueKind.Array)
                foreach (var name in names.EnumerateArray())
                {
                    var chunk = Parse(await Http.GetStringAsync(baseUrl + name.GetString(), cancel));
                    if (chunk.ValueKind == JsonValueKind.Array) rows.AddRange(chunk.EnumerateArray());
                }
        }
        else if (data.ValueKind == JsonValueKind.Array) rows.AddRange(data.EnumerateArray());
        else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array) rows.AddRange(list.EnumerateArray());
        return rows;
    }

    private async Task WaitForLimitAsync(HttpResponseMessage response, CancellationToken cancel, bool force = false)
    {
        int? remaining = Header(response, "x-ratelimit-remaining");
        if (!force && (remaining == null || remaining > 1)) return;
        int? reset = Header(response, "x-ratelimit-reset");
        var wait = reset is { } r ? DateTimeOffset.FromUnixTimeSeconds(r) - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(30);
        if (response.Headers.RetryAfter?.Delta is { } after) wait = after;
        wait = TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds + 1, 2, 15 * 60));
        Log.Here.Info($"iRacing: rate limit reached, waiting {wait.TotalSeconds:0} s");
        Status?.Invoke(F("iRacing's rate limit: waiting {0:0} seconds…", wait.TotalSeconds));
        await Task.Delay(wait, cancel);
    }

    private static int? Header(HttpResponseMessage r, string name) =>
        r.Headers.TryGetValues(name, out var v) && int.TryParse(v.FirstOrDefault(), out int n) ? n : null;

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Shorten(string s) => s.Length > 200 ? s[..200] + "…" : s;

    // ---------------------------------------------------------------- reading the answers

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        } : null;

    public static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) => d,
            _ => null,
        } : null;

    public static int Int(JsonElement e, string name, int fallback = 0) => Num(e, name) is { } d ? (int)d : fallback;

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static DateTime? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static IEnumerable<JsonElement> Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
