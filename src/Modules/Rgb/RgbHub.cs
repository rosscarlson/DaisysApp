using System.Collections.Concurrent;
using System.Diagnostics;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Rgb;

/// <summary>
/// The devices and what they're set to. Devices are found and changed on one thread of its own (HID and I2C calls can
/// take a while), which also draws the effects a device can't run by itself (<see cref="Devices.Drawn"/>), 30 times a
/// second, only while one needs it. SMBus devices (<see cref="IRgbDevice.NeedsAdmin"/>) are changed through
/// <see cref="SmbusDevices.RunAsync"/> instead, only when asked (Windows asks for administrator rights each time).
/// </summary>
public sealed class RgbHub : IDisposable
{
    private readonly RgbSettings settings;
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;
    private List<IRgbDevice> local = new();
    private readonly Dictionary<IRgbDevice, LightSetting> drawn = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();

    /// <summary>Every device: those found here, then the SMBus ones last found (see <see cref="RgbSettings.Smbus"/>).</summary>
    public IReadOnlyList<IRgbDevice> List { get; private set; } = Array.Empty<IRgbDevice>();
    public bool Finding { get; private set; }

    /// <summary>On the hub's thread: the list changed, or a device's status did.</summary>
    public event Action? Changed;

    /// <summary>A device's last problem (by id), shown under it.</summary>
    public ConcurrentDictionary<string, string> Problems { get; } = new();

    public RgbHub(RgbSettings settings)
    {
        this.settings = settings;
        smbus = settings.Smbus.ToList();
        thread = new Thread(Loop) { IsBackground = true, Name = "RGB" };
        thread.Start();
    }

    public LightSetting? SettingOf(IRgbDevice d) => settings.Lights.GetValueOrDefault(d.Id);

    /// <summary>What a device shows: off while all are off, else its own setting (null: left as it was).</summary>
    public LightSetting? Effective(IRgbDevice d) => settings.Off ? LightSetting.AllOff : SettingOf(d)?.Copy();

    /// <summary>Looks for the devices again, then sets each to its setting (without keeping it on the device: it's there already).</summary>
    public void Find()
    {
        // what to set them to, read here (the settings change on the UI's thread)
        bool off = settings.Off;
        var lights = settings.Lights.ToDictionary(k => k.Key, k => k.Value.Copy(), StringComparer.OrdinalIgnoreCase);
        work.Add(() => FindNow(off, lights));
    }

    private void FindNow(bool off, Dictionary<string, LightSetting> lights)
    {
        Finding = true;
        Changed?.Invoke();
        foreach (var d in local) d.Dispose();
        drawn.Clear();
        var found = new List<IRgbDevice>();
        try { found.AddRange(AuraUsb.Find()); }
        catch (Exception ex) { Log.Here.Warn($"ASUS Aura: {ex.Message}"); }
        try { found.AddRange(NvidiaGpus.Find()); }
        catch (Exception ex) { Log.Here.Warn($"NVIDIA: {ex.Message}"); }
        local = found;
        Finding = false;
        Relist();
        foreach (var d in local)
            if ((off ? LightSetting.AllOff : lights.GetValueOrDefault(d.Id)) is { } s) Set(d, s);
    }

    /// <summary>The SMBus devices the administrator job found (kept in the settings).</summary>
    public void SetSmbus(List<SmbusDevices.Seen> seen)
    {
        settings.Smbus = seen;
        var copy = seen.ToList();
        work.Add(() => { smbus = copy; Relist(); });
    }

    private List<SmbusDevices.Seen> smbus = new();

    private void Relist()
    {
        List = local.Concat(smbus.Select(s => (IRgbDevice)new SmbusProxy(s))).ToList();
        Changed?.Invoke();
    }

    /// <summary>Sets the devices (not SMBus ones) to what they should show now, kept on each device.</summary>
    public void Apply(IEnumerable<IRgbDevice> devices)
    {
        var list = devices.Where(d => !d.NeedsAdmin).Select(d => (Device: d, Setting: Effective(d))).Where(x => x.Setting != null).ToList();
        work.Add(() =>
        {
            foreach (var (d, s) in list) Set(d, s!);
            foreach (var owner in list.Select(x => x.Device).GroupBy(d => d.Owner))
            {
                try { owner.First().Save(); }
                catch (Exception ex) { Log.Here.Warn($"{owner.First().Name}: couldn't keep it: {ex.Message}"); }
            }
        });
    }

    private void Set(IRgbDevice d, LightSetting s)
    {
        try
        {
            if (Devices.Drawn(d, s.Effect))
            {
                drawn[d] = s.Copy();
            }
            else
            {
                drawn.Remove(d);
                d.Apply(s);
            }
            Problems.TryRemove(d.Id, out _);
            Log.Here.Debug(() => $"{d.Name}: {s.Effect} {s.Colour} {s.Speed}");
        }
        catch (Exception ex)
        {
            Problems[d.Id] = ex.Message;
            Log.Here.Warn($"{d.Name}: {ex.Message}");
            Changed?.Invoke();
        }
    }

    private void Loop()
    {
        while (!work.IsCompleted)
        {
            // drawing: a frame every 33 ms between jobs; else just wait for one
            Action? job;
            try
            {
                if (!work.TryTake(out job, drawn.Count > 0 ? 33 : Timeout.Infinite)) job = null;
            }
            catch (InvalidOperationException) { break; }
            if (job != null)
            {
                try { job(); }
                catch (Exception ex) { ErrorLog.Write("RGB", ex); }
            }
            if (drawn.Count > 0) Draw();
        }
    }

    private void Draw()
    {
        double t = clock.Elapsed.TotalSeconds;
        foreach (var (d, s) in drawn.ToList())
        {
            var colours = new Rgb[d.Leds];
            for (int i = 0; i < colours.Length; i++) colours[i] = Animation.At(s, t, i, colours.Length);
            try { d.Show(colours); }
            catch (Exception ex)
            {
                drawn.Remove(d);
                Problems[d.Id] = ex.Message;
                Log.Here.Warn($"{d.Name}: stopped drawing: {ex.Message}");
                Changed?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        work.Add(() =>
        {
            // a drawn effect stops on its own colour (not wherever it was), the others on the device go on by themselves
            foreach (var (d, s) in drawn)
            {
                try { if (LightSetting.HasColour(s.Effect)) d.Show(Enumerable.Repeat(s.Rgb, d.Leds).ToArray()); }
                catch { }
            }
            drawn.Clear();
            foreach (var d in local) d.Dispose();
            local.Clear();
        });
        work.CompleteAdding();
        thread.Join(3000);
    }
}

/// <summary>An SMBus device as last found (changes go through the administrator job).</summary>
internal sealed class SmbusProxy : IRgbDevice
{
    private readonly SmbusDevices.Seen seen;

    public SmbusProxy(SmbusDevices.Seen seen) => this.seen = seen;

    public string Id => seen.Id;
    public string Name => seen.Name;
    public DeviceKind Kind => seen.Kind;
    public string About => seen.About;
    public IReadOnlyCollection<LightEffect> Effects => EneChip.ChipEffects;
    public bool HasSpeed => true;
    public int Leds => 0;
    public bool NeedsAdmin => true;
    public object Owner => this;
    public void Apply(LightSetting s) { }
    public void Save() { }
    public void Show(Rgb[] colours) { }
    public void Dispose() { }
}
