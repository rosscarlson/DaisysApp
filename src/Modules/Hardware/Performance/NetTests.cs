using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>A host the Network tests card pings: a name to show and an IP address or hostname.</summary>
public sealed class NetHost
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>A response slower than this (ms) turns the host's line bold and red in the graph…</summary>
    public int AlertMs { get; set; } = 50;
    /// <summary>…until it has been back under it for this many seconds.</summary>
    public int AlertSeconds { get; set; } = 15;

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
/// Pings every host once a second (through Core's <see cref="DaisysApp.Shared.Network.Pings"/>, shared with Support → Ping), all at once, while Daisy's App runs, and keeps the last 30 minutes. A host that
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

    // (Core's one pinger: Support → Ping's pings to the same address are this one)
    private static async Task<(double Ms, string? Error)> PingAsync(string address)
    {
        var r = await DaisysApp.Shared.Network.Pings.SendAsync(address, TimeoutMs, maxAgeMs: 900);
        return (r.Ms, r.Answered ? null : r.NotFound ? T("not found") : r.Status == IPStatus.TimedOut ? T("no answer") : r.Status == IPStatus.Unknown ? T("error") : r.Status.ToString());
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
