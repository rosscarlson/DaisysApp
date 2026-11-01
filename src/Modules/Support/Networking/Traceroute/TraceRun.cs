using System.Net;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// One trace from start to end, the same for Traceroute's Trace button and Ping's Trace when down (since 3.34.0): the
/// host looked up, traced (<see cref="Tracer"/>), each new hop's place, network and name looked up as it answers, and
/// the trace logged when it ends, finished or stopped (<see cref="TraceLog"/>).
/// </summary>
internal static class TraceRun
{
    /// <summary>What a trace in progress tells whoever shows it (each on any thread).</summary>
    public sealed class Watch
    {
        /// <summary>The host's address, once it's looked up.</summary>
        public Action<IPAddress>? Resolved;
        /// <summary>After each round, and after each round's lookups.</summary>
        public Action? Changed;
        /// <summary>This PC's place (for the map's "You"), once it's known.</summary>
        public Task<GeoInfo?> Me = Task.FromResult<GeoInfo?>(null);
    }

    /// <summary>
    /// Traces <paramref name="host"/> into <paramref name="hops"/> (cleared first) and logs it. Null if the host wasn't
    /// found; else the record logged. Stopping it (<paramref name="ct"/>) logs what it had and throws.
    /// </summary>
    public static async Task<TraceRecord?> RunAsync(string host, int maxHops, bool repeat, string why, List<Hop> hops, Watch? watch, CancellationToken ct)
    {
        watch ??= new Watch();
        hops.Clear();
        if (await NetInfo.ResolveAsync(host, ct) is not { } target) return null;
        Log.Here.Info($"Traceroute to {host} ({target}){(repeat ? ", repeating" : "")}{(why.Length > 0 ? $": {why}" : "")}");
        watch.Resolved?.Invoke(target);
        watch.Me = Geo.MineAsync(CancellationToken.None);
        var lookups = new List<Task>();
        bool stopped = false;
        try
        {
            await Tracer.RunAsync(target, maxHops, repeat, hops, () =>
            {
                // (called between rounds, so the list holds still while it's copied)
                var fresh = hops.Where(h => h.Address.Length > 0 && h.Name.Length == 0 && h.Geo == null && h.Place.Length == 0).ToList();
                foreach (var h in fresh) h.Place = "…";
                if (fresh.Count > 0) lock (lookups) lookups.Add(LookUpAsync(fresh).ContinueWith(_ => watch.Changed?.Invoke()));
                watch.Changed?.Invoke();
            }, ct);
        }
        catch (OperationCanceledException) { stopped = true; }
        Task[] pending;
        lock (lookups) pending = lookups.ToArray();
        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(10000));
        // (not reached: the silent hops past the last that answered go, but one, so the table shows where it stops)
        if (hops.Count > 0 && !hops[^1].Destination)
        {
            int keep = hops.FindLastIndex(h => h.Received > 0) + 2;
            if (keep < hops.Count) hops.RemoveRange(keep, hops.Count - keep);
            watch.Changed?.Invoke();
        }
        var me = watch.Me.IsCompletedSuccessfully ? watch.Me.Result : null;
        var copy = hops.ToList();
        bool reached = copy.LastOrDefault()?.Destination == true;
        var record = new TraceRecord(DateTime.Now, host, target.ToString(), why, reached, Summary(copy),
            me?.Lat ?? double.NaN, me?.Lon ?? double.NaN, me?.Place ?? "", copy.Select(h => h.Record()).ToList());
        if (copy.Count > 0) TraceLog.Add(record);
        if (stopped) throw new OperationCanceledException(ct);
        return record;
    }

    /// <summary>Hops' places and networks (one query for all of them), then their names.</summary>
    private static async Task LookUpAsync(List<Hop> fresh)
    {
        var geo = await Geo.LookupAsync(fresh.Select(h => h.Address));
        foreach (var h in fresh)
        {
            if (geo.TryGetValue(h.Address, out var g)) { h.Geo = g; h.Place = g.Place; h.Network = g.As.Length > 0 ? g.As : g.Isp; }
            else h.Place = IPAddress.TryParse(h.Address, out var ip) && NetInfo.IsPrivate(ip) ? T("private") : "";
        }
        await Task.WhenAll(fresh.Select(async h => h.Name = await Names.ReverseAsync(h.Address) ?? ""));
    }

    /// <summary>
    /// What a trace found: reached, with loss along the way or not; or not, and the last hop that answered (where it
    /// dies).
    /// </summary>
    public static string Summary(IReadOnlyList<Hop> hops)
    {
        if (hops.Count == 0) return T("no answer");
        if (hops[^1].Destination)
        {
            // a hop losing packets matters when the ones after it lose them too (routers often don't bother answering)
            var lossy = hops.Where((h, i) => h.Loss >= 10 && h.Sent > 0 && hops.Skip(i + 1).Where(n => n.Sent > 0 && n.Address.Length > 0).All(n => n.Loss >= h.Loss * 0.5)).ToList();
            return (lossy.Count == 0 ? T("no loss along the way") : F("loss from hop {0} on", lossy[0].Number));
        }
        var last = hops.LastOrDefault(h => h.Received > 0);
        if (last == null) return T("no hop answered");
        string who = last.Name.Length > 0 ? $"{last.Address}, {last.Name}" : last.Address;
        return F("no answer past hop {0} ({1})", last.Number, who);
    }
}
