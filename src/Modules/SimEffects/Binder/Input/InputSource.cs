using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Binder.Input;

/// <summary>A controller plugged in, as the title's input reads it, with its last reading.</summary>
public sealed class LiveDevice
{
    public required DeviceId Id { get; init; }
    public required string Name { get; init; }
    public int Buttons { get; init; }
    public int Hats { get; init; }
    /// <summary>Its axes' names, by number as the input counts them ("" where it has none).</summary>
    public required string[] AxisNames { get; init; }
    public int AxisCount => AxisNames.Count(n => n.Length > 0);

    /// <summary>DirectInput's instance, where read through it.</summary>
    public Guid Instance { get; init; }
    /// <summary>SDL's GUID and number, where read through it (FSO keeps them).</summary>
    public string? SdlGuid { get; init; }
    public int SdlIndex { get; init; } = -1;

    /// <summary>Each axis from -1 to 1 (NaN where there's none).</summary>
    public double[] Axis = [];
    public bool[] Button = [];
    /// <summary>Each hat's directions held: bit 0 up, 1 right, 2 down, 3 left.</summary>
    public int[] Hat = [];
    /// <summary>When it last had input (the green dot).</summary>
    public long ActiveAt = long.MinValue / 2;

    public bool Active => Environment.TickCount64 - ActiveAt < 400;

    public string Detail => string.Join(" · ", new[]
    {
        P(Buttons, "{0} button", "{0} buttons"), P(AxisCount, "{0} axis", "{0} axes"), Hats > 0 ? P(Hats, "{0} hat", "{0} hats") : null,
    }.Where(s => s != null));
}

/// <summary>Something pressed or moved since the last poll.</summary>
public abstract record InputEvent(LiveDevice Device);
public sealed record ButtonDown(LiveDevice Device, int Button) : InputEvent(Device);
public sealed record HatDown(LiveDevice Device, int Hat, int Dir) : InputEvent(Device);

/// <summary>Controllers as a title's game numbers them.</summary>
public interface IInputSource : IDisposable
{
    InputKind Kind { get; }
    IReadOnlyList<LiveDevice> Devices { get; }
    /// <summary>Why it can't read controllers, or null.</summary>
    string? Problem { get; }
    /// <summary>Raised (on the UI thread, in <see cref="Poll"/>) when the list changed.</summary>
    event Action? DevicesChanged;
    /// <summary>Takes the newest readings (on the UI thread) and says what was pressed since last time.</summary>
    List<InputEvent> Poll();
    void Refresh();

    LiveDevice? Find(DeviceId? id) => id is { } d ? Devices.FirstOrDefault(x => x.Id == d) : null;
}

/// <summary>Numbers several alike devices (same vendor and product) in the order they're listed.</summary>
internal static class Nth
{
    public static List<DeviceId> Of(IEnumerable<(int Vid, int Pid)> devices)
    {
        var seen = new Dictionary<(int, int), int>();
        var ids = new List<DeviceId>();
        foreach (var d in devices)
        {
            seen[d] = seen.TryGetValue(d, out int n) ? n + 1 : 0;
            ids.Add(new DeviceId(d.Vid, d.Pid, seen[d]));
        }
        return ids;
    }
}

/// <summary>
/// The controllers through Core's one DirectInput list (<see cref="GameControllers"/>), read about 60 times a second on a
/// thread of their own (a slow driver holds that up, not the window), non-exclusively. Axes numbered as DirectInput's
/// state has them: X, Y, Z, the three rotations and two sliders, as Elite Dangerous and Assetto Corsa count them.
/// </summary>
public sealed class DirectInputSource : IInputSource
{
    private static readonly string[] Names = ["X", "Y", "Z", "Rx", "Ry", "Rz", "Slider 1", "Slider 2"];

    private sealed class Slot
    {
        public required LiveDevice Device;
        public required ControllerInfo Info;
        public GameControllers.Reader? Reader;
        public readonly ControllerState Reading = new(), Taken = new();
        public bool Fresh, Failed;
    }

    private readonly IntPtr window;
    private readonly object gate = new();
    private List<Slot> slots = new();
    private readonly Dictionary<LiveDevice, (bool[] Buttons, int[] Hats, double[] Still)> last = new();
    private Thread? thread;
    private volatile bool running = true, listChanged;

    public DirectInputSource(IntPtr window)
    {
        this.window = window;
        GameControllers.Changed += OnListed;
        List();
        thread = new Thread(Loop) { IsBackground = true, Name = "DaisysApp-Binder" };
        thread.Start();
    }

    public InputKind Kind => InputKind.DirectInput;
    public IReadOnlyList<LiveDevice> Devices { get; private set; } = [];
    public string? Problem => null;
    public event Action? DevicesChanged;

    private void OnListed() { List(); }

    private void List()
    {
        var infos = Task.Run(GameControllers.List).Result;
        var ids = Nth.Of(infos.Select(c => (c.Vid, c.Pid)));
        var made = infos.Select((c, i) => new Slot
        {
            Info = c,
            Device = new LiveDevice
            {
                Id = ids[i], Name = c.Name, Buttons = c.Buttons, Hats = c.Povs, Instance = c.Instance,
                AxisNames = Enumerable.Range(0, 8).Select(a => c.HasAxis[a] ? Names[a] : "").ToArray(),
                Axis = Enumerable.Repeat(double.NaN, 8).ToArray(), Button = new bool[GameControllers.MaxButtons], Hat = new int[4],
            },
        }).ToList();
        List<Slot> old;
        lock (gate) { old = slots; slots = made; }
        foreach (var s in old) s.Reader?.Dispose();
        Devices = made.Select(s => s.Device).ToList();
        listChanged = true;
    }

    public void Refresh() => _ = GameControllers.RefreshAsync();

    private void Loop()
    {
        while (running)
        {
            List<Slot> list;
            lock (gate) list = slots;
            foreach (var s in list)
            {
                if (s.Failed || !running) continue;
                s.Reader ??= GameControllers.Open(s.Info.Instance, window);
                if (s.Reader == null) { s.Failed = true; continue; }
                if (!s.Reader.Read(s.Reading)) { s.Reader.Dispose(); s.Reader = null; s.Failed = true; continue; }
                lock (gate) { s.Reading.CopyTo(s.Taken); s.Fresh = true; }
            }
            Thread.Sleep(16);
        }
        lock (gate) foreach (var s in slots) { s.Reader?.Dispose(); s.Reader = null; }
    }

    public List<InputEvent> Poll()
    {
        var events = new List<InputEvent>();
        if (listChanged)
        {
            listChanged = false;
            last.Clear();
            DevicesChanged?.Invoke();
        }
        List<Slot> list;
        lock (gate) list = slots;
        long now = Environment.TickCount64;
        foreach (var s in list)
        {
            var d = s.Device;
            lock (gate)
            {
                if (!s.Fresh) continue;
                s.Fresh = false;
                for (int a = 0; a < 8; a++) d.Axis[a] = double.IsNaN(s.Taken.Axes[a]) ? double.NaN : s.Taken.Axes[a] * 2 - 1;
                Array.Copy(s.Taken.Buttons, d.Button, d.Button.Length);
                for (int h = 0; h < 4; h++) d.Hat[h] = HatBits(s.Taken.Povs[h]);
            }
            bool first = !last.TryGetValue(d, out var was);
            if (first) last[d] = was = (new bool[d.Button.Length], new int[4], (double[])d.Axis.Clone());
            for (int b = 0; b < d.Button.Length; b++)
            {
                if (d.Button[b] && !was.Buttons[b] && !first) { events.Add(new ButtonDown(d, b)); d.ActiveAt = now; }
                was.Buttons[b] = d.Button[b];
            }
            for (int h = 0; h < 4; h++)
            {
                int newly = d.Hat[h] & ~was.Hats[h];
                for (int dir = 0; dir < 4; dir++)
                    if ((newly & (1 << dir)) != 0 && !first) { events.Add(new HatDown(d, h, dir)); d.ActiveAt = now; }
                was.Hats[h] = d.Hat[h];
            }
            for (int a = 0; a < 8; a++)
            {
                if (double.IsNaN(d.Axis[a])) continue;
                if (Math.Abs(d.Axis[a] - was.Still[a]) > 0.03) { was.Still[a] = d.Axis[a]; d.ActiveAt = now; }
            }
        }
        return events;
    }

    /// <summary>A hat's angle (hundredths of a degree, -1 centred) as the directions held.</summary>
    internal static int HatBits(int pov)
    {
        if (pov < 0) return 0;
        int bits = 0;
        if (pov >= 31500 || pov <= 4500) bits |= 1;
        if (pov is >= 4500 and <= 13500) bits |= 2;
        if (pov is >= 13500 and <= 22500) bits |= 4;
        if (pov is >= 22500 and <= 31500) bits |= 8;
        return bits;
    }

    public void Dispose()
    {
        GameControllers.Changed -= OnListed;
        running = false;
        if (thread is { } t && t != Thread.CurrentThread) t.Join(1000);
        thread = null;
    }
}
