using System.IO;
using System.Runtime.InteropServices;
using static DaisysApp.Shared.Win32.Native;

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

        /// <summary>Changes only when a process started or ended since the listing before (see <see cref="Watch"/>).</summary>
        public long Version { get; internal set; }

        /// <summary>The processes' ids, for comparing two listings.</summary>
        internal HashSet<int> Pids { get; }

        /// <summary>Each process's parent (the process that started it), by id.</summary>
        public IReadOnlyDictionary<int, int> Parents { get; private init; } = new Dictionary<int, int>();

        internal Snapshot(List<(string Name, int Pid)> processes, Dictionary<int, int> parents) : this(processes) => Parents = parents;

        internal Snapshot(List<(string Name, int Pid)> processes)
        {
            At = DateTime.UtcNow;
            Processes = processes;
            Names = processes.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Pids = processes.Select(p => p.Pid).ToHashSet();
        }

        /// <summary>The first list (by its name) with one of its programs running: the one whose program is in front
        /// first, when several are. Null if none.</summary>
        public string? FirstRunning(IEnumerable<(string Name, IEnumerable<string> Programs)> lists)
        {
            var matching = lists.Where(l => l.Programs.Any(Has)).ToList();
            if (matching.Count == 0) return null;
            string? front = Foreground() is { } f ? Bare(f) : null;
            return (front != null ? matching.FirstOrDefault(l => l.Programs.Any(g => Bare(g).Equals(front, StringComparison.OrdinalIgnoreCase))) : default).Name ?? matching[0].Name;
        }

        /// <summary>Whether a process with this id was running.</summary>
        public bool HasPid(int pid) => Pids.Contains(pid);

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
            var list = List(out var parents);
            var s = new Snapshot(list, parents);
            s.Version = last == null ? 1 : last.Pids.SetEquals(s.Pids) ? last.Version : last.Version + 1;
            return last = s;
        }
    }

    // ---------------------------------------------------------------- watching

    private sealed class Watcher
    {
        public required Action<Snapshot> Changed;
        public required int IntervalMs;
        public Func<string>? Also;
        public string? LastAlso;
        public SynchronizationContext? Context;
        public long Seen;
        public long Due;
        public volatile bool Gone, Busy;
    }

    private static readonly List<Watcher> watchers = new();
    private static Timer? watchTimer;
    private static int watchPeriod;

    /// <summary>
    /// Calls <paramref name="changed"/> with the running programs when a program has started or ended, looked at about
    /// every <paramref name="intervalMs"/> (and once now). One timer and one listing serve every module watching, and
    /// a watcher hears nothing while nothing changes. It's called on the thread that made the watch if that's the UI
    /// thread, else on the thread pool; never twice at once. Dispose to stop. <see cref="Recheck"/> calls it again
    /// whatever changed. <paramref name="lookingFor"/>: what it looks for, as text (its programs joined): a change to
    /// that tells it again too, so a game added to a list is seen without waiting for a program to start. (Since Core
    /// 0.55: before, seven modules each had their own timer.)
    /// </summary>
    public static IDisposable Watch(Action<Snapshot> changed, int intervalMs = 2000, Func<string>? lookingFor = null)
    {
        var w = new Watcher
        {
            Changed = changed, IntervalMs = Math.Clamp(intervalMs, 100, 60000), Also = lookingFor,
            Context = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread) != null ? SynchronizationContext.Current : null,
        };
        lock (watchers)
        {
            watchers.Add(w);
            Retime();
        }
        _ = Task.Run(() =>
        {
            try { w.LastAlso = w.Also?.Invoke(); } catch { }
            Deliver(w, Get(Math.Min(w.IntervalMs, 1000)));
        });
        return new Stopper(w);
    }

    /// <summary>The watch is told again on its next look, changed or not.</summary>
    public static void Recheck(IDisposable watch)
    {
        if (watch is Stopper { W: var w }) { w.Seen = 0; w.Due = 0; }
        lock (watchers) watchTimer?.Change(0, watchPeriod);
    }

    private sealed class Stopper(Watcher w) : IDisposable
    {
        public Watcher W => w;

        public void Dispose()
        {
            w.Gone = true;
            lock (watchers)
            {
                watchers.Remove(w);
                Retime();
            }
        }
    }

    // (under the lock) the timer at the fastest rate anyone asks for; none while nobody watches
    private static void Retime()
    {
        int period = watchers.Count == 0 ? 0 : watchers.Min(x => x.IntervalMs);
        if (period == watchPeriod) return;
        watchPeriod = period;
        if (period == 0) { watchTimer?.Dispose(); watchTimer = null; return; }
        watchTimer ??= new Timer(_ => Look());
        watchTimer.Change(period, period);
    }

    private static int looking;

    private static void Look()
    {
        if (Interlocked.Exchange(ref looking, 1) == 1) return;
        try
        {
            List<Watcher> due;
            long now = Environment.TickCount64;
            lock (watchers) due = watchers.Where(x => now >= x.Due - 50).ToList();
            if (due.Count == 0) return;
            var snapshot = Get(Math.Max(50, due.Min(x => x.IntervalMs) - 50));
            foreach (var w in due)
            {
                w.Due = now + w.IntervalMs;
                string? also = null;
                if (w.Also != null) try { also = w.Also(); } catch { also = w.LastAlso; } // (a list being edited)
                if (w.Seen != snapshot.Version || also != w.LastAlso) { w.LastAlso = also; Deliver(w, snapshot); }
            }
        }
        catch (Exception ex) { Logging.Log.App.Warn("Watching the running programs", ex); }
        finally { looking = 0; }
    }

    private static void Deliver(Watcher w, Snapshot s)
    {
        if (w.Gone || w.Busy) return;
        w.Busy = true;
        w.Seen = s.Version;
        void Run(object? _)
        {
            try { if (!w.Gone) w.Changed(s); }
            catch (Exception ex) { Logging.ErrorLog.Write("A running-programs watch", ex); }
            finally { w.Busy = false; }
        }
        if (w.Context != null) w.Context.Post(Run, null);
        else Run(null);
    }

    public static Task<Snapshot> GetAsync(int maxAgeMs = 1000)
    {
        lock (gate)
            if (last != null && (DateTime.UtcNow - last.At).TotalMilliseconds < maxAgeMs) return Task.FromResult(last);
        return Task.Run(() => Get(maxAgeMs));
    }

    /// <summary>Whether a program is running ("LibreHardwareMonitor", "game.exe"), from a listing up to
    /// <paramref name="maxAgeMs"/> old.</summary>
    public static bool IsRunning(string program, int maxAgeMs = 2000) => Get(maxAgeMs).Has(program);

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
        int pid = ProcessOf(hwnd);
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
                    int pid = ProcessOf(hwnd);
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
    private static unsafe List<(string, int)> List(out Dictionary<int, int> parents)
    {
        var list = new List<(string, int)>(400);
        parents = new Dictionary<int, int>(400);
        int status;
        while ((status = NtQuerySystemInformation(5, buffer, buffer.Length, out int needed)) == STATUS_INFO_LENGTH_MISMATCH)
            buffer = GC.AllocateUninitializedArray<byte>(Math.Max(needed, buffer.Length) + 64 * 1024, pinned: true);
        if (status < 0) return list;
        bool x64 = IntPtr.Size == 8;
        fixed (byte* start = buffer)
        {
            for (byte* e = start; ; )
            {
                // SYSTEM_PROCESS_INFORMATION: ImageName (UNICODE_STRING) at 0x38, UniqueProcessId at 0x50 (0x44 on 32-bit), its parent's after it
                int length = *(ushort*)(e + 0x38);
                char* name = (char*)*(nint*)(e + (x64 ? 0x40 : 0x3C));
                int pid = (int)*(nint*)(e + (x64 ? 0x50 : 0x44));
                parents[pid] = (int)*(nint*)(e + (x64 ? 0x58 : 0x48)); // InheritedFromUniqueProcessId
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

    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int infoClass, byte[] info, int length, out int needed);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint cmd);
}
