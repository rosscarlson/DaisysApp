namespace DaisysApp.Applets.Performance;

/// <summary>A warning level: at or above <see cref="Warn"/> it's orange, at or above <see cref="Critical"/> red.</summary>
public sealed record LimitDef(string Key, string Name, string Unit, double Warn, double Critical);

/// <summary>
/// The orange and red levels for the tiles and the process list (Settings are edited from each history window's
/// Warnings button, and the process list's gear). Saved in Performance.json; anything not set uses the defaults here.
/// </summary>
public sealed class PerfLimits
{
    public const string CpuCore = "cpuCore";

    public static readonly IReadOnlyList<LimitDef> Tiles = new LimitDef[]
    {
        new("cpu", "CPU", "%", 80, 95),
        new(CpuCore, T("Busiest CPU core"), "%", 90, 98),
        new("gpu", "GPU", "%", 90, 98),
        new("ram", T("Memory"), "%", 80, 90),
        new("vram", T("Video memory"), "%", 85, 95),
        new("diskActive", T("Busiest disk's active time"), "%", 80, 95),
        new("netDown", T("Download"), "Mbit/s", 400, 800),
        new("netUp", T("Upload"), "Mbit/s", 200, 400),
        new("cpuTemp", T("CPU temperature"), "°C", 80, 90),
        new("gpuTemp", T("GPU temperature"), "°C", 80, 88),
    };

    public static readonly IReadOnlyList<LimitDef> Processes = new LimitDef[]
    {
        new("proc.cpu", "CPU", "%", 25, 50),
        new("proc.ram", "RAM", "MB", 4096, 8192),
        new("proc.gpu", "GPU", "%", 80, 95),
        new("proc.vram", "VRAM", "MB", 4096, 8192),
        new("proc.disk", T("Disk"), "MB/s", 100, 300),
    };

    private readonly PerformanceSettings settings;

    public PerfLimits(PerformanceSettings settings) => this.settings = settings;

    /// <summary>Raised after levels are changed.</summary>
    public event Action? Changed;

    public static LimitDef? Def(string key) => Tiles.Concat(Processes).FirstOrDefault(d => d.Key == key);

    public (double Warn, double Critical) Get(string key)
    {
        if (settings.Limits.TryGetValue(key, out var v) && v.Length == 2) return (v[0], v[1]);
        return Def(key) is { } d ? (d.Warn, d.Critical) : (double.NaN, double.NaN);
    }

    public void Set(IEnumerable<(string Key, double Warn, double Critical)> values)
    {
        foreach (var (key, warn, critical) in values)
        {
            var d = Def(key);
            if (d != null && warn == d.Warn && critical == d.Critical) settings.Limits.Remove(key); // back to the default
            else settings.Limits[key] = new[] { warn, critical };
        }
        settings.Save();
        Changed?.Invoke();
    }

    /// <summary>0 = fine, 1 = orange, 2 = red.</summary>
    public int Severity(string key, double value)
    {
        if (double.IsNaN(value)) return 0;
        var (warn, critical) = Get(key);
        return value >= critical ? 2 : value >= warn ? 1 : 0;
    }

    public int ProcessSeverity(ProcessRow p) => new[]
    {
        Severity("proc.cpu", p.Cpu), Severity("proc.ram", p.MemoryMB), Severity("proc.gpu", p.Gpu),
        // Windows counts every app's on-screen windows as the desktop compositor's (dwm) video memory, so its figure
        // is always huge; it isn't judged on it
        p.Name.Equals("dwm", StringComparison.OrdinalIgnoreCase) ? 0 : Severity("proc.vram", p.VramMB),
        Severity("proc.disk", p.IoMBps),
    }.Max();

    /// <summary>The limit key for a metric, if it has one.</summary>
    public static string? KeyOf(Metric m) => Tiles.FirstOrDefault(d => d.Key == MetricInfo.Of(m).Key)?.Key;
}
