using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.Plex;

/// <summary>A refusal or failure from Plex or Tautulli, its message a sentence for the user.</summary>
public sealed class PlexException(string message) : Exception(message);

/// <summary>
/// Plex Media Server's HTTP API (JSON with Accept: application/json; the token in X-Plex-Token), and plex.tv's for the
/// sign-in (a PIN the browser approves) and the account's servers.
/// </summary>
public sealed class PlexClient
{
    private static readonly HttpClient shared = Http.Client(30);
    private static readonly Lazy<HttpClient> trusting = new(() => new HttpClient(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
    }) { Timeout = TimeSpan.FromSeconds(30) });

    public const string Product = "Daisy's App";

    private readonly Uri baseUri;
    private readonly string token;
    private readonly string clientId;
    private readonly HttpClient http;

    public PlexClient(Uri baseUri, string token, string clientId, bool ignoreCertificate)
    {
        this.baseUri = baseUri;
        this.token = token;
        this.clientId = clientId;
        http = ignoreCertificate ? trusting.Value : shared;
    }

    public static PlexClient? For(PlexSettings s) =>
        s.BaseUri() is { } u && s.Token.Length > 0 ? new PlexClient(u, s.Token, s.ClientId, s.IgnoreCertificate) : null;

    public Uri Url(string path)
    {
        var b = new UriBuilder(baseUri);
        int q = path.IndexOf('?');
        b.Path = b.Path.TrimEnd('/') + (q < 0 ? path : path[..q]);
        b.Query = q < 0 ? "" : path[(q + 1)..];
        return b.Uri;
    }

    /// <summary>The URL of an image on the server, scaled by its photo transcoder (for thumbnails), with the token.</summary>
    public Uri Photo(string thumb, int width, int height) =>
        Url($"/photo/:/transcode?width={width}&height={height}&minSize=1&upscale=1&url={Uri.EscapeDataString(thumb)}&X-Plex-Token={Uri.EscapeDataString(token)}");

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var r = new HttpRequestMessage(method, Url(path));
        r.Headers.Accept.ParseAdd("application/json");
        Headers(r, clientId);
        r.Headers.Add("X-Plex-Token", token);
        return r;
    }

    internal static void Headers(HttpRequestMessage r, string clientId)
    {
        r.Headers.Add("X-Plex-Product", Product);
        r.Headers.Add("X-Plex-Client-Identifier", clientId);
        r.Headers.Add("X-Plex-Device-Name", Environment.MachineName);
        r.Headers.Add("X-Plex-Platform", "Windows");
    }

    /// <summary>GET, the answer's MediaContainer (or the whole answer if it has none).</summary>
    public async Task<JsonElement> GetAsync(string path, CancellationToken cancel = default)
    {
        using var r = Request(HttpMethod.Get, path);
        using var resp = await Send(r, cancel);
        string body = await resp.Content.ReadAsStringAsync(cancel);
        if (body.Length == 0) return default;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement.Clone();
        return root.TryGetProperty("MediaContainer", out var mc) ? mc : root;
    }

    public async Task<byte[]> BytesAsync(string path, CancellationToken cancel = default)
    {
        using var r = Request(HttpMethod.Get, path);
        using var resp = await Send(r, cancel);
        return await resp.Content.ReadAsByteArrayAsync(cancel);
    }

    /// <summary>PUT, POST or DELETE with nothing to read back.</summary>
    public async Task SendAsync(HttpMethod method, string path, byte[]? body = null, CancellationToken cancel = default)
    {
        using var r = Request(method, path);
        if (body != null)
        {
            r.Content = new ByteArrayContent(body);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        }
        using var resp = await Send(r, cancel);
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage r, CancellationToken cancel)
    {
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(r, HttpCompletionOption.ResponseContentRead, cancel); }
        catch (HttpRequestException ex) { throw new PlexException(F("Couldn't reach Plex at {0}: {1}", baseUri.Authority, ex.InnerException?.Message ?? ex.Message)); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { throw new PlexException(F("Plex at {0} didn't answer.", baseUri.Authority)); }
        if (resp.IsSuccessStatusCode) return resp;
        var code = resp.StatusCode;
        resp.Dispose();
        throw new PlexException(code == HttpStatusCode.Unauthorized ? T("Plex refused the token (401).")
            : F("Plex answered {0} ({1}) to {2}.", (int)code, code, r.RequestUri?.AbsolutePath ?? ""));
    }

    // ---------------------------------------------------------------- plex.tv

    private static readonly HttpClient tv = Http.Client(20, "application/json");

    private static async Task<JsonElement> Tv(HttpMethod method, string url, string clientId, string? token = null, CancellationToken cancel = default)
    {
        using var r = new HttpRequestMessage(method, url);
        Headers(r, clientId);
        if (token != null) r.Headers.Add("X-Plex-Token", token);
        using var resp = await tv.SendAsync(r, cancel);
        string body = await resp.Content.ReadAsStringAsync(cancel);
        if (!resp.IsSuccessStatusCode) throw new PlexException(F("plex.tv answered {0}.", (int)resp.StatusCode));
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Signs in with plex.tv as Plex's own apps do: a PIN, approved in the browser (app.plex.tv/auth), polled until
    /// it has a token (5 minutes at most). Returns the account's token.
    /// </summary>
    public static async Task<string> SignInAsync(string clientId, CancellationToken cancel)
    {
        var pin = await Tv(HttpMethod.Post, "https://plex.tv/api/v2/pins?strong=true", clientId, cancel: cancel);
        long id = pin.GetProperty("id").GetInt64();
        string code = pin.GetProperty("code").GetString() ?? "";
        string url = "https://app.plex.tv/auth#?clientID=" + Uri.EscapeDataString(clientId) + "&code=" + Uri.EscapeDataString(code)
            + "&context%5Bdevice%5D%5Bproduct%5D=" + Uri.EscapeDataString(Product);
        Ui.Open(url);
        var until = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(1500, cancel);
            var p = await Tv(HttpMethod.Get, $"https://plex.tv/api/v2/pins/{id}", clientId, cancel: cancel);
            if (p.TryGetProperty("authToken", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } token) return token;
        }
        throw new PlexException(T("The sign-in wasn't approved in time."));
    }

    /// <summary>The servers the account can use: name, its token for it, and its addresses (local ones first).</summary>
    public static async Task<List<PlexServerChoice>> ServersAsync(string clientId, string token, CancellationToken cancel)
    {
        var list = await Tv(HttpMethod.Get, "https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=0", clientId, token, cancel);
        var servers = new List<PlexServerChoice>();
        foreach (var r in list.EnumerateArray())
        {
            if (Str(r, "provides")?.Contains("server") != true) continue;
            var uris = new List<string>();
            if (r.TryGetProperty("connections", out var cs))
                foreach (var c in cs.EnumerateArray().OrderByDescending(c => c.TryGetProperty("local", out var l) && l.ValueKind == JsonValueKind.True))
                    if (Str(c, "uri") is { } u) uris.Add(u);
            servers.Add(new PlexServerChoice(Str(r, "name") ?? "Plex", Str(r, "accessToken") ?? token, Str(r, "clientIdentifier") ?? "",
                r.TryGetProperty("owned", out var o) && o.ValueKind == JsonValueKind.True, uris));
        }
        return servers;
    }

    // ---------------------------------------------------------------- reading answers

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(), _ => null }
            : null;

    public static long Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? (long)d : 0
            : 0;

    public static bool Flag(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0) || v.GetString_() == "1");

    public static IEnumerable<JsonElement> List(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    public static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    public static DateTime? Time(JsonElement e, string name) => Num(e, name) is > 0 and var s ? DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime : null;
}

internal static class JsonExtensions
{
    public static string? GetString_(this JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}

/// <summary>A server on the signed-in account.</summary>
public sealed record PlexServerChoice(string Name, string Token, string Id, bool Owned, List<string> Uris)
{
    public override string ToString() => Owned ? Name : F("{0} (shared with you)", Name);
}
