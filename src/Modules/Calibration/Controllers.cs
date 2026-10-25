using System.Runtime.InteropServices;
using Vortice.DirectInput;

namespace DaisysApp.Applets.Calibration;

/// <summary>
/// One axis of a controller: its place in a reading, DirectInput's name and id for it, and the range its device
/// reports (HID's logical range, in raw units: what Windows' default calibration is).
/// </summary>
public sealed record Axis(int Index, string Name, int ObjectId, int Offset, int Low, int High)
{
    /// <summary>Windows' calibration for an axis nobody has calibrated: its whole range, centred.</summary>
    public Cal Default => Cal.Straight(Low, High);
}

/// <summary>A controller plugged in. <see cref="Key"/> is how Windows files its calibration: by VID and PID.</summary>
public sealed record ControllerInfo(Guid Instance, string Name, int Vid, int Pid, int AxisCount)
{
    public string Key => $"{Vid:X4}:{Pid:X4}";
}

/// <summary>A controller open for calibrating: raw and calibrated readings, and its calibration to get and set.</summary>
public interface IController : IDisposable
{
    ControllerInfo Info { get; }
    IReadOnlyList<Axis> Axes { get; }

    /// <summary>
    /// Reads every axis: <paramref name="raw"/> as the device sends it, <paramref name="output"/> (0 to 1) as games get
    /// it, after Windows' calibration. False when it has gone (unplugged).
    /// </summary>
    bool Read(int[] raw, double[] output);

    /// <summary>The calibration Windows has for the axis now.</summary>
    Cal Get(Axis axis);

    /// <summary>Gives Windows the axis's new calibration, which it keeps (for every program, after restarts too); the problem, or null.</summary>
    string? Set(Axis axis, Cal cal);
}

/// <summary>
/// Game controllers through DirectInput, as Windows' Game Controllers page (joy.cpl) and Heusinkveld's DIView calibrate
/// them: DIPROP_CALIBRATION per axis, which DirectInput keeps in HKCU\System\CurrentControlSet\Control\
/// MediaProperties\PrivateProperties\DirectInput\VID_…&amp;PID_…\Calibration\…\Type\Axes\n (min, centre, max), so every
/// program reading the device through DirectInput or the classic joystick API gets it calibrated. Each controller is
/// open twice: once in raw mode (DIPROP_CALIBRATIONMODE raw: what it sends) and once as games see it.
/// </summary>
public static class Controllers
{
    private const int VJoyVid = 0x1234, VJoyPid = 0xBEAD;
    private static IDirectInput8? directInput;
    private static readonly object gate = new();

    internal static IDirectInput8 DI => directInput ??= DInput.DirectInput8Create();

    /// <summary>A pretend set of pedals, for trying the tab on a PC with none (DAISYSAPP_FAKE_CONTROLLER=1).</summary>
    internal static bool Fake => Environment.GetEnvironmentVariable("DAISYSAPP_FAKE_CONTROLLER") == "1";

    /// <summary>The game controllers plugged in that have an axis (vJoy's own devices left out).</summary>
    public static List<ControllerInfo> List()
    {
        var list = new List<ControllerInfo>();
        if (Fake) list.Add(FakeController.Info);
        lock (gate)
        {
            try
            {
                foreach (var d in DI.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
                {
                    try
                    {
                        using var dev = DI.CreateDevice(d.InstanceGuid);
                        var props = dev.Properties;
                        if (props.VendorId == VJoyVid && props.ProductId == VJoyPid) continue;
                        int axes = dev.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis).Count;
                        if (axes > 0) list.Add(new ControllerInfo(d.InstanceGuid, d.InstanceName.Trim(), props.VendorId, props.ProductId, axes));
                    }
                    catch (Exception ex) { Logging.Log.Here.Debug(() => $"Couldn't read the controller {d.InstanceName}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Logging.Log.Here.Warn($"Couldn't list the controllers: {ex.Message}"); }
        }
        return list;
    }

    /// <summary>Opens a controller, or null when it isn't there (the reason in <paramref name="problem"/>).</summary>
    public static IController? Open(ControllerInfo info, IntPtr window, out string? problem)
    {
        problem = null;
        if (info.Instance == FakeController.Info.Instance) return new FakeController();
        lock (gate)
        {
            try
            {
                if (!DI.IsDeviceAttached(info.Instance)) { problem = T("Not connected"); return null; }
                return new DiController(info, window);
            }
            catch (Exception ex)
            {
                Logging.Log.Here.Warn($"Couldn't open the controller {info.Name}: {ex.Message}");
                problem = ex.Message;
                return null;
            }
        }
    }

    /// <summary>Tells programs that read controllers through the classic joystick API that the calibration changed (as joy.cpl does).</summary>
    [DllImport("winmm.dll")]
    internal static extern int joyConfigChanged(int flags);
}

/// <summary>A controller through DirectInput (see <see cref="Controllers"/>).</summary>
internal sealed unsafe class DiController : IController
{
    // DirectInput's properties are MAKEDIPROP(n): small numbers in place of a GUID pointer
    private const int PropCalibrationMode = 10, PropCalibration = 11, PropLogicalRange = 19;
    private const uint ByDevice = 0, ById = 2;
    private const int StateSize = 272;   // DIJOYSTATE2, what RawJoystickState's data format is

    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Size, HeaderSize, Obj, How; }
    [StructLayout(LayoutKind.Sequential)] private struct PropDword { public Header H; public uint Data; }
    [StructLayout(LayoutKind.Sequential)] private struct PropRange { public Header H; public int Min, Max; }
    [StructLayout(LayoutKind.Sequential)] private struct PropCal { public Header H; public int Min, Center, Max; }

    private readonly IntPtr window;
    private readonly List<Axis> axes = new();
    private IDirectInputDevice8 raw;
    private IDirectInputDevice8 cooked;
    private readonly byte[] state = new byte[StateSize];
    private readonly int[] cookedLow, cookedHigh;

    public ControllerInfo Info { get; }
    public IReadOnlyList<Axis> Axes => axes;

    public DiController(ControllerInfo info, IntPtr window)
    {
        Info = info;
        this.window = window;
        raw = Make(rawMode: true);
        foreach (var o in raw.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis))
        {
            // the offset in the data format set: axes past DIJOYSTATE2's (none, in practice) can't be read
            if (o.Offset < 0 || o.Offset > StateSize - 4) continue;
            int id = (int)o.ObjectId;
            var r = new PropRange { H = Head(sizeof(PropRange), (uint)id, ById) };
            int low = 0, high = 65535;
            if (Call(raw, 5, PropLogicalRange, &r.H) >= 0 && r.Max > r.Min) { low = r.Min; high = r.Max; }
            axes.Add(new Axis(axes.Count, string.IsNullOrWhiteSpace(o.Name) ? $"Axis {axes.Count + 1}" : o.Name.Trim(), id, o.Offset, low, high));
        }
        axes.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        for (int i = 0; i < axes.Count; i++) axes[i] = axes[i] with { Index = i };
        cookedLow = new int[axes.Count];
        cookedHigh = new int[axes.Count];
        cooked = Make(rawMode: false);
        Acquire();
        Logging.Log.Here.Info($"Opened {info.Name} ({info.Key}): " + string.Join(", ", axes.Select(a => $"{a.Name} {a.Low}–{a.High}, now {Get(a)}")));
    }

    /// <summary>
    /// The device, read non-exclusively in the background so games still see it. Raw: as it sends (calibration
    /// ignored); cooked: as games get it, each axis 0–65535.
    /// </summary>
    private IDirectInputDevice8 Make(bool rawMode)
    {
        var dev = Controllers.DI.CreateDevice(Info.Instance);
        dev.SetCooperativeLevel(window, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
        dev.SetDataFormat<RawJoystickState>();
        if (rawMode)
        {
            var m = new PropDword { H = Head(sizeof(PropDword), 0, ByDevice), Data = 1 };   // DIPROPCALIBRATIONMODE_RAW
            int hr = Call(dev, 6, PropCalibrationMode, &m.H);
            if (hr < 0) Logging.Log.Here.Warn($"{Info.Name}: raw readings weren't allowed (0x{hr:X8})");
        }
        else
        {
            foreach (var o in dev.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis))
            {
                try { dev.GetObjectPropertiesById(o.ObjectId).Range = new InputRange(0, 65535); } catch { }
            }
        }
        return dev;
    }

    private void Acquire()
    {
        try { raw.Acquire(); } catch { }
        try { cooked.Acquire(); } catch { }
    }

    private static Header Head(int size, uint obj, uint how) => new() { Size = (uint)size, HeaderSize = (uint)sizeof(Header), Obj = obj, How = how };

    // IDirectInputDevice8W's vtable: 5 GetProperty, 6 SetProperty, 9 GetDeviceState (Vortice keeps the first two to itself)
    private static int Call(IDirectInputDevice8 dev, int slot, int prop, Header* h)
    {
        var vt = *(IntPtr**)dev.NativePointer;
        return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Header*, int>)vt[slot])(dev.NativePointer, prop, h);
    }

    private bool ReadState(IDirectInputDevice8 dev)
    {
        try
        {
            if (dev.Poll().Failure)
            {
                dev.Acquire();
                dev.Poll();
            }
            var vt = *(IntPtr**)dev.NativePointer;
            fixed (byte* p = state)
                return ((delegate* unmanaged[Stdcall]<IntPtr, int, byte*, int>)vt[9])(dev.NativePointer, StateSize, p) >= 0;
        }
        catch { return false; }
    }

    public bool Read(int[] rawValues, double[] output)
    {
        if (!ReadState(raw)) return false;
        foreach (var a in axes) rawValues[a.Index] = BitConverter.ToInt32(state, a.Offset);
        if (!ReadState(cooked)) return false;
        foreach (var a in axes) output[a.Index] = Math.Clamp(BitConverter.ToInt32(state, a.Offset) / 65535.0, 0, 1);
        return true;
    }

    public Cal Get(Axis axis)
    {
        var c = new PropCal { H = Head(sizeof(PropCal), (uint)axis.ObjectId, ById) };
        return Call(raw, 5, PropCalibration, &c.H) >= 0 ? new Cal(c.Min, c.Center, c.Max) : axis.Default;
    }

    public string? Set(Axis axis, Cal cal)
    {
        if (!cal.Valid) return T("Min, centre and max must go up in that order");
        try { raw.Unacquire(); } catch { }
        var c = new PropCal { H = Head(sizeof(PropCal), (uint)axis.ObjectId, ById), Min = cal.Min, Center = cal.Center, Max = cal.Max };
        int hr = Call(raw, 6, PropCalibration, &c.H);
        // the cooked copy read its calibration when it was made: made again, it reads as games will now
        try { cooked.Unacquire(); } catch { }
        cooked.Dispose();
        cooked = Make(rawMode: false);
        Acquire();
        if (hr < 0)
        {
            Logging.Log.Here.Warn($"{Info.Name} {axis.Name}: Windows didn't take {cal} (0x{hr:X8})");
            return F("Windows didn't take it (0x{0:X8})", hr);
        }
        var now = Get(axis);
        if (now != cal)
        {
            Logging.Log.Here.Warn($"{Info.Name} {axis.Name}: set {cal}, Windows has {now}");
            return F("Windows kept {0}", now);
        }
        try { Controllers.joyConfigChanged(0); } catch { }
        return null;
    }

    public void Dispose()
    {
        try { raw.Unacquire(); } catch { }
        try { cooked.Unacquire(); } catch { }
        raw.Dispose();
        cooked.Dispose();
    }
}

/// <summary>Pretend pedals (three 12-bit axes moving by themselves), so the tab can be tried with nothing plugged in.</summary>
internal sealed class FakeController : IController
{
    public static readonly ControllerInfo Info = new(new Guid("6a1c0de0-0000-4000-8000-00000000fa4e"), "Test Pedals", 0x1209, 0xFA4E, 3);

    private readonly Axis[] axes =
    {
        new(0, "X Axis", 1, 0, 0, 4095),
        new(1, "Y Axis", 2, 4, 0, 4095),
        new(2, "Z Axis", 3, 8, 0, 4095),
    };
    private readonly Cal[] cals;
    private readonly DateTime start = DateTime.UtcNow;

    ControllerInfo IController.Info => Info;
    public IReadOnlyList<Axis> Axes => axes;

    public FakeController() => cals = axes.Select(a => a.Default).ToArray();

    public bool Read(int[] raw, double[] output)
    {
        double t = (DateTime.UtcNow - start).TotalSeconds;
        for (int i = 0; i < axes.Length; i++)
        {
            // a pedal that never quite reaches either end: 300 to 3800
            double s = 0.5 - 0.5 * Math.Cos(t * (0.9 + 0.35 * i));
            raw[i] = (int)(300 + s * 3500);
            output[i] = cals[i].Cook(raw[i]);
        }
        return true;
    }

    public Cal Get(Axis axis) => cals[axis.Index];

    public string? Set(Axis axis, Cal cal)
    {
        if (!cal.Valid) return T("Min, centre and max must go up in that order");
        cals[axis.Index] = cal;
        return null;
    }

    public void Dispose() { }
}
