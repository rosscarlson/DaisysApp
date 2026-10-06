using System.Globalization;
using System.IO;
using System.Text;

namespace DaisysApp.Applets.Performance;

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

    private static DateTime SlotOf(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.FromSeconds(Seconds).Ticks, t.Kind);

    /// <summary>True when <paramref name="now"/> is in a new 10 seconds, so the previous ones can be taken.</summary>
    public bool Due(DateTime now) => samples.Count > 0 && SlotOf(now) != slot;

    public void Add(PerfSample s, IReadOnlyList<ProcessSample>? procs)
    {
        if (samples.Count == 0) slot = SlotOf(s.Time);
        samples.Add(s);
        if (procs != null) processSamples.Add(procs);
    }

    public LogRow Take()
    {
        var row = new LogRow(slot);
        for (int i = 0; i < row.Avg.Length; i++)
        {
            var values = samples.Select(s => s.Values[i]).Where(v => !double.IsNaN(v)).ToList();
            if (values.Count == 0) continue;
            row.Avg[i] = values.Average();
            row.Max[i] = values.Max();
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
/// (about 1 MB a day). Old days are deleted after the number of days set in Settings → Performance.
/// </summary>
public sealed class PerfLog
{
    private readonly object gate = new();
    private readonly HashSet<string> checkedFiles = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastCleanup = DateTime.MinValue;

    public static string Folder => Path.Combine(AppPaths.LogFolder, "performance");

    public bool Enabled { get; set; } = true;
    public int KeepDays { get; set; } = 30;

    private static string FileFor(DateTime day) => Path.Combine(Folder, $"perf-{day:yyyy-MM-dd}.csv");

    private static string Header() =>
        "time," + string.Join(",", MetricInfo.All.Select(m => $"{m.Key}_avg,{m.Key}_max")) + ",top_cpu,top_memory";

    public void Append(LogRow row)
    {
        if (!Enabled) return;
        var sb = new StringBuilder();
        sb.Append(row.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        for (int i = 0; i < row.Avg.Length; i++) sb.Append(',').Append(Num(row.Avg[i])).Append(',').Append(Num(row.Max[i]));
        sb.Append(',').Append(Quote(Top(row.TopCpu))).Append(',').Append(Quote(Top(row.TopMemory)));
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string file = FileFor(row.Time);
                if (checkedFiles.Add(file) && File.Exists(file)) UpgradeColumns(file);
                bool isNew = !File.Exists(file);
                File.AppendAllText(file, (isNew ? Header() + Environment.NewLine : "") + sb + Environment.NewLine);
                if (DateTime.Now - lastCleanup > TimeSpan.FromHours(6)) Cleanup();
            }
            catch { /* disk full / folder locked: skip this row */ }
        }
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

    private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Top(List<(string Name, double Value)> list) =>
        string.Join(";", list.Select(p => p.Name.Replace(";", "_").Replace("=", "_") + "=" + p.Value.ToString("0.#", CultureInfo.InvariantCulture)));

    internal static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>Deletes days older than <see cref="KeepDays"/>.</summary>
    public void Cleanup()
    {
        lastCleanup = DateTime.Now;
        var cutoff = DateTime.Today.AddDays(-Math.Max(1, KeepDays));
        foreach (var day in Days().Where(d => d < cutoff))
        {
            try { File.Delete(FileFor(day)); } catch { }
            try { File.Delete(Path.Combine(Folder, $"sensors-{day:yyyy-MM-dd}.csv")); } catch { }
        }
    }

    /// <summary>The days that have a log, oldest first.</summary>
    public static List<DateTime> Days()
    {
        try
        {
            return Directory.Exists(Folder)
                ? Directory.GetFiles(Folder, "perf-*.csv")
                    .Select(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f)[5..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
                    .OfType<DateTime>().OrderBy(d => d).ToList()
                : new();
        }
        catch { return new(); }
    }

    public static long SizeBytes()
    {
        try { return Directory.Exists(Folder) ? Directory.GetFiles(Folder).Sum(f => new FileInfo(f).Length) : 0; }
        catch { return 0; }
    }

    public void DeleteAll()
    {
        lock (gate)
            if (Directory.Exists(Folder))
                foreach (var f in Directory.GetFiles(Folder))
                    try { File.Delete(f); } catch { }
    }

    /// <summary>The rows between two times, oldest first.</summary>
    public List<LogRow> Read(DateTime from, DateTime to)
    {
        var rows = new List<LogRow>();
        lock (gate)
        {
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                string file = FileFor(day);
                if (!File.Exists(file)) continue;
                string[] lines;
                try
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }
                catch { continue; }
                if (lines.Length < 2) continue;
                var columns = SplitCsv(lines[0].TrimEnd('\r'));
                var index = columns.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i);
                foreach (var raw in lines.Skip(1))
                {
                    var f = SplitCsv(raw.TrimEnd('\r'));
                    if (f.Count == 0 || !DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
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
        index.TryGetValue(column, out int i) && i < f.Count && double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    private static void ParseTop(List<string> f, Dictionary<string, int> index, string column, List<(string, double)> into)
    {
        if (!index.TryGetValue(column, out int i) || i >= f.Count) return;
        foreach (var part in f[i].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.LastIndexOf('=');
            if (eq > 0 && double.TryParse(part[(eq + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) into.Add((part[..eq], v));
        }
    }

    internal static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
