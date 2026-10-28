using DaisysApp.Applets.Gaming.ForzaWheel;
using DaisysApp.Applets.Gaming.Sessions;
using DaisysApp.Applets.SimEffects.Profiles;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>The inputs now: steering (0.5 straight), the pedals (0–1 pressed), NaN where there's none; the speed in m/s, NaN without telemetry.</summary>
public readonly record struct OverlayValues(double Steer, double Throttle, double Brake, double Clutch, double Handbrake, double SpeedMs)
{
    public static readonly OverlayValues None = new(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
}

/// <summary>
/// Reads what the overlay draws, on its own thread while it's wanted: the module's telemetry (<see cref="SimData"/>: the
/// profile in use's game, read once for every tool), for the speed always and for the
/// pedals and wheel in Game mode; in Device mode the pedals and wheel from the controllers' axes (DirectInput, as
/// ForzaWheel reads them). Writes the session log for Forza Analysis while it reads, if that's on.
/// </summary>
internal sealed class OverlayReader : IDisposable
{
    private readonly OverlaySettings settings;
    private readonly IntPtr window;
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly SessionRecorder recorder = new();
    private Thread? thread;
    private volatile bool wanted, quit, reopen;
    private OverlayValues now = OverlayValues.None;
    private string status = "";
    private bool receiving, paused;

    public OverlayReader(OverlaySettings settings, IntPtr window)
    {
        this.settings = settings;
        this.window = window;
    }

    /// <summary>Reading (while the overlay or the tab shows).</summary>
    public bool Wanted
    {
        get => wanted;
        set
        {
            wanted = value;
            if (value && thread == null)
            {
                thread = new Thread(Loop) { IsBackground = true, Name = "DaisysApp-InputOverlay" };
                thread.Start();
            }
            wake.Set();
        }
    }

    /// <summary>Recording only while the overlay shows (not for the tab's live values).</summary>
    public volatile bool Recording;

    /// <summary>The profile, the source or an axis changed: opened again.</summary>
    public void Changed()
    {
        reopen = true;
        wake.Set();
    }

    public OverlayValues Now { get { lock (gate) return now; } }
    public string Status { get { lock (gate) return status; } }
    public bool Receiving { get { lock (gate) return receiving; } }
    /// <summary>The game is paused (Game mode): the overlay holds still.</summary>
    public bool Paused { get { lock (gate) return paused; } }

    // ---------------------------------------------------------------- the thread

    private ITelemetrySource? lease;
    private readonly Dictionary<Guid, GameControllers.Reader> readers = new();
    private readonly Dictionary<Guid, ControllerState> states = new();
    private long nextOpen, nextList;
    private List<ControllerInfo>? attached;

    private void Loop()
    {
        var frame = new TelemetryFrame();
        long lastFrame = 0;
        try
        {
            while (!quit)
            {
                try { lastFrame = Step(frame, lastFrame); }
                catch (Exception ex) { Logging.ErrorLog.Write("Input Overlay", ex); Thread.Sleep(100); }
            }
        }
        finally { Close(); lease?.Dispose(); }
    }

    /// <summary>One reading; the time of the last frame from the game.</summary>
    private long Step(TelemetryFrame frame, long lastFrame)
    {
        if (!wanted)
        {
            Close();
            lock (gate) { now = OverlayValues.None; status = ""; receiving = paused = false; }
            wake.WaitOne(1000);
            return lastFrame;
        }
        if (reopen) { reopen = false; Close(); }
        bool device = settings.Source == OverlaySource.Device;
        // the module's telemetry (the profile in use's game): the speed, and in Game mode the pedals and wheel (it paces
        // the loop when there are no controllers to read)
        lease ??= SimData.Open();
        bool got = lease.Read(frame, device ? 0 : 20);
        long t = Environment.TickCount64;
        if (got) lastFrame = t;
        bool coming = t - lastFrame < 1000, live = coming && frame.Live;
        // paused or in a menu (the game still sending): what it showed last stays, and the traces stop
        bool hold = coming && !frame.Live;
        OverlayValues was;
        lock (gate) was = now;
        double speed = live ? frame.SpeedMs : hold ? was.SpeedMs : double.NaN;
        OverlayValues v;
        if (device) v = ReadDevices(t) with { SpeedMs = speed };
        else if (live) v = new(frame.Steer, frame.Throttle, frame.Brake, frame.Clutch, frame.Handbrake, speed);
        else if (hold) v = was;
        else v = OverlayValues.None;
        string s = device ? DeviceStatus() : SimData.Status(lease, lastFrame > 0 && t - lastFrame < 1000, live);
        lock (gate)
        {
            now = v;
            status = s;
            receiving = device ? readers.Count > 0 : live;
            paused = !device && hold;
        }
        if (Recording && settings.Record && (device || live)) recorder.Sample(v.Steer, v.Throttle, v.Brake, v.Clutch, v.Handbrake);
        else recorder.Close();
        if (device) wake.WaitOne(SessionRecorder.IntervalMs - 1);
        else if (!got) Thread.Sleep(5);
        return lastFrame;
    }

    private string DeviceStatus()
    {
        var names = settings.Axes.Values.Where(a => a.Input != null).Select(a => a.Input!).GroupBy(i => i.Device).ToList();
        if (names.Count == 0) return T("No axes set");
        var missing = names.Where(g => !readers.ContainsKey(g.Key)).Select(g => g.First().DeviceName).ToList();
        return missing.Count == 0 ? F("Reading {0}", string.Join(", ", names.Select(g => g.First().DeviceName)))
            : F("{0} isn't plugged in", string.Join(", ", missing));
    }

    private OverlayValues ReadDevices(long t)
    {
        var inputs = OverlaySettings.Roles.Select(r => settings.Axes.TryGetValue(r, out var a) ? a : null).ToArray();
        if (t >= nextOpen)
        {
            nextOpen = t + 2000;
            foreach (var input in inputs.Where(a => a?.Input != null).Select(a => a!.Input!).DistinctBy(i => i.Device))
            {
                if (readers.ContainsKey(input.Device) || window == IntPtr.Zero) continue;
                if (Resolve(input, t) is not { } instance || GameControllers.Open(instance, window) is not { } r) continue;
                readers[input.Device] = r;
                states.TryAdd(input.Device, new ControllerState());
                Logging.Log.Here.Info($"Input Overlay reading {input.DeviceName}");
            }
        }
        foreach (var (device, reader) in readers.ToList())
        {
            if (reader.Read(states[device])) continue;
            Logging.Log.Here.Warn($"Input Overlay lost {inputs.FirstOrDefault(a => a?.Input?.Device == device)?.Input?.DeviceName}");
            reader.Dispose();
            readers.Remove(device);
            nextOpen = 0;
        }
        double Value(int i)
        {
            if (inputs[i]?.Input is not { } input || !readers.ContainsKey(input.Device) || input.Index is < 0 or > 7) return double.NaN;
            double x = states[input.Device].Axes[input.Index];
            return double.IsNaN(x) ? x : inputs[i]!.Invert ? 1 - x : x;
        }
        return new(Value(0), Value(1), Value(2), Value(3), Value(4), double.NaN);
    }

    /// <summary>The controller's instance now: the one saved, or the same product (and name) if Windows renumbered it.</summary>
    private Guid? Resolve(InputRef input, long t)
    {
        if (attached == null || t >= nextList)
        {
            try { attached = GameControllers.List(); } catch { attached = new(); }
            nextList = t + 5000;
        }
        if (attached.Any(c => c.Instance == input.Device)) return input.Device;
        var same = attached.Where(c => c.Product == input.Product).ToList();
        return (same.FirstOrDefault(c => c.Name == input.DeviceName) ?? (same.Count == 1 ? same[0] : null))?.Instance;
    }

    private void Close()
    {
        foreach (var r in readers.Values) r.Dispose();
        readers.Clear();
        nextOpen = nextList = 0;
        recorder.Close();
    }

    public void Dispose()
    {
        quit = true;
        wanted = false;
        wake.Set();
        thread?.Join(1000);
        recorder.Dispose();
    }
}
