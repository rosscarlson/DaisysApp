using System.Net.Http;
using System.Text.Json;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.Plex;

/// <summary>Tautulli's API v2: /api/v2?apikey=…&amp;cmd=…, the answer's response.data.</summary>
public sealed class TautulliClient(Uri baseUri, string key)
{
    private static readonly HttpClient http = Http.Client(20, "application/json");

    public static TautulliClient? For(PlexSettings s) => s.TautulliUri() is { } u && s.TautulliKey.Length > 0 ? new TautulliClient(u, s.TautulliKey) : null;

    public async Task<JsonElement> CallAsync(string cmd, string? args = null, CancellationToken cancel = default)
    {
        var b = new UriBuilder(baseUri);
        b.Path = b.Path.TrimEnd('/') + "/api/v2";
        b.Query = $"apikey={Uri.EscapeDataString(key)}&cmd={cmd}" + (args != null ? "&" + args : "");
        string body;
        try
        {
            using var resp = await http.GetAsync(b.Uri, cancel);
            body = await resp.Content.ReadAsStringAsync(cancel);
            if (!resp.IsSuccessStatusCode && body.Length == 0) throw new PlexException(F("Tautulli answered {0}.", (int)resp.StatusCode));
        }
        catch (HttpRequestException ex) { throw new PlexException(F("Couldn't reach Tautulli at {0}: {1}", baseUri.Authority, ex.InnerException?.Message ?? ex.Message)); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { throw new PlexException(F("Tautulli at {0} didn't answer.", baseUri.Authority)); }
        JsonElement r;
        try
        {
            using var doc = JsonDocument.Parse(body);
            r = doc.RootElement.GetProperty("response").Clone();
        }
        catch (Exception) { throw new PlexException(T("That isn't Tautulli's API (check the address).")); }
        if (PlexClient.Str(r, "result") != "success")
            throw new PlexException(PlexClient.Str(r, "message") is { Length: > 0 } m ? F("Tautulli: {0}", m) : T("Tautulli refused the request."));
        return r.TryGetProperty("data", out var d) ? d : default;
    }
}
