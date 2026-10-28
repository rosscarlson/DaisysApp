using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>How much a run writes to the Support log: Standard (each test's start, result and problems) or Maximum
/// (also every second's readings, each pass and a heartbeat, so a crash shows where it was).</summary>
public enum StressLogging { Standard, Maximum }

/// <summary>A test the suite can run: its group (CPU, Memory…), what it measures and the score's reference.</summary>
/// <param name="Reference">The headline value that scores 1,000 (the reference PC: an i7-8086K, 4 × 16 GB DDR4 at
/// 2133, a GTX 1080 Ti and a WD SN720).</param>
internal sealed record TestInfo(string Id, string Group, string Name, string Tip, string Unit, double Reference, bool LowerIsBetter = false, bool Scored = true)
{
    public double ScoreOf(double value) => !Scored || value <= 0 || double.IsNaN(value) ? 0
        : 1000 * (LowerIsBetter ? Reference / value : value / Reference);
}

internal static class Catalog
{
    public const string Cpu = "CPU", Memory = "Memory", Storage = "Storage", Gpu = "GPU", Video = "Video", Stress = "Stress";

    public static readonly string[] Groups = [Cpu, Memory, Storage, Gpu, Video, Stress];

    public static readonly TestInfo[] Tests =
    [
        new("CpuSingle", Cpu, "Single-Core", "Integer, floating point and branchy work on one thread, each result checked", "Mops/s", 330),
        new("CpuMulti", Cpu, "Multi-Core", "The same work on every logical processor at once", "Mops/s", 3280),
        new("CpuVector", Cpu, "Vector (AVX)", "SIMD multiply-adds on every logical processor: the hottest CPU load", "GFLOPS", 700),
        new("MemBandwidth", Memory, "Bandwidth", "Reading, writing and copying memory on every core", "GB/s", 23.5),
        new("MemLatency", Memory, "Latency", "Random reads that each wait for the one before", "ns", 105, LowerIsBetter: true),
        new("MemIntegrity", Memory, "Integrity", "Patterns written over a large part of free memory and read back", "GB/s", 10.5),
        new("Storage", Storage, "Storage", "A test file written and read sequentially and at random, every block checked", "MB/s", 2000),
        new("Gpu3D", Gpu, "3D", "Lit, instanced 3D drawn as fast as the GPU can", "fps", 1100),
        new("GpuCompute", Gpu, "Compute", "Multiply-adds in a compute shader, every result checked", "GFLOPS", 13000),
        new("Vram", Gpu, "VRAM", "Most of the free video memory filled with patterns and checked", "GB/s", 335),
        new("Encode", Video, "Encode", "1080p frames through the GPU's H.264 and HEVC encoders", "fps", 330),
        new("Decode", Video, "Decode", "A 1080p H.264 clip through the GPU's decoder", "fps", 750),
        new("AllAtOnce", Stress, "All at Once", "CPU, memory and GPU loaded together, to see whether anything gives up, throttles or errs", "%", 100, Scored: false),
    ];

    public static TestInfo? Find(string id) => Tests.FirstOrDefault(t => t.Id == id);

    /// <summary>"CPU → Single-Core"; just "Storage" where the test is its group.</summary>
    public static string FullName(string group, string name) => T(name) == GroupTitle(group) ? T(name) : $"{GroupTitle(group)} → {T(name)}";

    public static string GroupTitle(string g) => g switch
    {
        Cpu => T("CPU"), Memory => T("Memory"), Storage => T("Storage"), Gpu => T("GPU"), Video => T("Video"), _ => T("Stress"),
    };

    /// <summary>The geometric mean of the scores (one weak test pulls it down, as in most suites); 0 if there are none.</summary>
    public static double Mean(IEnumerable<double> scores)
    {
        var list = scores.Where(s => s > 0).ToList();
        return list.Count == 0 ? 0 : Math.Exp(list.Average(Math.Log));
    }
}

/// <summary>One number a test measured.</summary>
public sealed class Metric
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public bool LowerIsBetter { get; set; }

    public Metric() { }
    public Metric(string name, double value, string unit, bool lowerIsBetter = false) { Name = name; Value = value; Unit = unit; LowerIsBetter = lowerIsBetter; }

    public string Text => Format(Value, Unit);

    public static string Format(double v, string unit)
    {
        if (double.IsNaN(v)) return "—";
        string n = Math.Abs(v) >= 1000 ? v.ToString("#,0") : v == Math.Round(v) ? v.ToString("0") : Math.Abs(v) >= 100 ? v.ToString("0") : Math.Abs(v) >= 10 ? v.ToString("0.0") : v.ToString("0.00");
        return unit.Length == 0 ? n : unit == "%" ? n + "%" : $"{n} {unit}";
    }
}

/// <summary>What the sensors read during a test (NaN where there was nothing to read).</summary>
public sealed class SensorSummary
{
    public double CpuLoadAvg { get; set; } = double.NaN;
    public double CpuClockAvg { get; set; } = double.NaN;
    public double CpuClockMin { get; set; } = double.NaN;
    public double CpuTempMax { get; set; } = double.NaN;
    public double CpuPowerMax { get; set; } = double.NaN;
    public double GpuLoadAvg { get; set; } = double.NaN;
    public double GpuClockAvg { get; set; } = double.NaN;
    public double GpuTempMax { get; set; } = double.NaN;
    public double GpuPowerMax { get; set; } = double.NaN;
    public double RamUsedMaxGB { get; set; } = double.NaN;
}

/// <summary>One test's result in a run (a test run several times, Repeat, has a result each time).</summary>
public sealed class TestResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public int Loop { get; set; } = 1;
    /// <summary>The headline value (Unit), and its score against the reference PC (0: not scored or it failed).</summary>
    public double Value { get; set; } = double.NaN;
    public string Unit { get; set; } = "";
    public double Score { get; set; }
    public List<Metric> Metrics { get; set; } = [];
    /// <summary>Wrong results found (a CPU result, a memory or VRAM pattern, a block read back, a lost GPU).</summary>
    public int Errors { get; set; }
    /// <summary>What went wrong, or anything worth knowing (throttling, a software encoder).</summary>
    public List<string> Notes { get; set; } = [];
    /// <summary>Why it couldn't run at all (no GPU, no encoder…).</summary>
    public string? Failed { get; set; }
    public bool Stopped { get; set; }
    /// <summary>What it ran on ("NVIDIA GeForce GTX 1080 Ti", "NVIDIA H.264 Encoder MFT"…).</summary>
    public string? Device { get; set; }
    public double Seconds { get; set; }
    public SensorSummary Sensors { get; set; } = new();
    /// <summary>The headline value through the test (for its graph), a point a second.</summary>
    public List<double> Trace { get; set; } = [];

    public Metric? MetricNamed(string name) => Metrics.FirstOrDefault(m => m.Name == name);
}

/// <summary>Something Windows logged during a run that matters for stability (an unexpected restart, a hardware
/// error, the graphics driver reset, a disk error).</summary>
public sealed class RunEvent
{
    public DateTime Time { get; set; }
    public string Source { get; set; } = "";
    public int Id { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>One run of the suite: the PC, what ran and every result. Saved in logs\stresstests (a file each), and
/// exported as the same JSON for comparing on another PC.</summary>
public sealed class StressRun
{
    public int Format { get; set; } = 1;
    public string AppVersion { get; set; } = "";
    public DateTime Started { get; set; }
    public DateTime Ended { get; set; }
    public SystemSpec System { get; set; } = new();
    public int SecondsPerTest { get; set; }
    public int Loops { get; set; } = 1;
    public StressLogging Logging { get; set; }
    public List<string> Tests { get; set; } = [];
    public List<TestResult> Results { get; set; } = [];
    public List<RunEvent> Events { get; set; } = [];
    public bool Stopped { get; set; }
    /// <summary>The app (or Windows) stopped during the run: found at the next start from the run's marker.</summary>
    public bool Interrupted { get; set; }
    public string? InterruptedDuring { get; set; }
    public double Score { get; set; }
    public Dictionary<string, double> GroupScores { get; set; } = [];

    [JsonIgnore] public string? File { get; set; }
    /// <summary>Opened from another PC's export (not one of this PC's runs).</summary>
    [JsonIgnore] public bool Imported { get; set; }

    public int Errors => Results.Sum(r => r.Errors);

    /// <summary>A test's results averaged over the loops it ran (null if it didn't run).</summary>
    public TestResult? Averaged(string id)
    {
        var list = Results.Where(r => r.Id == id).ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];
        var ok = list.Where(r => r.Failed == null && !double.IsNaN(r.Value)).ToList();
        var first = list[0];
        var avg = new TestResult
        {
            Id = id, Name = first.Name, Group = first.Group, Unit = first.Unit, Device = first.Device, Loop = 0,
            Value = ok.Count > 0 ? ok.Average(r => r.Value) : double.NaN,
            Score = ok.Count > 0 ? ok.Average(r => r.Score) : 0,
            Errors = list.Sum(r => r.Errors), Failed = ok.Count == 0 ? first.Failed : null,
            Seconds = list.Sum(r => r.Seconds), Stopped = list.Any(r => r.Stopped),
            Notes = list.SelectMany(r => r.Notes).Distinct().ToList(),
            Sensors = first.Sensors,
        };
        foreach (var m in ok.SelectMany(r => r.Metrics).GroupBy(m => m.Name))
            avg.Metrics.Add(new Metric(m.Key, m.Average(x => x.Value), m.First().Unit, m.First().LowerIsBetter));
        if (ok.Count > 1 && avg.Value > 0)
        {
            double spread = (ok.Max(r => r.Value) - ok.Min(r => r.Value)) / avg.Value * 100;
            avg.Metrics.Add(new Metric(T("Spread between loops"), spread, "%", true));
        }
        return avg;
    }

    /// <summary>The overall score and each group's, from the tests' scores averaged over the loops.</summary>
    public void ComputeScores()
    {
        var averaged = Results.Select(r => r.Id).Distinct().Select(Averaged).Where(r => r != null).Cast<TestResult>().ToList();
        Score = Catalog.Mean(averaged.Select(r => r.Score));
        GroupScores = averaged.Where(r => r.Score > 0).GroupBy(r => r.Group).ToDictionary(g => g.Key, g => Catalog.Mean(g.Select(r => r.Score)));
    }

    public string Title => $"{System.Pc} · {Started:g}";

    public static StressRun Read(string path)
    {
        var run = JsonSerializer.Deserialize<StressRun>(global::System.IO.File.ReadAllText(path), Json) ?? throw new InvalidDataException(T("Not a stress test run"));
        if (run.Results == null || run.System == null || run.Started == default) throw new InvalidDataException(T("Not a stress test run"));
        run.File = path;
        run.ComputeScores();
        return run;
    }

    public void Write(string path) => global::System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };
}
