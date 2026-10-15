using System.Runtime.InteropServices;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// The emulator: reads the bound controllers (about a thousand times a second while it feeds vJoy) and drives the vJoy
/// device: its axes from the steering and pedals, its buttons from everything bound to them, its hat from the d-pad.
/// It also reads (slowly, without feeding) while the WheelEmu tab shows, so each binding's live value can be seen.
/// Controllers that are unplugged are looked for again every two seconds.
/// </summary>
internal sealed class WheelEmulator : IDisposable
{
    private readonly WheelEmuSettings settings;
    private readonly object gate = new();
    private Thread? thread;
    private readonly AutoResetEvent wake = new(false);
    private volatile bool quit, feeding, preview;
    private IntPtr window;

    private readonly Dictionary<Guid, GameControllers.Reader> readers = new();
    private readonly Dictionary<Guid, ControllerState> states = new();
    private long nextOpen;
    private List<ControllerInfo>? attached;
    private long nextList;

    // what's sent, so only changes are written
    private VJoy.DeviceSetup? setup;
    private readonly int[] sentAxes = new int[8];
    private readonly bool[] sentButtons = new bool[VJoy.MaxButtons + 1];
    private int sentPov = int.MinValue;

    /// <summary>Each role's value now (axes 0–1, buttons 0 or 1), for the tab.</summary>
    private Dictionary<string, double> values = new(StringComparer.OrdinalIgnoreCase);

    public WheelEmulator(WheelEmuSettings settings) => this.settings = settings;

    /// <summary>Whether it's feeding the vJoy device.</summary>
    public bool Running => feeding;

    /// <summary>Why it stopped or couldn't start, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Raised (on the emulator's thread) when it starts, stops or hits a problem.</summary>
    public event Action? StateChanged;

    /// <summary>The controllers it has open (by instance), and how many it's waiting for.</summary>
    public (int Open, int Missing) Devices
    {
        get
        {
            lock (gate)
            {
                var wanted = settings.Config.Devices.Select(d => d.Device).ToList();
                int open = wanted.Count(readers.ContainsKey);
                return (open, wanted.Count - open);
            }
        }
    }

    public IReadOnlyDictionary<string, double> Values { get { lock (gate) return values; } }

    /// <summary>Reading for the tab's live values (slowly) while it shows.</summary>
    public bool Preview
    {
        get => preview;
        set { preview = value; if (value) EnsureThread(); wake.Set(); }
    }

    /// <summary>The window DirectInput reads in the background for (any of the app's; set from the UI thread).</summary>
    public void SetWindow(IntPtr handle) => window = handle;

    /// <summary>Starts feeding vJoy. False (with <see cref="Problem"/>) if the vJoy device can't be used.</summary>
    public bool Start()
    {
        if (feeding) return true;
        Problem = null;
        uint id = settings.Config.VJoyDevice;
        if (!VJoy.Available) return Fail(T("vJoy isn't installed. Install vJoy 2.1.9.1 (see the setup steps), then restart Daisy's App."));
        if (!VJoy.Enabled) return Fail(T("vJoy is switched off: tick Enable vJoy in Configure vJoy."));
        var d = VJoy.Read(id);
        if (d == null || d.Status == VJoy.Status.Missing) return Fail(F("vJoy device {0} doesn't exist: use Set up vJoy device.", id));
        if (d.Status == VJoy.Status.Busy) return Fail(F("vJoy device {0} is in use by another program (EmuWheel, a feeder or Joystick Gremlin?): close it first.", id));
        if (d.Status != VJoy.Status.Own && !VJoy.Acquire(id)) return Fail(F("vJoy device {0} couldn't be taken over.", id));
        lock (gate)
        {
            setup = d;
            Array.Fill(sentAxes, int.MinValue);
            Array.Clear(sentButtons);
            sentPov = int.MinValue;
        }
        VJoy.Reset(id);
        feeding = true;
        Logging.Log.Here.Info($"WheelEmu started on vJoy device {id} ({d.Buttons} buttons, {d.DiscretePovs} 4-way hats, {d.ContinuousPovs} continuous, FFB {d.Ffb})");
        EnsureThread();
        wake.Set();
        StateChanged?.Invoke();
        return true;
    }

    public void Stop()
    {
        if (!feeding) return;
        feeding = false;
        lock (gate)
        {
            uint id = settings.Config.VJoyDevice;
            VJoy.Reset(id);
            VJoy.Relinquish(id);
            setup = null;
        }
        Logging.Log.Here.Info("WheelEmu stopped");
        StateChanged?.Invoke();
    }

    private bool Fail(string why)
    {
        Problem = why;
        Logging.Log.Here.Warn("WheelEmu: " + why);
        StateChanged?.Invoke();
        return false;
    }

    /// <summary>Opens the controllers again (after the bindings changed or Hush hid or showed devices).</summary>
    public void Reopen()
    {
        lock (gate)
        {
            foreach (var r in readers.Values) r.Dispose();
            readers.Clear();
            nextOpen = 0;
            nextList = 0;
        }
        wake.Set();
    }

    private void EnsureThread()
    {
        if (thread != null) return;
        thread = new Thread(Loop) { IsBackground = true, Name = "WheelEmu", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    private void Loop()
    {
        bool fine = false;
        try
        {
            while (!quit)
            {
                if (!feeding && !preview)
                {
                    if (fine) { timeEndPeriod(1); fine = false; }
                    wake.WaitOne(1000);
                    continue;
                }
                if (feeding && !fine) { timeBeginPeriod(1); fine = true; }
                try { Tick(); }
                catch (Exception ex) { Logging.ErrorLog.Write("WheelEmu tick", ex); Thread.Sleep(100); }
                if (feeding) Thread.Sleep(1);
                else wake.WaitOne(30);
            }
        }
        finally { if (fine) timeEndPeriod(1); }
    }

    private void Tick()
    {
        var config = settings.Config;
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (now >= nextOpen)
            {
                nextOpen = now + 2000;
                foreach (var (device, product, name) in config.Devices)
                {
                    if (readers.ContainsKey(device)) continue;
                    var instance = Resolve(device, product, name, now);
                    if (instance is { } inst && window != IntPtr.Zero && GameControllers.Open(inst, window) is { } r)
                    {
                        readers[device] = r;
                        states.TryAdd(device, new ControllerState());
                        Logging.Log.Here.Info($"WheelEmu reading {name}");
                    }
                }
            }
            foreach (var (device, reader) in readers.ToList())
            {
                if (reader.Read(states[device])) continue;
                Logging.Log.Here.Warn($"WheelEmu lost {config.Devices.FirstOrDefault(d => d.Device == device).Name}");
                reader.Dispose();
                readers.Remove(device);
                nextOpen = 0;
            }
            Compute(config);
            if (feeding) Write(config.VJoyDevice);
        }
    }

    /// <summary>The controller's instance now: the one saved, or the same product (and name) if Windows renumbered it.</summary>
    private Guid? Resolve(Guid device, Guid product, string name, long now)
    {
        if (attached == null || now >= nextList)
        {
            try { attached = GameControllers.List(); } catch { attached = new(); }
            nextList = now + 5000;
        }
        if (attached.Any(c => c.Instance == device)) return device;
        var same = attached.Where(c => c.Product == product).ToList();
        return (same.FirstOrDefault(c => c.Name == name) ?? (same.Count == 1 ? same[0] : null))?.Instance;
    }

    // ---------------------------------------------------------------- from the controllers to the game's controller

    private readonly double[] axisOut = new double[8];
    /// <summary>The vJoy axes of the throttle and brake (as Windows is told: <see cref="GameInputRegistry"/>).</summary>
    private static readonly int Throttle = Roles.Find("Throttle")!.Output, Brake = Roles.Find("Brake")!.Output;
    /// <summary>The axes sent reversed (the pedals: Windows is told they read full when released).</summary>
    private static readonly bool[] reversed = Enumerable.Range(0, 8).Select(i => Roles.All.Any(r => r.Kind == RoleKind.Axis && r.Output == i && r.Reversed)).ToArray();
    private readonly bool[] buttonOut = new bool[VJoy.MaxButtons + 1];
    private readonly bool[] dpad = new bool[4];

    private void Compute(WheelConfig config)
    {
        var v = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        Array.Clear(axisOut);
        axisOut[0] = 0.5; // steering rests in the middle
        Array.Clear(buttonOut);
        Array.Clear(dpad);
        Binding? combined = null;
        foreach (var b in config.Bindings)
        {
            if (b.Inputs.Count == 0 || Roles.Find(b.Role) is not { } role) continue;
            switch (role.Kind)
            {
                case RoleKind.Axis:
                    if (AxisValue(b, role.Centred) is { } a) { axisOut[role.Output] = a; v[role.Id] = a; }
                    break;
                case RoleKind.CombinedPedals:
                    combined = b;
                    break;
                case RoleKind.Button:
                    if (Pressed(b)) { buttonOut[role.Output] = true; v[role.Id] = 1; } else v[role.Id] = 0;
                    break;
                case RoleKind.DPad:
                    if (Pressed(b)) { dpad[role.Output] = true; v[role.Id] = 1; } else v[role.Id] = 0;
                    break;
            }
        }
        // one pedal axis for both: from the middle up is throttle, down is brake
        if (combined != null && AxisValue(combined, true) is { } c)
        {
            v["Combined"] = c;
            v["Throttle"] = axisOut[Throttle] = Math.Max(0, c * 2 - 1);
            v["Brake"] = axisOut[Brake] = Math.Max(0, 1 - c * 2);
        }
        values = v;
    }

    private double? AxisValue(Binding b, bool centred)
    {
        var input = b.Inputs[0];
        if (!states.TryGetValue(input.Device, out var s) || !readers.ContainsKey(input.Device)) return null;
        double x = input.Kind switch
        {
            InputRef.InputKind.Axis => s.Axes[input.Index],
            InputRef.InputKind.Button => s.Buttons[input.Index] ? 1 : 0,
            _ => PovIs(s.Povs[input.Index], input.Direction) ? 1 : 0,
        };
        if (double.IsNaN(x)) return null;
        if (b.Invert) x = 1 - x;
        double dz = Math.Clamp(b.Deadzone, 0, 0.5);
        if (centred)
        {
            double c = x * 2 - 1;
            c = Math.Abs(c) <= dz ? 0 : Math.Sign(c) * (Math.Abs(c) - dz) / (1 - dz);
            c = Math.Clamp(c * Math.Clamp(b.Sensitivity, 1, 4), -1, 1);
            return (c + 1) / 2;
        }
        return x <= dz ? 0 : (x - dz) / (1 - dz);
    }

    private bool Pressed(Binding b)
    {
        foreach (var input in b.Inputs)
        {
            if (!states.TryGetValue(input.Device, out var s) || !readers.ContainsKey(input.Device)) continue;
            bool on = input.Kind switch
            {
                InputRef.InputKind.Button => s.Buttons[input.Index],
                InputRef.InputKind.Pov => PovIs(s.Povs[input.Index], input.Direction),
                _ => !double.IsNaN(s.Axes[input.Index]) && (input.Direction >= 0 ? s.Axes[input.Index] > 0.75 : s.Axes[input.Index] < 0.25),
            };
            if (on) return true;
        }
        return false;
    }

    /// <summary>Whether a hat (hundredths of a degree, -1 centred) points that way (0 up, 1 right, 2 down, 3 left); diagonals count for both.</summary>
    public static bool PovIs(int angle, int direction)
    {
        if (angle < 0) return false;
        angle %= 36000;
        return direction switch
        {
            0 => angle >= 31500 || angle <= 4500,
            1 => angle >= 4500 && angle <= 13500,
            2 => angle >= 13500 && angle <= 22500,
            _ => angle >= 22500 && angle <= 31500,
        };
    }

    private void Write(uint id)
    {
        if (setup is not { } d) return;
        for (int i = 0; i < 8; i++)
        {
            if (!d.Axes[i]) continue;
            double x = reversed[i] ? 1 - axisOut[i] : axisOut[i];
            int value = (int)Math.Round(d.AxisMin + x * (d.AxisMax - d.AxisMin));
            if (value == sentAxes[i]) continue;
            VJoy.SetAxisValue(id, VJoy.Axes[i], value);
            sentAxes[i] = value;
        }
        int buttons = Math.Min(d.Buttons, VJoy.MaxButtons);
        for (int b = 1; b <= buttons; b++)
        {
            if (buttonOut[b] == sentButtons[b]) continue;
            VJoy.SetButton(id, b, buttonOut[b]);
            sentButtons[b] = buttonOut[b];
        }
        // the d-pad: a 4-way hat takes one direction (up, right, down, left first); a continuous one diagonals too
        int pov;
        if (d.DiscretePovs > 0)
        {
            pov = Array.IndexOf(dpad, true);
            if (pov != sentPov) { VJoy.SetDiscretePov(id, 1, pov); sentPov = pov; }
        }
        else if (d.ContinuousPovs > 0)
        {
            int dx = (dpad[1] ? 1 : 0) - (dpad[3] ? 1 : 0), dy = (dpad[0] ? 1 : 0) - (dpad[2] ? 1 : 0);
            pov = dx == 0 && dy == 0 ? -1 : (int)((Math.Atan2(dx, dy) * 180 / Math.PI + 360) % 360 * 100);
            if (pov != sentPov) { VJoy.SetContinuousPov(id, 1, pov); sentPov = pov; }
        }
    }

    public void Dispose()
    {
        Stop();
        quit = true;
        wake.Set();
        thread?.Join(500);
        lock (gate)
        {
            foreach (var r in readers.Values) r.Dispose();
            readers.Clear();
        }
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
