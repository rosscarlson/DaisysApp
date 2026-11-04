using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Gaming.Joy2Key;

/// <summary>
/// Turns controller input into keys, mouse and the rest, on a thread of its own: it reads the active profile's
/// controllers every few milliseconds and runs each assigned input's press / hold / repeat / long-press timing. Works on
/// a copy of the profiles, so editing them in the tab never races it (<see cref="SetProfiles"/> hands it a new copy).
/// </summary>
internal sealed class Joy2KeyEngine : IDisposable
{
    private const int TickMs = 4;
    private const int IdleMs = 100;
    /// <summary>How often the running programs are looked at, for a game starting or closing.</summary>
    private const int GameCheckMs = 2000;

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

    /// <summary>Raised on the UI thread when a game starting changed the chosen profile (settings.Active).</summary>
    public event Action? ActiveChanged;

    /// <summary>Raised on the UI thread when a game starting or closing switched it on or off (settings.Enabled).</summary>
    public event Action? EnabledChanged;

    /// <summary>On because a game started (not by hand): closing the app then leaves it off for the next start.</summary>
    public bool OnByGame => gameSwitch.OnByGame;

    // a game starting switches it on if it's off; the game closing switches it off however it was switched on (the
    // user's choice for Button2Key, 3.1.6)
    private readonly Shared.GameSwitch gameSwitch;
    private IDisposable? gameWatch;
    private string? seenGame;
    private bool gameSeen;

    /// <summary>The last key (or mouse button, or program) sent, and when: shown in the tab, to see that it's working.</summary>
    public string? LastSent => runner.LastSent;
    public DateTime LastSentAt => runner.LastSentAt;

    public bool IsPaused => Volatile.Read(ref paused) > 0;

    private static readonly Logging.Logger log = Logging.Log.Here;

    private readonly ActionRunner runner = new(log, "Button2Key");

    public Joy2KeyEngine(Joy2KeySettings settings)
    {
        this.settings = settings;
        runner.SwitchTo = name => switchTo = name;
        gameSwitch = new Shared.GameSwitch(() => enabled, on => SetByGame(on)) { OffEvenIfByHand = true };
    }

    // (the app's one watch on the running programs: told only when a program starts or ends)
    private void OnPrograms(Shared.RunningPrograms.Snapshot running)
    {
        if (!settings.AutoSwitch) return;
        List<J2KProfile> all;
        lock (gate) all = profiles;
        string? game = running.FirstRunning(all.Where(p => p.Programs.Count > 0).Select(p => (p.Name, (IEnumerable<string>)p.Programs)));
        lock (gate) { seenGame = game; gameSeen = true; }
    }

    public void SetProfiles(IEnumerable<J2KProfile> list)
    {
        var copy = list.Select(p => p.Clone()).ToList();
        lock (gate) { profiles = copy; profilesChanged = true; }
        if (gameWatch != null) Shared.RunningPrograms.Recheck(gameWatch); // (a profile's programs may have changed)
        log.Debug($"Profiles: {string.Join(", ", copy.Select(p => $"{p.Name} ({p.Devices.Count} controllers, programs {string.Join("/", p.Programs)})"))}");
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (value != enabled) log.Info($"Button2Key {(value ? "on" : "off")}");
            enabled = value;
            // the thread runs while off too, to see a game start
            if (thread == null)
            {
                stopping = false;
                thread = new Thread(Run) { IsBackground = true, Name = "DaisysApp-Joy2Key", Priority = ThreadPriority.AboveNormal };
                thread.Start();
                gameWatch ??= Shared.RunningPrograms.Watch(OnPrograms, GameCheckMs);
            }
            lock (gate) profilesChanged = true;
        }
    }

    /// <summary>On or off by hand (the button, the shortcut, the tray). A watched game closing still switches it off.</summary>
    public void Turn(bool on)
    {
        gameSwitch.ByHand();
        settings.Enabled = on;
        settings.Save();
        Enabled = on;
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
        gameWatch?.Dispose();
        thread?.Join(500);
    }

    // ---- the thread ----

    private sealed class InputRun
    {
        public required J2KDevice Device;
        public required string Input;
        public required ActionRunner.Input State;
    }

    private IDisposable? highResTimer;

    private void Run()
    {
        // an error mustn't stop it for good: log it, let go of everything, and start over a moment later
        int errors = 0;
        while (!stopping)
        {
            try { Loop(); }
            catch (Exception ex)
            {
                if (errors++ < 5) Logging.ErrorLog.Write("Button2Key", ex);
                try { ReleaseAll(); } catch { }
                lock (gate) profilesChanged = true;
                Thread.Sleep(1000);
            }
        }
        try { ReleaseAll(); } catch { }
        SetTimer(false);
    }

    private void Loop()
    {
        J2KProfile? profile = null;
        var runs = new List<InputRun>();
        // the runs by controller, made when the runs change (not every tick)
        var groups = Array.Empty<(J2KDevice Device, InputRun[] Runs)>();
        void SetRuns(List<InputRun> list)
        {
            runs = list;
            groups = list.GroupBy(r => r.Device).Select(g => (g.Key, g.ToArray())).ToArray();
        }
        string? lastGame = null;
        bool watching = false;
        var watch = Stopwatch.StartNew();
        long last = watch.ElapsedMilliseconds;
        bool wasPaused = false;

        while (!stopping)
        {
            long now = watch.ElapsedMilliseconds;
            bool reload;
            lock (gate) { reload = profilesChanged; profilesChanged = false; }

            // a game starting makes its profile the one in use, once (a profile picked by hand afterwards stays), and
            // switches Button2Key on if it was off; the game closing switches it off, however it was switched on.
            // Looked at while off too. Switched off by hand with the game running, it stays off until the game starts again.
            if (settings.AutoSwitch)
            {
                if (!watching && gameWatch != null) { watching = true; Shared.RunningPrograms.Recheck(gameWatch); }
                string? game;
                bool fresh;
                lock (gate) { fresh = gameSeen; gameSeen = false; game = seenGame; }
                if (fresh && !string.Equals(game, lastGame, StringComparison.OrdinalIgnoreCase))
                {
                    lastGame = game;
                    log.Info(game != null ? $"Game running: {game}" : "No game running");
                    if (game != null && !game.Equals(settings.Active, StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Active = settings.Showing = game;
                        Application.Current?.Dispatcher.BeginInvoke(() => ActiveChanged?.Invoke());
                        reload = true;
                    }
                    gameSwitch.Game(game != null);
                }
            }
            else if (watching) { watching = false; lastGame = null; gameSwitch.Forget(); }

            if (!enabled)
            {
                if (runs.Count > 0 || runner.Busy) { ReleaseAll(); SetRuns(new()); }
                profile = null;
                SetTimer(false);
                if (Current != null) SetCurrent(null);
                Thread.Sleep(IdleMs);
                continue;
            }

            // off drops the profile: on again (a game starting whose profile was already the chosen one) picks it again
            if (profile == null) reload = true;
            string? wanted = switchTo;
            if (wanted != null) { switchTo = null; reload = true; }

            if (reload)
            {
                List<J2KProfile> list;
                lock (gate) list = profiles;
                J2KProfile? pick = null;
                if (wanted != null) pick = list.FirstOrDefault(p => p.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                pick ??= list.FirstOrDefault(p => p.Name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
                if (!ReferenceEquals(pick, profile))
                {
                    ReleaseAll();
                    profile = pick;
                    SetRuns(RunsOf(profile));
                    log.Info($"Profile in use: {profile?.Name ?? "none"}, {runs.Count} inputs mapped");
                    SetCurrent(profile?.Name);
                }
            }

            // paused (the tab is in front, or a key is being picked): the profile is still picked above, so the tab
            // shows the right one, but nothing is sent, and every input starts afresh afterwards
            if (paused > 0)
            {
                if (!wasPaused)
                {
                    wasPaused = true;
                    ReleaseAll();
                    SetRuns(RunsOf(profile));
                }
                SetTimer(false);
                Thread.Sleep(IdleMs);
                continue;
            }
            wasPaused = false;

            if (profile == null || runs.Count == 0)
            {
                SetTimer(false);
                runner.Flush(now);
                Thread.Sleep(IdleMs);
                continue;
            }

            var connected = Joysticks.Connected();
            float threshold = Math.Clamp(profile.Threshold, 5, 95) / 100f;
            double dt = Math.Clamp(now - last, 0, 100) / 1000.0;
            last = now;
            bool any = false;
            foreach (var (device, group) in groups)
            {
                var joy = Joysticks.Resolve(device, connected);
                bool ok = false;
                JoyState state = default;
                if (joy != null)
                {
                    ok = Joysticks.Read(joy, out state);
                }
                any |= ok;
                foreach (var r in group)
                    runner.Step(r.State, ok ? Joysticks.Amount(state, r.Input, threshold, profile.Pov8Way) : 0, now, dt);
            }
            runner.Flush(now);
            SetTimer(any);
            Thread.Sleep(any ? TickMs : IdleMs);
        }
    }

    private void SetByGame(bool on)
    {
        log.Info(on ? "Switched on: a game started" : "Switched off: the game closed");
        settings.Enabled = on;
        enabled = on;
        Application.Current?.Dispatcher.BeginInvoke(() => EnabledChanged?.Invoke());
    }

    private static List<InputRun> RunsOf(J2KProfile? profile) => profile == null ? new() : profile.Devices
        .Where(d => !d.Off) // a controller switched off in the profile is ignored
        .SelectMany(d => d.Inputs.Where(i => !i.Value.IsEmpty).Select(i => new InputRun { Device = d, Input = i.Key, State = new ActionRunner.Input { Action = i.Value } }))
        .ToList();

    private void ReleaseAll() => runner.ReleaseAll();

    private void SetCurrent(string? name)
    {
        if (name == Current) return;
        Current = name;
        Application.Current?.Dispatcher.BeginInvoke(() => CurrentChanged?.Invoke());
    }

    // 1 ms timer resolution only while a controller is being read, so presses are timed closely
    private void SetTimer(bool on)
    {
        if (on == (highResTimer != null)) return;
        if (on) highResTimer = Shared.Win32.HighResTimer.Hold();
        else { highResTimer?.Dispose(); highResTimer = null; }
    }

    // ---- the game running ----

    public static bool SameProgram(string a, string b) =>
        Shared.RunningPrograms.Bare(a).Equals(Shared.RunningPrograms.Bare(b), StringComparison.OrdinalIgnoreCase);
}
