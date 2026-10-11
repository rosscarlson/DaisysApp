using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DaisysApp.Applets.Performance;

/// <summary>A host the Network tests card pings: a name to show and an IP address or hostname.</summary>
public sealed class NetHost
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";

    public string Display => Name.Length > 0 ? Name : Address;

    /// <summary>Your router (the default gateway), Cloudflare and Google: the first run's hosts.</summary>
    public static List<NetHost> Defaults()
    {
        var list = new List<NetHost>();
        if (Gateway() is { } gw) list.Add(new NetHost { Name = T("Router"), Address = gw });
        list.Add(new NetHost { Name = "Cloudflare", Address = "1.1.1.1" });
        list.Add(new NetHost { Name = "Google", Address = "8.8.8.8" });
        return list;
    }

    /// <summary>The IPv4 default gateway of the first network adapter that's up and has one.</summary>
    public static string? Gateway()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString();
        }
        catch { return null; }
    }
}

/// <summary>
/// Pings every host once a second, all at once, while Daisy's App runs, and keeps the last 30 minutes. A host that
/// doesn't answer within a second (or can't be found) gets a missing point, which shows as a gap in its line.
/// </summary>
public sealed class PingMonitor : IDisposable
{
    public const int KeepSeconds = 1800;
    private const int TimeoutMs = 1000;

    private readonly PerformanceSettings settings;
    private readonly Timer timer;
    private readonly object gate = new();
    private readonly Dictionary<string, List<(DateTime T, double V)>> history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> errors = new(StringComparer.OrdinalIgnoreCase);
    private int busy;

    /// <summary>Raised on a background thread after each round of pings.</summary>
    public event Action? Updated;

    public PingMonitor(PerformanceSettings settings)
    {
        this.settings = settings;
        timer = new Timer(_ => _ = TickAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => timer.Change(1000, 1000);

    public IReadOnlyList<NetHost> Hosts => settings.NetHosts;

    private async Task TickAsync()
    {
        if (!settings.NetTestsOn)
        {
            lock (gate) { history.Clear(); errors.Clear(); } // so turning it back on starts a fresh graph
            return;
        }
        if (Interlocked.Exchange(ref busy, 1) == 1) return; // the last round is still waiting on a timeout
        try
        {
            var hosts = settings.NetHosts.Select(h => h.Address.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var now = DateTime.Now;
            var results = await Task.WhenAll(hosts.Select(PingAsync));
            lock (gate)
            {
                for (int i = 0; i < hosts.Count; i++)
                {
                    if (!history.TryGetValue(hosts[i], out var list)) history[hosts[i]] = list = new();
                    list.Add((now, results[i].Ms));
                    if (list.Count > KeepSeconds + 60) list.RemoveRange(0, list.Count - KeepSeconds);
                    if (results[i].Error is { } e) errors[hosts[i]] = e; else errors.Remove(hosts[i]);
                }
                foreach (var gone in history.Keys.Where(k => !hosts.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) history.Remove(gone);
            }
            Updated?.Invoke();
        }
        catch { /* never let a ping round take anything down */ }
        finally { Volatile.Write(ref busy, 0); }
    }

    private static async Task<(double Ms, string? Error)> PingAsync(string address)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeoutMs);
            return reply.Status == IPStatus.Success
                ? (Math.Max(reply.RoundtripTime, 0.5), null) // under a millisecond reads as 0
                : (double.NaN, reply.Status == IPStatus.TimedOut ? T("no answer") : reply.Status.ToString());
        }
        catch (PingException ex) when (ex.InnerException is SocketException)
        {
            return (double.NaN, T("not found"));
        }
        catch (Exception)
        {
            return (double.NaN, T("error"));
        }
    }

    /// <summary>A copy of a host's results since <paramref name="since"/>.</summary>
    public List<(DateTime T, double V)> Points(string address, DateTime since)
    {
        lock (gate)
            return history.TryGetValue(address.Trim(), out var list) ? list.Where(p => p.T >= since).ToList() : new();
    }

    /// <summary>Why the last ping failed (e.g. "no answer"), or null if it answered.</summary>
    public string? Error(string address)
    {
        lock (gate) return errors.GetValueOrDefault(address.Trim());
    }

    public void Dispose() => timer.Dispose();
}

/// <summary>
/// One speed test: download and upload in Mbit/s, the latency to the test server, and the data it used. A test that
/// failed has <see cref="Error"/> set and NaN for whatever it didn't measure.
/// </summary>
public sealed record SpeedResult(DateTime Time, double DownMbps, double UpMbps, double PingMs, double MegaBytes, string? Error = null, string? Server = null)
{
    public bool Failed => Error != null;
}

/// <summary>
/// A speed test server: one of speedtest.net's (Ookla's) servers, or Cloudflare's. They take the same three kinds of
/// request at different addresses: a tiny one for latency, a download of a given size, and an upload.
/// </summary>
public sealed record SpeedServer(string Name, string BaseUrl, bool Ookla)
{
    public static readonly SpeedServer Cloudflare = new("Cloudflare", "https://speed.cloudflare.com", false);

    private static string NoCache() => Guid.NewGuid().ToString("N");
    public string LatencyUrl => Ookla ? $"{BaseUrl}/hello?nocache={NoCache()}" : $"{BaseUrl}/__down?bytes=0";
    public string DownloadUrl(int bytes) => Ookla ? $"{BaseUrl}/download?nocache={NoCache()}&size={bytes}" : $"{BaseUrl}/__down?bytes={bytes}";
    public string UploadUrl => Ookla ? $"{BaseUrl}/upload?nocache={NoCache()}" : $"{BaseUrl}/__up";
}

/// <summary>
/// Internet speed tests: a few quick requests for latency, then several downloads at once for the set time, then
/// several uploads at once for the same time. The server is the nearest-answering of speedtest.net's (picked from its
/// public list, again every few hours), with Cloudflare's as the fallback; a server that refuses (e.g. "too many
/// requests") is skipped for the next one. Each phase's clock starts when data starts moving, so a slow start doesn't
/// eat into a short test; if nothing arrives (or it stops arriving) for 10 seconds the test fails. Runs on a schedule
/// while Daisy's App runs (every 10 minutes by default) and keeps 30 days of results, failures included, in
/// speedtest.csv in the performance log folder.
/// </summary>
public sealed class SpeedTester : IDisposable
{
    public const int MinSeconds = 1, MaxSeconds = 30;
    /// <summary>How long to wait for data (or an answer) before the test fails.</summary>
    public const int NoDataSeconds = 10;
    /// <summary>Use only this server (for tests).</summary>
    internal SpeedServer? ForcedServer { get; init; }
    private const string ServerList = "https://www.speedtest.net/api/js/servers?engine=js&https_functional=true&limit=10";
    private const int ChunkBytes = 25_000_000;       // per request (Cloudflare allows up to 50 MB, speedtest.net more)
    private const int Streams = 6;
    private SpeedServer server = SpeedServer.Cloudflare;   // the one the current test uses
    private List<SpeedServer>? candidates;                 // nearest first, Cloudflare last
    private DateTime candidatesAt;
    private static readonly byte[] Payload = MakePayload();

    private readonly PerformanceSettings settings;
    private readonly Timer timer;
    private readonly HttpClient http;
    private readonly object gate = new();
    private readonly List<SpeedResult> results = new();
    private CancellationTokenSource? cts;
    private DateTime startedAt = DateTime.Now;

    /// <summary>Raised (on any thread) when a test starts, makes progress, finishes or fails.</summary>
    public event Action? Changed;

    public SpeedTester(PerformanceSettings settings)
    {
        this.settings = settings;
        var handler = new SocketsHttpHandler { MaxConnectionsPerServer = Streams * 2, PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
        http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        // speedtest.net's servers turn away requests that don't look like they come from a browser
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DaisysApp");
        Load();
        timer = new Timer(_ => CheckDue(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static string FilePath => Path.Combine(PerfLog.Folder, "speedtest.csv");

    public bool Running { get; private set; }
    /// <summary>"Latency", "Download" or "Upload" while running.</summary>
    public string Phase { get; private set; } = "";
    /// <summary>The speed so far in the current phase, Mbit/s.</summary>
    public double LiveMbps { get; private set; }
    /// <summary>Why the last test didn't finish (failed or cancelled this session), or null.</summary>
    public string? LastError { get; private set; }

    public IReadOnlyList<SpeedResult> Results
    {
        get { lock (gate) return results.ToList(); }
    }

    /// <summary>When the next scheduled test runs, or null if they're off.</summary>
    public DateTime? NextDue
    {
        get
        {
            if (!settings.SpeedTestOn || !settings.SpeedTestEnabled) return null;
            var last = Results.LastOrDefault()?.Time;
            var due = (last ?? DateTime.MinValue).AddMinutes(settings.SpeedTestMinutes);
            var earliest = startedAt.AddSeconds(45); // not right as Windows starts up
            return due < earliest ? earliest : due;
        }
    }

    public void Start()
    {
        startedAt = DateTime.Now;
        timer.Change(15000, 15000);
    }

    private void CheckDue()
    {
        if (!Running && NextDue is DateTime due && DateTime.Now >= due) _ = RunAsync();
    }

    /// <summary>Starts a test now (does nothing if one is running).</summary>
    public Task RunNowAsync() => RunAsync();

    public void Cancel() => cts?.Cancel();

    private async Task RunAsync()
    {
        lock (gate)
        {
            if (Running || !settings.SpeedTestOn) return;
            Running = true;
        }
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        LastError = null;
        long total = 0;
        double ping = double.NaN, down = double.NaN, up = double.NaN;
        try
        {
            Report("Latency", 0);
            ping = await PickServerAndLatencyAsync(ct);
            var seconds = TimeSpan.FromSeconds(Math.Clamp(settings.SpeedTestSeconds, MinSeconds, MaxSeconds));
            (down, long downBytes) = await MeasureAsync("Download", DownloadLoopAsync, seconds, ct);
            total += downBytes;
            (up, long upBytes) = await MeasureAsync("Upload", UploadLoopAsync, seconds, ct);
            total += upBytes;
            Record(new SpeedResult(DateTime.Now, down, up, ping, total / 1e6, null, server.Name));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LastError = T("Cancelled."); // not a failure: nothing is recorded
        }
        catch (Exception ex)
        {
            LastError = ex switch
            {
                SpeedTestException s => s.Message,
                HttpRequestException { StatusCode: HttpStatusCode code } => F("The test server said {0} ({1}).", (int)code, code),
                HttpRequestException { InnerException: SocketException } => T("Couldn't reach the test server (no internet connection?)."),
                _ => T("Couldn't reach the test server: ") + ex.Message,
            };
            candidates = null; // pick again next time, in case it was this server
            Record(new SpeedResult(DateTime.Now, down, up, ping, total / 1e6, LastError, server.Name));
        }
        finally
        {
            Running = false;
            Phase = "";
            LiveMbps = 0;
            cts.Dispose();
            cts = null;
            Changed?.Invoke();
        }
    }

    private void Record(SpeedResult r)
    {
        Logging.Log.Here.Info(r.Error == null
            ? $"Speed test ({r.Server}): {r.DownMbps:0.0} Mbit/s down, {r.UpMbps:0.0} up, ping {r.PingMs:0} ms, {r.MegaBytes:0} MB"
            : $"Speed test ({r.Server}) failed: {r.Error}");
        lock (gate) results.Add(r);
        Append(r);
    }

    private void Report(string phase, double mbps)
    {
        Phase = phase;
        LiveMbps = mbps;
        Changed?.Invoke();
    }

    /// <summary>A test failure with a message to show as it is.</summary>
    private sealed class SpeedTestException(string message) : Exception(message);

    /// <summary>
    /// Tries the servers in order (nearest first) until one answers, measures its latency and makes it this test's
    /// server. The last server's error is the test's if none answers.
    /// </summary>
    private async Task<double> PickServerAndLatencyAsync(CancellationToken ct)
    {
        var list = ForcedServer != null ? new List<SpeedServer> { ForcedServer } : await CandidatesAsync(ct);
        Exception? last = null;
        foreach (var s in list)
        {
            try
            {
                server = s;
                return await LatencyAsync(s, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex; // refused, busy or not answering: the next one
            }
        }
        throw last ?? new SpeedTestException(T("No test server to use."));
    }

    /// <summary>
    /// speedtest.net's nearest servers (its list is ordered by distance), sorted by how fast each answers one request,
    /// then Cloudflare's. Kept for a few hours.
    /// </summary>
    private async Task<List<SpeedServer>> CandidatesAsync(CancellationToken ct)
    {
        if (candidates != null && DateTime.Now - candidatesAt < TimeSpan.FromHours(6)) return candidates;
        var found = new List<SpeedServer>();
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(NoDataSeconds));
            using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(ServerList, wait.Token));
            var servers = doc.RootElement.EnumerateArray()
                .Select(e => (Host: e.GetProperty("host").GetString(), Sponsor: Str(e, "sponsor"), Place: Str(e, "name")))
                .Where(e => !string.IsNullOrEmpty(e.Host))
                .Select(e => new SpeedServer(e.Sponsor.Length > 0 ? $"{e.Sponsor} ({e.Place})" : e.Place, "https://" + e.Host, true))
                .ToList();
            var timed = await Task.WhenAll(servers.Select(async s =>
            {
                try
                {
                    using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    one.CancelAfter(TimeSpan.FromSeconds(3));
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using var resp = await http.GetAsync(s.LatencyUrl, one.Token);
                    return resp.IsSuccessStatusCode ? (s, sw.Elapsed.TotalMilliseconds) : (s, double.MaxValue);
                }
                catch { return (s, double.MaxValue); }
            }));
            found.AddRange(timed.Where(t => t.Item2 < double.MaxValue).OrderBy(t => t.Item2).Take(4).Select(t => t.s));
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* the list is unavailable: Cloudflare only */ }
        found.Add(SpeedServer.Cloudflare);
        candidates = found;
        candidatesAt = DateTime.Now;
        return found;

        static string Str(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    /// <summary>Median time of a few tiny requests (one round trip each on a warm connection).</summary>
    private async Task<double> LatencyAsync(SpeedServer s, CancellationToken ct)
    {
        var times = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(NoDataSeconds));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var resp = await http.GetAsync(s.LatencyUrl, wait.Token);
                resp.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SpeedTestException(F("No answer from the test server in {0} seconds.", NoDataSeconds));
            }
            if (i > 0) times.Add(sw.Elapsed.TotalMilliseconds); // the first one also opens the connection
        }
        times.Sort();
        return times[times.Count / 2];
    }

    /// <summary>
    /// Runs <paramref name="loop"/> on several connections at once until <paramref name="length"/> after the first data
    /// arrives, and returns the speed in Mbit/s. The first fifth of that time (at most a second) is left out of the speed,
    /// while TCP gets up to speed. Fails if no data comes for <see cref="NoDataSeconds"/>, at the start or part way.
    /// </summary>
    private async Task<(double Mbps, long Bytes)> MeasureAsync(string phase, Func<Counter, CancellationToken, Task> loop, TimeSpan length, CancellationToken ct)
    {
        var counter = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, Streams).Select(_ => loop(counter, stop.Token)).ToList();
        double ramp = Math.Min(1, length.TotalSeconds / 5);
        double start = -1, warmTime = -1, lastDataTime = 0, end;
        long warmBytes = 0, lastBytes = 0;
        Report(phase, 0);
        try
        {
            while (true)
            {
                double t = sw.Elapsed.TotalSeconds;
                long bytes = counter.Bytes;
                if (bytes > lastBytes) { lastBytes = bytes; lastDataTime = t; }
                if (start < 0 && bytes > 0) start = t;

                // done once the time is up, as long as data kept moving after the ramp-up: an upload's first writes only
                // fill Windows' send buffer, so a server that never takes any would otherwise "pass" at 0 Mbit/s (it
                // waits instead, and fails below once nothing has moved for NoDataSeconds)
                if (start >= 0 && t >= start + length.TotalSeconds && warmTime >= 0 && lastBytes > warmBytes) { end = t; break; }
                if (tasks.All(x => x.IsCompleted))
                {
                    if (bytes == 0 && tasks.FirstOrDefault(x => x.IsFaulted)?.Exception?.InnerException is { } failed) throw failed;
                    throw new SpeedTestException(phase == "Download" ? T("The test server stopped the download.") : T("The test server stopped the upload."));
                }
                if (t - lastDataTime >= NoDataSeconds)
                    throw new SpeedTestException(start < 0
                        ? (phase == "Download" ? F("No download data from the test server in {0} seconds.", NoDataSeconds) : F("No upload data from the test server in {0} seconds.", NoDataSeconds))
                        : (phase == "Download" ? F("The download stalled: no data for {0} seconds.", NoDataSeconds) : F("The upload stalled: no data for {0} seconds.", NoDataSeconds)));

                if (start >= 0 && warmTime < 0 && t >= start + ramp) { warmTime = t; warmBytes = bytes; }
                if (warmTime >= 0 && t > warmTime + 0.05) Report(phase, (bytes - warmBytes) * 8 / 1e6 / (t - warmTime));

                double left = start >= 0 ? start + length.TotalSeconds - t : 0;
                await Task.Delay(TimeSpan.FromSeconds(left > 0 ? Math.Clamp(left, 0.01, 0.1) : 0.1), ct);
            }
        }
        finally
        {
            stop.Cancel();
            try { await Task.WhenAll(tasks); }
            catch { /* the connections cut off at the end, or the failure already being reported */ }
        }
        // lastBytes is the count when the clock ran out, not what trickled in while the connections closed
        if (warmTime < 0 || end <= warmTime) { warmTime = start; warmBytes = 0; }
        double mbps = end > warmTime ? (lastBytes - warmBytes) * 8 / 1e6 / (end - warmTime) : 0;
        return (mbps, counter.Bytes);
    }

    private async Task DownloadLoopAsync(Counter counter, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (!ct.IsCancellationRequested)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, server.DownloadUrl(ChunkBytes))
            {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact, // one TCP connection per stream
            };
            using var resp = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            int n;
            while ((n = await stream.ReadAsync(buffer, ct)) > 0) counter.Add(n);
        }
    }

    private async Task UploadLoopAsync(Counter counter, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, server.UploadUrl)
            {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new CountingContent(counter),
            };
            using var resp = await http.SendAsync(request, ct);
            resp.EnsureSuccessStatusCode();
        }
    }

    private sealed class Counter
    {
        private long bytes;
        public long Bytes => Interlocked.Read(ref bytes);
        public void Add(long n) => Interlocked.Add(ref bytes, n);
    }

    /// <summary>An upload body that counts what's been sent as it goes.</summary>
    private sealed class CountingContent(Counter counter) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            for (int sent = 0; sent < ChunkBytes; sent += Payload.Length)
            {
                int n = Math.Min(Payload.Length, ChunkBytes - sent);
                await stream.WriteAsync(Payload.AsMemory(0, n), ct);
                counter.Add(n);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = ChunkBytes;
            return true;
        }
    }

    private static byte[] MakePayload()
    {
        var b = new byte[65536];
        Random.Shared.NextBytes(b); // not compressible
        return b;
    }

    // ---------------------------------------------------------------- results file

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var cutoff = DateTime.Now.AddDays(-30);
            // 0.7.2's files have no server column (and 0.7.1's no error column either); they're rewritten in this format
            bool hasServer = File.ReadLines(FilePath).FirstOrDefault() == Header;
            int errorAt = hasServer ? 6 : 5;
            foreach (var line in File.ReadLines(FilePath).Skip(1))
            {
                var p = line.Split(',');
                if (p.Length < 5 || !DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || t < cutoff) continue;
                double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
                string? srv = hasServer && p.Length > 5 && p[5].Length > 0 ? p[5].Replace(';', ',') : null;
                string? error = p.Length > errorAt && p[errorAt].Length > 0 ? string.Join(",", p.Skip(errorAt)) : null; // may hold commas
                results.Add(new SpeedResult(t, D(p[1]), D(p[2]), D(p[3]), D(p[4]), error, srv));
            }
            if (!hasServer || (results.Count > 0 && File.ReadLines(FilePath).Count() - 1 > results.Count + 200)) Rewrite(); // drop old lines now and then
        }
        catch { /* unreadable: start fresh */ }
    }

    private static string Line(SpeedResult r) => string.Format(CultureInfo.InvariantCulture,
        "{0:yyyy-MM-dd HH:mm:ss},{1:0.0},{2:0.0},{3:0.0},{4:0.0},{5},{6}", r.Time, r.DownMbps, r.UpMbps, r.PingMs, r.MegaBytes,
        r.Server?.Replace(',', ';') ?? "", r.Error?.ReplaceLineEndings(" ") ?? "");

    private const string Header = "time,down_mbps,up_mbps,ping_ms,data_mb,server,error";

    private static void Append(SpeedResult r)
    {
        try
        {
            Directory.CreateDirectory(PerfLog.Folder);
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Header + Environment.NewLine);
            File.AppendAllText(FilePath, Line(r) + Environment.NewLine);
        }
        catch { }
    }

    private void Rewrite()
    {
        try { File.WriteAllLines(FilePath, new[] { Header }.Concat(results.Select(Line))); }
        catch { }
    }

    public void Dispose()
    {
        timer.Dispose();
        cts?.Cancel();
        http.Dispose();
    }
}
