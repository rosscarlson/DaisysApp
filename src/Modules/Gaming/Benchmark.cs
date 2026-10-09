using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming;

/// <summary>One finished benchmark: a game's frame rates and the PC's load over a run.</summary>
public sealed class BenchmarkResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Starts as e.g. "Cyberpunk 2077 run 3"; the user can rename it.</summary>
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Game { get; set; } = "";
    public DateTime Started { get; set; }
    /// <summary>A run of a set length (and how long it was set to), or one that ran until it was stopped.</summary>
    public bool Timed { get; set; }
    public int PlannedSeconds { get; set; }
    /// <summary>How long it actually measured.</summary>
    public double Seconds { get; set; }
    public string Resolution { get; set; } = "";
    public string Gpu { get; set; } = "";

    public long Frames { get; set; }
    public double AvgFps { get; set; }
    public double Low1Fps { get; set; }
    public double Low01Fps { get; set; }
    /// <summary>The slowest and fastest whole second.</summary>
    public double MinFps { get; set; }
    public double MaxFps { get; set; }
    public double AvgFrametimeMs { get; set; }
    public double MaxFrametimeMs { get; set; }

    public double GpuPercent { get; set; } = double.NaN;
    public double GpuTempC { get; set; } = double.NaN;
    public double GpuTempMaxC { get; set; } = double.NaN;
    public double GpuClockMHz { get; set; } = double.NaN;
    public double GpuPowerW { get; set; } = double.NaN;
    public double VramMaxMB { get; set; } = double.NaN;
    public double CpuPercent { get; set; } = double.NaN;
    public double GameCpuPercent { get; set; } = double.NaN;
    public double RamMaxMB { get; set; } = double.NaN;

    /// <summary>The frame rate of each second, for the graph.</summary>
    public double[] FpsPerSecond { get; set; } = Array.Empty<double>();

    /// <summary>Whether two runs measure the same thing: the same game, and runs of the same set length (or, for ones
    /// stopped by hand, lengths within 10% or 5 seconds of each other).</summary>
    public bool ComparableWith(BenchmarkResult o)
    {
        if (!Exe.Equals(o.Exe, StringComparison.OrdinalIgnoreCase) || Timed != o.Timed) return false;
        if (Timed) return PlannedSeconds == o.PlannedSeconds;
        return Math.Abs(Seconds - o.Seconds) <= Math.Max(5, Math.Max(Seconds, o.Seconds) * 0.1);
    }
}

/// <summary>Every benchmark, in %APPDATA%\DaisysApp\GamingBenchmarks.json, newest first.</summary>
public sealed class BenchmarkStore
{
    private const string FileName = "GamingBenchmarks";
    public List<BenchmarkResult> Runs { get; set; } = new();

    // readings a PC doesn't have (e.g. GPU power on a non-NVIDIA card) are NaN, which plain JSON can't hold
    private static readonly JsonSerializerOptions Options = new(JsonStore.Options) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static BenchmarkStore Load()
    {
        try
        {
            string path = JsonStore.PathFor(FileName);
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<BenchmarkStore>(File.ReadAllText(path), Options) ?? new();
                s.Runs ??= new();
                return s;
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Loading benchmarks", ex); }
        return new BenchmarkStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsFolder);
            File.WriteAllText(JsonStore.PathFor(FileName), JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Saving benchmarks", ex); }
    }

    /// <summary>"Cyberpunk 2077 run 3": the next number for that game.</summary>
    public string NextName(string game) =>
        F("{0} run {1}", game, Runs.Count(r => r.Game.Equals(game, StringComparison.OrdinalIgnoreCase)) + 1);
}

/// <summary>A benchmark being measured: every frame of the game from the start, and a hardware reading a second.</summary>
internal sealed class BenchmarkRun
{
    public required CurrentGame Game { get; init; }
    public DateTime StartedAt { get; } = DateTime.Now;
    public long StartQpc { get; } = Stopwatch.GetTimestamp();
    public bool Timed { get; init; }
    public int PlannedSeconds { get; init; }
    public string Resolution { get; init; } = "";

    public readonly List<long> Frames = new();
    public readonly List<HardwareSample> Samples = new();
    private long lastFrame;

    /// <summary>The moment a timed run ends (QPC ticks), or long.MaxValue.</summary>
    public long EndQpc => Timed ? StartQpc + (long)PlannedSeconds * Stopwatch.Frequency : long.MaxValue;

    public TimeSpan Elapsed => TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - StartQpc) / (double)Stopwatch.Frequency);

    /// <summary>Adds the frames that arrived since the last call (only those inside the run).</summary>
    public void Collect(FrameMonitor monitor, List<long> buffer)
    {
        monitor.CopyFrames(Game.Pid, lastFrame == 0 ? StartQpc : lastFrame, buffer);
        long end = EndQpc;
        foreach (long t in buffer)
            if (t > lastFrame && t >= StartQpc && t <= end) Frames.Add(t);
        if (buffer.Count > 0) lastFrame = Math.Max(lastFrame, buffer[^1]);
    }

    /// <summary>The figures, or null if it caught too little to say anything (under 2 seconds of frames).</summary>
    public BenchmarkResult? Finish(string name, string gpu)
    {
        if (Frames.Count < 10) return null;
        double span = (Frames[^1] - Frames[0]) / (double)Stopwatch.Frequency;
        if (span < 2) return null;
        var stats = FrameStats.From(Frames, true);

        // a frame rate for each whole second from the start
        int seconds = (int)Math.Ceiling((Frames[^1] - StartQpc) / (double)Stopwatch.Frequency);
        var counts = new int[Math.Max(1, seconds)];
        foreach (long t in Frames)
            counts[Math.Clamp((int)((t - StartQpc) / Stopwatch.Frequency), 0, counts.Length - 1)]++;
        // the first and last seconds are partly outside the run: leave them out of the min / max when there's enough
        var whole = counts.Length > 3 ? counts.Skip(1).Take(counts.Length - 2).ToArray() : counts;

        double Avg(Func<HardwareSample, double> f) => Samples.Select(f).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average();
        double Max(Func<HardwareSample, double> f) => Samples.Select(f).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max();

        return new BenchmarkResult
        {
            Name = name,
            Exe = Game.Exe,
            Game = Game.Name,
            Started = StartedAt,
            Timed = Timed,
            PlannedSeconds = PlannedSeconds,
            Seconds = Math.Round(span, 1),
            Resolution = Resolution,
            Gpu = gpu,
            Frames = stats.Frames,
            AvgFps = stats.Fps,
            Low1Fps = stats.Low1Fps,
            Low01Fps = stats.Low01Fps,
            MinFps = whole.Min(),
            MaxFps = whole.Max(),
            AvgFrametimeMs = stats.FrametimeMs,
            MaxFrametimeMs = stats.MaxFrametimeMs,
            GpuPercent = Avg(s => s.GpuPercent),
            GpuTempC = Avg(s => s.GpuTempC),
            GpuTempMaxC = Max(s => s.GpuTempC),
            GpuClockMHz = Avg(s => s.GpuClockMHz),
            GpuPowerW = Avg(s => s.GpuPowerW),
            VramMaxMB = Max(s => s.VramUsedMB),
            CpuPercent = Avg(s => s.CpuPercent),
            GameCpuPercent = Avg(s => s.GameCpuPercent),
            RamMaxMB = Max(s => s.RamUsedMB),
            FpsPerSecond = counts.Select(c => (double)c).ToArray(),
        };
    }
}
