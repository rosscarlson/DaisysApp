using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>One hop of a trace: who answered at that distance, and its figures over every round so far.</summary>
internal sealed class Hop(int number) : INotifyPropertyChanged
{
    private string address = "", name = "", place = "", network = "";
    private int sent, received;
    private double last = double.NaN, best = double.NaN, worst = double.NaN, sum, sumSq;

    public int Number { get; } = number;
    public string Address { get => address; set => Set(ref address, value); }
    public string Name { get => name; set => Set(ref name, value); }
    public string Place { get => place; set => Set(ref place, value); }
    public string Network { get => network; set => Set(ref network, value); }
    public GeoInfo? Geo { get; set; }
    public bool Destination { get; set; }
    /// <summary>Other addresses that answered at this distance (routes that change, or share the load).</summary>
    public HashSet<string> Others { get; } = new();

    public int Sent => sent;
    public string LossText => sent == 0 ? "" : $"{Loss:0.#}%";
    public double Loss => sent == 0 ? 0 : 100.0 * (sent - received) / sent;
    public string LastText => NetUi.Ms(last);
    public string AvgText => NetUi.Ms(Avg);
    public double Avg => received == 0 ? double.NaN : sum / received;
    public double AvgNumber => received == 0 ? double.MaxValue : sum / received;
    public string BestText => NetUi.Ms(best);
    public string WorstText => NetUi.Ms(worst);
    /// <summary>The standard deviation of its times: how much they jump about.</summary>
    public string StDevText => received < 2 ? "" : NetUi.Ms(Math.Sqrt(Math.Max(sumSq / received - Avg * Avg, 0)));

    public void Add(double ms)
    {
        sent++;
        last = ms;
        if (!double.IsNaN(ms))
        {
            received++;
            sum += ms;
            sumSq += ms * ms;
            best = double.IsNaN(best) ? ms : Math.Min(best, ms);
            worst = double.IsNaN(worst) ? ms : Math.Max(worst, ms);
        }
        foreach (var p in new[] { nameof(Sent), nameof(LossText), nameof(Loss), nameof(LastText), nameof(AvgText), nameof(AvgNumber), nameof(BestText), nameof(WorstText), nameof(StDevText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string v, [CallerMemberName] string? n = null)
    {
        if (field == v) return;
        field = v;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

/// <summary>
/// A traceroute: every distance (time to live) from 1 up is pinged at once, each round; a router that drops a packet at
/// that distance answers "time exceeded", which says who it is, and the target itself answers at its own distance.
/// Three rounds, or with Repeat one a second until stopped (as mtr does), each hop's loss and times adding up.
/// </summary>
internal static class Tracer
{
    private static readonly byte[] Payload = new byte[32];

    /// <param name="hops">The hops so far (the caller shows them); grown and filled as answers come.</param>
    /// <param name="changed">Called after each round (on the caller's thread when it has a context).</param>
    public static async Task RunAsync(IPAddress target, int maxHops, bool repeat, List<Hop> hops, Action changed, CancellationToken ct)
    {
        int limit = maxHops;
        // a first round that isn't counted: starting this many pings at once takes Windows a moment, which would add
        // to every hop's first time
        await Task.WhenAll(Enumerable.Range(1, limit).Select(ttl => ProbeAsync(target, ttl)));
        ct.ThrowIfCancellationRequested();
        for (int round = 0; repeat || round < 3; round++)
        {
            var started = Stopwatch.StartNew();
            var results = await Task.WhenAll(Enumerable.Range(1, limit).Select(ttl => ProbeAsync(target, ttl)));
            ct.ThrowIfCancellationRequested();
            int reached = Array.FindIndex(results, r => r.Status == IPStatus.Success);
            if (reached >= 0 && reached + 1 < limit) limit = reached + 1;
            while (hops.Count < limit) hops.Add(new Hop(hops.Count + 1));
            if (hops.Count > limit) hops.RemoveRange(limit, hops.Count - limit);
            for (int i = 0; i < limit; i++)
            {
                var (status, address, ms) = results[i];
                var hop = hops[i];
                if (address != null)
                {
                    string a = address.ToString();
                    if (hop.Address.Length == 0) hop.Address = a;
                    else if (hop.Address != a) hop.Others.Add(a);
                }
                hop.Destination = i == limit - 1 && reached >= 0;
                hop.Add(status is IPStatus.Success or IPStatus.TtlExpired or IPStatus.TimeExceeded ? ms : double.NaN);
            }
            changed();
            // after the first round, a round a second (the first three quickly when not repeating)
            var wait = TimeSpan.FromMilliseconds((repeat ? 1000 : 250) - started.ElapsedMilliseconds);
            if (wait > TimeSpan.Zero && (repeat || round < 2)) await Task.Delay(wait, ct);
        }
    }

    private static async Task<(IPStatus Status, IPAddress? Address, double Ms)> ProbeAsync(IPAddress target, int ttl)
    {
        try
        {
            using var ping = new Ping();
            var sw = Stopwatch.StartNew();
            // (timed off the UI thread: waiting for it there would add whatever it's busy with)
            var reply = await ping.SendPingAsync(target, 1500, Payload, new PingOptions(ttl, true)).ConfigureAwait(false);
            // (Windows gives a time only for the target's own answer; a router's is timed here)
            double ms = reply.Status == IPStatus.Success && reply.RoundtripTime > 0 ? reply.RoundtripTime : sw.Elapsed.TotalMilliseconds;
            return (reply.Status, reply.Status == IPStatus.TimedOut ? null : reply.Address, Math.Max(ms, 0.1));
        }
        catch { return (IPStatus.Unknown, null, double.NaN); }
    }
}
