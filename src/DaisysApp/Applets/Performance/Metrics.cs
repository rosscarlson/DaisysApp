using System.Globalization;

namespace DaisysApp.Applets.Performance;

public enum Metric
{
    Cpu, CpuClock, Ram, RamUsed, Commit, Gpu, GpuClock, Vram, VramUsed, GpuTemp, GpuPower, GpuFan,
    DiskRead, DiskWrite, DiskActive, NetDown, NetUp, CpuTemp, CpuPower,
}

/// <summary>What a metric is called, its unit, how it's shown, and the graph's fixed top (null = scale to the data).</summary>
public sealed record MetricInfo(Metric Id, string Key, string Name, string Unit, string Format, double? FixedMax)
{
    public string Text(double v) => double.IsNaN(v) ? "—" : v.ToString(Format, CultureInfo.CurrentCulture) + (Unit == "%" ? "%" : " " + Unit);

    public static readonly IReadOnlyList<MetricInfo> All = new MetricInfo[]
    {
        new(Metric.Cpu, "cpu", "CPU", "%", "0", 100),
        new(Metric.CpuClock, "cpuClock", "CPU clock", "GHz", "0.00", null),
        new(Metric.Ram, "ram", "Memory", "%", "0", 100),
        new(Metric.RamUsed, "ramUsed", "Memory in use", "GB", "0.0", null),
        new(Metric.Commit, "commit", "Committed memory", "GB", "0.0", null),
        new(Metric.Gpu, "gpu", "GPU", "%", "0", 100),
        new(Metric.GpuClock, "gpuClock", "GPU clock", "MHz", "0", null),
        new(Metric.Vram, "vram", "Video memory", "%", "0", 100),
        new(Metric.VramUsed, "vramUsed", "Video memory in use", "GB", "0.0", null),
        new(Metric.GpuTemp, "gpuTemp", "GPU temperature", "°C", "0", 100),
        new(Metric.GpuPower, "gpuPower", "GPU power", "W", "0", null),
        new(Metric.GpuFan, "gpuFan", "GPU fan", "%", "0", 100),
        new(Metric.DiskRead, "diskRead", "Disk read", "MB/s", "0.0", null),
        new(Metric.DiskWrite, "diskWrite", "Disk write", "MB/s", "0.0", null),
        new(Metric.DiskActive, "diskActive", "Busiest disk's active time", "%", "0", 100),
        new(Metric.NetDown, "netDown", "Download", "Mbit/s", "0.0", null),
        new(Metric.NetUp, "netUp", "Upload", "Mbit/s", "0.0", null),
        new(Metric.CpuTemp, "cpuTemp", "CPU temperature", "°C", "0", 100),
        new(Metric.CpuPower, "cpuPower", "CPU power", "W", "0", null),
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
    public Metric[] Graph => Graphs[0].Metrics;
    public IEnumerable<Metric> AllMetrics => Graphs.SelectMany(g => g.Metrics);

    public static readonly MetricGroup Cpu = new("CPU", new GraphSpec[] { new("CPU", Metric.Cpu), new("CPU clock", Metric.CpuClock) }, true);
    public static readonly MetricGroup Gpu = new("GPU", new GraphSpec[] { new("GPU", Metric.Gpu), new("GPU clock", Metric.GpuClock), new("GPU power", Metric.GpuPower) }, false);
    public static readonly MetricGroup Memory = new("Memory", new GraphSpec[] { new("Memory", Metric.Ram), new("In use and committed", Metric.RamUsed, Metric.Commit) }, true);
    public static readonly MetricGroup Vram = new("Video memory", new GraphSpec[] { new("Video memory", Metric.Vram), new("Video memory in use", Metric.VramUsed) }, false);
    public static readonly MetricGroup Disk = new("Disk", new GraphSpec[] { new("Read and write (all disks)", Metric.DiskRead, Metric.DiskWrite), new("Busiest disk's active time", Metric.DiskActive) }, false);
    public static readonly MetricGroup Network = new("Network", new GraphSpec[] { new("Download and upload", Metric.NetDown, Metric.NetUp) }, false);
    public static readonly MetricGroup Temperature = new("Temperatures", new GraphSpec[]
    {
        new("CPU and GPU temperature", Metric.CpuTemp, Metric.GpuTemp),
        new("CPU and GPU power", Metric.CpuPower, Metric.GpuPower),
        new("GPU fan", Metric.GpuFan),
    }, false);
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
