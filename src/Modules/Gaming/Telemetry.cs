using System.Diagnostics;
using System.Runtime.InteropServices;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Gaming;

/// <summary>Frame rate figures from a run of frame times.</summary>
internal readonly record struct FrameStats(double Fps, double FrametimeMs, double Low1Fps, double Low01Fps, double MaxFrametimeMs, int Frames)
{
    public static readonly FrameStats None = new(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0);

    /// <summary>
    /// From frame timestamps (QPC ticks, oldest first). The lows are the frame rate at the 99th and 99.9th percentile
    /// frame time (the slowest 1% / 0.1% of frames), the usual way to show stutter.
    /// </summary>
    public static FrameStats From(List<long> times, bool lows, double[]? scratch = null)
    {
        int n = times.Count - 1;
        if (n < 1) return None;
        double toMs = 1000.0 / Stopwatch.Frequency;
        double span = (times[^1] - times[0]) * toMs;
        double max = 0;
        var ft = lows ? (scratch != null && scratch.Length >= n ? scratch : new double[n]) : null;
        for (int i = 0; i < n; i++)
        {
            double d = (times[i + 1] - times[i]) * toMs;
            if (d > max) max = d;
            if (ft != null) ft[i] = d;
        }
        double avg = span / n;
        double low1 = double.NaN, low01 = double.NaN;
        if (ft != null)
        {
            Array.Sort(ft, 0, n);
            low1 = 1000.0 / ft[Math.Min(n - 1, (int)(n * 0.99))];
            low01 = 1000.0 / ft[Math.Min(n - 1, (int)(n * 0.999))];
        }
        return new FrameStats(1000.0 / avg, avg, low1, low01, max, n);
    }
}

/// <summary>One reading of the PC's load, taken once a second.</summary>
internal sealed record HardwareSample
{
    public double GpuPercent { get; init; } = double.NaN;
    public double GpuTempC { get; init; } = double.NaN;
    public double GpuClockMHz { get; init; } = double.NaN;
    public double GpuPowerW { get; init; } = double.NaN;
    public double VramUsedMB { get; init; } = double.NaN;
    public double VramTotalMB { get; init; } = double.NaN;
    public double CpuPercent { get; init; } = double.NaN;
    public double GameCpuPercent { get; init; } = double.NaN;
    public double RamUsedMB { get; init; } = double.NaN;
    public double RamTotalMB { get; init; } = double.NaN;
    public double GameRamMB { get; init; } = double.NaN;

    public static readonly HardwareSample Empty = new();
}

/// <summary>
/// GPU (NVIDIA's management library; other GPUs: video memory only, from Windows' counters), CPU and memory. Each
/// reading is a handful of cheap calls.
/// </summary>
internal sealed class Telemetry : IDisposable
{
    private Nvml? nvml;
    private bool nvmlTried;
    private PdhQuery? pdh;
    private const string VramCounter = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private double vramTotalMB = double.NaN;
    private long lastIdle, lastKernel, lastUser;
    private uint gamePid;
    private long gameCpuLast, gameTimeLast;

    public string GpuName => nvml?.Name ?? "";

    public HardwareSample Read(uint pid)
    {
        if (!nvmlTried)
        {
            nvmlTried = true;
            nvml = Nvml.TryOpen();
            if (nvml == null)
            {
                pdh = new PdhQuery();
                if (!pdh.Add(VramCounter)) { pdh.Dispose(); pdh = null; }
                vramTotalMB = DedicatedVideoMemoryMB();
            }
        }

        double gpu = double.NaN, temp = double.NaN, clock = double.NaN, power = double.NaN, used = double.NaN, total = vramTotalMB;
        if (nvml != null)
        {
            gpu = nvml.Utilization().Gpu;
            temp = nvml.TemperatureC();
            clock = nvml.ClockMHz();
            power = nvml.PowerW();
            var m = nvml.Memory();
            used = m.UsedBytes / 1048576;
            total = m.TotalBytes / 1048576;
        }
        else if (pdh != null)
        {
            pdh.Collect();
            var v = pdh.Values(VramCounter);
            if (v.Count > 0) used = v.Values.Sum() / 1048576;
        }

        double cpu = double.NaN;
        if (GetSystemTimes(out long idle, out long kernel, out long user))
        {
            long di = idle - lastIdle, dk = kernel - lastKernel, du = user - lastUser;
            if (lastKernel != 0 && dk + du > 0) cpu = Math.Clamp(100.0 * (dk + du - di) / (dk + du), 0, 100);
            lastIdle = idle; lastKernel = kernel; lastUser = user;
        }

        double ramUsed = double.NaN, ramTotal = double.NaN;
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            ramTotal = mem.ullTotalPhys / 1048576.0;
            ramUsed = (mem.ullTotalPhys - mem.ullAvailPhys) / 1048576.0;
        }

        double gameCpu = double.NaN, gameRam = double.NaN;
        if (pid != 0)
        {
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION: enough for times and memory, and anti-cheat allows it
            if (h != IntPtr.Zero)
            {
                try
                {
                    if (GetProcessTimes(h, out _, out _, out long k, out long u))
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (pid == gamePid && gameTimeLast != 0)
                        {
                            double wall = (now - gameTimeLast) / (double)Stopwatch.Frequency * 1e7;
                            gameCpu = Math.Clamp(100.0 * (k + u - gameCpuLast) / (wall * Environment.ProcessorCount), 0, 100);
                        }
                        gamePid = pid;
                        gameCpuLast = k + u;
                        gameTimeLast = now;
                    }
                    var pmc = new PROCESS_MEMORY_COUNTERS { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>() };
                    if (K32GetProcessMemoryInfo(h, ref pmc, pmc.cb)) gameRam = pmc.WorkingSetSize / 1048576.0;
                }
                finally { CloseHandle(h); }
            }
        }

        return new HardwareSample
        {
            GpuPercent = gpu, GpuTempC = temp, GpuClockMHz = clock, GpuPowerW = power,
            VramUsedMB = used, VramTotalMB = total,
            CpuPercent = cpu, GameCpuPercent = gameCpu,
            RamUsedMB = ramUsed, RamTotalMB = ramTotal, GameRamMB = gameRam,
        };
    }

    private static double DedicatedVideoMemoryMB()
    {
        try
        {
            using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
            double best = 0;
            for (uint i = 0; factory.EnumAdapters1(i, out var a).Success; i++)
                using (a) best = Math.Max(best, (double)a.Description1.DedicatedVideoMemory / 1048576);
            return best > 0 ? best : double.NaN;
        }
        catch { return double.NaN; }
    }

    public void Dispose()
    {
        nvml?.Dispose();
        pdh?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool K32GetProcessMemoryInfo(IntPtr process, ref PROCESS_MEMORY_COUNTERS counters, uint size);
}
