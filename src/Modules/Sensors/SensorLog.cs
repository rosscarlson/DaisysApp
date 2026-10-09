using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DaisysApp.Shared;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Sensors;

/// <summary>What a logged sensor is, kept so history can label sensors that aren't being reported any more.</summary>
public sealed record SensorName(string Hardware, string Name, string Type, string Unit);

/// <summary>
/// LibreHardwareMonitor's temperature, fan and power sensors every 10 seconds (average and peak), in the\n/// performance log's folder (where they were before they had their own tab): one CSV per day (sensors-yyyy-MM-dd.csv) with a column pair per sensor, and sensors.json naming
/// them. Voltages, clocks and loads are shown live only, which keeps this to a few MB a day.
/// </summary>
public sealed class SensorLog
{
    private readonly SensorsSettings settings;
    private readonly object gate = new();
    private string? currentFile;
    private List<string> columns = new();
    private Dictionary<string, SensorName> names = new();
    private bool namesLoaded, namesDirty;

    public SensorLog(SensorsSettings settings) => this.settings = settings;

    public static string Folder => Path.Combine(AppPaths.LogFolder, "performance");
    public const int Seconds = 10;

    private static string FileFor(DateTime day) => Path.Combine(Folder, $"sensors-{day:yyyy-MM-dd}.csv");
    private static string NamesFile => Path.Combine(Folder, "sensors.json");

    /// <summary>The days that have a sensor log, oldest first.</summary>
    public static List<DateTime> Days()
    {
        try
        {
            return Directory.Exists(Folder)
                ? Directory.GetFiles(Folder, "sensors-*.csv")
                    .Select(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f)[8..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
                    .OfType<DateTime>().OrderBy(d => d).ToList()
                : new();
        }
        catch { return new(); }
    }

    /// <summary>The log's size on disk.</summary>
    public static long SizeBytes()
    {
        try { return Directory.Exists(Folder) ? Directory.GetFiles(Folder, "sensors-*.csv").Sum(f => new FileInfo(f).Length) : 0; }
        catch { return 0; }
    }

    /// <summary>Deletes days older than the setting.</summary>
    public void Cleanup()
    {
        var cutoff = DateTime.Today.AddDays(-Math.Max(1, settings.KeepDays));
        foreach (var day in Days().Where(d => d < cutoff))
            try { File.Delete(FileFor(day)); } catch { }
    }

    /// <summary>Deletes the whole sensor log.</summary>
    public void DeleteAll()
    {
        lock (gate)
        {
            foreach (var day in Days()) try { File.Delete(FileFor(day)); } catch { }
            currentFile = null;
        }
    }

    /// <summary>Which sensors are logged: temperatures, fans and power, minus fixed thresholds.</summary>
    public static bool Logs(HwSensor s) =>
        s.Type is "Temperature" or "Fan" or "Power" &&
        !new[] { "Limit", "Resolution", "Warning", "Critical", "Threshold", "Distance to TjMax" }.Any(w => s.Name.Contains(w, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, SensorName> Names
    {
        get { lock (gate) { LoadNames(); return new Dictionary<string, SensorName>(names); } }
    }

    private void LoadNames()
    {
        if (namesLoaded) return;
        namesLoaded = true;
        try
        {
            if (File.Exists(NamesFile))
                names = JsonSerializer.Deserialize<Dictionary<string, SensorName>>(File.ReadAllText(NamesFile)) ?? new();
        }
        catch { names = new(); }
    }

    public void Append(DateTime time, Dictionary<string, (double Avg, double Max)> values, IEnumerable<HwSensor> sensors)
    {
        if (!settings.LogEnabled || values.Count == 0) return;
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                LoadNames();
                foreach (var s in sensors.Where(s => values.ContainsKey(s.Key)))
                {
                    var n = new SensorName(s.Hardware, s.Name, s.Type, s.Unit);
                    if (!names.TryGetValue(s.Key, out var old) || old != n) { names[s.Key] = n; namesDirty = true; }
                }
                if (namesDirty)
                {
                    File.WriteAllText(NamesFile, JsonSerializer.Serialize(names));
                    namesDirty = false;
                }

                string file = FileFor(time);
                if (file != currentFile)
                {
                    currentFile = file;
                    columns = File.Exists(file) ? ReadHeader(file) : new();
                }
                var missing = values.Keys.Where(k => !columns.Contains(k + "@avg")).ToList();
                if (missing.Count > 0)
                {
                    var newColumns = columns.Concat(missing.SelectMany(k => new[] { k + "@avg", k + "@max" })).ToList();
                    if (File.Exists(file)) Rewrite(file, columns, newColumns);
                    else File.WriteAllText(file, HeaderLine(newColumns) + Environment.NewLine);
                    columns = newColumns;
                }

                var sb = new StringBuilder(time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                foreach (string c in columns)
                {
                    string key = c[..c.LastIndexOf('@')];
                    sb.Append(',');
                    if (values.TryGetValue(key, out var v)) sb.Append(Num(c.EndsWith("@avg") ? v.Avg : v.Max));
                }
                File.AppendAllText(file, sb + Environment.NewLine);
            }
            catch { /* disk full / folder locked: skip this row */ }
        }
    }

    private static string HeaderLine(List<string> columns) => "time," + string.Join(",", columns.Select(Csv.Quote));

    private static List<string> ReadHeader(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return Csv.Split(reader.ReadLine() ?? "").Skip(1).ToList();
    }

    /// <summary>Adds columns for sensors that appeared during the day (earlier rows get them empty).</summary>
    private static void Rewrite(string file, List<string> oldColumns, List<string> newColumns)
    {
        var lines = File.ReadAllLines(file);
        var output = new List<string> { HeaderLine(newColumns) };
        foreach (var line in lines.Skip(1))
            output.Add(line + new string(',', newColumns.Count - oldColumns.Count));
        File.WriteAllLines(file, output);
    }

    private static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Readings of the given sensors between two times: time → key → (average, peak).</summary>
    public List<(DateTime Time, Dictionary<string, (double Avg, double Max)> Values)> Read(DateTime from, DateTime to, IReadOnlyCollection<string> keys)
    {
        var rows = new List<(DateTime, Dictionary<string, (double, double)>)>();
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
                var header = Csv.Split(lines[0].TrimEnd('\r'));
                var wanted = keys.Select(k => (k, Avg: header.IndexOf(k + "@avg"), Max: header.IndexOf(k + "@max"))).Where(x => x.Avg > 0).ToList();
                if (wanted.Count == 0) continue;
                foreach (var raw in lines.Skip(1))
                {
                    var f = raw.TrimEnd('\r').Split(',');
                    if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || t < from || t > to) continue;
                    var values = new Dictionary<string, (double, double)>();
                    foreach (var (k, a, m) in wanted) values[k] = (Field(f, a), Field(f, m));
                    rows.Add((t, values));
                }
            }
        }
        return rows;
    }

    private static double Field(string[] f, int i) =>
        i < f.Length && double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
}

/// <summary>Collects LibreHardwareMonitor snapshots into 10-second averages and peaks for the sensor log.</summary>
internal sealed class SensorAggregator
{
    private DateTime slot = DateTime.MinValue;
    private readonly Dictionary<string, List<double>> values = new();
    private IReadOnlyList<HwSensor> lastSensors = Array.Empty<HwSensor>();

    private static DateTime SlotOf(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.FromSeconds(SensorLog.Seconds).Ticks, t.Kind);

    public bool Due(DateTime now) => values.Count > 0 && SlotOf(now) != slot;

    public void Add(HwSnapshot s)
    {
        if (values.Count == 0) slot = SlotOf(s.Time);
        lastSensors = s.Sensors;
        foreach (var x in s.Sensors.Where(SensorLog.Logs))
            (values.TryGetValue(x.Key, out var l) ? l : values[x.Key] = new()).Add(x.Value);
    }

    public (DateTime Time, Dictionary<string, (double Avg, double Max)> Values, IReadOnlyList<HwSensor> Sensors) Take()
    {
        var result = values.ToDictionary(p => p.Key, p => (p.Value.Average(), p.Value.Max()));
        values.Clear();
        return (slot, result, lastSensors);
    }
}
