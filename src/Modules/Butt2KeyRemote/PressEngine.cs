using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// Does what ButtScreen buttons are set to do, on a thread of its own: presses and let-gos come in from devices and
/// pop-outs (any thread), and each button held is stepped through Button2Key's <see cref="ActionRunner"/>, so hold, tap,
/// repeat, toggle, long press, macros, mouse movement and Run work exactly as they do there. A button pressed from two
/// places at once counts as held until both let go. Works on a copy of the ButtScreens (<see cref="SetScreens"/>).
/// </summary>
internal sealed class PressEngine : IDisposable
{
    private const int TickMs = 4;
    private const int IdleMs = 250;
    private const int MinHoldMs = 40;

    private static readonly Logging.Logger log = Logging.Log.Here;

    private readonly ConcurrentQueue<(string Source, string Screen, string Button, Kind What)> events = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly ActionRunner runner = new(log, "Button2Key Remote");
    private readonly Thread thread;
    private volatile bool stopping;
    private volatile bool enabled = true;
    private Dictionary<(string Screen, string Button), J2KAction> actions = new();
    private bool highResTimer;

    private enum Kind { Down, Up, Tap, LetGoAll, LetGoEverything }

    private sealed class Held
    {
        public required ActionRunner.Input State;
        /// <summary>Who's holding it (a device, a pop-out).</summary>
        public HashSet<string> By = new();
        /// <summary>A tap: let go at this time.</summary>
        public long TapEnd;
    }

    /// <summary>A Show a ButtScreen button was pressed: from where, and the ButtScreen's name. On the engine's thread.</summary>
    public event Action<string, string>? ShowScreen;

    /// <summary>The last key (or program) sent, and when.</summary>
    public string? LastSent => runner.LastSent;
    public DateTime LastSentAt => runner.LastSentAt;

    public PressEngine()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "DaisysApp-Butt2KeyRemote", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// <summary>Off: presses are ignored, and whatever is held is let go.</summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (value == enabled) return;
            enabled = value;
            log.Info($"Button2Key Remote {(value ? "on" : "off")}");
            if (!value) Post("", "", "", Kind.LetGoEverything);
        }
    }

    public void SetScreens(IEnumerable<ButtScreen> screens)
    {
        var map = new Dictionary<(string, string), J2KAction>();
        foreach (var s in screens)
            foreach (var b in s.Buttons)
                if (b.Action is { IsEmpty: false } a) map[(s.Id, b.Id)] = a.Clone();
        Volatile.Write(ref actions, map);
    }

    public void Down(string source, string screen, string button) { if (enabled) Post(source, screen, button, Kind.Down); }
    public void Up(string source, string screen, string button) => Post(source, screen, button, Kind.Up);
    /// <summary>A press and let-go in one (a device that only knows taps): held for <see cref="MinHoldMs"/>.</summary>
    public void Tap(string source, string screen, string button) { if (enabled) Post(source, screen, button, Kind.Tap); }
    /// <summary>Lets go of everything a source holds (a device that went away).</summary>
    public void LetGo(string source) => Post(source, "", "", Kind.LetGoAll);

    private void Post(string source, string screen, string button, Kind what)
    {
        events.Enqueue((source, screen, button, what));
        wake.Set();
    }

    public void Dispose()
    {
        stopping = true;
        wake.Set();
        thread.Join(500);
    }

    private void Run()
    {
        var held = new Dictionary<(string Screen, string Button), Held>();
        var watch = Stopwatch.StartNew();
        long last = 0;
        int errors = 0;
        while (!stopping)
        {
            try
            {
                long now = watch.ElapsedMilliseconds;
                double dt = Math.Clamp(now - last, 0, 100) / 1000.0;
                last = now;

                while (events.TryDequeue(out var e))
                {
                    switch (e.What)
                    {
                        case Kind.LetGoEverything:
                            foreach (var h in held.Values) runner.Step(h.State, 0, now, 0);
                            held.Clear();
                            toggles.Clear();
                            runner.ReleaseAll();
                            continue;
                        case Kind.LetGoAll:
                            foreach (var (key, h) in held.ToList())
                                if (h.By.Remove(e.Source) && h.By.Count == 0) { runner.Step(h.State, 0, now, 0); held.Remove(key); }
                            continue;
                    }
                    var id = (e.Screen, e.Button);
                    if (e.What is Kind.Down or Kind.Tap)
                    {
                        if (!enabled) continue;
                        if (!held.TryGetValue(id, out var h))
                        {
                            if (!Volatile.Read(ref actions).TryGetValue(id, out var action)) continue;
                            h = new Held { State = Toggles(id, action) };
                            held[id] = h;
                        }
                        h.By.Add(e.Source);
                        if (e.What == Kind.Tap) h.TapEnd = now + MinHoldMs;
                        if (!h.State.Down)
                        {
                            log.Debug(() => $"Pressed {e.Button} on {e.Screen} ({e.Source})");
                            runner.SwitchTo = name => { if (name != null) ShowScreen?.Invoke(e.Source, name); };
                            runner.Step(h.State, 1, now, 0);
                        }
                    }
                    else if (held.TryGetValue(id, out var h) && h.By.Remove(e.Source) && h.By.Count == 0 && h.TapEnd == 0)
                    {
                        // a quick touch still holds the keys long enough for a game to see them
                        if (now - h.State.DownAt < MinHoldMs) h.TapEnd = h.State.DownAt + MinHoldMs;
                        else
                        {
                            runner.Step(h.State, 0, now, 0);
                            held.Remove(id);
                        }
                    }
                }

                foreach (var (key, h) in held.ToList())
                {
                    if (h.TapEnd > 0 && now >= h.TapEnd) { h.TapEnd = 0; h.By.Clear(); }
                    if (h.By.Count == 0 && h.TapEnd == 0) { runner.Step(h.State, 0, now, dt); held.Remove(key); }
                    else runner.Step(h.State, 1, now, dt);
                }
                runner.Flush(now);

                bool busy = held.Count > 0 || runner.Busy;
                SetTimer(busy);
                wake.WaitOne(busy ? TickMs : IdleMs);
                errors = 0;
            }
            catch (Exception ex)
            {
                // an error mustn't stop it for good: log it, let go of everything, and carry on
                if (errors++ < 5) Logging.ErrorLog.Write("Button2Key Remote", ex);
                try { runner.ReleaseAll(); } catch { }
                held.Clear();
                Thread.Sleep(500);
            }
        }
        try { runner.ReleaseAll(); } catch { }
        SetTimer(false);
    }

    // a toggle remembers it's on between presses (one push holds the keys, the next lets go)
    private readonly Dictionary<(string, string), ActionRunner.Input> toggles = new();

    private ActionRunner.Input Toggles((string, string) id, J2KAction action)
    {
        if (action.Kind != ActionKind.Keys || action.Mode != PressMode.Toggle) return new ActionRunner.Input { Action = action };
        if (!toggles.TryGetValue(id, out var state) || !ReferenceEquals(state.Action, action))
            toggles[id] = state = new ActionRunner.Input { Action = action };
        return state;
    }

    // 1 ms timer resolution only while something is held, so presses are timed closely
    private void SetTimer(bool on)
    {
        if (on == highResTimer) return;
        highResTimer = on;
        if (on) timeBeginPeriod(1); else timeEndPeriod(1);
    }

    [DllImport("winmm.dll")] private static extern int timeBeginPeriod(int ms);
    [DllImport("winmm.dll")] private static extern int timeEndPeriod(int ms);
}
