using System.Globalization;

namespace DaisysApp.Applets.Performance;

public enum Metric
{
    Cpu, CpuClock, Ram, RamUsed, Commit, Gpu, GpuClock, Vram, VramUsed, GpuTemp, GpuPower, GpuFan,
    DiskRead, DiskWrite, DiskActive, NetDown, NetUp, CpuTemp, CpuPower,
    // the load of a second, third and fourth graphics card, on PCs that have them (new ones go at the end: the log's columns follow this order)
    Gpu2, Gpu3, Gpu4,
}

/// <summary>What a metric is called, its unit, how it's shown, and the graph's fixed top (null = scale to the data).</summary>
public sealed record MetricInfo(Metric Id, string Key, string Name, string Unit, string Format, double? FixedMax) : DaisysApp.Shared.Charts.IGraphUnit
{
    public string Text(double v) => double.IsNaN(v) ? "—" : v.ToString(Format, CultureInfo.CurrentCulture) + (Unit == "%" ? "%" : " " + Unit);

    public static readonly IReadOnlyList<MetricInfo> All = new MetricInfo[]
    {
        new(Metric.Cpu, "cpu", "CPU", "%", "0", 100),
        new(Metric.CpuClock, "cpuClock", T("CPU clock"), "GHz", "0.00", null),
        new(Metric.Ram, "ram", T("Memory"), "%", "0", 100),
        new(Metric.RamUsed, "ramUsed", T("Memory in use"), "GB", "0.0", null),
        new(Metric.Commit, "commit", T("Committed memory"), "GB", "0.0", null),
        new(Metric.Gpu, "gpu", T("GPU"), "%", "0", 100),
        new(Metric.GpuClock, "gpuClock", T("GPU clock"), "MHz", "0", null),
        new(Metric.Vram, "vram", T("Video memory"), "%", "0", 100),
        new(Metric.VramUsed, "vramUsed", T("Video memory in use"), "GB", "0.0", null),
        new(Metric.GpuTemp, "gpuTemp", T("GPU temperature"), "°C", "0", 100),
        new(Metric.GpuPower, "gpuPower", T("GPU power"), "W", "0", null),
        new(Metric.GpuFan, "gpuFan", T("GPU fan"), "%", "0", 100),
        new(Metric.DiskRead, "diskRead", T("Disk read"), "MB/s", "0.0", null),
        new(Metric.DiskWrite, "diskWrite", T("Disk write"), "MB/s", "0.0", null),
        new(Metric.DiskActive, "diskActive", T("Busiest disk's active time"), "%", "0", 100),
        new(Metric.NetDown, "netDown", T("Download"), "Mbit/s", "0.0", null),
        new(Metric.NetUp, "netUp", T("Upload"), "Mbit/s", "0.0", null),
        new(Metric.CpuTemp, "cpuTemp", T("CPU temperature"), "°C", "0", 100),
        new(Metric.CpuPower, "cpuPower", T("CPU power"), "W", "0", null),
        new(Metric.Gpu2, "gpu2", T("GPU 2"), "%", "0", 100),
        new(Metric.Gpu3, "gpu3", T("GPU 3"), "%", "0", 100),
        new(Metric.Gpu4, "gpu4", T("GPU 4"), "%", "0", 100),
    };

    /// <summary>Bytes per GB as Windows counts them (Task Manager, Explorer): 1024³.</summary>
    public const double GB = 1024d * 1024 * 1024;

    public static MetricInfo Of(Metric m) => All[(int)m];
}

/// <summary>One graph in a history window: metrics with the same unit, drawn together.</summary>
public sealed record GraphSpec(string Title, params Metric[] Metrics);

/// <summary>A tile on the page, and the history window it opens. The tile's graph is the first one.</summary>
public sealed record MetricGroup(string Title, GraphSpec[] Graphs, bool ShowProcesses)
{
    /// <summary>Stable name for settings (tile order, widgets): not the title, which is translated.</summary>
    public string Key { get; init; } = "";

    public Metric[] Graph => Graphs[0].Metrics;
    public IEnumerable<Metric> AllMetrics => Graphs.SelectMany(g => g.Metrics);

    public static readonly MetricGroup Cpu = new("CPU", new GraphSpec[] { new("CPU", Metric.Cpu), new(T("CPU clock"), Metric.CpuClock) }, true) { Key = "cpu" };
    public static readonly MetricGroup Gpu = new("GPU", new GraphSpec[] { new("GPU", Metric.Gpu), new(T("GPU clock"), Metric.GpuClock), new(T("GPU power"), Metric.GpuPower) }, false) { Key = "gpu" };
    public static readonly MetricGroup Memory = new(T("Memory"), new GraphSpec[] { new(T("Memory"), Metric.Ram), new(T("In use and committed"), Metric.RamUsed, Metric.Commit) }, true) { Key = "memory" };
    public static readonly MetricGroup Vram = new(T("Video memory"), new GraphSpec[] { new(T("Video memory"), Metric.Vram), new(T("Video memory in use"), Metric.VramUsed) }, false) { Key = "vram" };
    public static readonly MetricGroup Disk = new(T("Disk"), new GraphSpec[] { new(T("Read and write (all disks)"), Metric.DiskRead, Metric.DiskWrite), new(T("Busiest disk's active time"), Metric.DiskActive) }, false) { Key = "disk" };
    public static readonly MetricGroup Network = new(T("Network"), new GraphSpec[] { new(T("Download and upload"), Metric.NetDown, Metric.NetUp) }, false) { Key = "network" };
    public static readonly MetricGroup Temperature = new(T("Temperatures"), new GraphSpec[]
    {
        new(T("CPU and GPU temperature"), Metric.CpuTemp, Metric.GpuTemp),
        new(T("CPU and GPU power"), Metric.CpuPower, Metric.GpuPower),
        new(T("GPU fan"), Metric.GpuFan),
    }, false) { Key = "temperature" };

    public static readonly IReadOnlyList<MetricGroup> All = new[] { Cpu, Gpu, Memory, Vram, Disk, Network, Temperature };

    public static MetricGroup? ByKey(string? key) => All.FirstOrDefault(g => g.Key == key);
}

/// <summary>Every metric at one moment (NaN where it isn't available on this PC).</summary>
public sealed class PerfSample
{
    public PerfSample(DateTime time)
    {
        Time = time;
        Values = new double[MetricInfo.All.Count];
        Array.Fill(Values, double.NaN);
    }

    public DateTime Time { get; }
    public double[] Values { get; }
    public double this[Metric m]
    {
        get => Values[(int)m];
        set => Values[(int)m] = value;
    }

    public double[] Cores { get; set; } = Array.Empty<double>();
    /// <summary>The disk with the highest active time, e.g. "Disk 1 (D:)".</summary>
    public string BusiestDisk { get; set; } = "";

    public int Processes { get; set; }
    public int Threads { get; set; }
}

/// <summary>One process's figures at the last process sample.</summary>
/// <param name="GpuEngine">The GPU and engine it's using most, as Task Manager shows it ("GPU 0 - 3D"), or "" if none.</param>
public sealed record ProcessSample(int Pid, string Name, double Cpu, double MemoryMB, double Gpu, double VramMB, double IoMBps, int Threads, string GpuEngine = "");
