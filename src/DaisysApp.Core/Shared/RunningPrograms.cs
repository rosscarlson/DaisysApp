using System.IO;
using System.Runtime.InteropServices;

namespace DaisysApp.Shared;

/// <summary>
/// The programs running, for every module that watches for a game or program (Button2Key, Button2Key Remote,
/// ForzaWheel, SimEffects, Mini Mirror, the Resizer). Windows' process list is read once for all of them: a caller
/// gets the last listing if it's younger than the age it asks for, else one new listing that every caller waiting meanwhile
/// shares. Read straight from Windows' list (names and ids only), not <see cref="System.Diagnostics.Process.GetProcesses()"/>, which
/// builds an object for every process and thread. (Since Core 0.47: before, six modules each read the whole list every
/// 1 to 3 seconds.)
/// </summary>
public static class RunningPrograms
{
    /// <summary>One listing: each process's name (as Windows' process names are: no ".exe") and id.</summary>
    public sealed class Snapshot
    {
        public DateTime At { get; }
        public IReadOnlyList<(string Name, int Pid)> Processes { get; }
        public IReadOnlySet<string> Names { get; }

        internal Snapshot(List<(string Name, int Pid)> processes)
        {
            At = DateTime.UtcNow;
            Processes = processes;
            Names = processes.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>"game.exe", "game" or a full path running.</summary>
        public bool Has(string program) => Names.Contains(Bare(program));

        /// <summary>The first of the programs running, as "name.exe", or null.</summary>
        public string? FirstOf(IEnumerable<string> programs) =>
            programs.Select(Bare).FirstOrDefault(n => n.Length > 0 && Names.Contains(n)) is { } n ? n + ".exe" : null;

        /// <summary>The ids of a program's processes.</summary>
        public List<int> PidsOf(string program)
        {
            string name = Bare(program);
            return name.Length == 0 ? new() : Processes.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Pid).ToList();
        }
    }

    private static readonly object gate = new();
    private static Snapshot? last;

    /// <summary>The running programs, listed at most <paramref name="maxAgeMs"/> ago (blocks for a new listing: call it
    /// off the UI thread, or use <see cref="GetAsync"/>).</summary>
    public static Snapshot Get(int maxAgeMs = 1000)
    {
        lock (gate)
        {
            if (last != null && (DateTime.UtcNow - last.At).TotalMilliseconds < maxAgeMs) return last;
            return last = new Snapshot(List());
        }
    }

    public static Task<Snapshot> GetAsync(int maxAgeMs = 1000)
    {
        lock (gate)
            if (last != null && (DateTime.UtcNow - last.At).TotalMilliseconds < maxAgeMs) return Task.FromResult(last);
        return Task.Run(() => Get(maxAgeMs));
    }

    /// <summary>"game.exe", "game" or a full path: the bare name, as Windows' process names are.</summary>
    public static string Bare(string program)
    {
        string s = Path.GetFileName(program.Trim().Trim('"'));
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    private static int frontPid;
    private static string? frontName;

    /// <summary>The program whose window is in front, as "name.exe", or null.</summary>
    public static string? Foreground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        GetWindowThreadProcessId(hwnd, out int pid);
        lock (gate)
        {
            if (pid == frontPid) return frontName;
            frontPid = pid;
            frontName = null;
            // (the last listing, else a new one: a program just started)
            var (name, _) = last?.Processes.FirstOrDefault(p => p.Pid == pid) ?? default;
            if (name == null) (name, _) = Get(0).Processes.FirstOrDefault(p => p.Pid == pid);
            return frontName = name != null ? name + ".exe" : null;
        }
    }

    /// <summary>Programs with a window showing (not this one, nor Explorer), as "name.exe", sorted: for picking one.
    /// <paramref name="all"/>: every program running.</summary>
    public static SortedSet<string> Windowed(bool all = false)
    {
        var withWindows = new HashSet<int>();
        if (!all)
            EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == 0)
                {
                    GetWindowThreadProcessId(hwnd, out int pid);
                    withWindows.Add(pid);
                }
                return true;
            }, 0);
        int self = Environment.ProcessId;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, pid) in Get(0).Processes)
            if (pid is not (0 or 4) && pid != self && (all || withWindows.Contains(pid) && !name.Equals("explorer", StringComparison.OrdinalIgnoreCase)))
                names.Add(name + ".exe");
        return names;
    }

    // (pinned: the names' pointers Windows writes point into it)
    private static byte[] buffer = GC.AllocateUninitializedArray<byte>(512 * 1024, pinned: true);

    // Windows' process list (NtQuerySystemInformation's SystemProcessInformation, what .NET reads too), keeping only
    // each process's name and id. Called under the lock (one buffer).
    private static unsafe List<(string, int)> List()
    {
        var list = new List<(string, int)>(400);
        int status;
        while ((status = NtQuerySystemInformation(5, buffer, buffer.Length, out int needed)) == STATUS_INFO_LENGTH_MISMATCH)
            buffer = GC.AllocateUninitializedArray<byte>(Math.Max(needed, buffer.Length) + 64 * 1024, pinned: true);
        if (status < 0) return list;
        bool x64 = IntPtr.Size == 8;
        fixed (byte* start = buffer)
        {
            for (byte* e = start; ; )
            {
                // SYSTEM_PROCESS_INFORMATION: ImageName (UNICODE_STRING) at 0x38, UniqueProcessId at 0x50 (0x44 on 32-bit)
                int length = *(ushort*)(e + 0x38);
                char* name = (char*)*(nint*)(e + (x64 ? 0x40 : 0x3C));
                int pid = (int)*(nint*)(e + (x64 ? 0x50 : 0x44));
                string n = name == null || length == 0 ? (pid == 0 ? "Idle" : "") : new string(name, 0, length / 2);
                if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
                list.Add((n, pid));
                int next = *(int*)e;
                if (next == 0) break;
                e += next;
            }
        }
        return list;
    }

    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int infoClass, byte[] info, int length, out int needed);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(nint hwnd, out int pid);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint cmd);
}
