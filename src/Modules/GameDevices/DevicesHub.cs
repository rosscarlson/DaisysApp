using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DaisysApp.Controllers;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;
using DaisysApp.Shell;

namespace DaisysApp.Applets.GameDevices;

/// <summary>A device on the list: plugged in (<see cref="Info"/>), or in the normal order but not plugged in.</summary>
internal sealed class Device
{
    public required KnownDevice Known { get; init; }
    public ControllerInfo? Info { get; init; }
    /// <summary>Its joystick number, from 1.</summary>
    public int? Number { get; init; }
    /// <summary>Its place in DirectInput's list, from 1.</summary>
    public int Position { get; init; }
    /// <summary>Programs using the classic joystick API can see it.</summary>
    public bool InJoystickApi { get; init; }

    public string Key => Known.Key;
    public bool Connected => Info != null;
    public string Name => string.IsNullOrWhiteSpace(Known.Name) ? Known.WindowsName : Known.Name;
    public DeviceKind Detected => Info != null ? DeviceKinds.Detect(Info) : DeviceKind.Other;
    public DeviceKind Kind => Known.Kind ?? Detected;
    public int FirstGear => Known.FirstGear ?? DeviceKinds.FirstGear(Kind, Known.WindowsName);
    public bool[] HasAxis => Info?.HasAxis ?? new bool[8];
    public Dictionary<string, int> Roles => DeviceKinds.Assign(Kind, HasAxis, Known.Roles);

    /// <summary>A reading (0 – 1) as the tab's bars show it, the way the picture goes: a reversed control turned over, and
    /// Invert turning it over again. Only what's shown: games get the reading as it is.</summary>
    public double Shown(int axis, double reading)
    {
        bool reversed = Roles.Any(r => r.Value == axis && DeviceKinds.Reversed(Kind, r.Key));
        return reversed != Known.Inverted.Contains(axis) ? 1 - reading : reading;
    }
}

/// <summary>
/// The list of devices, what's normal and what differs from it, and the devices' widgets. The list is Core's one list
/// (<see cref="GameControllers.ListAll"/>), looked at when the app starts, on Refresh and when Windows says a device came
/// or went; the joystick numbers are read with it. <see cref="Ticked"/> comes about 30 times a second while the tab or a
/// widget shows.
/// </summary>
internal sealed class DevicesHub : IDisposable
{
    private const string BadgeId = "GameDevices";

    private readonly DevicesSettings settings;
    private readonly LiveDevices live;
    private readonly HotPlug hotPlug;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Dictionary<string, (Widget Window, DeviceArt Art)> widgets = new();
    private readonly ControllerState reading = new();
    private List<Device> devices = new();
    private bool listing, again, lookNext, viewShowing, widgetsRestored, disposed;

    public DevicesHub(DevicesSettings settings, HwndSource window)
    {
        this.settings = settings;
        live = new LiveDevices(window.Handle);
        timer.Tick += (_, _) => Tick();
        hotPlug = new HotPlug(window, () => _ = GameControllers.RefreshAsync());
        GameControllers.Changed += Relisted;
    }

    public DevicesSettings Settings => settings;
    public IReadOnlyList<Device> Devices => devices;
    public IReadOnlyList<DeviceAlert> Alerts => settings.Alerts;

    /// <summary>The list or the alerts changed.</summary>
    public event Action? Changed;

    public event Action? Ticked;

    public void Start()
    {
        ShowBadge();
        _ = Relist(false);
    }

    /// <summary>The tab shows (read while it does), or stopped showing.</summary>
    public void ViewShowing(bool on)
    {
        viewShowing = on;
        Reading();
        if (on) _ = Relist(false);
    }

    private void Reading()
    {
        bool on = viewShowing || widgets.Count > 0;
        live.Running = on;
        if (on) timer.Start();
        else timer.Stop();
    }

    private void Tick()
    {
        foreach (var (key, w) in widgets)
            w.Art.Show(devices.FirstOrDefault(d => d.Key == key), this);
        Ticked?.Invoke();
    }

    /// <summary>Its last reading into <paramref name="into"/>; false if there's none.</summary>
    public bool Read(Device d, ControllerState into) => d.Info != null && live.Get(d.Info.Instance, into);

    public bool Active(Device d) => d.Info != null && live.Active(d.Info.Instance);

    // ---------------------------------------------------------------- the list

    private void Relisted() => Application.Current?.Dispatcher.BeginInvoke(() => _ = Relist(true));

    /// <summary>Reads the list and the numbers (in the background: DirectInput opens each device to ask) and compares them
    /// with what's normal. <paramref name="lookAgain"/>: a device came or went, so the joystick API is told to look again.</summary>
    private async Task Relist(bool lookAgain)
    {
        if (listing) { again = true; lookNext |= lookAgain; return; }
        listing = true;
        try
        {
            var (list, slots) = await Task.Run(() =>
            {
                var l = GameControllers.ListAll();
                if (FakeDevices.On) l.InsertRange(0, FakeDevices.List());
                return (l, JoystickSlots(lookAgain));
            });
            if (disposed) return;
            Apply(list, slots);
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Couldn't list the devices: {ex.Message}"); }
        finally { listing = false; }
        if (again)
        {
            again = false;
            bool look = lookNext;
            lookNext = false;
            await Relist(look);
        }
    }

    /// <summary>The classic joystick API's 16 numbers: the VID and PID on each (null: empty).</summary>
    private static (int Vid, int Pid)?[] JoystickSlots(bool lookAgain)
    {
        var slots = new (int, int)?[16];
        lock (ControllerButtons.JoystickApi)
        {
            if (lookAgain) { try { joyConfigChanged(0); } catch { } }
            for (int i = 0; i < 16; i++)
                if (joyGetDevCapsW(i, out var caps, Marshal.SizeOf<JOYCAPSW>()) == 0) slots[i] = (caps.wMid, caps.wPid);
        }
        return slots;
    }

    /// <summary>
    /// Each device's joystick number: the one DirectInput gives it (<see cref="ControllerInfo.JoystickId"/>) if the
    /// joystick API has that device there, else one the joystick API has it on (two alike: in order). A device it hasn't
    /// (it can leave some out) keeps DirectInput's number, unless that's taken, and <c>InApi</c> false.
    /// </summary>
    internal static (int? Number, bool InApi)[] Numbers(IReadOnlyList<ControllerInfo> list, IReadOnlyList<(int Vid, int Pid)?> slots)
    {
        var result = new (int? Number, bool InApi)[list.Count];
        var used = new HashSet<int>();
        for (int i = 0; i < list.Count; i++)
        {
            int id = list[i].JoystickId;
            if (id < 0 || id >= slots.Count || slots[id] != (list[i].Vid, list[i].Pid) || !used.Add(id)) continue;
            result[i] = (id + 1, true);
        }
        for (int i = 0; i < list.Count; i++)
        {
            if (result[i].Number != null) continue;
            int free = Enumerable.Range(0, slots.Count).Where(s => !used.Contains(s) && slots[s] == (list[i].Vid, list[i].Pid)).DefaultIfEmpty(-1).First();
            if (free < 0) continue;
            used.Add(free);
            result[i] = (free + 1, true);
        }
        for (int i = 0; i < list.Count; i++)
        {
            int id = list[i].JoystickId;
            if (result[i].Number == null && id >= 0 && used.Add(id)) result[i] = (id + 1, false);
        }
        return result;
    }

    private void Apply(List<ControllerInfo> list, (int Vid, int Pid)?[] slots)
    {
        var numbers = Numbers(list, slots);
        var here = list.Select(c => c.Instance.ToString()).ToHashSet();
        var now = new List<Device>();
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            now.Add(new Device
            {
                Known = KnownFor(c, here), Info = c, Number = numbers[i].Number, Position = i + 1, InJoystickApi = numbers[i].InApi,
            });
        }
        if (!settings.NormalSet && now.Count > 0)
        {
            settings.NormalSet = true;
            settings.Normal = now.Select(Slot).ToList();
            Logging.Log.Here.Info("Normal order: " + Order(now));
        }
        foreach (var s in settings.Normal)
        {
            if (now.Any(d => d.Key == s.Key)) continue;
            if (settings.Known.FirstOrDefault(k => k.Key == s.Key) is { } k)
                now.Add(new Device { Known = k, Number = s.Number, Position = s.Position });
        }
        devices = now.OrderBy(d => d.Number ?? int.MaxValue).ThenBy(d => d.Position).ToList();
        live.Follow(list);
        Compare();
        settings.Save();
        RestoreWidgets();
        ShowBadge();
        Changed?.Invoke();
    }

    private static NormalSlot Slot(Device d) => new() { Key = d.Key, Number = d.Number, Position = d.Position };

    private static string Order(IEnumerable<Device> list) =>
        string.Join(", ", list.Select(d => $"{d.Number?.ToString() ?? "-"}: {d.Name} ({d.Known.Vid:X4}:{d.Known.Pid:X4})"));

    /// <summary>
    /// What's kept for this device. A device with a new instance and the VID and PID of one not plugged in is that one,
    /// moved to another port (Windows makes a new instance then), so it keeps its name, type and place.
    /// </summary>
    private KnownDevice KnownFor(ControllerInfo c, HashSet<string> here)
    {
        string key = c.Instance.ToString();
        var k = settings.Known.FirstOrDefault(x => x.Key == key);
        if (k == null && (k = settings.Known.FirstOrDefault(x => x.Vid == c.Vid && x.Pid == c.Pid && !here.Contains(x.Key))) != null)
        {
            Logging.Log.Here.Info($"{c.Name}: a new instance, {k.Key} → {key} (another port)");
            foreach (var s in settings.Normal.Where(s => s.Key == k.Key)) s.Key = key;
            foreach (var a in settings.Alerts.Where(a => a.Key == k.Key)) a.Key = key;
            if (settings.Expanded.Remove(k.Key)) settings.Expanded.Add(key);
            if (settings.Selected == k.Key) settings.Selected = key;
            if (widgets.Remove(k.Key, out var w)) widgets[key] = w;
            k.Key = key;
        }
        if (k == null)
        {
            k = new KnownDevice { Key = key, Vid = c.Vid, Pid = c.Pid };
            settings.Known.Add(k);
        }
        k.WindowsName = c.Name;
        return k;
    }

    /// <summary>
    /// Alerts for what differs from normal, kept (the Alerts list is saved) until Set as Normal: an order change stays
    /// even if the order goes back. A device missing or new isn't a change of order, so those go when it's over.
    /// </summary>
    private void Compare()
    {
        var want = new List<DeviceAlert>();
        foreach (var s in settings.Normal)
        {
            var d = devices.FirstOrDefault(x => x.Key == s.Key);
            if (d == null) continue;
            string name = d.Name;
            if (!d.Connected)
                want.Add(new DeviceAlert { Id = $"missing:{s.Key}", Key = s.Key, Text = s.Number != null ? F("{0} is missing (it's number {1})", name, s.Number) : F("{0} is missing", name) });
            else if (d.Number != s.Number)
                want.Add(new DeviceAlert { Id = $"number:{s.Key}:{s.Number}:{d.Number}", Key = s.Key, Order = true, Text = F("{0}: number {1} → {2}", name, Show(s.Number), Show(d.Number)) });
            else if (d.Position != s.Position)
                want.Add(new DeviceAlert { Id = $"position:{s.Key}:{s.Position}:{d.Position}", Key = s.Key, Order = true, Text = F("{0}: DirectInput order {1} → {2}", name, s.Position, d.Position) });
        }
        foreach (var d in devices.Where(d => d.Connected && settings.Normal.All(s => s.Key != d.Key)))
            want.Add(new DeviceAlert { Id = $"new:{d.Key}", Key = d.Key, Text = F("New: {0} (number {1})", d.Name, Show(d.Number)) });

        settings.Alerts.RemoveAll(a => !a.Order && want.All(w => w.Id != a.Id));
        var fresh = want.Where(w => settings.Alerts.All(a => a.Id != w.Id)).ToList();
        foreach (var a in fresh)
        {
            a.When = DateTime.Now;
            settings.Alerts.Add(a);
            Logging.Log.Here.Warn(a.Text);
        }
        if (fresh.Count == 0) return;
        Logging.Log.Here.Info("Order now: " + Order(devices.Where(d => d.Connected)));
        Notifications.Show(T("Devices"), fresh.Any(a => a.Order) ? T("The order of your game controllers changed") : fresh[0].Text);
    }

    public static string Show(int? number) => number?.ToString() ?? "–";

    /// <summary>What's plugged in now is the new normal: the alerts go.</summary>
    public void SetNormal()
    {
        settings.Normal = devices.Where(d => d.Connected).Select(Slot).ToList();
        settings.NormalSet = true;
        settings.Alerts.Clear();
        Logging.Log.Here.Info("Normal order: " + Order(devices.Where(d => d.Connected)));
        settings.Save();
        devices = devices.Where(d => d.Connected).ToList();
        ShowBadge();
        Changed?.Invoke();
    }

    /// <summary>The title bar's bubble while there's an alert: red for an order change.</summary>
    private void ShowBadge()
    {
        bool order = settings.Alerts.Any(a => a.Order);
        TitleBadges.Set(BadgeId, settings.Alerts.Count == 0 ? null : order ? T("Devices: order changed") : T("Devices: check"),
            string.Join("\n", settings.Alerts.Select(a => a.Text)), order ? TitleBadges.Red : TitleBadges.Amber);
    }

    // ---------------------------------------------------------------- what the user sets

    public void Saved(Device d)
    {
        settings.Save();
        if (widgets.TryGetValue(d.Key, out var w)) w.Window.Title = d.Name;
    }

    public void Rename(Device d, string name)
    {
        d.Known.Name = string.IsNullOrWhiteSpace(name) || name.Trim() == d.Known.WindowsName ? null : name.Trim();
        Saved(d);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- widgets

    public bool HasWidget(Device d) => widgets.ContainsKey(d.Key);

    /// <summary>Make Widget: made and shown to be placed (one made already: its edit strip).</summary>
    public void MakeWidget(Device d)
    {
        if (!widgets.TryGetValue(d.Key, out var w))
        {
            (d.Known.Widget ??= new DeviceWidgetPlace()).Show = true;
            settings.Save();
            w = OpenWidget(d);
            Reading();
        }
        w.Window.BeginEdit();
    }

    private (Widget Window, DeviceArt Art) OpenWidget(Device d)
    {
        string key = d.Key;
        var art = new DeviceArt();
        var window = new Widget(d.Known.Widget!, settings.Save, () => DeleteWidget(key), firstSize: new Size(360, 240)) { Body = art, Title = d.Name };
        var made = (window, art);
        widgets[key] = made;
        window.Show();
        return made;
    }

    private void DeleteWidget(string key)
    {
        if (!widgets.Remove(key, out var w)) return;
        if (settings.Known.FirstOrDefault(k => k.Key == key) is { Widget: { } place }) place.Show = false;
        w.Window.Close();
        settings.Save();
        Reading();
    }

    /// <summary>The widgets left open when the app last closed, the first time the devices are known.</summary>
    private void RestoreWidgets()
    {
        if (widgetsRestored) return;
        widgetsRestored = true;
        foreach (var d in devices.Where(d => d.Known.Widget?.Show == true)) OpenWidget(d);
        Reading();
    }

    public void Dispose()
    {
        disposed = true;
        GameControllers.Changed -= Relisted;
        timer.Stop();
        hotPlug.Dispose();
        foreach (var (_, w) in widgets) w.Window.Close();
        widgets.Clear();
        live.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JOYCAPSW
    {
        public ushort wMid, wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax, wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int joyGetDevCapsW(nint id, out JOYCAPSW caps, int size);
    [DllImport("winmm.dll")] private static extern int joyConfigChanged(int flags);
}
