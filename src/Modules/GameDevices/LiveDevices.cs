using DaisysApp.Controllers;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// Every device plugged in, read about 30 times a second on a thread of its own while the tab or a widget shows (a slow
/// driver holds up this thread, not the window), non-exclusively so games still see them. <see cref="Active"/> is the
/// green dot: an axis that moved, or a button or hat that changed. Noise and a switch left on don't light it.
/// </summary>
internal sealed class LiveDevices : IDisposable
{
    private const double Moved = 0.015;      // of the axis's range, so a load cell's or a wheel's jitter doesn't count
    private const int LitMs = 400, HeldMs = 5000;

    private sealed class Slot
    {
        public required ControllerInfo Info;
        public readonly ControllerState State = new();
        public readonly ControllerState Reading = new();
        public readonly double[] Rest = new double[8];
        public readonly bool[] Last = new bool[GameControllers.MaxButtons];
        public readonly int[] LastPovs = { -1, -1, -1, -1 };
        public GameControllers.Reader? Reader;
        public bool Read, Failed, Remove;
        public long LitAt = long.MinValue / 2, ChangedAt = long.MinValue / 2;
    }

    private readonly IntPtr window;
    private readonly Dictionary<Guid, Slot> slots = new();
    private Thread? thread;
    private volatile bool running;

    public LiveDevices(IntPtr window) => this.window = window;

    /// <summary>Reading on or off.</summary>
    public bool Running
    {
        get => running;
        set
        {
            if (value == running) return;
            running = value;
            if (!value) return;
            thread = new Thread(Loop) { IsBackground = true, Name = "DaisysApp-Devices" };
            thread.Start();
        }
    }

    /// <summary>What's plugged in now; one that couldn't be read is tried again.</summary>
    public void Follow(IEnumerable<ControllerInfo> devices)
    {
        lock (slots)
        {
            var now = devices.ToDictionary(c => c.Instance);
            foreach (var (id, s) in slots)
            {
                s.Remove = !now.ContainsKey(id);
                s.Failed = false;
            }
            foreach (var (id, c) in now)
                if (!slots.ContainsKey(id)) slots[id] = new Slot { Info = c };
        }
    }

    /// <summary>Copies its last reading; false if it hasn't one.</summary>
    public bool Get(Guid instance, ControllerState into)
    {
        lock (slots)
        {
            if (!slots.TryGetValue(instance, out var s) || !s.Read) return false;
            s.State.CopyTo(into);
            return true;
        }
    }

    public bool Active(Guid instance)
    {
        lock (slots) return slots.TryGetValue(instance, out var s) && Environment.TickCount64 - s.LitAt < LitMs;
    }

    private void Loop()
    {
        var me = Thread.CurrentThread;
        while (running && thread == me)
        {
            List<Slot> list;
            lock (slots)
            {
                foreach (var gone in slots.Where(p => p.Value.Remove).ToList())
                {
                    gone.Value.Reader?.Dispose();
                    slots.Remove(gone.Key);
                }
                list = slots.Values.ToList();
            }
            foreach (var s in list)
            {
                if (s.Failed) continue;
                bool fake = FakeDevices.Is(s.Info);
                if (s.Reader == null && !fake)
                {
                    s.Reader = GameControllers.Open(s.Info.Instance, window);
                    if (s.Reader == null) { s.Failed = true; continue; }
                }
                if (fake) FakeDevices.Read(s.Info, s.Reading);
                else if (!s.Reader!.Read(s.Reading))
                {
                    s.Reader.Dispose();
                    s.Reader = null;
                    s.Failed = true;
                    continue;
                }
                lock (slots) Update(s);
            }
            Thread.Sleep(33);
        }
        lock (slots)
        {
            foreach (var s in slots.Values)
            {
                s.Reader?.Dispose();
                s.Reader = null;
                s.Read = false;
            }
        }
    }

    private static void Update(Slot s)
    {
        long now = Environment.TickCount64;
        var r = s.Reading;
        for (int i = 0; i < 8; i++)
        {
            double v = r.Axes[i];
            if (double.IsNaN(v)) continue;
            if (!s.Read) s.Rest[i] = v;
            else if (Math.Abs(v - s.Rest[i]) > Moved) { s.Rest[i] = v; s.LitAt = now; }
        }
        bool down = false;
        for (int i = 0; i < r.Buttons.Length; i++)
        {
            down |= r.Buttons[i];
            if (r.Buttons[i] == s.Last[i]) continue;
            s.Last[i] = r.Buttons[i];
            if (s.Read) s.ChangedAt = s.LitAt = now;
        }
        for (int i = 0; i < 4; i++)
        {
            down |= r.Povs[i] >= 0;
            if (r.Povs[i] == s.LastPovs[i]) continue;
            s.LastPovs[i] = r.Povs[i];
            if (s.Read) s.ChangedAt = s.LitAt = now;
        }
        // (held down it stays lit, for a while: a switch left on isn't input for ever)
        if (down && now - s.ChangedAt < HeldMs) s.LitAt = now;
        r.CopyTo(s.State);
        s.Read = true;
    }

    public void Dispose()
    {
        running = false;
        if (thread is { } t && t != Thread.CurrentThread) t.Join(1000);
    }
}
