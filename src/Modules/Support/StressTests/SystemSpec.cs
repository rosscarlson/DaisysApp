using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Vortice.DXGI;

namespace DaisysApp.Applets.Support.StressTests;

public sealed class GpuSpec
{
    public string Name { get; set; } = "";
    public double VramGB { get; set; }
    public string Driver { get; set; } = "";
    public override string ToString() => VramGB > 0 ? $"{Name} ({VramGB:0.#} GB)" : Name;
}

public sealed class DriveSpec
{
    public string Letter { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>"NVMe SSD", "SATA HDD"…</summary>
    public string Kind { get; set; } = "";
    public double SizeGB { get; set; }
    public override string ToString() => string.Join(" · ", new[] { Letter, Model, Kind, SizeGB > 0 ? $"{SizeGB:0} GB" : "" }.Where(s => s.Length > 0));
}

/// <summary>The PC a run was on, as the comparison report shows it.</summary>
public sealed class SystemSpec
{
    public string Pc { get; set; } = "";
    public string Os { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int CpuBaseMHz { get; set; }
    public string Board { get; set; } = "";
    public string Bios { get; set; } = "";
    public double RamGB { get; set; }
    /// <summary>"DDR4", "DDR5".</summary>
    public string RamType { get; set; } = "";
    /// <summary>The speed the memory runs at (MT/s), and the speed its modules are rated for.</summary>
    public int RamSpeed { get; set; }
    public int RamRatedSpeed { get; set; }
    public List<string> RamModules { get; set; } = [];
    public List<GpuSpec> Gpus { get; set; } = [];
    public List<DriveSpec> Drives { get; set; } = [];
    /// <summary>The GPU and drive the tests ran on.</summary>
    public string TestedGpu { get; set; } = "";
    public string TestedDrive { get; set; } = "";
    public string PowerPlan { get; set; } = "";

    public string RamText => RamGB <= 0 ? "" : string.Join(" ", new[]
    {
        $"{RamGB:0.#} GB", RamType, RamSpeed > 0 ? $"{RamSpeed} MT/s" : "",
        RamModules.Count > 0 ? $"({RamModules.Count} × {RamGB / RamModules.Count:0.#} GB)" : "",
        RamRatedSpeed > RamSpeed && RamSpeed > 0 ? F("rated {0}", RamRatedSpeed) : "",
    }.Where(s => s.Length > 0));

    public string CpuText => Cores > 0 ? $"{Cpu} ({F("{0} cores, {1} threads", Cores, Threads)})" : Cpu;

    public GpuSpec? Gpu => Gpus.FirstOrDefault(g => g.Name == TestedGpu) ?? Gpus.FirstOrDefault();

    public static SystemSpec Read(string? gpu, string? driveRoot)
    {
        var s = new SystemSpec { Pc = Environment.MachineName, Threads = Environment.ProcessorCount };
        Try(() => ReadOs(s));
        Try(() => ReadCpu(s));
        Try(() => Smbios.Read(s));
        Try(() =>
        {
            Mem.Status(out ulong total, out _);
            if (s.RamGB <= 0) s.RamGB = Math.Round(total / 1073741824.0);
        });
        Try(() => s.Gpus = Graphics.List().Select(a => a.Spec).ToList());
        s.TestedGpu = gpu ?? s.Gpus.FirstOrDefault()?.Name ?? "";
        Try(() => ReadDrives(s));
        s.TestedDrive = s.Drives.FirstOrDefault(d => driveRoot != null && driveRoot.StartsWith(d.Letter, StringComparison.OrdinalIgnoreCase))?.ToString() ?? driveRoot ?? "";
        Try(() => s.PowerPlan = ActivePowerPlan());
        return s;
    }

    private static void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) { Logging.Log.Here.Debug(() => $"Stress Tests: system details: {ex.Message}"); }
    }

    private static void ReadOs(SystemSpec s)
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (k == null) return;
        string name = k.GetValue("ProductName") as string ?? "Windows";
        int build = int.TryParse(k.GetValue("CurrentBuild") as string, out int b) ? b : 0;
        if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
        string version = k.GetValue("DisplayVersion") as string ?? "";
        object? ubr = k.GetValue("UBR");
        s.Os = $"{name} {version} ({build}{(ubr is int u ? "." + u : "")})".Replace("  ", " ");
    }

    private static void ReadCpu(SystemSpec s)
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        s.Cpu = (k?.GetValue("ProcessorNameString") as string ?? "").Trim();
        while (s.Cpu.Contains("  ")) s.Cpu = s.Cpu.Replace("  ", " ");
        if (k?.GetValue("~MHz") is int mhz) s.CpuBaseMHz = mhz;
        s.Cores = Cpus.PhysicalCores();
    }

    private static void ReadDrives(SystemSpec s)
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                var (model, kind) = DiskInfo.Describe(d.RootDirectory.FullName);
                s.Drives.Add(new DriveSpec { Letter = d.RootDirectory.FullName.TrimEnd('\\'), Model = model, Kind = kind, SizeGB = Math.Round(d.TotalSize / 1e9) });
            }
            catch { }
        }
    }

    private static string ActivePowerPlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr guid) != 0) return "";
        try
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, guid, IntPtr.Zero, IntPtr.Zero, null, ref size);
            var buf = new byte[size];
            if (PowerReadFriendlyName(IntPtr.Zero, guid, IntPtr.Zero, IntPtr.Zero, buf, ref size) != 0) return "";
            return Encoding.Unicode.GetString(buf).TrimEnd('\0');
        }
        finally { Marshal.FreeHGlobal(guid); }
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, IntPtr scheme, IntPtr sub, IntPtr setting, byte[]? buffer, ref uint size);
}

/// <summary>The CPU's cores and memory figures, from Windows.</summary>
internal static class Cpus
{
    public static int PhysicalCores()
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref len); // RelationProcessorCore
        var buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buf, ref len)) return 0;
            int cores = 0;
            for (long at = 0; at < len;)
            {
                int size = Marshal.ReadInt32(buf, (int)at + 4);
                cores++;
                at += size;
            }
            return cores;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relation, IntPtr buffer, ref uint length);
}

internal static class Mem
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtended;
    }

    public static void Status(out ulong total, out ulong available)
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref m);
        total = m.TotalPhys;
        available = m.AvailPhys;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx m);
}

/// <summary>The PC's firmware table (SMBIOS): the board, BIOS and memory modules, without WMI or admin rights.</summary>
internal static class Smbios
{
    public static void Read(SystemSpec s)
    {
        const uint Rsmb = 0x52534D42; // 'RSMB'
        uint size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        if (size == 0) return;
        var raw = new byte[size];
        if (GetSystemFirmwareTable(Rsmb, 0, raw, size) == 0) return;
        int length = BitConverter.ToInt32(raw, 4);
        int at = 8, end = Math.Min(raw.Length, 8 + length);
        double ramMB = 0;
        var speeds = new List<int>();
        var rated = new List<int>();
        string boardMaker = "", board = "";
        while (at + 4 <= end)
        {
            byte type = raw[at], len = raw[at + 1];
            if (len < 4 || at + len > end) break;
            // the strings after the formatted part, numbered from 1
            var strings = new List<string>();
            int p = at + len, next;
            if (p + 1 < end && raw[p] == 0 && raw[p + 1] == 0) next = p + 2;
            else
            {
                while (p < end && raw[p] != 0)
                {
                    int z = Array.IndexOf(raw, (byte)0, p, end - p);
                    if (z < 0) { p = end; break; }
                    strings.Add(Encoding.ASCII.GetString(raw, p, z - p).Trim());
                    p = z + 1;
                }
                next = p + 1;
            }
            string Str(int offset) => offset < len && raw[at + offset] is byte i && i > 0 && i <= strings.Count ? strings[i - 1] : "";
            ushort Word(int offset) => offset + 1 < len ? BitConverter.ToUInt16(raw, at + offset) : (ushort)0;

            if (type == 0) s.Bios = string.Join(" ", new[] { Str(4), Str(5), Str(8) }.Where(x => x.Length > 0));
            else if (type == 2) { boardMaker = Str(4); board = Str(5); }
            else if (type == 17 && len >= 0x15)
            {
                int sizeField = Word(0x0C);
                double mb = sizeField == 0 || sizeField == 0xFFFF ? 0
                    : sizeField == 0x7FFF && len >= 0x20 ? BitConverter.ToUInt32(raw, at + 0x1C)
                    : (sizeField & 0x8000) != 0 ? (sizeField & 0x7FFF) / 1024.0 : sizeField;
                if (mb > 0)
                {
                    ramMB += mb;
                    int speed = len >= 0x17 ? Word(0x15) : 0, configured = len >= 0x22 ? Word(0x20) : 0;
                    if (speed > 0) rated.Add(speed);
                    if (configured > 0) speeds.Add(configured);
                    string kind = len >= 0x13 ? raw[at + 0x12] switch { 0x1A => "DDR4", 0x22 => "DDR5", 0x18 => "DDR3", 0x1E => "LPDDR4", 0x23 => "LPDDR5", _ => "" } : "";
                    if (kind.Length > 0) s.RamType = kind;
                    string maker = Str(0x17), part = Str(0x1A);
                    s.RamModules.Add(string.Join(" ", new[] { maker, part, $"{mb / 1024:0.#} GB", kind.Length > 0 && speed > 0 ? $"{kind}-{speed}" : kind }.Where(x => x.Length > 0)));
                }
            }
            else if (type == 127) break;
            at = next;
        }
        if (board.Length > 0) s.Board = board.StartsWith(boardMaker, StringComparison.OrdinalIgnoreCase) || boardMaker.Length == 0 ? board : $"{boardMaker} {board}";
        if (ramMB > 0) s.RamGB = Math.Round(ramMB / 1024, 1);
        if (speeds.Count > 0) s.RamSpeed = speeds.Min();
        if (rated.Count > 0) s.RamRatedSpeed = rated.Min();
        if (s.RamSpeed == 0) s.RamSpeed = s.RamRatedSpeed;
    }

    [DllImport("kernel32.dll")] private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);
}

/// <summary>A disk's model and kind, asked of the volume (no admin rights needed), as Tools → DiskMark does.</summary>
internal static unsafe class DiskInfo
{
    public static (string Model, string Kind) Describe(string root)
    {
        using var h = CreateFileW(@"\\.\" + root.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) return ("", "");
        string model = "", bus = "";
        bool? ssd = null;
        var buf = new byte[1024];
        fixed (byte* p = buf)
        {
            var query = stackalloc int[3];
            query[0] = 0; query[1] = 0; query[2] = 0;
            if (DeviceIoControl(h, 0x2D1400, query, 12, p, buf.Length, out int got, IntPtr.Zero) && got >= 32)
            {
                string Text(int at)
                {
                    int off = BitConverter.ToInt32(buf, at);
                    if (off <= 0 || off >= got) return "";
                    int end = Array.IndexOf(buf, (byte)0, off);
                    return Encoding.ASCII.GetString(buf, off, (end < 0 ? got : end) - off).Trim();
                }
                string vendor = Text(12), product = Text(16);
                model = vendor.Length > 0 && !product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) ? $"{vendor} {product}" : product;
                bus = BitConverter.ToInt32(buf, 28) switch { 17 => "NVMe", 11 => "SATA", 3 => "ATA", 7 => "USB", 10 => "SAS", 8 => "RAID", _ => "" };
            }
            query[0] = 7;
            if (DeviceIoControl(h, 0x2D1400, query, 12, p, buf.Length, out got, IntPtr.Zero) && got >= 9) ssd = buf[8] == 0;
        }
        return (model, string.Join(" ", new[] { bus, ssd switch { true => "SSD", false => "HDD", _ => "" } }.Where(x => x.Length > 0)));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, void* input, int inputSize, void* output, int outputSize, out int returned, IntPtr overlapped);
}

/// <summary>The PC's graphics cards, as DXGI lists them (Microsoft's software one left out), with their drivers.</summary>
internal static class Graphics
{
    public sealed record Adapter(int Index, GpuSpec Spec);

    public static List<Adapter> List()
    {
        var list = new List<Adapter>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            using (adapter)
            {
                var d = adapter.Description1;
                if ((d.Flags & AdapterFlags.Software) != 0 || d.VendorId == 0x1414) continue;
                var spec = new GpuSpec { Name = d.Description.Trim(), VramGB = Math.Round(d.DedicatedVideoMemory / 1073741824.0, 1) };
                spec.Driver = DriverVersion(spec.Name);
                list.Add(new Adapter((int)i, spec));
            }
        }
        return list;
    }

    /// <summary>The driver's version from the display adapters' registry keys ("32.0.15.6094 (560.94)" for NVIDIA's).</summary>
    private static string DriverVersion(string name)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return "";
            foreach (var sub in cls.GetSubKeyNames())
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is not string desc || !desc.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    string v = k.GetValue("DriverVersion") as string ?? "";
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && v.Replace(".", "") is { Length: >= 5 } digits)
                    {
                        string last = digits[^5..];
                        return $"{v} ({last[..3]}.{last[3..]})";
                    }
                    return v;
                }
                catch { }
            }
        }
        catch { }
        return "";
    }
}
