using System.Runtime.InteropServices;

namespace DaisysApp.Shared.Hardware;

/// <summary>
/// A small wrapper over Windows' Performance Data Helper (the API behind Performance Monitor and Task Manager's
/// numbers). Counters use English paths, so they work on any Windows language. Wildcard paths like
/// <c>\GPU Engine(*)\Utilization Percentage</c> return every instance in one call. Rate counters (per second, % time)
/// need two collections before they have a value.
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private IntPtr query;
    private readonly Dictionary<string, IntPtr> counters = new();

    public PdhQuery()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) query = IntPtr.Zero;
    }

    /// <summary>Adds a counter; false if Windows doesn't have it (older Windows, or a category that isn't installed).</summary>
    public bool Add(string path)
    {
        if (query == IntPtr.Zero) return false;
        if (counters.ContainsKey(path)) return true;
        if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out var counter) != 0) return false;
        counters[path] = counter;
        return true;
    }

    public bool Has(string path) => counters.ContainsKey(path);

    public void Collect()
    {
        if (query != IntPtr.Zero) PdhCollectQueryData(query);
    }

    /// <summary>The counter's value, or NaN if it has none yet (or isn't there).</summary>
    public double Value(string path)
    {
        if (!counters.TryGetValue(path, out var counter)) return double.NaN;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) != 0) return double.NaN;
        return v.CStatus is 0 or 1 ? v.Value : double.NaN; // PDH_CSTATUS_VALID_DATA / NEW_DATA
    }

    /// <summary>Every instance of a wildcard counter: instance name â†’ value (instances without data are left out).</summary>
    public Dictionary<string, double> Values(string path)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!counters.TryGetValue(path, out var counter)) return result;
        uint size = 0;
        int status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return result;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, buffer) != 0) return result;
            int itemSize = Marshal.SizeOf<ItemW>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<ItemW>(buffer + i * itemSize);
                if (item.CStatus is not (0 or 1)) continue;
                string? name = Marshal.PtrToStringUni(item.Name);
                if (name == null) continue;
                // the same instance name can appear more than once (e.g. two processes called "svchost" in the old
                // Process category); add them up
                result[name] = result.TryGetValue(name, out double prev) ? prev + item.Value : item.Value;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = IntPtr.Zero;
        counters.Clear();
    }

    private const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ItemW
    {
        public IntPtr Name;
        public uint CStatus;
        private uint pad;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
}

/// <summary>
/// NVIDIA's management library (nvml.dll, installed with the driver): GPU load, temperature, power, fan, clock and
/// memory for the first NVIDIA GPU, without admin rights. Unavailable on other GPUs.
/// </summary>
public sealed class Nvml : IDisposable
{
    private readonly IntPtr device;

    private Nvml(IntPtr device, string name, string driver, double powerLimitW)
    {
        this.device = device;
        Name = name;
        DriverVersion = driver;
        PowerLimitW = powerLimitW;
    }

    public string Name { get; }
    public string DriverVersion { get; }
    public double PowerLimitW { get; }

    public static Nvml? TryOpen()
    {
        try
        {
            if (!NativeLibrary.TryLoad("nvml.dll", typeof(Nvml).Assembly, DllImportSearchPath.System32, out _)) return null;
            if (nvmlInit_v2() != 0) return null;
            if (nvmlDeviceGetCount_v2(out uint count) != 0 || count == 0 || nvmlDeviceGetHandleByIndex_v2(0, out var dev) != 0)
            {
                nvmlShutdown();
                return null;
            }
            var buffer = new byte[96];
            string name = nvmlDeviceGetName(dev, buffer, (uint)buffer.Length) == 0 ? Text(buffer) : "NVIDIA GPU";
            buffer = new byte[96];
            string driver = nvmlSystemGetDriverVersion(buffer, (uint)buffer.Length) == 0 ? Text(buffer) : "";
            double limit = nvmlDeviceGetPowerManagementLimit(dev, out uint mw) == 0 ? mw / 1000.0 : double.NaN;
            return new Nvml(dev, name, driver, limit);
        }
        catch { return null; } // a broken or old driver
    }

    private static string Text(byte[] b)
    {
        int end = Array.IndexOf(b, (byte)0);
        return System.Text.Encoding.ASCII.GetString(b, 0, end < 0 ? b.Length : end).Trim();
    }

    public (double Gpu, double MemoryController) Utilization() =>
        nvmlDeviceGetUtilizationRates(device, out var u) == 0 ? (u.Gpu, u.Memory) : (double.NaN, double.NaN);

    public double TemperatureC() => nvmlDeviceGetTemperature(device, 0, out uint t) == 0 ? t : double.NaN;
    public double PowerW() => nvmlDeviceGetPowerUsage(device, out uint mw) == 0 ? mw / 1000.0 : double.NaN;
    public double FanPercent() => nvmlDeviceGetFanSpeed(device, out uint f) == 0 ? f : double.NaN;
    public double ClockMHz() => nvmlDeviceGetClockInfo(device, 0, out uint c) == 0 ? c : double.NaN;
    public double MemoryClockMHz() => nvmlDeviceGetClockInfo(device, 2, out uint c) == 0 ? c : double.NaN;

    public (double UsedBytes, double TotalBytes) Memory() =>
        nvmlDeviceGetMemoryInfo(device, out var m) == 0 ? (m.Used, m.Total) : (double.NaN, double.NaN);

    public void Dispose()
    {
        try { nvmlShutdown(); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Utilization_t { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory_t { public ulong Total, Free, Used; }

    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlShutdown();
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport("nvml.dll")] private static extern int nvmlSystemGetDriverVersion(byte[] version, uint length);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization_t utilization);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperature);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint milliwatts);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint percent);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint mhz);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory_t memory);
}
