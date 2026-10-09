using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Performance;

/// <summary>
/// Samples the PC once a second on a background thread (CPU, memory, GPU, disk, network, temperature) and the process
/// list every couple of seconds (every 10 s while the tab isn't showing). Keeps the last 10 minutes in memory for the
/// live graphs and hands 10-second summaries to the log.
/// </summary>
public sealed partial class PerfMonitor : IDisposable
{
    public const int LiveSeconds = 600;
    private const int ProcessHistorySeconds = 600;

    private readonly object gate = new();
    private readonly LinkedList<PerfSample> live = new();
    private readonly Dictionary<int, LinkedList<(DateTime Time, ProcessSample Sample)>> processHistory = new();
    private readonly PerfLog log;
    private Thread? thread;
    private volatile bool running;
    private Nvml? nvml;
    private IDisposable? sensorUse;
    private PdhQuery? system, processes;
    private bool processV2;
    private long nextProcessSample;
    private IReadOnlyList<ProcessSample> lastProcesses = Array.Empty<ProcessSample>();

    public PerfMonitor(PerfLog log)
    {
        this.log = log;
        SeedFromLog();
    }

    /// <summary>
    /// Fills the live 10 minutes from the log, so a restart doesn't leave the tiles and live graphs empty: each logged
    /// 10-second average stands in for the seconds it covers until real readings take over.
    /// </summary>
    private void SeedFromLog()
    {
        try
        {
            var now = DateTime.Now;
            foreach (var row in log.Read(now.AddSeconds(-LiveSeconds), now))
                for (int i = 0; i < LogAggregator.Seconds; i++)
                {
                    var s = new PerfSample(row.Time.AddSeconds(i));
                    Array.Copy(row.Avg, s.Values, s.Values.Length);
                    live.AddLast(s);
                }
            while (live.Count > LiveSeconds) live.RemoveFirst();
        }
        catch (Exception ex) { ErrorLog.Write("Performance: reading the log at start", ex); }
    }

    /// <summary>Raised on the UI thread after each sample.</summary>
    public event Action<PerfSample>? Sampled;

    /// <summary>Raised on the UI thread after each process sample.</summary>
    public event Action<IReadOnlyList<ProcessSample>>? ProcessesSampled;

    /// <summary>Set while the tab is showing: processes are sampled every <see cref="ProcessIntervalMs"/> instead of every 10 s.</summary>
    public volatile bool Watching;

    /// <summary>How often the process list refreshes while the tab is showing (Performance → gear), 500 ms or more.</summary>
    public volatile int ProcessIntervalMs = 1000;

    /// <summary>Takes the next process list straight away (when the tab is shown, or the rate changed).</summary>
    public void RefreshProcessesSoon() => nextProcessSample = 0;

    public SystemInfo Info { get; private set; } = new();

    /// <summary>True while CPU temperature is coming from LibreHardwareMonitor.</summary>
    public bool HasCpuSensors => SensorHub.Connected;

    public IReadOnlyList<ProcessSample> LastProcesses
    {
        get { lock (gate) return lastProcesses; }
    }

    /// <summary>The samples of the last 10 minutes, oldest first.</summary>
    public List<PerfSample> Live()
    {
        lock (gate) return live.ToList();
    }

    /// <summary>One process's samples of the last 10 minutes (since Daisy's App started, if that's sooner).</summary>
    public List<(DateTime Time, ProcessSample Sample)> ProcessLive(int pid)
    {
        lock (gate) return processHistory.TryGetValue(pid, out var h) ? h.ToList() : new();
    }

    /// <summary>Every process's samples of the last 10 minutes, by process id.</summary>
    public Dictionary<int, List<(DateTime Time, ProcessSample Sample)>> AllProcessLive()
    {
        lock (gate) return processHistory.ToDictionary(p => p.Key, p => p.Value.ToList());
    }

    public void Start()
    {
        if (thread != null) return;
        sensorUse = SensorHub.Use(); // the Temperatures tile's CPU temperature (shared with the Sensors applet)
        running = true;
        thread = new Thread(Run) { IsBackground = true, Name = "DaisysApp-Performance", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    // ---------------------------------------------------------------- sampling

    private const string CpuUtil = @"\Processor Information(_Total)\% Processor Utility";
    private const string CpuPerf = @"\Processor Information(_Total)\% Processor Performance";
    private const string CpuFreq = @"\Processor Information(_Total)\Processor Frequency";
    private const string CoreUtil = @"\Processor Information(*)\% Processor Utility";
    // every disk separately: reads and writes are added up, and active time is the busiest disk's (an average over
    // all disks would hide one disk being flat out)
    private const string DiskRead = @"\PhysicalDisk(*)\Disk Read Bytes/sec";
    private const string DiskWrite = @"\PhysicalDisk(*)\Disk Write Bytes/sec";
    private const string DiskIdle = @"\PhysicalDisk(*)\% Idle Time";
    private const string NetDown = @"\Network Interface(*)\Bytes Received/sec";
    private const string NetUp = @"\Network Interface(*)\Bytes Sent/sec";
    private const string GpuEngine = @"\GPU Engine(*)\Utilization Percentage";
    private const string GpuAdapterMemory = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string GpuProcessMemory = @"\GPU Process Memory(*)\Dedicated Usage";
    private const string Committed = @"\Memory\Committed Bytes";
    private const string ProcessCount = @"\System\Processes";
    private const string ThreadCount = @"\System\Threads";

    private void Run()
    {
        try
        {
            nvml = Nvml.TryOpen();
            Info = SystemInfo.Read(nvml);
            system = new PdhQuery();
            foreach (var c in new[] { CpuUtil, CpuPerf, CpuFreq, CoreUtil, DiskRead, DiskWrite, DiskIdle, NetDown, NetUp, Committed, ProcessCount, ThreadCount })
                system.Add(c);
            if (nvml == null)
            {
                // without NVIDIA's library the GPU's numbers come from Windows' GPU counters (no temperature or power)
                system.Add(GpuEngine);
                system.Add(GpuAdapterMemory);
            }
            processes = CreateProcessQuery();
            system.Collect();
            processes?.Collect();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Performance monitor start", ex);
            return;
        }

        var aggregate = new LogAggregator();
        // a quarter-second tick: the system sample every second, the process list at its own rate
        const int Tick = 250;
        long nextSystem = Environment.TickCount64 + 1000;
        while (running)
        {
            Thread.Sleep(Tick - (int)(Environment.TickCount64 % Tick));
            long now = Environment.TickCount64;
            try
            {
                IReadOnlyList<ProcessSample>? procs = null;
                if (now >= nextProcessSample)
                {
                    procs = SampleProcesses(DateTime.Now);
                    nextProcessSample = now + (Watching ? Math.Max(500, ProcessIntervalMs) : 10000) - Tick / 2;
                    lock (gate) lastProcesses = procs;
                    aggregate.AddProcesses(procs);
                    Application.Current?.Dispatcher.BeginInvoke(() => ProcessesSampled?.Invoke(procs));
                }
                if (now < nextSystem) continue;
                nextSystem = now > nextSystem + 5000 ? now + 1000 : nextSystem + 1000; // after sleep / a long stall, start again

                var sample = Sample();

                lock (gate)
                {
                    live.AddLast(sample);
                    while (live.Count > LiveSeconds) live.RemoveFirst();
                }
                if (aggregate.Due(sample.Time)) log.Append(aggregate.Take());
                aggregate.Add(sample);

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    Sampled?.Invoke(sample);
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Performance sample", ex);
                Thread.Sleep(2000);
            }
        }
    }

    private PerfSample Sample()
    {
        var s = new PerfSample(DateTime.Now);
        var q = system!;
        q.Collect();

        s[Metric.Cpu] = Math.Clamp(q.Value(CpuUtil), 0, 100);
        double perf = q.Value(CpuPerf), freq = q.Value(CpuFreq);
        s[Metric.CpuClock] = perf * freq / 100 / 1000;
        s.Cores = q.Values(CoreUtil)
            .Where(p => !p.Key.Contains("_Total", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => CoreOrder(p.Key))
            .Select(p => Math.Clamp(p.Value, 0, 100)).ToArray();

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            double used = mem.ullTotalPhys - mem.ullAvailPhys;
            s[Metric.Ram] = used / mem.ullTotalPhys * 100;
            s[Metric.RamUsed] = used / MetricInfo.GB;
        }
        s[Metric.Commit] = q.Value(Committed) / MetricInfo.GB;
        if (SensorHub.Latest is { } hw)
        {
            s[Metric.CpuTemp] = HardwareMonitor.CpuTemp(hw);
            s[Metric.CpuPower] = HardwareMonitor.CpuPower(hw);
        }

        if (nvml != null)
        {
            s[Metric.Gpu] = nvml.Utilization().Gpu;
            s[Metric.GpuTemp] = nvml.TemperatureC();
            s[Metric.GpuPower] = nvml.PowerW();
            s[Metric.GpuFan] = nvml.FanPercent();
            s[Metric.GpuClock] = nvml.ClockMHz();
            var (vramUsed, vramTotal) = nvml.Memory();
            s[Metric.VramUsed] = vramUsed / MetricInfo.GB;
            s[Metric.Vram] = vramUsed / vramTotal * 100;
        }
        else
        {
            // like Task Manager: the busiest engine type (3D, copy, video decode…) across all processes
            s[Metric.Gpu] = Math.Clamp(EngineTypes(q.Values(GpuEngine)).Values.DefaultIfEmpty(double.NaN).Max(), 0, 100);
            double used = q.Values(GpuAdapterMemory).Values.DefaultIfEmpty(double.NaN).Max();
            s[Metric.VramUsed] = used / MetricInfo.GB;
            if (Info.VramBytes > 0) s[Metric.Vram] = used / Info.VramBytes * 100;
        }

        if (SensorHub.Latest is { } gpuHw)
        {
            if (double.IsNaN(s[Metric.GpuTemp])) s[Metric.GpuTemp] = HardwareMonitor.GpuTemp(gpuHw);
            if (double.IsNaN(s[Metric.GpuPower])) s[Metric.GpuPower] = HardwareMonitor.GpuPower(gpuHw);
            if (double.IsNaN(s[Metric.GpuFan])) s[Metric.GpuFan] = HardwareMonitor.GpuFan(gpuHw);
        }

        s[Metric.DiskRead] = Disks(q.Values(DiskRead)).Values.Sum() / 1e6;
        s[Metric.DiskWrite] = Disks(q.Values(DiskWrite)).Values.Sum() / 1e6;
        var active = Disks(q.Values(DiskIdle)).ToDictionary(p => p.Key, p => Math.Clamp(100 - p.Value, 0, 100));
        if (active.Count > 0)
        {
            var busiest = active.MaxBy(p => p.Value);
            s[Metric.DiskActive] = busiest.Value;
            s.BusiestDisk = DiskName(busiest.Key);
        }
        s[Metric.NetDown] = q.Values(NetDown).Values.Sum() * 8 / 1e6;
        s[Metric.NetUp] = q.Values(NetUp).Values.Sum() * 8 / 1e6;
        s.Processes = (int)SafeInt(q.Value(ProcessCount));
        s.Threads = (int)SafeInt(q.Value(ThreadCount));
        return s;
    }

    private static double SafeInt(double v) => double.IsNaN(v) ? 0 : v;

    private static Dictionary<string, double> Disks(Dictionary<string, double> instances) =>
        instances.Where(p => !p.Key.Equals("_Total", StringComparison.OrdinalIgnoreCase)).ToDictionary(p => p.Key, p => p.Value);

    /// <summary>"1 D: E:" → "Disk 1 (D: E:)", like Task Manager.</summary>
    private static string DiskName(string instance)
    {
        var parts = instance.Split(' ', 2);
        return parts.Length == 2 ? F("Disk {0} ({1})", parts[0], parts[1]) : F("Disk {0}", instance);
    }

    /// <summary>"0,11" → 11, so cores come out in order (0,10 would otherwise sort before 0,2).</summary>
    private static int CoreOrder(string instance)
    {
        var parts = instance.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int g) && int.TryParse(parts[1], out int c) ? g * 1000 + c : int.MaxValue;
    }

    [GeneratedRegex(@"pid_(\d+)_.*engtype_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EngineInstance();

    [GeneratedRegex(@"^pid_(\d+)_luid_(0x[0-9a-f]+)_(0x[0-9a-f]+)_.*engtype_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EngineWithAdapter();

    [GeneratedRegex(@"^pid_(\d+)_", RegexOptions.IgnoreCase)]
    private static partial Regex PidInstance();

    /// <summary>"GPU 0 - 3D" for an adapter LUID and engine type ("VideoDecode" → "Video Decode").</summary>
    private string EngineLabel(long luid, string engine)
    {
        string gpu = Info.Adapters.TryGetValue(luid, out var a) ? $"GPU {a.Index}" : "GPU";
        engine = Regex.Replace(engine.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
        return $"{gpu} - {engine}";
    }

    /// <summary>GPU engine instances summed per engine type.</summary>
    private static Dictionary<string, double> EngineTypes(Dictionary<string, double> engines)
    {
        var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, v) in engines)
            if (EngineInstance().Match(name) is { Success: true } m) byType[m.Groups[2].Value] = byType.GetValueOrDefault(m.Groups[2].Value) + v;
        return byType;
    }

    // ---------------------------------------------------------------- processes

    private PdhQuery? CreateProcessQuery()
    {
        var q = new PdhQuery();
        // "Process V2" names instances "name:pid", so processes with the same name don't get mixed up (Windows 10 1809+)
        processV2 = q.Add(@"\Process V2(*)\% Processor Time");
        string cat = processV2 ? "Process V2" : "Process";
        if (!processV2 && !q.Add(@"\Process(*)\% Processor Time")) { q.Dispose(); return null; }
        q.Add($@"\{cat}(*)\Working Set - Private");
        q.Add($@"\{cat}(*)\IO Data Bytes/sec");
        q.Add($@"\{cat}(*)\Thread Count");
        if (!processV2) q.Add(@"\Process(*)\ID Process");
        q.Add(GpuEngine);
        q.Add(GpuProcessMemory);
        return q;
    }

    private IReadOnlyList<ProcessSample> SampleProcesses(DateTime time)
    {
        var q = processes;
        if (q == null) return Array.Empty<ProcessSample>();
        q.Collect();
        string cat = processV2 ? "Process V2" : "Process";
        var cpu = q.Values($@"\{cat}(*)\% Processor Time");
        var mem = q.Values($@"\{cat}(*)\Working Set - Private");
        var io = q.Values($@"\{cat}(*)\IO Data Bytes/sec");
        var threads = q.Values($@"\{cat}(*)\Thread Count");
        var ids = processV2 ? null : q.Values(@"\Process(*)\ID Process");

        // per process GPU: the busiest engine (adapter + engine type), like Task Manager; and dedicated video memory
        var gpu = new Dictionary<int, Dictionary<(long Luid, string Type), double>>();
        foreach (var (name, v) in q.Values(GpuEngine))
        {
            if (EngineWithAdapter().Match(name) is not { Success: true } m || !int.TryParse(m.Groups[1].Value, out int pid)) continue;
            long luid = (Convert.ToInt64(m.Groups[2].Value, 16) << 32) | Convert.ToInt64(m.Groups[3].Value, 16);
            var types = gpu.TryGetValue(pid, out var t) ? t : gpu[pid] = new();
            var key = (luid, m.Groups[4].Value);
            types[key] = types.GetValueOrDefault(key) + v;
        }
        var vram = new Dictionary<int, double>();
        foreach (var (name, v) in q.Values(GpuProcessMemory))
            if (PidInstance().Match(name) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int pid)) vram[pid] = vram.GetValueOrDefault(pid) + v;

        int cores = Environment.ProcessorCount;
        var list = new List<ProcessSample>(cpu.Count);
        foreach (var (instance, cpuValue) in cpu)
        {
            if (instance.StartsWith("_Total", StringComparison.OrdinalIgnoreCase) || instance.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)) continue;
            string name;
            int pid;
            if (processV2)
            {
                int colon = instance.LastIndexOf(':');
                if (colon < 0 || !int.TryParse(instance[(colon + 1)..], out pid)) continue;
                name = instance[..colon];
            }
            else
            {
                name = instance.Split('#')[0];
                pid = (int)SafeInt(ids!.GetValueOrDefault(instance, double.NaN));
            }
            if (pid == 0) continue;
            list.Add(new ProcessSample(pid, name,
                Math.Clamp(cpuValue / cores, 0, 100),
                mem.GetValueOrDefault(instance) / 1e6,
                gpu.TryGetValue(pid, out var g) && g.Count > 0 ? Math.Clamp(g.Values.Max(), 0, 100) : 0,
                vram.GetValueOrDefault(pid) / 1e6,
                io.GetValueOrDefault(instance) / 1e6,
                (int)threads.GetValueOrDefault(instance),
                g != null && g.Count > 0 && g.Values.Max() >= 0.05 ? EngineLabel(g.MaxBy(e => e.Value).Key.Luid, g.MaxBy(e => e.Value).Key.Type) : ""));
        }

        lock (gate)
        {
            foreach (var p in list)
            {
                var h = processHistory.TryGetValue(p.Pid, out var x) ? x : processHistory[p.Pid] = new();
                h.AddLast((time, p));
                while (h.Count > 0 && (time - h.First!.Value.Time).TotalSeconds > ProcessHistorySeconds) h.RemoveFirst();
            }
            var gone = processHistory.Where(p => p.Value.Count == 0 || (time - p.Value.Last!.Value.Time).TotalSeconds > ProcessHistorySeconds).Select(p => p.Key).ToList();
            foreach (int pid in gone) processHistory.Remove(pid);
        }
        return list;
    }

    public void Dispose()
    {
        running = false;
        thread?.Join(3000);
        system?.Dispose();
        processes?.Dispose();
        nvml?.Dispose();
        sensorUse?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
}
