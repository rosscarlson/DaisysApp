using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// Turns controller input into keys, mouse and the rest, on a thread of its own: it reads the active profile's
/// controllers every few milliseconds and runs each assigned input's press / hold / repeat / long-press timing. Works on
/// a copy of the profiles, so editing them in the tab never races it (<see cref="SetProfiles"/> hands it a new copy).
/// </summary>
internal sealed class Joy2KeyEngine : IDisposable
{
    private const int TickMs = 4;
    private const int IdleMs = 100;
    private const int ForegroundMs = 400;

    private readonly object gate = new();
    private readonly Joy2KeySettings settings;
    private List<J2KProfile> profiles = new();
    private bool profilesChanged = true;
    private bool enabled;
    private int paused;
    private Thread? thread;
    private volatile bool stopping;
    private string? switchTo;

    /// <summary>The profile in use (on the engine's side). Raised on the UI thread when it changes.</summary>
    public string? Current { get; private set; }
    public event Action? CurrentChanged;

    public Joy2KeyEngine(Joy2KeySettings settings) => this.settings = settings;

    public void SetProfiles(IEnumerable<J2KProfile> list)
    {
        var copy = list.Select(p => p.Clone()).ToList();
        lock (gate) { profiles = copy; profilesChanged = true; }
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (value && thread == null)
            {
                stopping = false;
                thread = new Thread(Run) { IsBackground = true, Name = "DaisysApp-Joy2Key", Priority = ThreadPriority.AboveNormal };
                thread.Start();
            }
            lock (gate) profilesChanged = true;
        }
    }

    /// <summary>Stops sending anything (and lets go of what's held) until <see cref="Resume"/>: while the tab is in
    /// front, so buttons can be pressed to find them, and while a key is being picked.</summary>
    public void Pause() { Interlocked.Increment(ref paused); lock (gate) profilesChanged = true; }
    public void Resume() { Interlocked.Decrement(ref paused); lock (gate) profilesChanged = true; }

    /// <summary>Also tells the thread to pick the profile again (e.g. the chosen one changed).</summary>
    public void Refresh() { lock (gate) profilesChanged = true; }

    public void Dispose()
    {
        stopping = true;
        thread?.Join(500);
    }

    // ---- the thread ----

    private sealed class InputRun
    {
        public required J2KDevice Device;
        public required string Input;
        public required J2KAction Action;
        public bool Down;
        public long DownAt, NextRepeat;
        public bool LongFired, Toggled;
        public double FracX, FracY;
        public long MacroEnd;
    }

    private readonly Dictionary<string, int> held = new();
    private readonly List<(long At, List<KeyDef> Keys)> releases = new();
    // a macro's key presses and releases, still to come
    private readonly List<(long At, KeyDef Key, bool Down)> scheduled = new();
    private bool highResTimer;

    private void Run()
    {
        try { Loop(); }
        catch (Exception ex) { Logging.ErrorLog.Write("Joy 2 Key", ex); }
        finally
        {
            ReleaseAll();
            SetTimer(false);
        }
    }

    private void Loop()
    {
        J2KProfile? profile = null;
        var runs = new List<InputRun>();
        long nextForeground = 0;
        string? foreground = null;
        var watch = Stopwatch.StartNew();
        long last = watch.ElapsedMilliseconds;

        while (!stopping)
        {
            long now = watch.ElapsedMilliseconds;
            bool reload;
            lock (gate) { reload = profilesChanged; profilesChanged = false; }

            if (!enabled || paused > 0)
            {
                if (runs.Count > 0 || held.Count > 0) { ReleaseAll(); runs.Clear(); }
                profile = null;
                SetTimer(false);
                if (!enabled && Current != null) SetCurrent(null);
                Thread.Sleep(IdleMs);
                continue;
            }

            if (settings.AutoSwitch && now >= nextForeground)
            {
                string? f = ForegroundProgram();
                if (!string.Equals(f, foreground, StringComparison.OrdinalIgnoreCase)) { foreground = f; reload = true; }
                nextForeground = now + ForegroundMs;
            }

            string? wanted = switchTo;
            if (wanted != null) { switchTo = null; reload = true; }

            if (reload)
            {
                List<J2KProfile> list;
                lock (gate) list = profiles;
                J2KProfile? pick = null;
                if (wanted != null) pick = list.FirstOrDefault(p => p.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (pick == null && settings.AutoSwitch && foreground != null)
                    pick = list.FirstOrDefault(p => p.Programs.Any(g => SameProgram(g, foreground)));
                pick ??= list.FirstOrDefault(p => p.Name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
                if (!ReferenceEquals(pick, profile))
                {
                    ReleaseAll();
                    profile = pick;
                    runs = profile == null ? new() : profile.Devices
                        .SelectMany(d => d.Inputs.Where(i => !i.Value.IsEmpty).Select(i => new InputRun { Device = d, Input = i.Key, Action = i.Value }))
                        .ToList();
                    SetCurrent(profile?.Name);
                }
            }

            if (profile == null || runs.Count == 0)
            {
                SetTimer(false);
                FlushReleases(now, force: false);
                Thread.Sleep(IdleMs);
                continue;
            }

            var connected = Joysticks.Connected();
            float threshold = Math.Clamp(profile.Threshold, 5, 95) / 100f;
            double dt = Math.Clamp(now - last, 0, 100) / 1000.0;
            last = now;
            bool any = false;
            foreach (var group in runs.GroupBy(r => r.Device))
            {
                var joy = Joysticks.Resolve(group.Key, connected);
                bool ok = false;
                JoyState state = default;
                if (joy != null)
                {
                    ok = Joysticks.Read(joy, out state);
                    if (!ok) Joysticks.Invalidate();
                }
                any |= ok;
                foreach (var r in group)
                    Step(r, ok ? Joysticks.Amount(state, r.Input, threshold, profile.Pov8Way) : 0, now, dt);
            }
            FlushReleases(now, force: false);
            SetTimer(any);
            Thread.Sleep(any ? TickMs : IdleMs);
        }
    }

    private void Step(InputRun r, float amount, long now, double dt)
    {
        var a = r.Action;
        bool down = amount > 0;
        if (down && !r.Down)
        {
            r.Down = true;
            r.DownAt = now;
            r.LongFired = false;
            r.FracX = r.FracY = 0;
            switch (a.Kind)
            {
                case ActionKind.Keys when a.UsesLongPress:
                    break; // decided when it's let go, or held long enough
                case ActionKind.Keys:
                    switch (a.Mode)
                    {
                        case PressMode.Hold: Press(a.Keys); break;
                        case PressMode.Tap: Tap(a.Keys, now, a.PressMs); break;
                        case PressMode.Repeat:
                            Tap(a.Keys, now, RepeatPress(a));
                            r.NextRepeat = now + (a.RepeatDelayMs > 0 ? a.RepeatDelayMs : Math.Max(a.RepeatMs, 10));
                            break;
                        case PressMode.Toggle:
                            if (r.Toggled) Release(a.Keys); else Press(a.Keys);
                            r.Toggled = !r.Toggled;
                            break;
                    }
                    break;
                case ActionKind.Run:
                    RunProgram(a);
                    break;
                case ActionKind.Profile:
                    switchTo = a.Profile;
                    break;
                case ActionKind.Macro:
                    // a press while it's still playing is ignored
                    if (now >= r.MacroEnd) r.MacroEnd = PlayMacro(a, now);
                    break;
            }
        }
        else if (down)
        {
            if (a.Kind == ActionKind.Macro && a.Loop && now >= r.MacroEnd) r.MacroEnd = PlayMacro(a, now);
            if (a.Kind == ActionKind.Keys && a.UsesLongPress)
            {
                if (!r.LongFired && now - r.DownAt >= a.LongMs) { r.LongFired = true; Press(a.LongKeys); }
            }
            else if (a.Kind == ActionKind.Keys && a.Mode == PressMode.Repeat && now >= r.NextRepeat)
            {
                Tap(a.Keys, now, RepeatPress(a));
                r.NextRepeat = now + Math.Max(a.RepeatMs, 10);
            }
        }
        else if (r.Down)
        {
            r.Down = false;
            if (a.Kind == ActionKind.Keys)
            {
                if (a.UsesLongPress)
                {
                    if (r.LongFired) Release(a.LongKeys); else Tap(a.Keys, now, a.PressMs);
                }
                else if (a.Mode == PressMode.Hold) Release(a.Keys);
            }
        }

        if (down && a.Kind == ActionKind.Mouse)
        {
            r.FracX += a.MouseX * amount * dt;
            r.FracY += a.MouseY * amount * dt;
            int dx = (int)r.FracX, dy = (int)r.FracY;
            r.FracX -= dx;
            r.FracY -= dy;
            KeySender.MoveMouse(dx, dy);
        }
    }

    /// <summary>Lays a macro's steps out from now: each step's keys go down together, are held for PressMs, and let go
    /// before the step's pause. Returns when it's over (after the last pause, so a repeating macro keeps its rhythm).</summary>
    private long PlayMacro(J2KAction a, long now)
    {
        long t = now;
        int hold = Math.Max(a.PressMs, 1);
        foreach (var step in a.Steps ?? new())
        {
            var keys = Defs(step.Keys).ToList();
            if (keys.Count > 0)
            {
                foreach (var k in keys) scheduled.Add((t, k, true));
                for (int i = keys.Count - 1; i >= 0; i--) scheduled.Add((t + hold, keys[i], false));
                t += hold;
            }
            t += a.PauseAfter(step);
        }
        return Math.Max(t, now + 1);
    }

    /// <summary>A repeated press has to be let go before the next one.</summary>
    private static int RepeatPress(J2KAction a) => Math.Clamp(a.PressMs, 1, Math.Max(1, a.RepeatMs / 2));

    private static IEnumerable<KeyDef> Defs(IEnumerable<string> ids) => ids.Select(KeyCatalog.Find).OfType<KeyDef>();

    private void Press(IEnumerable<string> ids)
    {
        foreach (var k in Defs(ids)) PressKey(k);
    }

    private void Release(IEnumerable<string> ids)
    {
        foreach (var k in Defs(ids).Reverse()) ReleaseKey(k);
    }

    private void Tap(IEnumerable<string> ids, long now, int ms)
    {
        var keys = Defs(ids).ToList();
        if (keys.Count == 0) return;
        foreach (var k in keys) PressKey(k);
        keys.Reverse();
        releases.Add((now + Math.Max(ms, 1), keys));
    }

    // Two inputs can hold the same key: it's only let go when neither does.
    private void PressKey(KeyDef k)
    {
        held.TryGetValue(k.Id, out int n);
        held[k.Id] = n + 1;
        if (n == 0) KeySender.Down(k);
    }

    private void ReleaseKey(KeyDef k)
    {
        if (!held.TryGetValue(k.Id, out int n)) return;
        if (n <= 1) { held.Remove(k.Id); KeySender.Up(k); }
        else held[k.Id] = n - 1;
    }

    private void FlushReleases(long now, bool force)
    {
        if (scheduled.Count > 0)
        {
            // in time order; at the same moment, in the order they were laid out (a release before the next press)
            var due = scheduled.Select((e, i) => (e, i)).Where(x => force || x.e.At <= now).OrderBy(x => x.e.At).ThenBy(x => x.i).ToList();
            foreach (var (e, _) in due)
                if (e.Down) PressKey(e.Key); else ReleaseKey(e.Key);
            if (due.Count > 0) scheduled.RemoveAll(e => force || e.At <= now);
        }
        for (int i = 0; i < releases.Count; i++)
            if (force || now >= releases[i].At)
            {
                foreach (var k in releases[i].Keys) ReleaseKey(k);
                releases.RemoveAt(i--);
            }
    }

    private void ReleaseAll()
    {
        releases.Clear();
        scheduled.Clear();
        foreach (var id in held.Keys.ToList())
            if (KeyCatalog.Find(id) is { } k) KeySender.Up(k);
        held.Clear();
    }

    private static void RunProgram(J2KAction a)
    {
        string program = a.Program ?? "", args = a.Arguments ?? "";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Process.Start(new ProcessStartInfo(program, args) { UseShellExecute = true }); }
            catch (Exception ex) { Logging.ErrorLog.Write("Joy 2 Key: running " + program, ex); }
        });
    }

    private void SetCurrent(string? name)
    {
        if (name == Current) return;
        Current = name;
        Application.Current?.Dispatcher.BeginInvoke(() => CurrentChanged?.Invoke());
    }

    // 1 ms timer resolution only while a controller is being read, so presses are timed closely
    private void SetTimer(bool on)
    {
        if (on == highResTimer) return;
        highResTimer = on;
        if (on) timeBeginPeriod(1); else timeEndPeriod(1);
    }

    // ---- the program in front ----

    private int lastPid;
    private string? lastName;

    private string? ForegroundProgram()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        GetWindowThreadProcessId(hwnd, out int pid);
        if (pid == lastPid) return lastName;
        lastPid = pid;
        try
        {
            using var p = Process.GetProcessById(pid);
            lastName = p.ProcessName + ".exe";
        }
        catch { lastName = null; }
        return lastName;
    }

    public static bool SameProgram(string a, string b)
    {
        static string Norm(string s)
        {
            s = System.IO.Path.GetFileName(s.Trim());
            return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
        }
        return Norm(a).Equals(Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(nint hwnd, out int pid);
    [DllImport("winmm.dll")] private static extern int timeBeginPeriod(int ms);
    [DllImport("winmm.dll")] private static extern int timeEndPeriod(int ms);
}
