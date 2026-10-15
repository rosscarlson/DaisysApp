using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming.Joy2Key;

/// <summary>
/// A connected controller. Xbox-style (XInput) pads are read through XInput and numbered the way JoyToKey numbers them
/// (triggers as buttons 11 and 12 too, the guide button 13, right stick as axes 3 and 4); anything else through the
/// classic Windows joystick API, as JoyToKey reads DirectInput devices.
/// </summary>
public sealed record JoyInfo(int Id, string Name, int Vid, int Pid, int Buttons, int Axes, bool HasPov, int Occurrence,
    uint[] Min, uint[] Max)
{
    /// <summary>Which of its axes exist: X, Y, Z, R, U, V.</summary>
    public bool[] HasAxis { get; init; } = new bool[6];

    /// <summary>The XInput slot (0–3), or -1 for a joystick-API device.</summary>
    public int XInputSlot { get; init; } = -1;

    public bool IsXInput => XInputSlot >= 0;

    /// <summary>
    /// The same controller through DirectInput, when it sees more of it than the joystick API does (which stops at 32
    /// buttons, 6 axes and one hat, and misreads some devices: a Saitek / Logitech X-56 stick shows 1 button and 2
    /// axes there): its buttons, axes or hats are then read through DirectInput. Null otherwise.
    /// </summary>
    public Guid? DirectInput { get; init; }
    public bool DiButtons { get; init; }
    public bool DiAxes { get; init; }
    public bool DiPov { get; init; }
    /// <summary>Hats after the first (DirectInput has up to four).</summary>
    public int ExtraHats { get; init; }
}

/// <summary>One reading of a controller: axes from -1 to 1, buttons 1–128 as bits (32 a word), hats in degrees (-1 = centred).</summary>
public struct JoyState
{
    public float X, Y, Z, R, U, V;
    public uint Buttons, B2, B3, B4;
    public int Pov, Pov2, Pov3, Pov4;

    public readonly float Axis(int i) => i switch { 0 => X, 1 => Y, 2 => Z, 3 => R, 4 => U, _ => V };

    /// <summary>Whether button <paramref name="n"/> (1–128) is down.</summary>
    public readonly bool Button(int n)
    {
        if (n is < 1 or > 128) return false;
        uint word = ((n - 1) / 32) switch { 0 => Buttons, 1 => B2, 2 => B3, _ => B4 };
        return (word & (1u << ((n - 1) % 32))) != 0;
    }

    /// <summary>Hat <paramref name="n"/> (1–4): degrees, -1 centred.</summary>
    public readonly int Hat(int n) => n switch { 1 => Pov, 2 => Pov2, 3 => Pov3, 4 => Pov4, _ => -1 };
}

/// <summary>
/// Controllers and their inputs. The input ids are what profiles store: "Button1"–"Button32", "Axis1-" / "Axis1+" up
/// to axis 6 (X, Y, Z, R, U, V, the same numbers JoyToKey uses), and "PovUp", "PovUpRight"… for the hat.
/// </summary>
public static class Joysticks
{
    /// <summary>Buttons shown for a controller that isn't plugged in (a plugged-in one shows all it has, up to 128).</summary>
    public const int MaxButtons = 32;

    /// <summary>A window DirectInput reads controllers in the background for (set by the applet, on the UI thread).</summary>
    public static IntPtr Window { get; set; }

    private static readonly Dictionary<Guid, Applets.Gaming.ForzaWheel.GameControllers.Reader> readers = new();
    public static readonly string[] AxisLetters = { "X", "Y", "Z", "R", "U", "V" };
    public static readonly string[] PovIds = { "PovUp", "PovUpRight", "PovRight", "PovDownRight", "PovDown", "PovDownLeft", "PovLeft", "PovUpLeft" };

    private const int MaxControllers = 16;
    private const long RescanMs = 3000;
    private static readonly object gate = new();
    private static List<JoyInfo> connected = new();
    private static long nextScan;

    /// <summary>The controllers plugged in now. Looked for again every few seconds (asking about an empty slot is slow).</summary>
    public static IReadOnlyList<JoyInfo> Connected(bool force = false)
    {
        lock (gate)
        {
            if (force || Environment.TickCount64 >= nextScan)
            {
                var found = Scan();
                if (found.Count != connected.Count || found.Where((j, i) => j.Name != connected[i].Name || j.Id != connected[i].Id).Any())
                    Logging.Log.Here.Info(found.Count == 0 ? "No controllers connected" : "Controllers: " + string.Join(", ", found.Select(j => $"{j.Name} (id {j.Id}, {j.Vid:X4}:{j.Pid:X4}, {j.Buttons} buttons, {j.Axes} axes)")));
                connected = found;
                nextScan = Environment.TickCount64 + RescanMs;
            }
            return connected;
        }
    }

    /// <summary>Look again soon (a controller stopped answering).</summary>
    public static void Invalidate() { lock (gate) nextScan = 0; }

    /// <summary>The ids an XInput pad is saved with (XInput doesn't say which model it is).</summary>
    public const int XInputVid = 0x045E, XInputPid = 0xFFFF;

    private static List<JoyInfo> Scan()
    {
        var list = new List<JoyInfo>();
        for (int slot = 0; slot < 4; slot++)
        {
            if (!XInput.TryGet(slot, out _)) continue;
            list.Add(new JoyInfo(100 + slot, F("Xbox controller {0}", slot + 1), XInputVid, XInputPid, 13, 6, true, list.Count + 1,
                new uint[6], new uint[6]) { HasAxis = new[] { true, true, true, true, true, true }, XInputSlot = slot });
        }
        int xinputs = list.Count;

        var info = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNALL };
        for (int id = 0; id < MaxControllers; id++)
        {
            if (joyGetPosEx(id, ref info) != 0) continue;
            if (joyGetDevCapsW(id, out var caps, Marshal.SizeOf<JOYCAPSW>()) != 0) continue;
            int vid = caps.wMid, pid = caps.wPid;
            string name = OemName(vid, pid) ?? caps.szPname?.Trim() ?? "";
            if (name.Length == 0) name = F("Controller {0}", id + 1);
            // an XInput pad shows up here as well (both triggers on one axis): it's listed as the XInput one already
            if (xinputs > 0 && LooksLikeXInput(name)) { xinputs--; continue; }
            int occurrence = list.Count(j => j.Vid == vid && j.Pid == pid) + 1;
            var has = new[] { true, true, (caps.wCaps & JOYCAPS_HASZ) != 0, (caps.wCaps & JOYCAPS_HASR) != 0, (caps.wCaps & JOYCAPS_HASU) != 0, (caps.wCaps & JOYCAPS_HASV) != 0 };
            list.Add(new JoyInfo(id, name, vid, pid, (int)Math.Min(caps.wNumButtons, MaxButtons), (int)caps.wNumAxes,
                (caps.wCaps & JOYCAPS_HASPOV) != 0, occurrence,
                new[] { caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin },
                new[] { caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax })
            { HasAxis = has });
        }
        return WithDirectInput(list);
    }

    /// <summary>
    /// Each joystick-API controller matched with its DirectInput device (same USB ids, in order): where DirectInput sees
    /// more buttons, axes or hats, those are read through it. The ids stay the joystick API's (so profiles and JoyToKey
    /// imports are unchanged): buttons are numbered the same in both; axes, when DirectInput's are used (the joystick API
    /// found fewer than six), go X, Y, Z, R (Rz), U (Rx), V (Ry).
    /// </summary>
    private static List<JoyInfo> WithDirectInput(List<JoyInfo> list)
    {
        List<Applets.Gaming.ForzaWheel.ControllerInfo> di;
        try { di = Applets.Gaming.ForzaWheel.GameControllers.List(); }
        catch { return list; }
        for (int i = 0; i < list.Count; i++)
        {
            var j = list[i];
            if (j.IsXInput) continue;
            var d = di.Where(x => x.Vid == j.Vid && x.Pid == j.Pid).ElementAtOrDefault(j.Occurrence - 1);
            if (d == null) continue;
            int[] order = { 0, 1, 2, 5, 3, 4 }; // X, Y, Z, Rz, Rx, Ry as X, Y, Z, R, U, V
            int diAxes = order.Count(a => d.HasAxis[a]);
            bool axes = diAxes > j.Axes && j.Axes < 6, buttons = d.Buttons > j.Buttons, pov = !j.HasPov && d.Povs > 0;
            int extraHats = Math.Max(0, d.Povs - 1);
            if (!axes && !buttons && !pov && extraHats == 0) continue;
            list[i] = j with
            {
                Buttons = buttons ? d.Buttons : j.Buttons,
                Axes = axes ? diAxes : j.Axes,
                HasPov = j.HasPov || d.Povs > 0,
                HasAxis = axes ? order.Select(a => d.HasAxis[a]).ToArray() : j.HasAxis,
                DirectInput = d.Instance, DiButtons = buttons, DiAxes = axes, DiPov = pov, ExtraHats = extraHats,
            };
        }
        return list;
    }

    // Windows names an XInput pad's joystick-API side "Controller (Xbox One For Windows)", "Controller (XBOX 360 For
    // Windows)", "Controller (<maker>)"…
    private static bool LooksLikeXInput(string name) =>
        name.Contains("xbox", StringComparison.OrdinalIgnoreCase) || name.Contains("xinput", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Controller (", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name Windows shows in "Set up USB game controllers".</summary>
    private static string? OemName(int vid, int pid)
    {
        string key = $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{vid:X4}&PID_{pid:X4}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = root.OpenSubKey(key);
                if (k?.GetValue("OEMName") is string s && s.Trim().Length > 0) return s.Trim();
            }
            catch { }
        }
        return null;
    }

    /// <summary>Reads a controller; false if it's gone.</summary>
    public static bool Read(JoyInfo j, out JoyState state)
    {
        state = default;
        if (j.IsXInput) return XInput.Read(j.XInputSlot, out state);
        var info = new JOYINFOEX { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNALL | JOY_RETURNPOVCTS };
        if (joyGetPosEx(j.Id, ref info) != 0) return false;
        state.X = Scale(info.dwXpos, j, 0);
        state.Y = Scale(info.dwYpos, j, 1);
        state.Z = j.HasAxis[2] ? Scale(info.dwZpos, j, 2) : 0;
        state.R = j.HasAxis[3] ? Scale(info.dwRpos, j, 3) : 0;
        state.U = j.HasAxis[4] ? Scale(info.dwUpos, j, 4) : 0;
        state.V = j.HasAxis[5] ? Scale(info.dwVpos, j, 5) : 0;
        state.Buttons = info.dwButtons;
        state.Pov = j.HasPov && !j.DiPov && info.dwPOV != 0xFFFF && info.dwPOV < 36000 ? info.dwPOV / 100 : -1;
        state.Pov2 = state.Pov3 = state.Pov4 = -1;
        if (j.DirectInput is { } instance) ReadDirectInput(j, instance, ref state);
        return true;
    }

    /// <summary>What DirectInput sees that the joystick API doesn't: the buttons, axes or hats it reads better.</summary>
    private static void ReadDirectInput(JoyInfo j, Guid instance, ref JoyState state)
    {
        Applets.Gaming.ForzaWheel.GameControllers.Reader? reader;
        lock (readers)
        {
            if (!readers.TryGetValue(instance, out reader))
            {
                if (Window == IntPtr.Zero) return;
                reader = Applets.Gaming.ForzaWheel.GameControllers.Open(instance, Window);
                if (reader == null) return;
                readers[instance] = reader;
            }
        }
        var cs = new Applets.Gaming.ForzaWheel.ControllerState();
        bool ok;
        lock (reader) ok = reader.Read(cs);
        if (!ok)
        {
            lock (readers) { readers.Remove(instance); }
            reader.Dispose();
            return;
        }
        if (j.DiButtons)
        {
            uint[] words = new uint[4];
            for (int b = 0; b < cs.Buttons.Length && b < 128; b++)
                if (cs.Buttons[b]) words[b / 32] |= 1u << (b % 32);
            state.Buttons = words[0]; state.B2 = words[1]; state.B3 = words[2]; state.B4 = words[3];
        }
        if (j.DiAxes)
        {
            static float A(double v) => double.IsNaN(v) ? 0 : (float)Math.Clamp(v * 2 - 1, -1, 1);
            state.X = A(cs.Axes[0]); state.Y = A(cs.Axes[1]); state.Z = A(cs.Axes[2]);
            state.R = A(cs.Axes[5]); state.U = A(cs.Axes[3]); state.V = A(cs.Axes[4]);
        }
        static int Deg(int p) => p < 0 ? -1 : p / 100;
        if (j.DiPov) state.Pov = Deg(cs.Povs[0]);
        state.Pov2 = Deg(cs.Povs[1]); state.Pov3 = Deg(cs.Povs[2]); state.Pov4 = Deg(cs.Povs[3]);
    }

    /// <summary>Lets go of the DirectInput devices (on exit).</summary>
    public static void Close()
    {
        lock (readers)
        {
            foreach (var r in readers.Values) r.Dispose();
            readers.Clear();
        }
    }

    private static float Scale(int value, JoyInfo j, int axis)
    {
        double min = j.Min[axis], max = j.Max[axis];
        if (max <= min) return 0;
        return (float)Math.Clamp((value - min) / (max - min) * 2 - 1, -1, 1);
    }

    /// <summary>Every input id a controller has (or, not connected, could have), in display order.</summary>
    public static IEnumerable<string> InputsOf(JoyInfo? j, bool pov8Way)
    {
        for (int a = 0; a < 6; a++)
            if (j == null || j.HasAxis[a])
            {
                // an XInput pad's triggers (axes 5 and 6) only go one way
                if (j is not { IsXInput: true } || a < 4) yield return $"Axis{a + 1}-";
                yield return $"Axis{a + 1}+";
            }
        if (j == null || j.HasPov)
            for (int d = 0; d < 8; d++)
                if (pov8Way || d % 2 == 0) yield return PovIds[d];
        // further hats (DirectInput's 2nd to 4th): "Hat2PovUp"…
        for (int h = 2; j != null && h <= 1 + j.ExtraHats; h++)
            for (int d = 0; d < 8; d++)
                if (pov8Way || d % 2 == 0) yield return $"Hat{h}{PovIds[d]}";
        int buttons = j == null ? MaxButtons : Math.Max(j.Buttons, 1);
        for (int b = 1; b <= buttons; b++) yield return $"Button{b}";
    }

    /// <summary>How far an input is pressed: 0 (not at all) to 1. An axis only counts past the threshold.</summary>
    public static float Amount(in JoyState s, string input, float threshold, bool pov8Way)
    {
        if (input.StartsWith("Button", StringComparison.Ordinal))
            return int.TryParse(input.AsSpan(6), out int b) && s.Button(b) ? 1 : 0;
        int hat = 1;
        if (input.StartsWith("Hat", StringComparison.Ordinal) && input.Length > 4 && input[3] is >= '2' and <= '4')
        {
            hat = input[3] - '0';
            input = input[4..];
        }
        if (input.StartsWith("Axis", StringComparison.Ordinal) && input.Length >= 6)
        {
            int a = input[4] - '1';
            if (a is < 0 or > 5) return 0;
            float v = s.Axis(a) * (input[5] == '-' ? -1 : 1);
            if (v <= threshold) return 0;
            return threshold >= 1 ? 1 : (v - threshold) / (1 - threshold);
        }
        int d = Array.IndexOf(PovIds, input);
        int angle = s.Hat(hat);
        if (d < 0 || angle < 0) return 0;
        int sector = (int)Math.Round(angle / 45.0) % 8;
        if (pov8Way) return sector == d ? 1 : 0;
        if (d % 2 != 0) return 0;
        // four-way: a diagonal presses both its neighbours
        return sector == d || (sector % 2 == 1 && (sector + 1) % 8 == d) || (sector % 2 == 1 && (sector + 7) % 8 == d) ? 1 : 0;
    }

    /// <summary>The input's name for people, with what it is on an Xbox-style pad: "Button 1 (A)", "Axis 2 (left stick
    /// up)", "Button 11 (left trigger)".</summary>
    public static string Describe(string input, bool xinput)
    {
        if (!xinput) return Describe(input);
        string? what = input switch
        {
            "Axis1-" => T("left stick left"), "Axis1+" => T("left stick right"),
            "Axis2-" => T("left stick up"), "Axis2+" => T("left stick down"),
            "Axis3-" => T("right stick left"), "Axis3+" => T("right stick right"),
            "Axis4-" => T("right stick up"), "Axis4+" => T("right stick down"),
            "Axis5+" => T("left trigger"), "Axis6+" => T("right trigger"),
            "Button1" => "A", "Button2" => "B", "Button3" => "X", "Button4" => "Y",
            "Button5" => T("LB"), "Button6" => T("RB"), "Button7" => T("View / Back"), "Button8" => T("Menu / Start"),
            "Button9" => T("left stick click"), "Button10" => T("right stick click"),
            "Button11" => T("left trigger"), "Button12" => T("right trigger"), "Button13" => T("Xbox button"),
            _ => null,
        };
        if (what == null) return Describe(input);
        return input.StartsWith("Axis", StringComparison.Ordinal) ? F("Axis {0} ({1})", input[4], what) : F("{0} ({1})", Describe(input), what);
    }

    /// <summary>The input's name for people: "Button 5", "Axis 1 (X) −", "POV ↑".</summary>
    public static string Describe(string input)
    {
        if (input.StartsWith("Button", StringComparison.Ordinal)) return F("Button {0}", input[6..]);
        if (input.StartsWith("Hat", StringComparison.Ordinal) && input.Length > 4 && input[3] is >= '2' and <= '4')
            return F("Hat {0}: {1}", input[3], Describe(input[4..]));
        if (input.StartsWith("Axis", StringComparison.Ordinal) && input.Length >= 6 && input[4] is >= '1' and <= '6')
            return F("Axis {0} ({1}) {2}", input[4], AxisLetters[input[4] - '1'], input[5] == '-' ? "−" : "+");
        return Array.IndexOf(PovIds, input) switch
        {
            0 => T("POV up"),
            1 => T("POV up-right"),
            2 => T("POV right"),
            3 => T("POV down-right"),
            4 => T("POV down"),
            5 => T("POV down-left"),
            6 => T("POV left"),
            7 => T("POV up-left"),
            _ => input,
        };
    }

    /// <summary>The controller a profile's device means, if it's plugged in.</summary>
    public static JoyInfo? Resolve(J2KDevice d, IReadOnlyList<JoyInfo> list)
    {
        if (d.HasIds)
            return list.Where(j => j.Vid == d.Vid && j.Pid == d.Pid).ElementAtOrDefault(Math.Max(d.Number, 1) - 1);
        return list.ElementAtOrDefault(Math.Max(d.Number, 1) - 1);
    }

    private const int JOY_RETURNALL = 0xFF, JOY_RETURNPOVCTS = 0x200;
    private const int JOYCAPS_HASZ = 1, JOYCAPS_HASR = 2, JOYCAPS_HASU = 4, JOYCAPS_HASV = 8, JOYCAPS_HASPOV = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public uint dwButtons;
        public int dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JOYCAPSW
    {
        public ushort wMid, wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax,
            wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }

    [DllImport("winmm.dll")] private static extern int joyGetPosEx(int id, ref JOYINFOEX info);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int joyGetDevCapsW(nint id, out JOYCAPSW caps, int size);
}
