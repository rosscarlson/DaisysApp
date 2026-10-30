using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>A target's figures over a span of time: what the Statistics table shows.</summary>
internal sealed record PingStats(int Sent, int Answered, double Avg, double Min, double Max, double Jitter, int Outages, TimeSpan LongestOutage)
{
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Answered) / Sent;
}

/// <summary>
/// Tests every target at once each round (every <see cref="NetworkingSettings.PingSeconds"/>): a ping (Core's
/// <see cref="Shared.Network.Pings"/>, shared with Hardware → Performance), a DNS query
/// straight to the server (so Windows' cache doesn't answer it), or a TCP connection. Keeps 24 hours of results in
/// memory, and while logging is on writes each round to <c>logs\network\ping-yyyy-MM-dd.csv</c>. A target that misses
/// three in a row is <i>down</i> (an outage) until it answers again: <see cref="Outage"/> tells the Events tool.
/// </summary>
internal sealed class PingMonitor : IDisposable
{
    public const int KeepHours = 24;
    public const int DownAfter = 3;

    private readonly NetworkingSettings settings;
    private readonly Timer timer;
    private readonly object gate = new();
    private readonly Dictionary<string, List<(DateTime T, double V)>> history = new();
    private readonly Dictionary<string, string> errors = new();
    private readonly Dictionary<string, (int Misses, DateTime? DownSince)> state = new();
    private int busy, period;
    private StreamWriter? logWriter;
    private DateTime logDay;

    /// <summary>Raised on a background thread after each round.</summary>
    public event Action? Updated;

    /// <summary>A target went down (<c>back</c> false, with when it started) or answered again (true, with how long it was down).</summary>
    public event Action<NetTarget, bool, DateTime, TimeSpan>? Outage;

    /// <summary>Raised (on any thread) when logging starts or stops by itself.</summary>
    public event Action? LoggingChanged;

    public static string Folder => Path.Combine(AppPaths.LogFolder, "network");

    public PingMonitor(NetworkingSettings settings)
    {
        this.settings = settings;
        timer = new Timer(_ => _ = TickAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Cleanup();
        Reschedule();
    }

    /// <summary>After the interval changed.</summary>
    public void Reschedule()
    {
        period = Math.Clamp(settings.PingSeconds, 1, 3600) * 1000;
        timer.Change(1000, period);
    }

    private int TimeoutMs => Math.Min(period, 2000);

    private async Task TickAsync()
    {
        if (!settings.PingOn)
        {
            lock (gate) { history.Clear(); errors.Clear(); state.Clear(); }
            CloseLog();
            return;
        }
        if (Interlocked.Exchange(ref busy, 1) == 1) return; // the last round is still waiting
        try
        {
            var targets = settings.Targets.Where(t => t.Address.Trim().Length > 0).GroupBy(t => t.Key).Select(g => g.First()).ToList();
            var now = DateTime.Now;
            var results = await Task.WhenAll(targets.Select(t => ProbeAsync(t, TimeoutMs)));
            var events = new List<(NetTarget, bool, DateTime, TimeSpan)>();
            lock (gate)
            {
                var cutoff = now.AddHours(-KeepHours);
                for (int i = 0; i < targets.Count; i++)
                {
                    string key = targets[i].Key;
                    if (!history.TryGetValue(key, out var list)) history[key] = list = new();
                    list.Add((now, results[i].Ms));
                    if (list.Count > 64 && list[0].T < cutoff) list.RemoveRange(0, list.FindIndex(p => p.T >= cutoff) is var n and > 0 ? n : 1);
                    if (results[i].Error is { } e) errors[key] = e; else errors.Remove(key);

                    var (misses, down) = state.GetValueOrDefault(key);
                    if (double.IsNaN(results[i].Ms))
                    {
                        misses++;
                        if (misses == DownAfter && down == null)
                        {
                            down = now.AddSeconds(-(DownAfter - 1) * period / 1000.0);
                            events.Add((targets[i], false, down.Value, TimeSpan.Zero));
                        }
                    }
                    else
                    {
                        if (down is DateTime since) events.Add((targets[i], true, since, now - since));
                        misses = 0;
                        down = null;
                    }
                    state[key] = (misses, down);
                }
                var keys = targets.Select(t => t.Key).ToHashSet();
                foreach (var gone in history.Keys.Where(k => !keys.Contains(k)).ToList()) { history.Remove(gone); state.Remove(gone); }
            }
            WriteLog(now, targets, results);
            foreach (var e in events) Outage?.Invoke(e.Item1, e.Item2, e.Item3, e.Item4);
            Updated?.Invoke();
        }
        catch (Exception ex) { Log.Here.Debug(() => $"Ping round: {ex.Message}"); }
        finally { Volatile.Write(ref busy, 0); }
    }

    // ---------------------------------------------------------------- probes

    /// <summary>One test of a target: its time in ms (NaN if it failed) and why it failed.</summary>
    public static async Task<(double Ms, string? Error)> ProbeAsync(NetTarget t, int timeoutMs)
    {
        try
        {
            return t.Kind switch
            {
                NetTarget.DnsKind => await DnsAsync(t.Address.Trim(), t.Query.Trim(), timeoutMs),
                NetTarget.TcpKind => await TcpAsync(t.Address.Trim(), t.Port, timeoutMs),
                _ => await PingAsync(t.Address.Trim(), timeoutMs),
            };
        }
        catch (Exception) { return (double.NaN, T("error")); }
    }

    // (Core's one pinger: Hardware → Performance's pings to the same address are this one; a round's answer is shared
    // when it's under 90% of the interval old)
    private static async Task<(double, string?)> PingAsync(string address, int timeoutMs)
    {
        var r = await Shared.Network.Pings.SendAsync(address, timeoutMs, maxAgeMs: Math.Max(timeoutMs, 1000) * 9 / 10);
        return (r.Ms, r.Answered ? null : r.NotFound ? T("not found") : r.Status == IPStatus.TimedOut ? T("no answer") : r.Status == IPStatus.Unknown ? T("error") : r.Status.ToString());
    }

    private static async Task<(double, string?)> TcpAsync(string host, int port, int timeoutMs)
    {
        if (await NetInfo.ResolveAsync(host) is not { } ip) return (double.NaN, T("not found"));
        using var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        using var cts = new CancellationTokenSource(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(new IPEndPoint(ip, port), cts.Token);
            return (Math.Max(sw.Elapsed.TotalMilliseconds, 0.1), null);
        }
        catch (OperationCanceledException) { return (double.NaN, T("no answer")); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused) { return (double.NaN, T("refused")); }
        catch (SocketException ex) { return (double.NaN, ex.SocketErrorCode.ToString()); }
    }

    /// <summary>Asks <paramref name="server"/> for <paramref name="name"/>'s address (an A query over UDP) and times the answer.</summary>
    public static async Task<(double, string?)> DnsAsync(string server, string name, int timeoutMs)
    {
        var ip = IPAddress.TryParse(server, out var parsed) ? parsed : await NetInfo.ResolveAsync(server);
        if (ip == null) return (double.NaN, T("not found"));
        ushort id = (ushort)Random.Shared.Next(1, 65535);
        var query = Query(id, name);
        using var udp = new UdpClient(ip.AddressFamily);
        using var cts = new CancellationTokenSource(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(query, new IPEndPoint(ip, 53), cts.Token);
            while (true)
            {
                var r = await udp.ReceiveAsync(cts.Token);
                var b = r.Buffer;
                if (b.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(b) != id) continue;
                double ms = Math.Max(sw.Elapsed.TotalMilliseconds, 0.1);
                int rcode = b[3] & 0x0F;
                int answers = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(6));
                return rcode switch
                {
                    0 when answers > 0 => (ms, null),
                    0 or 3 => (double.NaN, T("no such name")),
                    2 => (double.NaN, T("server failure")),
                    5 => (double.NaN, T("refused")),
                    _ => (double.NaN, F("error {0}", rcode)),
                };
            }
        }
        catch (OperationCanceledException) { return (double.NaN, T("no answer")); }
        catch (SocketException ex) { return (double.NaN, ex.SocketErrorCode.ToString()); }
    }

    private static byte[] Query(ushort id, string name)
    {
        var ms = new MemoryStream();
        Span<byte> head = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(head, id);
        head[2] = 0x01; // recursion desired
        head[5] = 1;    // one question
        ms.Write(head);
        foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)Math.Min(bytes.Length, 63));
            ms.Write(bytes, 0, Math.Min(bytes.Length, 63));
        }
        ms.Write(new byte[] { 0, 0, 1, 0, 1 }); // end, type A, class IN
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- results

    /// <summary>A copy of a target's results since <paramref name="since"/>.</summary>
    public List<(DateTime T, double V)> Points(string key, DateTime since)
    {
        lock (gate)
        {
            if (!history.TryGetValue(key, out var list)) return new();
            int from = list.FindIndex(p => p.T >= since);
            return from < 0 ? new() : list.GetRange(from, list.Count - from);
        }
    }

    /// <summary>Why the last test failed, or null if it answered.</summary>
    public string? Error(string key)
    {
        lock (gate) return errors.GetValueOrDefault(key);
    }

    /// <summary>Down now: since when (null if it's answering).</summary>
    public DateTime? DownSince(string key)
    {
        lock (gate) return state.GetValueOrDefault(key).DownSince;
    }

    /// <summary>A target's figures since <paramref name="since"/>: jitter is the average change between answers in a row.</summary>
    public PingStats Stats(string key, DateTime since)
    {
        var points = Points(key, since);
        var answered = points.Where(p => !double.IsNaN(p.V)).Select(p => p.V).ToList();
        double jitter = 0;
        int pairs = 0;
        for (int i = 1; i < points.Count; i++)
            if (!double.IsNaN(points[i].V) && !double.IsNaN(points[i - 1].V)) { jitter += Math.Abs(points[i].V - points[i - 1].V); pairs++; }
        int outages = 0, run = 0;
        DateTime runStart = default;
        var longest = TimeSpan.Zero;
        foreach (var p in points.Append((DateTime.MaxValue, 0)))
        {
            if (double.IsNaN(p.V))
            {
                if (run++ == 0) runStart = p.T;
                continue;
            }
            if (run >= DownAfter)
            {
                outages++;
                var end = p.T == DateTime.MaxValue ? DateTime.Now : p.T;
                if (end - runStart > longest) longest = end - runStart;
            }
            run = 0;
        }
        return new PingStats(points.Count, answered.Count,
            answered.Count > 0 ? answered.Average() : double.NaN, answered.Count > 0 ? answered.Min() : double.NaN,
            answered.Count > 0 ? answered.Max() : double.NaN, pairs > 0 ? jitter / pairs : double.NaN, outages, longest);
    }

    // ---------------------------------------------------------------- log

    /// <summary>Logging on, and not past its end (which switches it off).</summary>
    private bool LogOn()
    {
        if (!settings.PingLog) return false;
        if (settings.PingLogUntil is DateTime until && DateTime.Now >= until)
        {
            settings.PingLog = false;
            settings.PingLogUntil = null;
            settings.Save();
            Log.Here.Info($"Ping logging finished (it was set to stop at {until:g})");
            LoggingChanged?.Invoke();
            return false;
        }
        return true;
    }

    private void WriteLog(DateTime now, List<NetTarget> targets, (double Ms, string? Error)[] results)
    {
        if (!LogOn()) { CloseLog(); return; }
        try
        {
            if (logWriter == null || logDay != now.Date)
            {
                CloseLog();
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"ping-{now:yyyy-MM-dd}.csv");
                bool fresh = !File.Exists(path);
                logWriter = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) { AutoFlush = true };
                logDay = now.Date;
                if (fresh) logWriter.WriteLine("time,name,kind,target,ms,error");
            }
            var sb = new StringBuilder();
            for (int i = 0; i < targets.Count; i++)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4},{5}\r\n", now, Csv(targets[i].Display), targets[i].Kind,
                    Csv(targets[i].What), double.IsNaN(results[i].Ms) ? "" : results[i].Ms.ToString("0.0", CultureInfo.InvariantCulture), Csv(results[i].Error ?? "")));
            logWriter.Write(sb.ToString());
        }
        catch (Exception ex)
        {
            Log.Here.Warn($"Couldn't write the ping log: {ex.Message}");
            CloseLog();
        }

        static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    private void CloseLog()
    {
        try { logWriter?.Dispose(); } catch { }
        logWriter = null;
    }

    /// <summary>Deletes ping logs older than <see cref="NetworkingSettings.KeepDays"/>.</summary>
    public void Cleanup()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            var cutoff = DateTime.Today.AddDays(-Math.Max(settings.KeepDays, 1));
            foreach (var f in Directory.GetFiles(Folder, "ping-*.csv"))
                if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
        }
        catch { }
    }

    public void Dispose()
    {
        timer.Dispose();
        CloseLog();
    }
}
