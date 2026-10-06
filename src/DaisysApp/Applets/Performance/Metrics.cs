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
        new(Metric.DiskActive, "diskActive", "Disk active time", "%", "0", 100),
        new(Metric.NetDown, "netDown", "Download", "Mbit/s", "0.0", null),
        new(Metric.NetUp, "netUp", "Upload", "Mbit/s", "0.0", null),
        new(Metric.CpuTemp, "cpuTemp", "CPU temperature", "°C", "0", 100),
        new(Metric.CpuPower, "cpuPower", "CPU power", "W", "0", null),
    };

    /// <summary>Bytes per GB as Windows counts them (Task Manager, Explorer): 1024³.</summary>
    public const double GB = 1024d * 1024 * 1024;

    public static MetricInfo Of(Metric m) => All[(int)m];
}

/// <summary>A tile on the page, and the history window it opens: its metrics share one graph.</summary>
public sealed record MetricGroup(string Title, string GraphTitle, Metric[] Graph, Metric[] Extra, bool ShowProcesses)
{
    public static readonly MetricGroup Cpu = new("CPU", "CPU", new[] { Metric.Cpu }, new[] { Metric.CpuClock }, true);
    public static readonly MetricGroup Gpu = new("GPU", "GPU", new[] { Metric.Gpu }, new[] { Metric.GpuClock, Metric.GpuPower }, false);
    public static readonly MetricGroup Memory = new("Memory", "Memory", new[] { Metric.Ram }, new[] { Metric.RamUsed, Metric.Commit }, true);
    public static readonly MetricGroup Vram = new("Video memory", "Video memory", new[] { Metric.Vram }, new[] { Metric.VramUsed }, false);
    public static readonly MetricGroup Disk = new("Disk", "Read and write", new[] { Metric.DiskRead, Metric.DiskWrite }, new[] { Metric.DiskActive }, false);
    public static readonly MetricGroup Network = new("Network", "Download and upload", new[] { Metric.NetDown, Metric.NetUp }, Array.Empty<Metric>(), false);
    public static readonly MetricGroup Temperature = new("Temperatures", "CPU and GPU temperature", new[] { Metric.CpuTemp, Metric.GpuTemp }, new[] { Metric.CpuPower, Metric.GpuPower, Metric.GpuFan }, false);
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
    public int Processes { get; set; }
    public int Threads { get; set; }
}

/// <summary>One process's figures at the last process sample.</summary>
public sealed record ProcessSample(int Pid, string Name, double Cpu, double MemoryMB, double Gpu, double VramMB, double IoMBps, int Threads);
