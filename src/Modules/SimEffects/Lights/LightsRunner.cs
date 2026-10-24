using System.Diagnostics;

namespace DaisysApp.Applets.SimEffects.Lights;

/// <summary>Where to shift and what the lights show, at one moment (the same for every tool that has lights).</summary>
public sealed record RevState
{
    public bool Live { get; init; }
    public Game? Game { get; init; }
    public string Car { get; init; } = "";
    public string CarId { get; init; } = "";
    /// <summary>-1 reverse, 0 neutral, 1 first…</summary>
    public int Gear { get; init; }
    public float Rpm { get; init; }
    /// <summary>The car's redline, and where it came from.</summary>
    public float Redline { get; init; }
    public RedlineFrom From { get; init; }
    /// <summary>RPM: the first light, all lights (shift), flashing.</summary>
    public float Start { get; init; }
    public float Shift { get; init; }
    public float Flash { get; init; }
    /// <summary>How much of the lights are on, 0..1 (from <see cref="Start"/> to <see cref="Shift"/>).</summary>
    public float Fraction { get; init; }
    public bool AtShift { get; init; }
    public bool OverRev { get; init; }
    /// <summary>The flash's on half (when <see cref="OverRev"/>).</summary>
    public bool BlinkOn { get; init; }

    public string GearText => Gear < 0 ? "R" : Gear == 0 ? "N" : Gear.ToString();

    public static readonly RevState Off = new();
}

public enum RedlineFrom { None, Game, Learned, Yours, GameLights }

/// <summary>
/// The shift lights' brain, shared by Notify and Fanatec: follows the game (the one set, or whichever is running),
/// works out each car's redline as SimHub does (iRacing gives its own shift lights per car; otherwise the highest RPM
/// the car has held, once it's near the game's maximum, else the game's maximum; or your own for that car), and where
/// the lights are. Runs on its own thread while some tool wants it (<see cref="Want"/>); each frame goes to
/// <see cref="Frame"/> (on that thread). <see cref="Demo"/> sweeps the RPM through the gears without a game.
/// </summary>
public sealed class LightsRunner
{
    private static readonly object instanceGate = new();
    private static LightsRunner? instance;

    /// <summary>The one there is (its settings loaded the first time).</summary>
    public static LightsRunner Shared
    {
        get { lock (instanceGate) return instance ??= new LightsRunner(LightsSettings.Load()); }
    }

    private readonly object gate = new();
    private readonly HashSet<string> wanted = new();
    private Thread? thread;
    private volatile bool restart;
    private DateTime demoUntil;
    private readonly float[] recent = new float[3];
    private int recentCount;
    private string lastCar = "";
    private DateTime nextSave;
    private bool dirty;

    public LightsSettings Settings { get; }

    /// <summary>The newest state (for the tabs' previews).</summary>
    public RevState State { get; private set; } = RevState.Off;

    /// <summary>What it's doing, for a status line.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Every frame (on the lights' thread).</summary>
    public event Action<RevState>? Frame;

    private LightsRunner(LightsSettings settings) => Settings = settings;

    /// <summary>A tool wants the lights (or no longer does): the thread runs while one does.</summary>
    public void Want(string who, bool on)
    {
        lock (gate)
        {
            if (on) wanted.Add(who); else wanted.Remove(who);
            if (wanted.Count > 0 && thread == null)
            {
                thread = new Thread(Loop) { IsBackground = true, Name = "DaisysApp-Lights" };
                thread.Start();
            }
        }
    }

    /// <summary>The game or a port changed: listened for again.</summary>
    public void Restart() => restart = true;

    /// <summary>Sweeps the RPM up through the gears for a few seconds.</summary>
    public void Demo(int seconds = 12)
    {
        lock (gate) demoUntil = DateTime.UtcNow.AddSeconds(seconds);
    }

    public bool Demoing { get { lock (gate) return DateTime.UtcNow < demoUntil; } }

    private bool Running()
    {
        lock (gate)
        {
            if (wanted.Count > 0) return true;
            thread = null;
            return false;
        }
    }

    private void Loop()
    {
        Logging.Log.Here.Info("Lights: started");
        ITelemetrySource? source = null;
        (Game, int)? listening = null;
        var frame = new TelemetryFrame();
        var nextLook = DateTime.MinValue;
        Game? running = null;
        var lastFrame = DateTime.MinValue;
        var started = Stopwatch.StartNew();
        try
        {
            while (Running())
            {
                // which game
                var now = DateTime.UtcNow;
                if (Settings.Game == null && now >= nextLook)
                {
                    nextLook = now.AddSeconds(3);
                    running = RunningGame();
                }
                var game = Settings.Game ?? running;
                (Game, int)? want = game is { } g ? (g, Settings.PortFor(g)) : null;
                if (want != listening || restart)
                {
                    restart = false;
                    source?.Dispose();
                    source = want is { } w ? TelemetryHub.Open(w.Item1, w.Item2) : null;
                    listening = want;
                }

                if (Demoing)
                {
                    Publish(DemoFrame(started.Elapsed.TotalSeconds), null, demo: true);
                    Thread.Sleep(16);
                    continue;
                }
                if (source == null)
                {
                    Status = T("Waiting for a game");
                    Publish(null, null);
                    Thread.Sleep(100);
                    continue;
                }
                if (source.Read(frame, 100))
                {
                    lastFrame = DateTime.UtcNow;
                    Status = source.Problem ?? (frame.Live ? F("{0}: driving", Games.Title(game!.Value)) : F("{0}: not driving", Games.Title(game!.Value)));
                    Publish(frame, game);
                }
                // (a moment without a frame keeps the lights as they are)
                else if (DateTime.UtcNow - lastFrame > TimeSpan.FromMilliseconds(500))
                {
                    Status = source.Problem ?? (Games.UsesPort(game!.Value) ? F("Waiting for {0} on port {1}", Games.Title(game.Value), Settings.PortFor(game.Value)) : F("Waiting for {0}", Games.Title(game.Value)));
                    Publish(null, game);
                }
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Lights", ex); }
        finally
        {
            source?.Dispose();
            State = RevState.Off;
            Status = "";
            if (dirty) Settings.Save();
            Logging.Log.Here.Info("Lights: stopped");
        }
    }

    private static Game? RunningGame()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); } catch { }
            p.Dispose();
        }
        foreach (var g in Games.All)
            if (Games.Programs(g).Any(prog => names.Contains(Games.Bare(prog)))) return g;
        return null;
    }

    /// <summary>A pretend car: 1st to 6th, each gear revving from 4,000 to 8,000 in two seconds, then shifting.</summary>
    private static TelemetryFrame DemoFrame(double t)
    {
        double inGear = t % 2.2;
        int gear = 1 + (int)(t / 2.2) % 6;
        return new TelemetryFrame
        {
            Live = true, Gear = gear, GearNo = gear, MaxRpm = 8000, IdleRpm = 900, Throttle = 1,
            Rpm = (float)Math.Min(8000, 4000 + inGear * 2000), CarId = "demo", Car = T("Demo"),
        };
    }

    private void Publish(TelemetryFrame? f, Game? game, bool demo = false)
    {
        RevState s;
        if (f == null || !f.Live) s = RevState.Off with { Game = game };
        else
        {
            if (!demo) Learn(f);
            s = Work(f, game);
        }
        State = s;
        try { Frame?.Invoke(s); }
        catch (Exception ex) { Logging.ErrorLog.Write("Lights frame", ex); }
        if (dirty && DateTime.UtcNow >= nextSave)
        {
            dirty = false;
            Settings.Save();
        }
    }

    /// <summary>The highest RPM the car has held for three frames in a row, under throttle (its limiter, once reached).</summary>
    private void Learn(TelemetryFrame f)
    {
        if (f.CarId.Length == 0) return;
        if (f.CarId != lastCar)
        {
            lastCar = f.CarId;
            recentCount = 0;
        }
        if (f.Throttle < 0.9f || f.GearNo <= 0 || f.Rpm <= 0) { recentCount = 0; return; }
        recent[recentCount++ % recent.Length] = f.Rpm;
        if (recentCount < recent.Length) return;
        float held = recent.Min();
        // (beyond the game's own maximum is a glitch)
        if (f.MaxRpm > 0 && held > f.MaxRpm * 1.02f) return;
        lock (Settings.Gate)
        {
            if (!Settings.Cars.TryGetValue(f.CarId, out var car)) Settings.Cars[f.CarId] = car = new CarRedline { Name = f.Car };
            car.Seen = DateTime.Now;
            if (held > car.Learned + 10)
            {
                car.Learned = held;
                Mark();
            }
        }
    }

    private void Mark()
    {
        if (!dirty) nextSave = DateTime.UtcNow.AddSeconds(10);
        dirty = true;
    }

    private RevState Work(TelemetryFrame f, Game? game)
    {
        CarRedline? car = null;
        if (f.CarId.Length > 0)
            lock (Settings.Gate)
            {
                if (!Settings.Cars.TryGetValue(f.CarId, out car) && f.CarId != "demo")
                {
                    Settings.Cars[f.CarId] = car = new CarRedline { Name = f.Car, Seen = DateTime.Now };
                    Mark();
                }
                else if (car != null && car.Name != f.Car && f.Car.Length > 0) { car.Name = f.Car; Mark(); }
            }

        float redline, start, shift, flash;
        RedlineFrom from;
        if (car?.Yours is > 0 and var yours)
        {
            redline = yours;
            from = RedlineFrom.Yours;
        }
        else if (Settings.UseGameLights && f.ShiftRpm > 0)
        {
            redline = f.ShiftRpm;
            from = RedlineFrom.GameLights;
        }
        else if (car != null && car.Learned > 0 && (f.MaxRpm <= 0 || car.Learned >= f.MaxRpm * 0.85f))
        {
            redline = car.Learned;
            from = RedlineFrom.Learned;
        }
        else
        {
            redline = f.MaxRpm;
            from = f.MaxRpm > 0 ? RedlineFrom.Game : RedlineFrom.None;
        }
        if (from == RedlineFrom.GameLights)
        {
            shift = f.ShiftRpm;
            start = f.FirstRpm > 0 && f.FirstRpm < shift ? f.FirstRpm : shift * Settings.StartAt / Settings.ShiftAt;
            flash = f.BlinkRpm > shift ? f.BlinkRpm : shift * Settings.FlashAt / Settings.ShiftAt;
        }
        else
        {
            start = redline * Settings.StartAt / 100f;
            shift = redline * Settings.ShiftAt / 100f;
            flash = redline * Settings.FlashAt / 100f;
        }
        if (f.CarId != lastLogged)
        {
            lastLogged = f.CarId;
            Logging.Log.Here.Info($"Lights: {(f.Car.Length > 0 ? f.Car : "a car")}, redline {redline:0} ({from})");
        }
        bool over = redline > 0 && f.Rpm >= flash;
        long ms = Environment.TickCount64;
        int half = 500 / Math.Max(1, Settings.FlashRate);
        return new RevState
        {
            Live = true, Game = game, Car = f.Car, CarId = f.CarId, Gear = f.GearNo, Rpm = f.Rpm,
            Redline = redline, From = from, Start = start, Shift = shift, Flash = flash,
            Fraction = redline > 0 && shift > start ? Math.Clamp((f.Rpm - start) / (shift - start), 0, 1) : 0,
            AtShift = redline > 0 && f.Rpm >= shift,
            OverRev = over,
            BlinkOn = over && ms / half % 2 == 0,
        };
    }

    private string lastLogged = "";

    /// <summary>Your own redline for a car (null: worked out again); from the UI.</summary>
    public void SetYours(string carId, float? rpm)
    {
        lock (Settings.Gate)
            if (Settings.Cars.TryGetValue(carId, out var car)) car.Yours = rpm is > 0 ? rpm : null;
        Settings.Save();
    }

    /// <summary>Forgets what was learned about a car.</summary>
    public void Forget(string carId)
    {
        lock (Settings.Gate) Settings.Cars.Remove(carId);
        Settings.Save();
    }
}
