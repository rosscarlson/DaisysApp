using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DaisysApp.Server;
using DaisysApp.Settings;
using DaisysApp.Shell;
using DaisysServer.Protocol;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>(since 4.17.0) A session shared to Daisy's Server: the server's id for it, and what it was shared as.</summary>
public sealed class SharedSession
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime Shared { get; set; }
    /// <summary>The YouTube link it was shared with (an edit sends a newer one).</summary>
    public string? Video { get; set; }
}

/// <summary>
/// (since 4.17.0) Sharing sessions on Daisy's Server, saved in %APPDATA%\DaisysApp\AnalysisShares.json: each session
/// shared (by its log's file name), each one downloaded (the server's id → its file in Imported), and whether each
/// session is shared by itself once it's kept (<see cref="ShareQueue"/>).
/// </summary>
public sealed class SessionShares
{
    private const string FileName = "AnalysisShares";

    /// <summary>Each session shared by itself (after its YouTube upload, when that's on), from <see cref="AutoSince"/>.</summary>
    public bool Auto { get; set; }
    public DateTime AutoSince { get; set; }

    public Dictionary<string, SharedSession> Shared { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The server's sessions downloaded: id → the session's key ("Imported\file").</summary>
    public Dictionary<string, string> Downloaded { get; set; } = new();

    private static SessionShares? all;
    public static SessionShares All => all ??= JsonStore.Load<SessionShares>(FileName);

    public static SharedSession? Of(string? key) => key != null && All.Shared.TryGetValue(key, out var s) ? s : null;

    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>
/// (since 4.17.0) The <c>sessions</c> module on Daisy's Server (its PROTOCOL.md; from: <c>Analysis</c>): one channel,
/// opened when first asked (the app's one connection, Core's <see cref="ServerLink"/>, encrypted end to end with the
/// token) and again after it's lost. Questions carry a <c>rid</c> that their answer repeats; anything else the server
/// sends (a session added, changed or removed) is <see cref="Pushed"/>. A session goes up as its export file, gzipped,
/// in pieces (<see cref="UploadAsync"/>), and comes down the same way (<see cref="DownloadAsync"/>). Not sealed with the
/// user's key: every user of the server sees what's shared.
/// </summary>
internal static class SessionServer
{
    public const string Module = "sessions";
    private const int Piece = 512 * 1024;

    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly Dictionary<int, TaskCompletionSource<JsonObject>> asks = new();
    private static ServerChannel? channel;
    private static int nextAsk;

    /// <summary>The server's own news, by op ("added", "changed", "removed"), on a background thread.</summary>
    public static event Action<string, JsonObject>? Pushed;

    private static async Task<ServerChannel> ChannelAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (channel is { Ended.Length: 0 } c) return c;
            c = await ServerLink.OpenAsync(Module, "Analysis", ct);
            channel = c;
            _ = Task.Run(() => ReadAsync(c));
            return c;
        }
        finally { gate.Release(); }
    }

    private static async Task ReadAsync(ServerChannel c)
    {
        while (await c.ReadAsync() is { } m)
        {
            if ((int?)m["rid"] is int rid)
            {
                TaskCompletionSource<JsonObject>? ask;
                lock (asks) asks.Remove(rid, out ask);
                ask?.TrySetResult(m);
                continue;
            }
            try { Pushed?.Invoke(ServerClient.Str(m, "op"), m); }
            catch (Exception ex) { Logging.Log.Here.Warn($"Analysis: server sessions: {ex.Message}"); }
        }
        if (channel == c) channel = null;
        List<TaskCompletionSource<JsonObject>> left;
        lock (asks) { left = asks.Values.ToList(); asks.Clear(); }
        string why = c.Ended.Length > 0 ? c.Ended : T("closed");
        foreach (var a in left) a.TrySetException(new ServerException(why));
    }

    /// <summary>A question to the server's module; its answer (an error comes back as ServerException, worded for the user).</summary>
    public static async Task<JsonObject> AskAsync(JsonObject request, CancellationToken ct = default)
    {
        var c = await ChannelAsync(ct);
        int rid = Interlocked.Increment(ref nextAsk);
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (asks) asks[rid] = tcs;
        request["rid"] = rid;
        c.Post(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using (timeout.Token.Register(() => tcs.TrySetCanceled()))
        {
            JsonObject r;
            try { r = await tcs.Task; }
            catch (TaskCanceledException)
            {
                lock (asks) asks.Remove(rid);
                ct.ThrowIfCancellationRequested();
                throw new ServerException(T("The server didn't answer"));
            }
            if (ServerClient.Str(r, "op") == "error")
            {
                string message = ServerClient.Str(r, "message");
                // (a server from before the sessions module)
                throw new ServerException(message.Length > 0 ? message : T("The server couldn't do that"));
            }
            return r;
        }
    }

    /// <summary>The sessions shared on the server, everyone's.</summary>
    public static async Task<List<ServerSession>> ListAsync(CancellationToken ct = default)
    {
        var r = await AskAsync(new JsonObject { ["op"] = "list" }, ct);
        return (r["sessions"] as JsonArray)?.OfType<JsonObject>().Select(ServerSession.From).ToList() ?? new();
    }

    /// <summary>Shares a session's export file: its details, then the file gzipped, a piece at a time. The server's id.</summary>
    public static async Task<string> UploadAsync(JsonObject meta, string text, IProgress<double>? progress, CancellationToken ct = default)
    {
        byte[] data = Gzip(Encoding.UTF8.GetBytes(text));
        var share = await AskAsync(new JsonObject
        {
            ["op"] = "share", ["meta"] = meta, ["size"] = data.Length, ["sha256"] = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
        }, ct);
        string id = ServerClient.Str(share, "id");
        if (id.Length == 0) throw new ServerException(T("The server didn't take it"));
        for (int offset = 0; offset < data.Length || offset == 0;)
        {
            int n = Math.Min(Piece, data.Length - offset);
            bool eof = offset + n >= data.Length;
            await AskAsync(new JsonObject
            {
                ["op"] = "put", ["id"] = id, ["offset"] = offset, ["data"] = Convert.ToBase64String(data, offset, n), ["eof"] = eof,
            }, ct);
            offset += n;
            progress?.Report((double)offset / Math.Max(1, data.Length));
            if (eof) break;
        }
        return id;
    }

    /// <summary>A shared session's export file, as text.</summary>
    public static async Task<string> DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var all = new MemoryStream();
        long offset = 0;
        while (true)
        {
            var r = await AskAsync(new JsonObject { ["op"] = "get", ["id"] = id, ["offset"] = offset, ["length"] = 1 << 20 }, ct);
            var piece = Convert.FromBase64String(ServerClient.Str(r, "data"));
            all.Write(piece);
            offset += piece.Length;
            long size = (long?)r["size"] ?? offset;
            progress?.Report(size > 0 ? (double)offset / size : 1);
            if ((bool?)r["eof"] == true || piece.Length == 0) break;
        }
        return Encoding.UTF8.GetString(Gunzip(all.ToArray()));
    }

    public static Task EditAsync(string id, string title, string description, string? video, double? videoAt, CancellationToken ct = default) =>
        AskAsync(new JsonObject { ["op"] = "edit", ["id"] = id, ["title"] = title, ["description"] = description, ["video"] = video, ["videoAt"] = videoAt }, ct);

    public static Task DeleteAsync(string id, CancellationToken ct = default) => AskAsync(new JsonObject { ["op"] = "delete", ["id"] = id }, ct);

    private static byte[] Gzip(byte[] plain)
    {
        using var output = new MemoryStream();
        using (var z = new GZipStream(output, CompressionLevel.Optimal)) z.Write(plain);
        return output.ToArray();
    }

    private static byte[] Gunzip(byte[] packed)
    {
        using var input = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}

/// <summary>(since 4.17.0) A session shared on the server, as it lists it.</summary>
internal sealed class ServerSession
{
    public string Id { get; init; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Driver { get; init; } = "";
    /// <summary>Who shared it (the server's user) and from which PC.</summary>
    public string User { get; init; } = "";
    public string Client { get; init; } = "";
    public string Game { get; init; } = "";
    public string Track { get; init; } = "";
    public string Car { get; init; } = "";
    public string Class { get; init; } = "";
    public int Laps { get; init; }
    public float Best { get; init; }
    public DateTime Started { get; init; }
    public DateTime SharedAt { get; init; }
    public long Size { get; init; }
    public string? Video { get; set; }
    /// <summary>Shared by this user (theirs to edit and delete).</summary>
    public bool Mine { get; init; }

    public static ServerSession From(JsonObject m) => new()
    {
        Id = ServerClient.Str(m, "id"), Title = ServerClient.Str(m, "title"), Description = ServerClient.Str(m, "description"),
        Driver = ServerClient.Str(m, "driver"), User = ServerClient.Str(m, "user"), Client = ServerClient.Str(m, "client"),
        Game = ServerClient.Str(m, "game"), Track = ServerClient.Str(m, "track"), Car = ServerClient.Str(m, "car"), Class = ServerClient.Str(m, "class"),
        Laps = (int?)Num(m, "laps") ?? 0, Best = (float)(Num(m, "best") ?? 0),
        Started = Time(m, "started"), SharedAt = Time(m, "shared"), Size = (long)(Num(m, "size") ?? 0),
        Video = ServerClient.Str(m, "video") is { Length: > 0 } v ? v : null, Mine = m["mine"] is JsonValue b && b.TryGetValue<bool>(out bool mine) && mine,
    };

    private static double? Num(JsonObject m, string name) => m[name] is JsonValue v && v.TryGetValue<double>(out double d) ? d : null;

    /// <summary>A time: milliseconds since 1970 (UTC), local here.</summary>
    private static DateTime Time(JsonObject m, string name) => Num(m, name) is double ms && ms > 0 ? DateTime.UnixEpoch.AddMilliseconds(ms).ToLocalTime() : DateTime.MinValue;

    public static long Ms(DateTime t) => t == DateTime.MinValue ? 0 : (long)(t.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds;
}

/// <summary>
/// (since 4.17.0) Sessions going to Daisy's Server, one at a time: shared from the Share Session window, or by
/// themselves (<see cref="SessionShares.Auto"/>) once kept. A session whose race video is going to YouTube (or waiting
/// to) waits for it, so it's shared with the video's link. Everything here is on the UI thread.
/// </summary>
internal static class ShareQueue
{
    private static readonly Dictionary<string, double> going = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string Message, DateTime At)> failed = new(StringComparer.OrdinalIgnoreCase);
    private static bool busy;
    private const string BadgeId = "AnalysisShare";
    // (the title bar's blue while a share goes; green or red when it's done)
    private const string Blue = "#58A6FF";
    private static bool waiting, held;
    private static int turn;

    /// <summary>A share started, moved on, finished or failed.</summary>
    public static event Action? Changed;

    /// <summary>How far a session's share is (0 to 1), or null when it isn't going.</summary>
    public static double? Progress(string key) => going.TryGetValue(key, out double p) ? p : null;

    public static string? Failure(string key) => failed.TryGetValue(key, out var f) ? f.Message : null;

    /// <summary>A session's share as a word: Shared, Sharing n%, Waiting for YouTube, Failed, or "".</summary>
    public static string State(SessionsView.Item item) =>
        Progress(item.Key) is double p ? F("Sharing {0:0}%", p * 100)
        : SessionShares.Of(item.Key) != null ? T("Shared")
        : Failure(item.Key) != null ? T("Failed")
        : SessionShares.All.Auto && Wanted(item) && WaitsForYouTube(item.Key) ? T("Waiting for YouTube")
        : "";

    /// <summary>Shares a session now (or, shared already, sends its title, description and video link again). Null, or the problem.</summary>
    public static async Task<string?> Share(SessionsView.Item item, string title, string description, string driver)
    {
        if (going.ContainsKey(item.Key)) return null;
        going[item.Key] = 0;
        failed.Remove(item.Key);
        Changed?.Invoke();
        Badge(T("Preparing"), title, Blue);
        try
        {
            var link = RaceVideos.Of(item.Key);
            var started = SessionLog.StartedOf(item.File);
            double? videoAt = link?.YouTube != null ? link.VideoAt(started, 0) : null;
            if (SessionShares.Of(item.Key) is { } was)
            {
                await SessionServer.EditAsync(was.Id, title, description, link?.YouTube, videoAt);
                was.Title = title;
                was.Description = description;
                was.Video = link?.YouTube;
                SessionShares.All.Save();
                Logging.Log.Here.Info($"Analysis: {item.Key} on Daisy's Server changed");
                Done(T("Share Complete"), title, TitleBadges.Green);
                return null;
            }
            string text = await Task.Run(() => SessionsView.ExportText(item.File.FullName, started, 0, long.MaxValue, driver, title, link?.YouTube, videoAt));
            var meta = new JsonObject
            {
                ["title"] = title, ["description"] = description, ["driver"] = driver, ["game"] = item.Game, ["track"] = item.TrackName, ["car"] = item.CarName,
                ["class"] = item.ClassName, ["laps"] = item.Laps, ["best"] = Math.Round(item.Best, 3), ["started"] = ServerSession.Ms(started),
                ["video"] = link?.YouTube, ["videoAt"] = videoAt,
            };
            Badge(F("Uploading {0:0}%", 0), title, Blue);
            var progress = new Progress<double>(p =>
            {
                if (!going.ContainsKey(item.Key)) return;
                going[item.Key] = p;
                Badge(F("Uploading {0:0}%", p * 100), title, Blue);
                Changed?.Invoke();
            });
            string id = await SessionServer.UploadAsync(meta, text, progress);
            SessionShares.All.Shared[item.Key] = new SharedSession { Id = id, Title = title, Description = description, Shared = DateTime.Now, Video = link?.YouTube };
            SessionShares.All.Save();
            Logging.Log.Here.Info($"Analysis: {item.Key} shared on Daisy's Server ({id})");
            Done(T("Share Complete"), title, TitleBadges.Green);
            return null;
        }
        catch (Exception ex) when (ex is ServerException or IOException or OperationCanceledException or InvalidOperationException)
        {
            failed[item.Key] = (ex.Message.TrimEnd('.'), DateTime.Now);
            Logging.Log.Here.Warn($"Analysis: {item.Key} couldn't be shared: {ex.Message}");
            Done(T("Share Failed"), ex.Message.TrimEnd('.'), TitleBadges.Red);
            return ex.Message.TrimEnd('.');
        }
        finally
        {
            going.Remove(item.Key);
            Changed?.Invoke();
        }
    }

    /// <summary>(since 4.17.6) The title bar's bubble, Core's <see cref="TitleBadges"/>: "Analysis: " and what a share is
    /// doing (Preparing, Uploading n%), then Share Complete (green) or Share Failed (red) for 10 seconds; between shares,
    /// Waiting for YouTube while one to share by itself waits for its race video. Clicking it opens Analysis.</summary>
    private static void Badge(string? text, string? tip, string? color)
    {
        turn++;
        TitleBadges.Set(BadgeId, text == null ? null : F("Analysis: {0}", text), tip, color, "SimEffects/" + SimEffectsApplet.Analysis);
    }

    private static async void Done(string text, string tip, string? color)
    {
        Badge(text, tip, color);
        held = true;
        int mine = turn;
        await Task.Delay(10000);
        if (mine != turn) return;
        held = false;
        Idle();
    }

    /// <summary>No share going: Waiting for YouTube, or no bubble.</summary>
    private static void Idle() => Badge(waiting ? T("Waiting for YouTube") : null, waiting ? T("Shared once its video is on YouTube") : null, Blue);

    /// <summary>Takes a session off the server (the user's own). Null, or the problem.</summary>
    public static async Task<string?> Unshare(string id)
    {
        try { await SessionServer.DeleteAsync(id); }
        catch (ServerException ex) { return ex.Message.TrimEnd('.'); }
        foreach (var k in SessionShares.All.Shared.Where(s => s.Value.Id == id).Select(s => s.Key).ToList()) SessionShares.All.Shared.Remove(k);
        SessionShares.All.Save();
        Changed?.Invoke();
        return null;
    }

    /// <summary>A session that would be shared by itself: one of this PC's, finished, from when that was switched on.</summary>
    private static bool Wanted(SessionsView.Item i) => !i.Imported && !i.Incomplete && (i.Laps > 0 || i.Best > 0) && i.Started >= SessionShares.All.AutoSince;

    /// <summary>Its race video is going to YouTube, or waiting to (not failed): shared once it's there, with its link.</summary>
    private static bool WaitsForYouTube(string key) =>
        RaceVideos.All.YouTubeAuto && RaceVideos.Of(key) is { YouTube: null } link
        && (RaceUploads.Progress(key) != null || (link.Upload && RaceUploads.Failure(key) == null));

    /// <summary>The sessions listed: those to share by themselves, shared in turn (the newest last). Called when they're
    /// listed again, an upload to YouTube moves on, and each minute.</summary>
    public static async void Consider(IReadOnlyList<SessionsView.Item> items, Func<SessionsView.Item, (string Title, string Description)> describe, string driver)
    {
        bool wait = SessionShares.All.Auto && ServerLink.Configured
            && items.Any(i => Wanted(i) && SessionShares.Of(i.Key) == null && Failure(i.Key) == null && WaitsForYouTube(i.Key));
        if (wait != waiting)
        {
            waiting = wait;
            // (not over a share going, or one just done)
            if (going.Count == 0 && !held) Idle();
        }
        if (busy || !SessionShares.All.Auto || !ServerLink.Configured) return;
        busy = true;
        try
        {
            foreach (var i in items.Where(Wanted).OrderBy(i => i.Started).ToList())
            {
                if (SessionShares.Of(i.Key) != null || going.ContainsKey(i.Key)) continue;
                // (one that failed: again after 10 minutes)
                if (failed.TryGetValue(i.Key, out var f) && DateTime.Now - f.At < TimeSpan.FromMinutes(10)) continue;
                if (WaitsForYouTube(i.Key)) continue;
                // (the race's log still being written, or just ended: its video still being saved)
                i.File.Refresh();
                if (!i.File.Exists || DateTime.Now - i.File.LastWriteTime < TimeSpan.FromSeconds(60)) continue;
                var (title, description) = describe(i);
                if (await Share(i, title, description, driver) != null) break;
            }
        }
        finally { busy = false; }
    }
}
