using System.Runtime.InteropServices;
using Microsoft.Win32;
using Vortice.DXGI;

namespace DaisysApp.Applets.Performance;

/// <summary>The PC's fixed facts, read once: processor, memory, graphics card, Windows version.</summary>
public sealed class SystemInfo
{
    public string Cpu { get; init; } = "";
    public int Cores { get; init; }
    public int Threads { get; init; }
    public double RamBytes { get; init; }
    public string Gpu { get; init; } = "";
    public double VramBytes { get; init; }
    public string GpuDriver { get; init; } = "";
    public bool HasGpuSensors { get; init; }
    public string Windows { get; init; } = "";

    internal static SystemInfo Read(Nvml? nvml)
    {
        string cpu = "";
        string windows = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
            using var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = nt?.GetValue("ProductName") as string ?? "Windows";
            int build = int.TryParse(nt?.GetValue("CurrentBuildNumber") as string, out int b) ? b : 0;
            if (build >= 22000) product = product.Replace("Windows 10", "Windows 11"); // the registry still says 10
            windows = $"{product} {nt?.GetValue("DisplayVersion")} (build {build})".Trim();
        }
        catch { }

        string gpu = nvml?.Name ?? "";
        double vram = 0;
        try
        {
            // the adapter with the most dedicated memory (skips Microsoft's software renderer)
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var d = adapter.Description1;
                    if ((d.Flags & AdapterFlags.Software) != 0 || (double)d.DedicatedVideoMemory <= vram) continue;
                    vram = d.DedicatedVideoMemory;
                    if (nvml == null) gpu = d.Description;
                }
            }
        }
        catch { }
        if (nvml != null && nvml.Memory().TotalBytes is double total && !double.IsNaN(total)) vram = total;

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        double ram = GlobalMemoryStatusEx(ref mem) ? mem.ullTotalPhys : 0;

        return new SystemInfo
        {
            Cpu = cpu,
            Cores = PhysicalCores(),
            Threads = Environment.ProcessorCount,
            RamBytes = ram,
            Gpu = gpu,
            VramBytes = vram,
            GpuDriver = nvml?.DriverVersion ?? "",
            HasGpuSensors = nvml != null,
            Windows = windows,
        };
    }

    private static int PhysicalCores()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(0 /* RelationProcessorCore */, IntPtr.Zero, ref length);
        if (length == 0) return Environment.ProcessorCount;
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return Environment.ProcessorCount;
            int count = 0;
            for (int offset = 0; offset < length; count++)
                offset += Marshal.ReadInt32(buffer, offset + 4); // each entry: Relationship, Size, …
            return count;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
}
