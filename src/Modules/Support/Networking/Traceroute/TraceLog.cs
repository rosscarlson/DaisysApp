using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>A hop as it was logged (<see cref="Hop.Record"/>, <see cref="Hop.From"/>).</summary>
internal sealed record HopRecord(int N, string Address, string Name, string Place, string Network, double Lat, double Lon,
    int Sent, int Received, double Last, double Best, double Worst, double Sum, double SumSq, List<string>? Others);

/// <summary>
/// A trace as it was logged: when, to what, why (empty for one started by hand; "Google stopped answering" for one Ping's
/// Trace when down started), whether it got there, what it found, where this PC was, and its hops.
/// </summary>
internal sealed record TraceRecord(DateTime Time, string Host, string Target, string Why, bool Reached, string Summary,
    double MeLat, double MeLon, string MePlace, List<HopRecord> Hops)
{
    [JsonIgnore] public bool Auto => Why.Length > 0;

    /// <summary>For the Past traces list: "31/10 21:05 · 8.8.8.8 · Google stopped answering · no answer past hop 3".</summary>
    public override string ToString() =>
        string.Join(" · ", new[] { NetUi.When(Time), Host, Why, Summary }.Where(s => s.Length > 0));
}

/// <summary>
/// Every trace, by hand or by Trace when down (since 3.34.0), one JSON line each in <c>logs\network\traces.jsonl</c>,
/// kept as long as Ping's logs (<see cref="NetworkingSettings.KeepDays"/>). Traceroute's Past traces shows them again.
/// </summary>
internal static class TraceLog
{
    private static readonly object gate = new();
    private static readonly JsonSerializerOptions Options = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static string FilePath => Path.Combine(PingMonitor.Folder, "traces.jsonl");

    /// <summary>Raised (on any thread) after a trace is logged.</summary>
    public static event Action<TraceRecord>? Logged;

    public static void Add(TraceRecord r)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(PingMonitor.Folder);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(r, Options) + "\n", Encoding.UTF8);
            }
        }
        catch (Exception ex) { Log.Here.Warn($"Couldn't log the trace to {r.Host}: {ex.Message}"); }
        Logged?.Invoke(r);
    }

    /// <summary>Every trace logged, newest first.</summary>
    public static List<TraceRecord> All()
    {
        var list = new List<TraceRecord>();
        try
        {
            string[] lines;
            lock (gate)
            {
                if (!File.Exists(FilePath)) return list;
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }
            foreach (var line in lines)
            {
                if (line.Length == 0) continue;
                try { if (JsonSerializer.Deserialize<TraceRecord>(line, Options) is { } r) list.Add(r); }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) { Log.Here.Warn($"Couldn't read the trace log: {ex.Message}"); }
        list.Reverse();
        return list;
    }

    /// <summary>Drops the traces older than <paramref name="days"/>.</summary>
    public static void Cleanup(int days)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-Math.Max(days, 1));
            var all = All();
            if (all.All(r => r.Time >= cutoff)) return;
            var kept = all.Where(r => r.Time >= cutoff).Reverse().Select(r => JsonSerializer.Serialize(r, Options));
            lock (gate) File.WriteAllLines(FilePath, kept, Encoding.UTF8);
        }
        catch (Exception ex) { Log.Here.Debug(() => $"Trace log cleanup: {ex.Message}"); }
    }
}
