using System.Globalization;
using System.IO;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>One program's figures in Hardware → Performance's log: how often it was among the 5 busiest, its CPU and
/// memory then, and its CPU over time (only while it was among them).</summary>
public sealed class ProgramHistory
{
    public int Rows;
    public int TopCpuRows, TopMemoryRows;
    public double CpuSum, PeakCpu, PeakMemoryMB, MemorySum;
    public double AvgCpuWhenTop => TopCpuRows == 0 ? 0 : CpuSum / TopCpuRows;
    public double AvgMemoryWhenTop => TopMemoryRows == 0 ? 0 : MemorySum / TopMemoryRows;
    /// <summary>Its share of the log's 10-second rows in which it was among the 5 busiest by CPU.</summary>
    public double TopCpuShare => Rows == 0 ? 0 : TopCpuRows / (double)Rows;
    public List<(DateTime T, double V)> Cpu = new();
    public List<(DateTime T, double V)> Memory = new();
}

/// <summary>
/// Reads Hardware → Performance's log (%LOCALAPPDATA%\DaisysApp\logs\performance\perf-yyyy-MM-dd.csv: a row every 10
/// seconds, with the 5 busiest programs by CPU in <c>top_cpu</c> and by memory in <c>top_memory</c>, "name=value;…"),
/// so a program's past use is there without this module keeping its own. Read again after 10 minutes.
/// </summary>
internal static class PerfHistory
{
    public static string Folder => Path.Combine(AppPaths.LogFolder, "performance");

    private static (DateTime At, int Days, Dictionary<string, ProgramHistory> Map, int Rows) cache;
    private static readonly object gate = new();

    /// <summary>Whether there's any Performance log at all (Hardware switched on, and its log on).</summary>
    public static bool Exists => Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "perf-*.csv").Any();

    /// <summary>Every program's history over the last <paramref name="days"/> days (by name, lower case).</summary>
    public static Dictionary<string, ProgramHistory> Read(int days)
    {
        lock (gate)
        {
            if (cache.Map != null && cache.Days == days && DateTime.Now - cache.At < TimeSpan.FromMinutes(10)) return cache.Map;
            var map = new Dictionary<string, ProgramHistory>(StringComparer.OrdinalIgnoreCase);
            int rows = 0;
            var from = DateTime.Now.AddDays(-days);
            for (var day = from.Date; day <= DateTime.Today; day = day.AddDays(1))
            {
                string file = Path.Combine(Folder, $"perf-{day:yyyy-MM-dd}.csv");
                if (!File.Exists(file)) continue;
                string[] lines;
                try
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var r = new StreamReader(fs);
                    lines = r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }
                catch { continue; }
                if (lines.Length < 2) continue;
                var header = Csv.Split(lines[0].TrimEnd('\r'));
                int cpuCol = header.IndexOf("top_cpu"), memCol = header.IndexOf("top_memory");
                if (cpuCol < 0) continue;
                foreach (var raw in lines.Skip(1))
                {
                    var f = Csv.Split(raw.TrimEnd('\r'));
                    if (f.Count <= cpuCol || !DailyLog.TryTime(f[0], out var t) || t < from) continue;
                    rows++;
                    foreach (var (name, v) in Top(f[cpuCol]))
                    {
                        var h = Get(map, name);
                        h.TopCpuRows++;
                        h.CpuSum += v;
                        h.PeakCpu = Math.Max(h.PeakCpu, v);
                        h.Cpu.Add((t, v));
                    }
                    if (memCol >= 0 && memCol < f.Count)
                        foreach (var (name, v) in Top(f[memCol]))
                        {
                            var h = Get(map, name);
                            h.TopMemoryRows++;
                            h.MemorySum += v;
                            h.PeakMemoryMB = Math.Max(h.PeakMemoryMB, v);
                            h.Memory.Add((t, v));
                        }
                }
            }
            foreach (var h in map.Values) h.Rows = rows;
            cache = (DateTime.Now, days, map, rows);
            return map;
        }
    }

    private static ProgramHistory Get(Dictionary<string, ProgramHistory> map, string name)
    {
        string key = KnownProcess.Key(name);
        if (!map.TryGetValue(key, out var h)) map[key] = h = new ProgramHistory();
        return h;
    }

    private static IEnumerable<(string, double)> Top(string field)
    {
        foreach (var part in field.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.LastIndexOf('=');
            if (eq > 0 && double.TryParse(part[(eq + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) yield return (part[..eq], v);
        }
    }
}
