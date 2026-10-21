using System.Diagnostics;

namespace DaisysApp.Applets.Gaming.Joy2Key;

/// <summary>
/// What an input does, as it's pressed, held and let go: keys held, tapped, repeated or toggled, a long press, a macro
/// played (once or over and over), the mouse moved, a program run, a profile switched to. Button2Key's engine steps each
/// controller input through it; Button2Key Remote (linked into its project) steps its ButtScreen buttons the same way.
/// Not thread-safe: one thread calls it.
/// </summary>
internal sealed class ActionRunner
{
    /// <summary>One input's state between steps.</summary>
    public sealed class Input
    {
        public required J2KAction Action;
        public bool Down;
        public long DownAt, NextRepeat;
        public bool LongFired, Toggled;
        public double FracX, FracY;
        public long MacroEnd;
    }

    private readonly Logging.Logger log;
    private readonly string name;
    private readonly Dictionary<string, int> held = new();
    private readonly List<(long At, List<KeyDef> Keys)> releases = new();
    // a macro's key presses and releases, still to come
    private readonly List<(long At, KeyDef Key, bool Down)> scheduled = new();

    /// <summary>A Switch profile action: the profile's name (called on the runner's thread).</summary>
    public Action<string?>? SwitchTo;

    /// <summary>The last key (or mouse button, or program) sent, and when: shown in the tab, to see that it's working.</summary>
    public string? LastSent { get; private set; }
    public DateTime LastSentAt { get; private set; }

    /// <summary>Keys are down or still to be pressed or let go (so it needs stepping soon).</summary>
    public bool Busy => held.Count > 0 || releases.Count > 0 || scheduled.Count > 0;

    /// <param name="name">For the error log ("Button2Key").</param>
    public ActionRunner(Logging.Logger log, string name)
    {
        this.log = log;
        this.name = name;
    }

    /// <summary>The input at this moment: amount 0 = let go, above 0 = pressed (how far, for mouse movement).</summary>
    public void Step(Input r, float amount, long now, double dt)
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
                    SwitchTo?.Invoke(a.Profile);
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
        if (n == 0)
        {
            log.Debug($"Key down: {k.Name}");
            KeySender.Down(k);
            LastSent = k.Name;
            LastSentAt = DateTime.Now;
        }
    }

    private void ReleaseKey(KeyDef k)
    {
        if (!held.TryGetValue(k.Id, out int n)) return;
        if (n <= 1) { held.Remove(k.Id); KeySender.Up(k); log.Debug($"Key up: {k.Name}"); }
        else held[k.Id] = n - 1;
    }

    /// <summary>Presses and lets go of what's due by now (taps ending, a macro's keys).</summary>
    public void Flush(long now)
    {
        if (scheduled.Count > 0)
        {
            // in time order; at the same moment, in the order they were laid out (a release before the next press)
            var due = scheduled.Select((e, i) => (e, i)).Where(x => x.e.At <= now).OrderBy(x => x.e.At).ThenBy(x => x.i).ToList();
            foreach (var (e, _) in due)
                if (e.Down) PressKey(e.Key); else ReleaseKey(e.Key);
            if (due.Count > 0) scheduled.RemoveAll(e => e.At <= now);
        }
        for (int i = 0; i < releases.Count; i++)
            if (now >= releases[i].At)
            {
                foreach (var k in releases[i].Keys) ReleaseKey(k);
                releases.RemoveAt(i--);
            }
    }

    /// <summary>Lets go of every key it holds, and drops what was still to come.</summary>
    public void ReleaseAll()
    {
        releases.Clear();
        scheduled.Clear();
        foreach (var id in held.Keys.ToList())
            if (KeyCatalog.Find(id) is { } k) KeySender.Up(k);
        held.Clear();
    }

    private void RunProgram(J2KAction a)
    {
        string program = a.Program ?? "", args = a.Arguments ?? "";
        LastSent = System.IO.Path.GetFileName(program);
        LastSentAt = DateTime.Now;
        log.Info($"Running {program} {args}");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Process.Start(new ProcessStartInfo(program, args) { UseShellExecute = true }); }
            catch (Exception ex) { Logging.ErrorLog.Write(name + ": running " + program, ex); }
        });
    }
}
