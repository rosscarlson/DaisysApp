using System.Windows;
using System.Windows.Controls;
using Vortice.DirectInput;

namespace DaisysApp.Controllers;

/// <summary>A game controller plugged in: a wheel, pedals, shifter, handbrake or button box (never a vJoy device).</summary>
public sealed record ControllerInfo(Guid Instance, Guid Product, string Name, int Vid, int Pid, int UsagePage, int Usage,
    int Buttons, int Povs, bool[] HasAxis);

/// <summary>One reading: eight axes from 0 to 1 (NaN where it has none), 128 buttons, four hats (hundredths of a degree, -1 centred).</summary>
public sealed class ControllerState
{
    /// <summary>NaN until read (and where it has no such axis), so an unread device never looks like it moved.</summary>
    public readonly double[] Axes = { double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN };
    public readonly bool[] Buttons = new bool[GameControllers.MaxButtons];
    public readonly int[] Povs = { -1, -1, -1, -1 };

    public void CopyTo(ControllerState other)
    {
        Array.Copy(Axes, other.Axes, 8);
        Array.Copy(Buttons, other.Buttons, Buttons.Length);
        Array.Copy(Povs, other.Povs, 4);
    }
}

/// <summary>
/// Game controllers through DirectInput, which (unlike the classic joystick API) reads all eight axes, 128 buttons and
/// four hats of each, as Forza does: the one place every module (ForzaWheel, Button2Key, SimEffects, Calibration) finds
/// and reads them. Read in the background, non-exclusively, so other programs still see them unless Hush hides them.
/// The list of what's plugged in is made once, the first time it's asked for, and again only by <see cref="RefreshAsync"/>
/// (each module's Refresh button): listing opens every device, which a game running beside it feels (since 3.25.3;
/// before, four modules each listed them every few seconds).
/// </summary>
public static class GameControllers
{
    /// <summary>The most buttons a controller is read with (as many as a vJoy device has).</summary>
    public const int MaxButtons = 128;

    private const int VJoyVid = 0x1234, VJoyPid = 0xBEAD;
    private static IDirectInput8? directInput;
    private static List<ControllerInfo>? list;
    private static readonly object listGate = new();
    private static Task? refreshing;

    /// <summary>
    /// Held around every DirectInput call (a wheel's force-feedback driver runs inside the app, and Fanatec's crashed
    /// when called from two threads at once). Core's, so the joystick API's rule holds: never take
    /// <see cref="Shared.Hotkeys.ControllerButtons.JoystickApi"/> while holding it.
    /// </summary>
    public static readonly object Gate = Shared.Hotkeys.ControllerButtons.DirectInput;

    /// <summary>The one DirectInput (take <see cref="Gate"/> around its calls).</summary>
    public static IDirectInput8 DI => directInput ??= DInput.DirectInput8Create();

    /// <summary>Raised on the UI thread after <see cref="RefreshAsync"/> has listed the controllers again.</summary>
    public static event Action? Changed;

    /// <summary>The game controllers plugged in (vJoy's own devices left out), as last listed.</summary>
    public static List<ControllerInfo> List()
    {
        // (the first to ask lists them; others wait for it rather than listing too)
        lock (listGate) return (list ??= Enumerate()).ToList();
    }

    /// <summary>
    /// Lists the controllers again (in the background), and has Core's controller buttons look for them again too,
    /// then raises <see cref="Changed"/>. A second press while it runs waits for the same one.
    /// </summary>
    public static async Task RefreshAsync()
    {
        Task task;
        lock (listGate) task = refreshing ??= Task.Run(() =>
        {
            var fresh = Enumerate();
            lock (listGate) { list = fresh; refreshing = null; }
            Shared.Hotkeys.ControllerButtons.Rescan();
        });
        await task;
        Changed?.Invoke();
    }

    /// <summary>The Refresh button (a module's own tip, for its translations): lists the controllers again.</summary>
    public static Button RefreshButton(string tip)
    {
        var t = new TextBlock { Text = "", FontSize = 12 };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var b = new Button { Content = t, ToolTip = tip, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 5, 5), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += async (_, _) =>
        {
            b.IsEnabled = false;
            try { await RefreshAsync(); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }

    private static List<ControllerInfo> Enumerate()
    {
        var found = new List<ControllerInfo>();
        var took = System.Diagnostics.Stopwatch.StartNew();
        // (held a device at a time, so readers and force feedback wait one device's look, not the whole list's)
        List<DeviceInstance> devices;
        try { lock (Gate) devices = DI.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly).ToList(); }
        catch (Exception ex)
        {
            Logging.Log.App.Warn($"Couldn't list the game controllers: {ex.Message}");
            return found;
        }
        foreach (var d in devices)
        {
            try
            {
                lock (Gate)
                {
                    using var dev = DI.CreateDevice(d.InstanceGuid);
                    var props = dev.Properties;
                    if (props.VendorId == VJoyVid && props.ProductId == VJoyPid) continue;
                    var caps = dev.Capabilities;
                    found.Add(new ControllerInfo(d.InstanceGuid, d.ProductGuid, d.InstanceName.Trim(), props.VendorId, props.ProductId, (int)d.UsagePage, (int)d.Usage,
                        Math.Min(caps.ButtonCount, MaxButtons), Math.Min(caps.PovCount, 4), AxesOf(dev)));
                }
            }
            catch (Exception ex) { Logging.Log.App.Warn($"Couldn't read the controller {d.InstanceName}: {ex.Message}"); }
        }
        Logging.Log.App.Info($"Game controllers (listed in {took.ElapsedMilliseconds} ms): " + (found.Count == 0 ? "none" : string.Join(", ", found.Select(c => $"{c.Name} ({c.Vid:X4}:{c.Pid:X4})"))));
        return found;
    }

    private static bool[] AxesOf(IDirectInputDevice8 dev)
    {
        var has = new bool[8];
        int sliders = 0;
        foreach (var o in dev.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis))
        {
            int i = AxisIndex(o.ObjectType, ref sliders);
            if (i >= 0) has[i] = true;
        }
        return has;
    }

    private static int AxisIndex(Guid type, ref int sliders)
    {
        if (type == ObjectGuid.XAxis) return 0;
        if (type == ObjectGuid.YAxis) return 1;
        if (type == ObjectGuid.ZAxis) return 2;
        if (type == ObjectGuid.RxAxis) return 3;
        if (type == ObjectGuid.RyAxis) return 4;
        if (type == ObjectGuid.RzAxis) return 5;
        if (type == ObjectGuid.Slider) return sliders < 2 ? 6 + sliders++ : -1;
        return -1;
    }

    /// <summary>
    /// Opens a controller for reading, or null if it isn't plugged in. <paramref name="forceFeedback"/>: exclusively,
    /// with its auto-centre spring off, as force feedback needs (EmuWheel opens the wheel so).
    /// </summary>
    public static Reader? Open(Guid instance, IntPtr window, bool forceFeedback = false)
    {
        lock (Gate)
        {
            try
            {
                if (!DI.IsDeviceAttached(instance)) return null;
                var dev = DI.CreateDevice(instance);
                dev.SetCooperativeLevel(window, CooperativeLevel.Background | (forceFeedback ? CooperativeLevel.Exclusive : CooperativeLevel.NonExclusive));
                dev.SetDataFormat<RawJoystickState>();
                foreach (var o in dev.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis))
                {
                    try { dev.GetObjectPropertiesById(o.ObjectId).Range = new InputRange(0, 65535); } catch { }
                }
                if (forceFeedback)
                {
                    try { dev.Properties.AutoCenter = false; } catch { }
                }
                dev.Acquire();
                return new Reader(instance, dev);
            }
            catch (Exception ex)
            {
                Logging.Log.App.Warn($"Couldn't open the controller {instance}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>An open controller.</summary>
    public sealed class Reader : IDisposable
    {
        private readonly IDirectInputDevice8 dev;
        private JoystickState raw = new();
        private readonly bool[] hasAxis = new bool[8];
        /// <summary>Each axis's range as the device reports it (0–65535 is asked for, but not every driver takes it).</summary>
        private readonly double[] min = new double[8], max = Enumerable.Repeat(65535.0, 8).ToArray();

        public Guid Instance { get; }

        /// <summary>The DirectInput device, for force-feedback effects.</summary>
        public IDirectInputDevice8 Device => dev;

        internal Reader(Guid instance, IDirectInputDevice8 dev)
        {
            Instance = instance;
            this.dev = dev;
            int sliders = 0;
            foreach (var o in dev.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis))
            {
                int i = AxisIndex(o.ObjectType, ref sliders);
                if (i < 0) continue;
                hasAxis[i] = true;
                try
                {
                    var range = dev.GetObjectPropertiesById(o.ObjectId).Range;
                    if (range.Maximum > range.Minimum) { min[i] = range.Minimum; max[i] = range.Maximum; }
                }
                catch { }
            }
        }

        /// <summary>Reads it into <paramref name="state"/>; false if it has gone (unplugged).</summary>
        public bool Read(ControllerState state)
        {
            // two threads may share a reader (Button2Key's engine and its tab): one disposing it while the other reads
            // would call a released device
            lock (this)
            lock (Gate)
            {
                if (disposed) return false;
                try
                {
                    if (dev.Poll().Failure)
                    {
                        dev.Acquire();
                        dev.Poll();
                    }
                    dev.GetCurrentJoystickState(ref raw);
                }
                catch { return false; }
            }
            int[] v = { raw.X, raw.Y, raw.Z, raw.RotationX, raw.RotationY, raw.RotationZ, raw.Sliders[0], raw.Sliders[1] };
            for (int i = 0; i < 8; i++) state.Axes[i] = hasAxis[i] ? Math.Clamp((v[i] - min[i]) / (max[i] - min[i]), 0, 1) : double.NaN;
            int n = Math.Min(raw.Buttons.Length, state.Buttons.Length);
            for (int i = 0; i < n; i++) state.Buttons[i] = raw.Buttons[i];
            for (int i = 0; i < 4; i++)
            {
                int p = raw.PointOfViewControllers[i];
                state.Povs[i] = p < 0 || p > 36000 ? -1 : p; // a centred hat reads 0xFFFF (or -1)
            }
            return true;
        }

        public void Dispose()
        {
            lock (this)
            lock (Gate)
            {
                if (disposed) return;
                disposed = true;
                try { dev.Unacquire(); } catch { }
                dev.Dispose();
            }
        }

        private bool disposed;
    }
}
