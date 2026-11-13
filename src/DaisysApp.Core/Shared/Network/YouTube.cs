using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaisysApp.Logging;
using DaisysApp.Settings;

namespace DaisysApp.Shared.Network;

/// <summary>Who can watch a video uploaded: YouTube's privacy status.</summary>
public enum YouTubePrivacy { Unlisted, Private, Public }

/// <summary>(since Core 0.62, 4.15.0) The YouTube account the app uploads with: the app's own Google OAuth client
/// (since 4.15.1; <see cref="YouTube.AppClient"/>) or the user's own (a Desktop app client of a Google Cloud project
/// with the YouTube Data API on; <see cref="ClientId"/> "" = the app's), and the sign-in it gave.
/// YouTube.json; the secret and the sign-in encrypted for this Windows user.</summary>
public sealed class YouTubeAccount
{
    public string ClientId { get; set; } = "";
    public string SecretProtected { get; set; } = "";
    public string RefreshProtected { get; set; } = "";
    /// <summary>The channel signed in to (its name), "" none.</summary>
    public string Channel { get; set; } = "";

    internal string Secret { get => Unprotect(SecretProtected); set => SecretProtected = Protect(value); }
    internal string Refresh { get => Unprotect(RefreshProtected); set => RefreshProtected = Protect(value); }

    private static string Protect(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    private static string Unprotect(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

/// <summary>
/// (since Core 0.62, 4.15.0) YouTube for every module: one account (<see cref="YouTubeAccount"/>, signed in with
/// <see cref="SignIn"/> in the browser: Google's loopback sign-in for desktop apps, with PKCE), videos uploaded
/// (<see cref="Upload"/>: resumable, in 8 MB pieces, with its progress) and deleted, and their links
/// (<see cref="WatchUrl"/>, <see cref="IdOf"/>). Its settings card is <see cref="YouTubeAccountView"/>.
/// </summary>
public static class YouTube
{
    private const string FileName = "YouTube";
    private const string Scope = "https://www.googleapis.com/auth/youtube";
    private const int Piece = 8 * 1024 * 1024;

    private static readonly HttpClient http = Http.Client(TimeSpan.FromMinutes(10), "application/json");
    private static readonly SemaphoreSlim tokenGate = new(1, 1);
    private static YouTubeAccount? account;
    private static string? accessToken;
    private static DateTime accessUntil;

    /// <summary>The account as kept.</summary>
    public static YouTubeAccount Account => account ??= JsonStore.Load<YouTubeAccount>(FileName);

    /// <summary>Signed in: uploads can go.</summary>
    public static bool SignedIn => Account.RefreshProtected.Length > 0 && ClientId.Length > 0;

    /// <summary>(since Core 0.63, 4.15.1) The app has its own OAuth client (built in by the release from its
    /// YOUTUBE_CLIENT secret): anyone signs in with their Google account, no client of their own.</summary>
    public static bool AppClient => builtIn.Value.Id.Length > 0;

    private static readonly Lazy<(string Id, string Secret)> builtIn = new(() =>
    {
        try
        {
            string? packed = typeof(YouTube).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "YouTubeClient")?.Value;
            if (string.IsNullOrWhiteSpace(packed)) return ("", "");
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(packed.Trim())).Split('\n', 2);
            return parts.Length == 2 ? (parts[0].Trim(), parts[1].Trim()) : ("", "");
        }
        catch { return ("", ""); }
    });

    /// <summary>The client signing in: the user's own if they've given one, else the app's.</summary>
    private static string ClientId => Account.ClientId.Length > 0 ? Account.ClientId : builtIn.Value.Id;
    private static string Secret => Account.ClientId.Length > 0 ? Account.Secret : builtIn.Value.Secret;

    /// <summary>Raised (on the thread that changed it) when it's signed in or out, or the client changes.</summary>
    public static event Action? Changed;

    /// <summary>Where a Desktop app OAuth client is made (the YouTube Data API v3 switched on in its project).</summary>
    public const string ConsoleUrl = "https://console.cloud.google.com/apis/credentials";

    /// <summary>The OAuth client to sign in with (a change signs out).</summary>
    public static void SetClient(string id, string secret)
    {
        id = id.Trim();
        secret = secret.Trim();
        if (id == Account.ClientId && secret == Account.Secret) return;
        Account.ClientId = id;
        Account.Secret = secret;
        Forget();
    }

    public static string ClientSecret => Account.Secret;

    /// <summary>Signs in in the browser (the page Google shows, then back to the app); the channel's name, or why not.</summary>
    public static async Task<(bool Ok, string Message)> SignIn(CancellationToken cancel)
    {
        if (ClientId.Length == 0) return (false, T("Needs the OAuth client's ID"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = $"http://127.0.0.1:{port}/";
            string verifier = Base64Url(RandomNumberGenerator.GetBytes(32)), state = Base64Url(RandomNumberGenerator.GetBytes(16));
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string url = "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&access_type=offline&prompt=consent"
                + $"&client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(redirect)}&scope={Uri.EscapeDataString(Scope)}"
                + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}";
            Ui.Open(url);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            string? code = null, error = null;
            while (code == null && error == null)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII);
                string? request = await reader.ReadLineAsync(timeout.Token);
                // ("GET /?state=…&code=… HTTP/1.1"; anything else, such as the favicon, is answered and waited past)
                string path = request?.Split(' ') is { Length: >= 2 } parts ? parts[1] : "";
                var query = Query(path);
                bool ours = query.TryGetValue("state", out var s) && s == state;
                if (ours)
                {
                    query.TryGetValue("code", out code);
                    if (code == null) error = query.TryGetValue("error", out var e) ? e : T("No answer");
                }
                string page = !ours ? "" : code != null ? T("Signed in to YouTube. You can close this tab.") : F("Not signed in: {0}", error!);
                byte[] body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>Daisy's App</title><body style=\"font-family:Segoe UI;margin:40px\">{WebUtility.HtmlEncode(page)}</body>");
                byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 {(ours ? 200 : 404)} OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
            if (code == null) return (false, F("Not signed in: {0}", error!));

            var tokens = await Post("https://oauth2.googleapis.com/token", new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = ClientId, ["client_secret"] = Secret, ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
            }, cancel);
            string? refresh = tokens["refresh_token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(refresh)) return (false, T("Google gave no sign-in to keep"));
            Account.Refresh = refresh;
            Took(tokens);
            Account.Channel = await ChannelName(cancel) ?? T("YouTube");
            Save();
            Log.App.Info($"YouTube: signed in ({Account.Channel})");
            return (true, Account.Channel);
        }
        catch (OperationCanceledException) { return (false, T("Not signed in: no answer from the browser")); }
        catch (Exception ex)
        {
            Log.App.Warn("YouTube: sign-in failed", ex);
            return (false, F("Not signed in: {0}", ex.Message));
        }
        finally { listener.Stop(); }
    }

    /// <summary>Signs out: the sign-in is revoked at Google and forgotten.</summary>
    public static void SignOut()
    {
        string refresh = Account.Refresh;
        if (refresh.Length > 0)
            _ = Task.Run(async () =>
            {
                try { await http.PostAsync("https://oauth2.googleapis.com/revoke?token=" + Uri.EscapeDataString(refresh), null); }
                catch (Exception ex) { Log.App.Debug($"YouTube: revoke failed: {ex.Message}"); }
            });
        Forget();
        Log.App.Info("YouTube: signed out");
    }

    private static void Forget()
    {
        Account.Refresh = "";
        Account.Channel = "";
        accessToken = null;
        Save();
    }

    private static void Save()
    {
        JsonStore.Save(FileName, Account);
        Changed?.Invoke();
    }

    /// <summary>A video uploaded: its ID and link.</summary>
    public sealed record Uploaded(string Id, string Url);

    /// <summary>Uploads a video (resumable: 8 MB at a time, <paramref name="progress"/> 0 to 1). Title up to 100
    /// characters (cut), tags optional; category 20 (Gaming) unless given.</summary>
    public static async Task<Uploaded> Upload(string file, string title, string description, YouTubePrivacy privacy,
        IProgress<double>? progress, CancellationToken cancel, IEnumerable<string>? tags = null, string category = "20")
    {
        long total = new FileInfo(file).Length;
        var meta = new JsonObject
        {
            ["snippet"] = new JsonObject
            {
                ["title"] = Title(title), ["description"] = description.Replace("<", "‹").Replace(">", "›"), ["categoryId"] = category,
                ["tags"] = new JsonArray((tags ?? Array.Empty<string>()).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            },
            ["status"] = new JsonObject { ["privacyStatus"] = privacy.ToString().ToLowerInvariant(), ["selfDeclaredMadeForKids"] = false },
        };
        var start = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status")
        {
            Content = new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        start.Headers.Add("X-Upload-Content-Length", total.ToString());
        start.Headers.Add("X-Upload-Content-Type", Mime(file));
        using var begun = await Send(start, cancel);
        if (!begun.IsSuccessStatusCode) throw new IOException(await Problem(begun));
        var session = begun.Headers.Location ?? throw new IOException(T("YouTube gave no upload address"));
        Log.App.Info($"YouTube: uploading {Path.GetFileName(file)} ({total / 1048576.0:0.0} MB, {privacy})");

        await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[Piece];
        long sent = 0;
        int failures = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            source.Position = sent;
            int n = 0;
            while (n < buffer.Length) { int r = await source.ReadAsync(buffer.AsMemory(n), cancel); if (r == 0) break; n += r; }
            long at = sent;
            var put = new HttpRequestMessage(HttpMethod.Put, session) { Content = new PieceContent(buffer, n, w => progress?.Report((double)(at + w) / Math.Max(1, total))) };
            put.Content.Headers.ContentType = new MediaTypeHeaderValue(Mime(file));
            put.Content.Headers.ContentRange = n > 0 ? new ContentRangeHeaderValue(sent, sent + n - 1, total) : new ContentRangeHeaderValue(total);
            HttpResponseMessage answer;
            try { answer = await Send(put, cancel); }
            catch (HttpRequestException) when (++failures <= 5) { await Task.Delay(TimeSpan.FromSeconds(2 * failures), cancel); sent = await Received(session, total, cancel) ?? sent; continue; }
            using (answer)
            {
                if ((int)answer.StatusCode == 308)
                {
                    // (what it has: "bytes=0-n"; none yet: no Range)
                    sent = answer.Headers.TryGetValues("Range", out var range) && range.FirstOrDefault() is { } rr && long.TryParse(rr[(rr.IndexOf('-') + 1)..], out long last) ? last + 1 : 0;
                    failures = 0;
                    progress?.Report((double)sent / Math.Max(1, total));
                    continue;
                }
                if (answer.IsSuccessStatusCode)
                {
                    var done = JsonNode.Parse(await answer.Content.ReadAsStringAsync(cancel));
                    string id = done?["id"]?.GetValue<string>() ?? throw new IOException(T("YouTube gave no video ID"));
                    progress?.Report(1);
                    Log.App.Info($"YouTube: uploaded {Path.GetFileName(file)}: {WatchUrl(id)}");
                    return new Uploaded(id, WatchUrl(id));
                }
                if ((int)answer.StatusCode >= 500 && ++failures <= 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * failures), cancel);
                    sent = await Received(session, total, cancel) ?? sent;
                    continue;
                }
                throw new IOException(await Problem(answer));
            }
        }
    }

    /// <summary>A piece of an upload, telling how much of it has gone (64 KB at a time, at most 4 times a second), so
    /// progress moves while it goes rather than once a piece.</summary>
    private sealed class PieceContent(byte[] buffer, int count, Action<long> written) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) => await Write(stream, default);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancel) => await Write(stream, cancel);

        private async Task Write(Stream stream, CancellationToken cancel)
        {
            long last = 0;
            for (int at = 0; at < count;)
            {
                int n = Math.Min(64 * 1024, count - at);
                await stream.WriteAsync(buffer.AsMemory(at, n), cancel);
                at += n;
                if (Environment.TickCount64 - last >= 250 || at == count) { last = Environment.TickCount64; written(at); }
            }
        }

        protected override bool TryComputeLength(out long length) { length = count; return true; }
    }

    /// <summary>How much of an upload YouTube has (null: couldn't ask).</summary>
    private static async Task<long?> Received(Uri session, long total, CancellationToken cancel)
    {
        try
        {
            var ask = new HttpRequestMessage(HttpMethod.Put, session) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            ask.Content.Headers.ContentRange = new ContentRangeHeaderValue(total);
            using var answer = await Send(ask, cancel);
            if ((int)answer.StatusCode != 308) return null;
            return answer.Headers.TryGetValues("Range", out var range) && range.FirstOrDefault() is { } r && long.TryParse(r[(r.IndexOf('-') + 1)..], out long last) ? last + 1 : 0;
        }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>Deletes a video of the account's (by its ID or link).</summary>
    public static async Task Delete(string video, CancellationToken cancel = default)
    {
        string id = IdOf(video) ?? video;
        using var answer = await Send(new HttpRequestMessage(HttpMethod.Delete, "https://www.googleapis.com/youtube/v3/videos?id=" + Uri.EscapeDataString(id)), cancel);
        if (!answer.IsSuccessStatusCode && answer.StatusCode != HttpStatusCode.NotFound) throw new IOException(await Problem(answer));
        Log.App.Info($"YouTube: deleted {id}");
    }

    /// <summary>"https://youtu.be/ID".</summary>
    public static string WatchUrl(string id) => "https://youtu.be/" + id;

    /// <summary>The video's ID in a link (youtu.be/ID, youtube.com/watch?v=ID, /embed/ID, /shorts/ID, /live/ID), or a
    /// bare ID; null if it isn't one.</summary>
    public static string? IdOf(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        link = link.Trim();
        static bool Id(string s) => s.Length == 11 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        if (Id(link)) return link;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return null;
        string host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (host == "youtu.be") return segments.Length > 0 && Id(segments[0]) ? segments[0] : null;
        if (!host.EndsWith("youtube.com") && !host.EndsWith("youtube-nocookie.com")) return null;
        if (Query(uri.PathAndQuery).TryGetValue("v", out var v) && Id(v)) return v;
        return segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live" or "v" && Id(segments[1]) ? segments[1] : null;
    }

    /// <summary>A title YouTube takes: no &lt; or &gt;, at most 100 characters.</summary>
    public static string Title(string title)
    {
        title = title.Replace("<", "‹").Replace(">", "›").Replace('\n', ' ').Trim();
        if (title.Length == 0) title = "Video";
        return title.Length <= 100 ? title : title[..99].TrimEnd() + "…";
    }

    private static string Mime(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".mkv" => "video/x-matroska", ".webm" => "video/webm", ".mov" => "video/quicktime", ".avi" => "video/x-msvideo", _ => "video/mp4",
    };

    private static async Task<string?> ChannelName(CancellationToken cancel)
    {
        try
        {
            using var answer = await Send(new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true"), cancel);
            if (!answer.IsSuccessStatusCode) return null;
            var json = JsonNode.Parse(await answer.Content.ReadAsStringAsync(cancel));
            return json?["items"]?[0]?["snippet"]?["title"]?.GetValue<string>();
        }
        catch (Exception ex) { Log.App.Debug($"YouTube: the channel's name couldn't be read: {ex.Message}"); return null; }
    }

    /// <summary>Sends a request with the sign-in (renewed when it's run out).</summary>
    private static async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancel)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token(cancel));
        return await http.SendAsync(request, cancel);
    }

    private static async Task<string> Token(CancellationToken cancel)
    {
        await tokenGate.WaitAsync(cancel);
        try
        {
            if (accessToken != null && DateTime.UtcNow < accessUntil) return accessToken;
            if (!SignedIn) throw new InvalidOperationException(T("Not signed in to YouTube"));
            try
            {
                Took(await Post("https://oauth2.googleapis.com/token", new Dictionary<string, string>
                {
                    ["client_id"] = ClientId, ["client_secret"] = Secret, ["refresh_token"] = Account.Refresh, ["grant_type"] = "refresh_token",
                }, cancel));
            }
            catch (YouTubeRefused ex) when (ex.Code == "invalid_grant")
            {
                // (revoked, or a testing project's week-long sign-in ran out)
                Forget();
                throw new InvalidOperationException(T("YouTube's sign-in has run out: sign in again"));
            }
            return accessToken!;
        }
        finally { tokenGate.Release(); }
    }

    private static void Took(JsonNode tokens)
    {
        accessToken = tokens["access_token"]?.GetValue<string>() ?? throw new IOException(T("Google gave no access"));
        double seconds = tokens["expires_in"]?.GetValue<double>() ?? 3600;
        accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
    }

    private sealed class YouTubeRefused(string code, string message) : IOException(message)
    {
        public string Code { get; } = code;
    }

    private static async Task<JsonNode> Post(string url, Dictionary<string, string> form, CancellationToken cancel)
    {
        using var answer = await http.PostAsync(url, new FormUrlEncodedContent(form), cancel);
        string text = await answer.Content.ReadAsStringAsync(cancel);
        JsonNode? json = null;
        try { json = JsonNode.Parse(text); } catch (JsonException) { }
        if (!answer.IsSuccessStatusCode || json == null)
            throw new YouTubeRefused(json?["error"]?.ToString() ?? "", json?["error_description"]?.GetValue<string>() ?? json?["error"]?.ToString() ?? $"{(int)answer.StatusCode} {answer.ReasonPhrase}");
        return json;
    }

    /// <summary>What a refusal says: YouTube's own message, else its status.</summary>
    private static async Task<string> Problem(HttpResponseMessage answer)
    {
        try
        {
            var json = JsonNode.Parse(await answer.Content.ReadAsStringAsync());
            if (json?["error"]?["message"]?.GetValue<string>() is { Length: > 0 } m) return m;
        }
        catch { }
        return $"{(int)answer.StatusCode} {answer.ReasonPhrase}";
    }

    private static Dictionary<string, string> Query(string pathAndQuery)
    {
        var q = new Dictionary<string, string>(StringComparer.Ordinal);
        int at = pathAndQuery.IndexOf('?');
        if (at < 0) return q;
        foreach (var pair in pathAndQuery[(at + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string k = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            q[k] = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
        }
        return q;
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
