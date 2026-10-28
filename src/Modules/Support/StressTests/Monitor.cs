using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>One second's readings (NaN where there's nothing to read).</summary>
internal sealed record Reading(DateTime Time, double CpuLoad, double CpuMHz, double CpuTemp, double CpuPower,
    double GpuLoad, double GpuMHz, double GpuTemp, double GpuPower, double RamUsedGB)
{
    public string Text()
    {
        static string N(double v, string f, string unit) => double.IsNaN(v) ? "" : v.ToString(f) + unit;
        var cpu = string.Join(" ", new[] { N(CpuLoad, "0", "%"), double.IsNaN(CpuMHz) ? "" : $"{CpuMHz / 1000:0.00} GHz", N(CpuTemp, "0", " °C"), N(CpuPower, "0", " W") }.Where(s => s.Length > 0));
        var gpu = string.Join(" ", new[] { N(GpuLoad, "0", "%"), N(GpuMHz, "0", " MHz"), N(GpuTemp, "0", " °C"), N(GpuPower, "0", " W") }.Where(s => s.Length > 0));
        return string.Join(" · ", new[] { cpu.Length > 0 ? "CPU " + cpu : "", gpu.Length > 0 ? "GPU " + gpu : "", N(RamUsedGB, "0.0", " GB RAM used") }.Where(s => s.Length > 0));
    }
}

/// <summary>
/// The hardware's readings while a run goes: CPU load and clock (Windows' counters), CPU and GPU temperature and power
/// (NVIDIA's library on NVIDIA cards; LibreHardwareMonitor's when it's running), memory in use. Read once a second on its
/// own thread; each test's summary (peaks and averages) is made from them.
/// </summary>
internal sealed class Monitor : IDisposable
{
    private readonly Thread thread;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly List<Reading> readings = [];
    private readonly int baseMHz;
    private IDisposable? sensors;

    public Reading? Latest { get; private set; }

    /// <summary>Raised on the monitor's thread with each reading.</summary>
    public event Action<Reading>? Read;

    public Monitor(int baseMHz)
    {
        this.baseMHz = baseMHz;
        try { sensors = SensorHub.Use(); } catch { }
        thread = new Thread(Run) { IsBackground = true, Name = "StressTests-Monitor", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    private const string Utility = @"\Processor Information(_Total)\% Processor Utility";
    private const string Performance = @"\Processor Information(_Total)\% Processor Performance";
    private const string Time = @"\Processor(_Total)\% Processor Time";
    private const string Gpu3D = @"\GPU Engine(*engtype_3D)\Utilization Percentage";

    private void Run()
    {
        PdhQuery? pdh = null;
        Nvml? nvml = null;
        try
        {
            pdh = new PdhQuery();
            bool utility = pdh.Add(Utility);
            if (!utility) pdh.Add(Time);
            pdh.Add(Performance);
            pdh.Add(Gpu3D);
            pdh.Collect();
        }
        catch { pdh = null; }
        try { nvml = Nvml.TryOpen(); } catch { }
        try
        {
            while (!stop.Token.WaitHandle.WaitOne(1000 - (int)(Environment.TickCount64 % 1000)))
            {
                try
                {
                    pdh?.Collect();
                    double load = pdh == null ? double.NaN : pdh.Has(Utility) ? Math.Min(100, pdh.Value(Utility)) : pdh.Value(Time);
                    double perf = pdh?.Value(Performance) ?? double.NaN;
                    double mhz = baseMHz > 0 && !double.IsNaN(perf) ? baseMHz * perf / 100 : double.NaN;
                    double gpuLoad = double.NaN, gpuMHz = double.NaN, gpuTemp = double.NaN, gpuPower = double.NaN, cpuTemp = double.NaN, cpuPower = double.NaN;
                    if (nvml != null)
                    {
                        gpuLoad = nvml.Utilization().Gpu;
                        gpuMHz = nvml.ClockMHz();
                        gpuTemp = nvml.TemperatureC();
                        gpuPower = nvml.PowerW();
                    }
                    else if (pdh != null)
                    {
                        var engines = pdh.Values(Gpu3D);
                        if (engines.Count > 0) gpuLoad = Math.Min(100, engines.Values.Sum());
                    }
                    if (SensorHub.Latest is { } hw && SensorHub.Connected)
                    {
                        cpuTemp = HardwareMonitor.CpuTemp(hw);
                        cpuPower = HardwareMonitor.CpuPower(hw);
                        if (double.IsNaN(gpuTemp)) gpuTemp = HardwareMonitor.GpuTemp(hw);
                        if (double.IsNaN(gpuPower)) gpuPower = HardwareMonitor.GpuPower(hw);
                    }
                    Mem.Status(out ulong total, out ulong available);
                    var r = new Reading(DateTime.Now, load, mhz, cpuTemp, cpuPower, gpuLoad, gpuMHz, gpuTemp, gpuPower, (total - available) / 1073741824.0);
                    lock (gate)
                    {
                        readings.Add(r);
                        if (readings.Count > 4 * 3600) readings.RemoveRange(0, 3600);
                    }
                    Latest = r;
                    Read?.Invoke(r);
                }
                catch (Exception ex) { Logging.Log.Here.Debug(() => $"Stress Tests: reading the sensors: {ex.Message}"); }
            }
        }
        finally
        {
            pdh?.Dispose();
            nvml?.Dispose();
        }
    }

    /// <summary>Peaks and averages of the readings since <paramref name="from"/>.</summary>
    public SensorSummary Summary(DateTime from)
    {
        List<Reading> list;
        lock (gate) list = readings.Where(r => r.Time >= from).ToList();
        static double Avg(IEnumerable<double> v) { var l = v.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : Math.Round(l.Average(), 1); }
        static double Max(IEnumerable<double> v) { var l = v.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : Math.Round(l.Max(), 1); }
        static double Min(IEnumerable<double> v) { var l = v.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : Math.Round(l.Min(), 1); }
        // the first second is the test starting up
        var steady = list.Count > 3 ? list.Skip(1).ToList() : list;
        return new SensorSummary
        {
            CpuLoadAvg = Avg(steady.Select(r => r.CpuLoad)), CpuClockAvg = Avg(steady.Select(r => r.CpuMHz)), CpuClockMin = Min(steady.Select(r => r.CpuMHz)),
            CpuTempMax = Max(list.Select(r => r.CpuTemp)), CpuPowerMax = Max(list.Select(r => r.CpuPower)),
            GpuLoadAvg = Avg(steady.Select(r => r.GpuLoad)), GpuClockAvg = Avg(steady.Select(r => r.GpuMHz)),
            GpuTempMax = Max(list.Select(r => r.GpuTemp)), GpuPowerMax = Max(list.Select(r => r.GpuPower)),
            RamUsedMaxGB = Max(list.Select(r => r.RamUsedGB)),
        };
    }

    /// <summary>The CPU clock (MHz) of each reading since <paramref name="from"/>, for spotting throttling.</summary>
    public List<double> CpuClocks(DateTime from)
    {
        lock (gate) return readings.Where(r => r.Time >= from && !double.IsNaN(r.CpuMHz)).Select(r => r.CpuMHz).ToList();
    }

    public void Dispose()
    {
        stop.Cancel();
        thread.Join(2000);
        sensors?.Dispose();
        sensors = null;
    }
}
