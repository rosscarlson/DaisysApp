using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>One running process at one look.</summary>
public sealed class ProcInfo
{
    public int Pid;
    public int ParentPid;
    public string Exe = "";
    public string? Path;
    public double Cpu;
    public double MemoryMB;
    public int Threads;
    public int Handles;
    public DateTime? Started;
}

/// <summary>What a program file is: its version information, signature, size and dates. Worked out once per file.</summary>
public sealed class FileFacts
{
    public string Path = "";
    public string Company = "", Product = "", Description = "", Version = "", OriginalName = "";
    public SignState Sign;
    public string Signer = "";
    public long Size;
    public DateTime Modified;
    public bool Hidden;
}

/// <summary>
/// Looks at the running processes: each one's CPU (since the last look), private memory, threads, handles, path and
/// parent, by Windows' process snapshot and its query-limited access (works for nearly every process without
/// administrator rights). Program files' facts (<see cref="Facts"/>) are kept per path, worked out off the UI thread.
/// The services and sign-in entries (<see cref="Autostarts"/>) and network connections (<see cref="Connections"/>)
/// are asked for when needed.
/// </summary>
public sealed class Scanner
{
    private readonly Dictionary<int, (long Created, long Cpu)> lastCpu = new();
    private long lastTick;
    private readonly Dictionary<(int, long), string?> paths = new();
    private static readonly ConcurrentDictionary<string, FileFacts> facts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The processes now (the first look has no CPU figures: it needs two).</summary>
    /// <remarks>From Windows' own list of processes (NtQuerySystemInformation, as Task Manager), which has every
    /// process's times, memory, threads and handles without opening it, so services and protected processes have
    /// figures without administrator rights; each one's file is asked for by its id the same way.</remarks>
    public List<ProcInfo> Scan()
    {
        long now = Stopwatch.GetTimestamp();
        double wall = lastTick == 0 ? 0 : (now - lastTick) / (double)Stopwatch.Frequency;
        lastTick = now;
        int cores = Environment.ProcessorCount;
        var list = new List<ProcInfo>();
        var seen = new HashSet<int>();
        int st;
        while ((st = NtQuerySystemInformation(SystemProcessInformation, buffer, bufferSize, out int need)) == STATUS_INFO_LENGTH_MISMATCH)
        {
            Marshal.FreeHGlobal(buffer);
            bufferSize = Math.Max(need, bufferSize * 2) + 65536;
            buffer = Marshal.AllocHGlobal(bufferSize);
        }
        if (st != 0) return list;
        IntPtr e = buffer;
        while (true)
        {
            int next = Marshal.ReadInt32(e);
            int pid = (int)Marshal.ReadInt64(e, 80);
            var p = new ProcInfo
            {
                Pid = pid,
                ParentPid = (int)Marshal.ReadInt64(e, 88),
                Threads = Marshal.ReadInt32(e, 4),
                Handles = Marshal.ReadInt32(e, 96),
                // the private working set, as Task Manager's Memory column
                MemoryMB = Marshal.ReadInt64(e, 8) / 1048576.0,
            };
            int nameLength = (ushort)Marshal.ReadInt16(e, 56);
            IntPtr nameAt = Marshal.ReadIntPtr(e, 64);
            p.Exe = pid == 0 ? "Idle" : nameLength > 0 && nameAt != IntPtr.Zero ? Marshal.PtrToStringUni(nameAt, nameLength / 2) : pid == 4 ? "System" : "";
            long created = Marshal.ReadInt64(e, 32);
            long cpu = Marshal.ReadInt64(e, 40) + Marshal.ReadInt64(e, 48);
            if (pid != 0)
            {
                seen.Add(pid);
                p.Started = created > 0 ? DateTime.FromFileTime(created) : null;
                if (wall > 0 && lastCpu.TryGetValue(pid, out var last) && last.Created == created)
                    p.Cpu = Math.Clamp((cpu - last.Cpu) / 1e7 / wall / cores * 100, 0, 100);
                lastCpu[pid] = (created, cpu);
                var key = (pid, created);
                if (!paths.TryGetValue(key, out var path)) paths[key] = path = ImagePath(pid);
                p.Path = path;
            }
            list.Add(p);
            if (next <= 0) break;
            e += next;
        }
        foreach (var gone in lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) lastCpu.Remove(gone);
        foreach (var gone in paths.Keys.Where(k => !seen.Contains(k.Item1)).ToList()) paths.Remove(gone);
        return list;
    }

    // the list's buffer, kept between looks (it's about 300 KB for 300 processes); unmanaged, as the names in it
    // point into it
    private int bufferSize = 512 * 1024;
    private IntPtr buffer = Marshal.AllocHGlobal(512 * 1024);

    /// <summary>A process's file by its id (Windows' NT path, as a drive letter's), without opening the process.</summary>
    private static string? ImagePath(int pid)
    {
        if (pid == 4) return null;
        var buf = Marshal.AllocHGlobal(2048);
        try
        {
            var info = new SYSTEM_PROCESS_ID_INFORMATION { ProcessId = pid, ImageName = new UNICODE_STRING { Length = 0, MaximumLength = 2048, Buffer = buf } };
            if (NtQuerySystemInformation(SystemProcessIdInformation, ref info, Marshal.SizeOf<SYSTEM_PROCESS_ID_INFORMATION>(), out _) != 0 || info.ImageName.Length == 0) return null;
            return DosPath(Marshal.PtrToStringUni(buf, info.ImageName.Length / 2));
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static Dictionary<string, string>? devices;

    /// <summary>"\Device\HarddiskVolume3\…" as "C:\…".</summary>
    private static string? DosPath(string nt)
    {
        if (devices == null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var drive in Environment.GetLogicalDrives())
            {
                string letter = drive.TrimEnd('\\');
                var target = new StringBuilder(1024);
                if (QueryDosDeviceW(letter, target, target.Capacity) > 0) map[target.ToString()] = letter;
            }
            devices = map;
        }
        foreach (var (device, letter) in devices)
            if (nt.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase)) return letter + nt[device.Length..];
        if (nt.StartsWith(@"\??\")) return nt[4..];
        return File.Exists(nt) ? nt : null;
    }

    /// <summary>A process's command line (null: not allowed, or it's gone).</summary>
    public static string? CommandLine(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            int size = 4096;
            for (int tries = 0; tries < 3; tries++)
            {
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    int st = NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, size, out int need);
                    if (st == 0)
                    {
                        var us = Marshal.PtrToStructure<UNICODE_STRING>(buf);
                        return us.Buffer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
                    }
                    if (need <= size) return null;
                    size = need;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>The file's facts if they've been worked out (<see cref="FactsAsync"/> does it).</summary>
    public static FileFacts? Facts(string? path) => path != null && facts.TryGetValue(path, out var f) ? f : null;

    /// <summary>Works out the facts of files not seen yet (a signature check each), off the UI thread.</summary>
    public static Task FactsAsync(IEnumerable<string> paths) => Task.Run(() =>
    {
        foreach (var p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            if (!facts.ContainsKey(p)) facts[p] = Read(p);
    });

    private static FileFacts Read(string path)
    {
        var f = new FileFacts { Path = path };
        try
        {
            var fi = new FileInfo(path);
            f.Size = fi.Length;
            f.Modified = fi.LastWriteTime;
            f.Hidden = fi.Attributes.HasFlag(FileAttributes.Hidden);
            var v = FileVersionInfo.GetVersionInfo(path);
            f.Company = v.CompanyName?.Trim() ?? "";
            f.Product = v.ProductName?.Trim() ?? "";
            f.Description = v.FileDescription?.Trim() ?? "";
            f.Version = v.ProductVersion?.Trim() ?? v.FileVersion?.Trim() ?? "";
            f.OriginalName = v.OriginalFilename?.Trim() ?? "";
        }
        catch { }
        (f.Sign, f.Signer) = Signature.Check(path);
        return f;
    }

    // ---------------------------------------------------------------- services and sign-in entries

    private static (DateTime At, Dictionary<int, List<string>> ByPid, List<(string Where, string Command)> Run) auto;

    /// <summary>What starts a program by itself: the running services in its processes (with their start type), and
    /// the sign-in entries (Run keys, Startup folders) that name it. Looked at again after a minute.</summary>
    public static List<string> Autostarts(string exe, string? path, IEnumerable<int> pids)
    {
        if (DateTime.Now - auto.At > TimeSpan.FromMinutes(1)) auto = (DateTime.Now, Services(), RunEntries());
        var found = new List<string>();
        foreach (int pid in pids) if (auto.ByPid.TryGetValue(pid, out var services)) found.AddRange(services);
        string file = System.IO.Path.GetFileName(path ?? exe);
        if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file += ".exe";
        foreach (var (where, cmd) in auto.Run)
            if (cmd.Contains(file, StringComparison.OrdinalIgnoreCase)) found.Add(where);
        return found.Distinct().ToList();
    }

    /// <summary>The running services by process id (Windows' service manager), each "Service: name (start type)".</summary>
    private static Dictionary<int, List<string>> Services()
    {
        var map = new Dictionary<int, List<string>>();
        IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_ENUMERATE_SERVICE);
        if (scm == IntPtr.Zero) return map;
        try
        {
            uint resume = 0;
            EnumServicesStatusExW(scm, 0, SERVICE_WIN32, SERVICE_ACTIVE, IntPtr.Zero, 0, out uint need, out _, ref resume, null);
            if (need == 0) return map;
            var buf = Marshal.AllocHGlobal((int)need);
            try
            {
                resume = 0;
                if (!EnumServicesStatusExW(scm, 0, SERVICE_WIN32, SERVICE_ACTIVE, buf, need, out _, out uint count, ref resume, null)) return map;
                using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                int size = Marshal.SizeOf<ENUM_SERVICE_STATUS_PROCESSW>();
                for (int i = 0; i < count; i++)
                {
                    var e = Marshal.PtrToStructure<ENUM_SERVICE_STATUS_PROCESSW>(buf + i * size);
                    string name = Marshal.PtrToStringUni(e.lpServiceName) ?? "";
                    string display = Marshal.PtrToStringUni(e.lpDisplayName) ?? name;
                    int pid = (int)e.dwProcessId;
                    if (pid == 0) continue;
                    string start = (root?.OpenSubKey(name)?.GetValue("Start") as int?) switch { 2 => T("Automatic"), 3 => T("Manual"), 4 => T("Disabled"), _ => T("Boot") };
                    if (!map.TryGetValue(pid, out var l)) map[pid] = l = new();
                    l.Add(F("Service: {0} ({1})", display, start));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
        finally { CloseServiceHandle(scm); }
        return map;
    }

    private static List<(string, string)> RunEntries()
    {
        var list = new List<(string, string)>();
        void Key(RegistryKey hive, string sub, string where)
        {
            try
            {
                using var k = hive.OpenSubKey(sub);
                if (k == null) return;
                foreach (var v in k.GetValueNames())
                    if (k.GetValue(v) is string cmd) list.Add((F("Starts at sign-in: {0} ({1})", v, where), cmd));
            }
            catch { }
        }
        Key(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", T("your Run key"));
        Key(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", T("every user's Run key"));
        Key(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", T("every user's Run key"));
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(Environment.GetFolderPath(folder)))
                {
                    // a shortcut's target is in it as text (UTF-16 or ANSI): enough to match a program's file name
                    var bytes = File.ReadAllBytes(lnk);
                    string text = Encoding.Unicode.GetString(bytes) + Encoding.Latin1.GetString(bytes);
                    list.Add((F("Starts at sign-in: {0} (Startup folder)", System.IO.Path.GetFileNameWithoutExtension(lnk)), text));
                }
            }
            catch { }
        }
        return list;
    }

    // ---------------------------------------------------------------- network connections

    /// <summary>Each process's TCP connections now: the ports it listens on, and how many connections it has open.</summary>
    public static Dictionary<int, (SortedSet<int> Listening, int Connected)> Connections()
    {
        var map = new Dictionary<int, (SortedSet<int>, int)>();
        void Add(int pid, int state, int port)
        {
            if (!map.TryGetValue(pid, out var v)) map[pid] = v = (new SortedSet<int>(), 0);
            if (state == 2) v.Item1.Add(port); else if (state == 5) map[pid] = (v.Item1, v.Item2 + 1);
        }
        foreach (int af in new[] { 2, 23 })
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 5, 0);
            if (size == 0) continue;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, false, af, 5, 0) != 0) continue;
                int n = Marshal.ReadInt32(buf);
                int rowSize = af == 2 ? 24 : 56;
                for (int i = 0; i < n; i++)
                {
                    IntPtr row = buf + 4 + i * rowSize;
                    int state, port, pid;
                    if (af == 2) { state = Marshal.ReadInt32(row); port = Port(Marshal.ReadInt32(row + 8)); pid = Marshal.ReadInt32(row + 20); }
                    else { port = Port(Marshal.ReadInt32(row + 20)); state = Marshal.ReadInt32(row + 48); pid = Marshal.ReadInt32(row + 52); }
                    Add(pid, state, port);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return map;
    }

    private static int Port(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);

    // ---------------------------------------------------------------- Windows

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ProcessCommandLineInformation = 60, SystemProcessInformation = 5, SystemProcessIdInformation = 88;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESS_ID_INFORMATION { public nint ProcessId; public UNICODE_STRING ImageName; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ENUM_SERVICE_STATUS_PROCESSW
    {
        public IntPtr lpServiceName, lpDisplayName;
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint,
            dwWaitHint, dwProcessId, dwServiceFlags;
    }

    private const uint SC_MANAGER_ENUMERATE_SERVICE = 4, SERVICE_WIN32 = 0x30, SERVICE_ACTIVE = 1;
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenSCManagerW(string? machine, string? db, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumServicesStatusExW(IntPtr scm, int infoLevel, uint type, uint state, IntPtr buf, uint size, out uint need, out uint count, ref uint resume, string? group);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING { public ushort Length, MaximumLength; public IntPtr Buffer; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int cls, IntPtr info, int size, out int returned);
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int cls, ref SYSTEM_PROCESS_ID_INFORMATION info, int size, out int returned);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int QueryDosDeviceW(string device, StringBuilder target, int size);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr info, int size, out int returned);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, uint reserved);
}
