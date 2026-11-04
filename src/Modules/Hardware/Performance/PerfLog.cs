using System.Globalization;
using System.IO;
using System.Text;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>A 10-second summary: each metric's average and peak, and the busiest processes by CPU and by memory.</summary>
public sealed class LogRow
{
    public LogRow(DateTime time)
    {
        Time = time;
        Avg = new double[MetricInfo.All.Count];
        Max = new double[MetricInfo.All.Count];
        Array.Fill(Avg, double.NaN);
        Array.Fill(Max, double.NaN);
    }

    /// <summary>The start of the 10 seconds.</summary>
    public DateTime Time { get; }
    public double[] Avg { get; }
    public double[] Max { get; }

    /// <summary>Busiest processes by average CPU % over the 10 seconds (by name, all instances together).</summary>
    public List<(string Name, double Value)> TopCpu { get; } = new();

    /// <summary>Biggest processes by private memory in MB.</summary>
    public List<(string Name, double Value)> TopMemory { get; } = new();
}

/// <summary>Collects 1-second samples into 10-second log rows.</summary>
internal sealed class LogAggregator
{
    public const int Seconds = 10;
    private const int TopCount = 5;

    private DateTime slot = DateTime.MinValue;
    private readonly List<PerfSample> samples = new();
    private readonly List<IReadOnlyList<ProcessSample>> processSamples = new();

    private static DateTime SlotOf(DateTime t) => DailyLog.SlotOf(t, Seconds);

    /// <summary>True when <paramref name="now"/> is in a new 10 seconds, so the previous ones can be taken.</summary>
    public bool Due(DateTime now) => samples.Count > 0 && SlotOf(now) != slot;

    public void Add(PerfSample s)
    {
        if (samples.Count == 0) slot = SlotOf(s.Time);
        samples.Add(s);
    }

    /// <summary>A process list taken during these 10 seconds (it refreshes at its own rate).</summary>
    public void AddProcesses(IReadOnlyList<ProcessSample> procs) => processSamples.Add(procs);

    public LogRow Take()
    {
        var row = new LogRow(slot);
        for (int i = 0; i < row.Avg.Length; i++)
        {
            (row.Avg[i], row.Max[i]) = DailyLog.AvgMax(samples.Select(s => s.Values[i]));
        }
        if (processSamples.Count > 0)
        {
            var cpu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var memory = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in processSamples)
            {
                foreach (var g in snapshot.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                {
                    cpu[g.Key] = cpu.GetValueOrDefault(g.Key) + g.Sum(p => p.Cpu) / processSamples.Count;
                    memory[g.Key] = Math.Max(memory.GetValueOrDefault(g.Key), g.Sum(p => p.MemoryMB));
                }
            }
            row.TopCpu.AddRange(cpu.OrderByDescending(p => p.Value).Take(TopCount).Select(p => (p.Key, p.Value)));
            row.TopMemory.AddRange(memory.OrderByDescending(p => p.Value).Take(TopCount).Select(p => (p.Key, p.Value)));
        }
        samples.Clear();
        processSamples.Clear();
        return row;
    }
}

/// <summary>
/// The performance log: one CSV file per day in %LOCALAPPDATA%\DaisysApp\logs\performance, a row every 10 seconds
/// (about 1 MB a day), kept by Core's <see cref="DailyLog"/>. Old days are deleted after the number of days set in
/// Settings → Hardware → Performance.
/// </summary>
public sealed class PerfLog
{
    private static readonly DailyLog days = new(Path.Combine(AppPaths.LogFolder, "performance"), "perf");
    private readonly HashSet<string> checkedFiles = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastCleanup = DateTime.MinValue;

    public static string Folder => days.Folder;

    public bool Enabled { get; set; } = true;
    public int KeepDays { get; set; } = 30;

    private static string Header() =>
        "time," + string.Join(",", MetricInfo.All.Select(m => $"{m.Key}_avg,{m.Key}_max")) + ",top_cpu,top_memory";

    public void Append(LogRow row)
    {
        if (!Enabled) return;
        var sb = new StringBuilder();
        sb.Append(DailyLog.Time(row.Time));
        for (int i = 0; i < row.Avg.Length; i++) sb.Append(',').Append(DailyLog.Num(row.Avg[i])).Append(',').Append(DailyLog.Num(row.Max[i]));
        sb.Append(',').Append(Quote(Top(row.TopCpu))).Append(',').Append(Quote(Top(row.TopMemory))).Append(Environment.NewLine);
        try
        {
            string file = days.FileFor(row.Time);
            if (checkedFiles.Add(file) && File.Exists(file)) days.WithFile(row.Time, UpgradeColumns);
            days.Append(row.Time, sb.ToString(), Header()); // (disk full / folder locked: this row is skipped)
            if (DateTime.Now - lastCleanup > TimeSpan.FromHours(6)) Cleanup();
        }
        catch { /* the file couldn't be upgraded: skip this row */ }
    }

    /// <summary>
    /// If the file's columns aren't the current ones (it was started by an older version, before a metric was added),
    /// rewrites it with the current columns so new readings and old ones can be read together.
    /// </summary>
    private static void UpgradeColumns(string file)
    {
        var lines = File.ReadAllLines(file);
        string header = Header();
        if (lines.Length == 0 || lines[0] == header) return;
        var oldColumns = SplitCsv(lines[0]);
        var newColumns = SplitCsv(header);
        var map = newColumns.Select(c => oldColumns.IndexOf(c)).ToArray();
        var output = new List<string> { header };
        foreach (var line in lines.Skip(1))
        {
            var f = SplitCsv(line);
            output.Add(string.Join(",", map.Select((i, n) =>
            {
                string v = i >= 0 && i < f.Count ? f[i] : "";
                return newColumns[n].StartsWith("top_") ? Quote(v) : v;
            })));
        }
        File.WriteAllLines(file, output);
    }

    private static string Top(List<(string Name, double Value)> list) =>
        string.Join(";", list.Select(p => p.Name.Replace(";", "_").Replace("=", "_") + "=" + p.Value.ToString("0.#", CultureInfo.InvariantCulture)));

    private static string Quote(string s) => DaisysApp.Shared.Csv.Quote(s);

    /// <summary>Deletes days older than <see cref="KeepDays"/>.</summary>
    public void Cleanup()
    {
        lastCleanup = DateTime.Now;
        days.Cleanup(KeepDays);
    }

    /// <summary>The days that have a log, oldest first.</summary>
    public static List<DateTime> Days() => days.Days();

    public static long SizeBytes() => days.SizeBytes();

    public void DeleteAll()
    {
        days.DeleteAll();
        checkedFiles.Clear();
    }

    /// <summary>The rows between two times, oldest first.</summary>
    public List<LogRow> Read(DateTime from, DateTime to)
    {
        var rows = new List<LogRow>();
        {
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                var lines = days.ReadLines(day);
                if (lines.Length < 2) continue;
                var columns = SplitCsv(lines[0]);
                var index = columns.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i);
                foreach (var raw in lines.Skip(1))
                {
                    var f = SplitCsv(raw);
                    if (f.Count == 0 || !DailyLog.TryTime(f[0], out var t)) continue;
                    if (t < from || t > to) continue;
                    var row = new LogRow(t);
                    foreach (var m in MetricInfo.All)
                    {
                        row.Avg[(int)m.Id] = Field(f, index, m.Key + "_avg");
                        row.Max[(int)m.Id] = Field(f, index, m.Key + "_max");
                    }
                    ParseTop(f, index, "top_cpu", row.TopCpu);
                    ParseTop(f, index, "top_memory", row.TopMemory);
                    rows.Add(row);
                }
            }
        }
        return rows;
    }

    private static double Field(List<string> f, Dictionary<string, int> index, string column) =>
        index.TryGetValue(column, out int i) && i < f.Count ? DailyLog.Parse(f[i]) : double.NaN;

    private static void ParseTop(List<string> f, Dictionary<string, int> index, string column, List<(string, double)> into)
    {
        if (!index.TryGetValue(column, out int i) || i >= f.Count) return;
        foreach (var part in f[i].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.LastIndexOf('=');
            if (eq > 0 && double.TryParse(part[(eq + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) into.Add((part[..eq], v));
        }
    }

    private static List<string> SplitCsv(string line) => DaisysApp.Shared.Csv.Split(line);
}
