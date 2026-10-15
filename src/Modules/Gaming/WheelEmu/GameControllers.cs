using Vortice.DirectInput;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>A game controller plugged in: a wheel, pedals, shifter, handbrake or button box (never a vJoy device).</summary>
public sealed record ControllerInfo(Guid Instance, Guid Product, string Name, int Vid, int Pid, string? InterfacePath,
    int Buttons, int Povs, bool[] HasAxis)
{
    /// <summary>Its HID device instance ID (HID\VID_…&amp;PID_…\…), which HidHide hides by; null if unknown.</summary>
    public string? HidInstanceId => GameControllers.InstanceIdFromPath(InterfacePath);
}

/// <summary>One reading: eight axes from 0 to 1 (NaN where it has none), 128 buttons, four hats (hundredths of a degree, -1 centred).</summary>
public sealed class ControllerState
{
    public readonly double[] Axes = new double[8];
    public readonly bool[] Buttons = new bool[VJoy.MaxButtons];
    public readonly int[] Povs = { -1, -1, -1, -1 };

    public void CopyTo(ControllerState other)
    {
        Array.Copy(Axes, other.Axes, 8);
        Array.Copy(Buttons, other.Buttons, Buttons.Length);
        Array.Copy(Povs, other.Povs, 4);
    }
}

/// <summary>
/// Game controllers through DirectInput, which (unlike the classic joystick API Joy2Key uses) reads all eight axes,
/// 128 buttons and four hats of each, as Forza does. Read in the background, non-exclusively, so other programs still
/// see them unless Hush hides them.
/// </summary>
public static class GameControllers
{
    private const int VJoyVid = 0x1234, VJoyPid = 0xBEAD;
    private static IDirectInput8? directInput;
    private static readonly object gate = new();

    private static IDirectInput8 DI => directInput ??= DInput.DirectInput8Create();

    /// <summary>The game controllers plugged in now, vJoy's own devices left out.</summary>
    public static List<ControllerInfo> List()
    {
        var list = new List<ControllerInfo>();
        lock (gate)
        {
            foreach (var d in DI.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                try
                {
                    using var dev = DI.CreateDevice(d.InstanceGuid);
                    var props = dev.Properties;
                    if (props.VendorId == VJoyVid && props.ProductId == VJoyPid) continue;
                    string? path = null;
                    try { path = props.InterfacePath; } catch { }
                    var caps = dev.Capabilities;
                    list.Add(new ControllerInfo(d.InstanceGuid, d.ProductGuid, d.InstanceName.Trim(), props.VendorId, props.ProductId, path,
                        Math.Min(caps.ButtonCount, VJoy.MaxButtons), Math.Min(caps.PovCount, 4), AxesOf(dev)));
                }
                catch (Exception ex) { Logging.Log.Here.Warn($"Couldn't read the controller {d.InstanceName}: {ex.Message}"); }
            }
        }
        return list;
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

    /// <summary>"\\?\hid#vid_046d&amp;pid_c24f#7&amp;1a2b#{…}" → "HID\VID_046D&amp;PID_C24F\7&amp;1A2B".</summary>
    public static string? InstanceIdFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.TrimStart('\\', '?').Split('#');
        if (parts.Length < 3) return null;
        return $@"{parts[0]}\{parts[1]}\{parts[2]}".ToUpperInvariant();
    }

    /// <summary>Opens a controller for reading, or null if it isn't plugged in.</summary>
    public static Reader? Open(Guid instance, IntPtr window)
    {
        lock (gate)
        {
            try
            {
                if (!DI.IsDeviceAttached(instance)) return null;
                var dev = DI.CreateDevice(instance);
                dev.SetCooperativeLevel(window, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                dev.SetDataFormat<RawJoystickState>();
                foreach (var o in dev.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis))
                {
                    try { dev.GetObjectPropertiesById(o.ObjectId).Range = new InputRange(0, 65535); } catch { }
                }
                dev.Acquire();
                return new Reader(instance, dev);
            }
            catch (Exception ex)
            {
                Logging.Log.Here.Warn($"Couldn't open the controller {instance}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>An open controller.</summary>
    public sealed class Reader : IDisposable
    {
        private readonly IDirectInputDevice8 dev;
        private JoystickState raw = new();
        private readonly bool[] hasAxis;

        public Guid Instance { get; }

        internal Reader(Guid instance, IDirectInputDevice8 dev)
        {
            Instance = instance;
            this.dev = dev;
            hasAxis = AxesOf(dev);
        }

        /// <summary>Reads it into <paramref name="state"/>; false if it has gone (unplugged).</summary>
        public bool Read(ControllerState state)
        {
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
            int[] v = { raw.X, raw.Y, raw.Z, raw.RotationX, raw.RotationY, raw.RotationZ, raw.Sliders[0], raw.Sliders[1] };
            for (int i = 0; i < 8; i++) state.Axes[i] = hasAxis[i] ? Math.Clamp(v[i] / 65535.0, 0, 1) : double.NaN;
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
            try { dev.Unacquire(); } catch { }
            dev.Dispose();
        }
    }
}
