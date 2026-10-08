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
        if (Gateway() is { } gw) list.Add(new NetHost { Name = "Router", Address = gw });
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
                : (double.NaN, reply.Status == IPStatus.TimedOut ? "no answer" : reply.Status.ToString());
        }
        catch (PingException ex) when (ex.InnerException is SocketException)
        {
            return (double.NaN, "not found");
        }
        catch (Exception)
        {
            return (double.NaN, "error");
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

/// <summary>One speed test: download and upload in Mbit/s, the latency to the test server, and the data it used.</summary>
public sealed record SpeedResult(DateTime Time, double DownMbps, double UpMbps, double PingMs, double MegaBytes);

/// <summary>
/// Internet speed tests against Cloudflare's speed test servers (the ones speed.cloudflare.com uses): a few quick
/// requests for latency, then several downloads at once for the set time, then several uploads at once for the same
/// time. The first second of each is left out of the speed (TCP getting up to speed). Runs on a schedule while Daisy's
/// App runs (every 10 minutes by default) and keeps 30 days of results in speedtest.csv in the performance log folder.
/// </summary>
public sealed class SpeedTester : IDisposable
{
    private const string Server = "https://speed.cloudflare.com";
    private const int ChunkBytes = 25_000_000;       // per request (the server allows up to 50 MB)
    private const int Streams = 6;
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
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DaisysApp");
        Load();
        timer = new Timer(_ => CheckDue(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static string FilePath => Path.Combine(PerfLog.Folder, "speedtest.csv");

    public bool Running { get; private set; }
    /// <summary>"Latency", "Download" or "Upload" while running.</summary>
    public string Phase { get; private set; } = "";
    /// <summary>The speed so far in the current phase, Mbit/s.</summary>
    public double LiveMbps { get; private set; }
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
            if (!settings.SpeedTestEnabled) return null;
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
            if (Running) return;
            Running = true;
        }
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        LastError = null;
        long total = 0;
        try
        {
            Report("Latency", 0);
            double ping = await LatencyAsync(ct);
            var seconds = TimeSpan.FromSeconds(Math.Clamp(settings.SpeedTestSeconds, 3, 30));
            var (down, downBytes) = await MeasureAsync("Download", DownloadLoopAsync, seconds, ct);
            total += downBytes;
            var (up, upBytes) = await MeasureAsync("Upload", UploadLoopAsync, seconds, ct);
            total += upBytes;
            var r = new SpeedResult(DateTime.Now, down, up, ping, total / 1e6);
            lock (gate) results.Add(r);
            Append(r);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LastError = "Cancelled.";
        }
        catch (Exception ex)
        {
            LastError = ex is HttpRequestException h && h.StatusCode is HttpStatusCode code
                ? $"The test server said {(int)code} ({code})."
                : "Couldn't reach the test server: " + ex.Message;
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

    private void Report(string phase, double mbps)
    {
        Phase = phase;
        LiveMbps = mbps;
        Changed?.Invoke();
    }

    /// <summary>Median time of a few tiny requests (one round trip each on a warm connection).</summary>
    private async Task<double> LatencyAsync(CancellationToken ct)
    {
        var times = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var resp = await http.GetAsync($"{Server}/__down?bytes=0", ct);
            resp.EnsureSuccessStatusCode();
            if (i > 0) times.Add(sw.Elapsed.TotalMilliseconds); // the first one also opens the connection
        }
        times.Sort();
        return times[times.Count / 2];
    }

    /// <summary>Runs <paramref name="loop"/> on several connections at once for <paramref name="length"/>; Mbit/s after the first second.</summary>
    private async Task<(double Mbps, long Bytes)> MeasureAsync(string phase, Func<Counter, CancellationToken, Task> loop, TimeSpan length, CancellationToken ct)
    {
        var counter = new Counter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, Streams).Select(_ => loop(counter, stop.Token)).ToList();
        long warmBytes = -1;
        double warmTime = 0;
        Report(phase, 0);
        while (sw.Elapsed < length)
        {
            await Task.Delay(250, ct);
            if (tasks.All(t => t.IsCompleted)) break;
            double t = sw.Elapsed.TotalSeconds;
            if (warmBytes < 0 && t >= 1) { warmBytes = counter.Bytes; warmTime = t; }
            if (warmBytes >= 0 && t > warmTime) Report(phase, (counter.Bytes - warmBytes) * 8 / 1e6 / (t - warmTime));
        }
        double end = sw.Elapsed.TotalSeconds;
        long bytes = counter.Bytes;
        stop.Cancel();
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
        catch (Exception) when (bytes > 0) { /* connections cut off at the end */ }
        ct.ThrowIfCancellationRequested();
        if (tasks.FirstOrDefault(t => t.IsFaulted && bytes == 0)?.Exception?.InnerException is { } failed) throw failed;
        if (warmBytes < 0) { warmBytes = 0; warmTime = 0; }
        double mbps = end > warmTime ? (bytes - warmBytes) * 8 / 1e6 / (end - warmTime) : 0;
        return (mbps, bytes);
    }

    private async Task DownloadLoopAsync(Counter counter, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (!ct.IsCancellationRequested)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Server}/__down?bytes={ChunkBytes}")
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
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Server}/__up")
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
            foreach (var line in File.ReadLines(FilePath).Skip(1))
            {
                var p = line.Split(',');
                if (p.Length < 5 || !DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || t < cutoff) continue;
                double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
                results.Add(new SpeedResult(t, D(p[1]), D(p[2]), D(p[3]), D(p[4])));
            }
            if (results.Count > 0 && File.ReadLines(FilePath).Count() - 1 > results.Count + 200) Rewrite(); // drop old lines now and then
        }
        catch { /* unreadable: start fresh */ }
    }

    private static string Line(SpeedResult r) => string.Format(CultureInfo.InvariantCulture,
        "{0:yyyy-MM-dd HH:mm:ss},{1:0.0},{2:0.0},{3:0.0},{4:0.0}", r.Time, r.DownMbps, r.UpMbps, r.PingMs, r.MegaBytes);

    private const string Header = "time,down_mbps,up_mbps,ping_ms,data_mb";

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
