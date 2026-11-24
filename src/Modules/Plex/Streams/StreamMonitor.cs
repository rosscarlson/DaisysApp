using System.Text.Json;
using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.Plex.Streams;

/// <summary>
/// Watches every video stream for stalls (since 4.21.0): every 2 seconds the server's sessions (each player's state,
/// position, the file's bit rate, a transcode's speed), what Plex sent each player each second (its
/// /statistics/bandwidth, the dashboard's) and a ping to each player. When a player buffers, the stall is kept with what
/// was seen in it and the 30 seconds before, and why it most likely happened: nothing sent with the player's pings slow
/// or lost (its network, usually Wi-Fi), nothing sent with pings fine (the player stopped asking), less sent than the
/// file needs, a transcode slower than real time, or data still coming (the player itself). Runs while Streams shows,
/// or all the time with Watch in background (every 15 s while nothing plays). On the UI thread: no locks.
/// </summary>
public sealed class StreamMonitor
{
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);

    private readonly PlexService service;
    private readonly DispatcherTimer timer = new();
    private readonly Dictionary<string, StreamTrack> tracks = new();
    /// <summary>Per device (Plex's id): the last second of bandwidth already taken.</summary>
    private readonly Dictionary<long, long> bandwidthAt = new();
    private bool asking, visible;

    public IReadOnlyCollection<StreamTrack> Tracks => tracks.Values;
    /// <summary>Stalls since the app started, newest first (200 at most).</summary>
    public List<Stall> Stalls { get; } = new();
    public string? Error { get; private set; }

    /// <summary>Something new (on the UI thread).</summary>
    public event Action? Updated;

    public StreamMonitor(PlexService service)
    {
        this.service = service;
        timer.Tick += (_, _) => _ = Poll();
        service.Changed += () => timer.Dispatcher.BeginInvoke(Recheck);
    }

    /// <summary>The Streams tab shows (it runs while it does).</summary>
    public bool Visible
    {
        get => visible;
        set { visible = value; Recheck(); if (value) _ = Poll(); }
    }

    public void Recheck()
    {
        bool run = service.Client != null && service.Settings.Configured && (visible || service.Settings.WatchStreams);
        if (run && !timer.IsEnabled) { timer.Interval = TimeSpan.FromSeconds(2); timer.Start(); }
        else if (!run && timer.IsEnabled) timer.Stop();
    }

    public void Stop() => timer.Stop();

    private async Task Poll()
    {
        if (asking || service.Client is not { } c) return;
        asking = true;
        try
        {
            var now = DateTime.Now;
            var mc = await c.GetAsync("/status/sessions");
            var seen = new HashSet<string>();
            foreach (var m in PlexClient.List(mc, "Metadata"))
            {
                if (PlexClient.Str(m, "type") is not ("movie" or "episode" or "clip")) continue;
                string key = PlexClient.Str(PlexClient.Obj(m, "Session"), "id") ?? PlexClient.Str(m, "sessionKey") ?? "";
                if (key.Length == 0) continue;
                seen.Add(key);
                if (!tracks.TryGetValue(key, out var t)) tracks[key] = t = new StreamTrack(key, now);
                t.Read(m, PlexClient.Obj(m, "Player"), now);
            }
            foreach (var t in tracks.Values)
                if (!seen.Contains(t.Key) && t.Live) t.Ended(now);
            foreach (var gone in tracks.Values.Where(t => !t.Live && now - t.LastSeen > Keep).Select(t => t.Key).ToList()) tracks.Remove(gone);

            var live = tracks.Values.Where(t => t.Live).ToList();
            if (live.Count > 0)
            {
                var pings = live.Where(t => t.Address.Length > 0).Select(async t => (t, await Pings.SendAsync(t.Address, 1000, 1500))).ToList();
                await Bandwidth(c, live);
                foreach (var (t, r) in await Task.WhenAll(pings)) t.AddPing(now, r.Answered ? r.Ms : double.NaN);
                foreach (var t in live) t.Check(now, this);
            }
            timer.Interval = TimeSpan.FromSeconds(live.Count > 0 || visible ? 2 : 15);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            timer.Interval = TimeSpan.FromSeconds(15);
        }
        finally { asking = false; }
        Updated?.Invoke();
    }

    /// <summary>What Plex sent each playing device, a point a second (Plex keeps about the last 2 minutes).</summary>
    private async Task Bandwidth(PlexClient c, List<StreamTrack> live)
    {
        var mc = await c.GetAsync("/statistics/bandwidth?timespan=6");
        var devices = PlexClient.List(mc, "Device").Select(d => (Id: PlexClient.Num(d, "id"), Client: PlexClient.Str(d, "clientIdentifier") ?? "")).ToList();
        var bytes = PlexClient.List(mc, "StatisticsBandwidth")
            .GroupBy(b => (Device: PlexClient.Num(b, "deviceID"), At: PlexClient.Num(b, "at")))
            .ToDictionary(g => g.Key, g => g.Sum(b => PlexClient.Num(b, "bytes")));
        long newest = bytes.Count == 0 ? 0 : bytes.Keys.Max(k => k.At);
        foreach (var t in live)
        {
            long id = devices.FirstOrDefault(d => d.Client.Length > 0 && d.Client == t.ClientId).Id;
            if (id == 0 || newest == 0) continue;
            long last = bandwidthAt.TryGetValue(id, out var l) ? l : newest - 60;
            // seconds with nothing sent aren't listed: up to the newest second Plex has for anyone, they're 0
            for (long s = Math.Max(last + 1, newest - 180); s <= newest; s++)
                t.AddSent(DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime, bytes.TryGetValue((id, s), out var n) ? n * 8 / 1e6 : 0);
            bandwidthAt[id] = newest;
        }
    }

    internal void Add(Stall s)
    {
        Stalls.Insert(0, s);
        if (Stalls.Count > 200) Stalls.RemoveAt(Stalls.Count - 1);
    }
}

/// <summary>One video stream and the last 10 minutes of what was seen of it.</summary>
public sealed class StreamTrack(string key, DateTime started)
{
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);

    public string Key { get; } = key;
    public DateTime Started { get; } = started;
    public DateTime LastSeen { get; private set; } = started;
    public bool Live { get; private set; } = true;
    public string Title { get; private set; } = "";
    public string User { get; private set; } = "";
    public string Player { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string ClientId { get; private set; } = "";
    public bool Local { get; private set; }
    public string State { get; private set; } = "";
    public long OffsetMs { get; private set; }
    /// <summary>The file's average bit rate, kbps.</summary>
    public long FileKbps { get; private set; }
    public string Decision { get; private set; } = "";
    /// <summary>A transcode's speed (1 = real time), 0 = not transcoding.</summary>
    public double Speed { get; private set; }

    /// <summary>Mbps sent, a point a second.</summary>
    public List<(DateTime T, double V)> Sent { get; } = new();
    /// <summary>Ping ms, NaN = no answer.</summary>
    public List<(DateTime T, double V)> Ping { get; } = new();
    /// <summary>The state at each poll.</summary>
    public List<(DateTime T, string V)> States { get; } = new();
    public Stall? Open { get; private set; }
    public int StallCount { get; private set; }
    public TimeSpan Stalled { get; private set; }
    private DateTime seekedAt;

    internal void Read(JsonElement m, JsonElement player, DateTime now)
    {
        string type = PlexClient.Str(m, "type") ?? "";
        Title = type == "episode"
            ? $"{PlexClient.Str(m, "grandparentTitle")} S{PlexClient.Num(m, "parentIndex"):00}E{PlexClient.Num(m, "index"):00}"
            : PlexClient.Str(m, "title") + (PlexClient.Str(m, "year") is { } y ? $" ({y})" : "");
        User = PlexClient.Str(PlexClient.Obj(m, "User"), "title") ?? "";
        Player = $"{PlexClient.Str(player, "title")} ({PlexClient.Str(player, "product")})";
        Address = PlexClient.Str(player, "address") ?? "";
        ClientId = PlexClient.Str(player, "machineIdentifier") ?? "";
        Local = PlexClient.Flag(player, "local");
        var media = PlexClient.List(m, "Media").FirstOrDefault(x => PlexClient.Flag(x, "selected"));
        if (media.ValueKind != JsonValueKind.Object) media = PlexClient.List(m, "Media").FirstOrDefault();
        FileKbps = PlexClient.Num(media, "bitrate");
        var ts = PlexClient.Obj(m, "TranscodeSession");
        if (ts.ValueKind == JsonValueKind.Object)
        {
            Decision = PlexClient.Str(ts, "videoDecision") == "transcode" ? T("Transcode") : T("Direct Stream");
            Speed = double.TryParse(PlexClient.Str(ts, "speed"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sp) ? sp : 0;
            if (PlexClient.Flag(ts, "complete") || PlexClient.Flag(ts, "throttled")) Speed = 0; // (ahead of the player: its speed doesn't matter)
        }
        else { Decision = T("Direct Play"); Speed = 0; }

        long offset = PlexClient.Num(m, "viewOffset");
        if (States.Count > 0 && Math.Abs((offset - OffsetMs) / 1000.0 - (now - LastSeen).TotalSeconds) > 15) seekedAt = now;
        OffsetMs = offset;
        State = PlexClient.Str(player, "state") ?? "";
        LastSeen = now;
        Live = true;
        States.Add((now, State));
        Trim(States, now);
    }

    internal void AddSent(DateTime at, double mbps)
    {
        Sent.Add((at, mbps));
        Trim(Sent, at);
    }

    internal void AddPing(DateTime at, double ms)
    {
        Ping.Add((at, ms));
        Trim(Ping, at);
    }

    private static void Trim<TItem>(List<(DateTime T, TItem V)> list, DateTime now)
    {
        int n = list.FindIndex(p => now - p.T <= Keep);
        if (n > 0) list.RemoveRange(0, n);
    }

    /// <summary>A stall starts when the player buffers and ends when it plays (or pauses, or stops).</summary>
    internal void Check(DateTime now, StreamMonitor monitor)
    {
        if (State == "buffering" && Open == null)
        {
            string? kind = now - Started < TimeSpan.FromSeconds(10) ? T("Starting")
                : now - seekedAt < TimeSpan.FromSeconds(10) ? T("After a seek") : null;
            Open = new Stall(this, now, kind);
            if (kind == null) monitor.Add(Open);
        }
        else if (State != "buffering" && Open != null) Close(now);
        Open?.Judge(now);
    }

    internal void Ended(DateTime now)
    {
        Live = false;
        if (Open != null) Close(now);
        States.Add((now, "stopped"));
    }

    private void Close(DateTime now)
    {
        var s = Open!;
        Open = null;
        s.End = now;
        s.Judge(now);
        if (s.Kind != null) return; // (starting, a seek: not counted)
        StallCount++;
        Stalled += now - s.Start;
        Log.Here.Info($"Plex stall: \"{Title}\" on {Player} ({Address}), {(now - s.Start).TotalSeconds:0} s: {s.Cause}");
    }

    /// <summary>The most a second's data reached in the last 10 minutes (what the path to the player can carry, at least).</summary>
    public double PeakMbps => Sent.Count == 0 ? 0 : Sent.Max(p => p.V);
}

/// <summary>A time a player buffered while playing, and its likely cause.</summary>
public sealed class Stall(StreamTrack stream, DateTime start, string? kind)
{
    private static readonly TimeSpan Before = TimeSpan.FromSeconds(30);

    public StreamTrack Stream { get; } = stream;
    public DateTime Start { get; } = start;
    public DateTime? End { get; set; }
    /// <summary>Not a stall to count: "Starting", "After a seek"; null = a stall.</summary>
    public string? Kind { get; } = kind;
    public string Cause { get; private set; } = "";
    /// <summary>Mbps sent during it and in the 30 s before.</summary>
    public double SentDuring { get; private set; }
    public double SentBefore { get; private set; }
    public double PingMax { get; private set; }
    public int PingsLost { get; private set; }
    public int Pings { get; private set; }

    public string When => Start.ToString("HH:mm:ss");
    public double Seconds => ((End ?? DateTime.Now) - Start).TotalSeconds;
    public string Length => $"{Seconds:0} s" + (End == null ? "…" : "");
    public string Title => Stream.Title;
    public string Player => Stream.Player;
    public string Data => F("{0:0.0} / {1:0.0} Mbps", SentBefore, SentDuring);
    public string PingText => Pings == 0 ? "—" : PingsLost == Pings ? T("no answer") : PingsLost > 0 ? F("max {0:0} ms, {1} lost", PingMax, PingsLost) : F("max {0:0} ms", PingMax);

    /// <summary>Weighs what was seen from 30 s before it to now.</summary>
    internal void Judge(DateTime now)
    {
        var s = Stream;
        DateTime from = Start - Before, end = End ?? now;
        double Avg(DateTime a, DateTime b) => s.Sent.Where(p => p.T >= a && p.T < b).Select(p => p.V).DefaultIfEmpty(0).Average();
        SentDuring = Avg(Start, end.AddSeconds(1));
        SentBefore = Avg(from, Start);
        var pings = s.Ping.Where(p => p.T >= from && p.T <= end).ToList();
        Pings = pings.Count;
        PingsLost = pings.Count(p => double.IsNaN(p.V));
        PingMax = pings.Where(p => !double.IsNaN(p.V)).Select(p => p.V).DefaultIfEmpty(0).Max();
        bool answers = s.Ping.Any(p => !double.IsNaN(p.V));
        bool pingBad = answers && (PingsLost > 0 || PingMax >= (s.Local ? 150 : 400));
        double needs = s.FileKbps / 1000.0;

        Cause = Kind ?? (s.Speed is > 0 and < 1
            ? F("Plex transcoded slower than real time ({0:0.0}×): the server", s.Speed)
            : SentDuring < 0.3
                ? pingBad ? T("Nothing reached the player and its pings were slow or lost: its network (Wi-Fi?)")
                  : answers ? T("Nothing sent while the player answered pings: the player stopped asking (its app)")
                  : T("Nothing sent: the player's network or app (it doesn't answer pings)")
            : SentDuring < needs
                ? pingBad ? F("Data came slower than the file needs ({0:0.0} Mbps) and pings were slow: the network (Wi-Fi?)", needs)
                  : s.Local ? F("Data came slower than the file needs ({0:0.0} Mbps): the network or the server's disk", needs)
                  : F("Data came slower than the file needs ({0:0.0} Mbps): the internet between (the server's upload or theirs)", needs)
            : T("Data kept coming: the player couldn't keep up (its app or decoder)"));
    }
}
