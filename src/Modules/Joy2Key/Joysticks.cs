using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DaisysApp.Applets.Joy2Key;

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
}

/// <summary>One reading of a controller: axes from -1 to 1, buttons 1–32 as bits, POV in degrees (-1 = centred).</summary>
public struct JoyState
{
    public float X, Y, Z, R, U, V;
    public uint Buttons;
    public int Pov;

    public readonly float Axis(int i) => i switch { 0 => X, 1 => Y, 2 => Z, 3 => R, 4 => U, _ => V };
}

/// <summary>
/// Controllers and their inputs. The input ids are what profiles store: "Button1"–"Button32", "Axis1-" / "Axis1+" up
/// to axis 6 (X, Y, Z, R, U, V, the same numbers JoyToKey uses), and "PovUp", "PovUpRight"… for the hat.
/// </summary>
public static class Joysticks
{
    public const int MaxButtons = 32;
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
        state.Pov = j.HasPov && info.dwPOV != 0xFFFF && info.dwPOV < 36000 ? info.dwPOV / 100 : -1;
        return true;
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
        int buttons = j == null ? MaxButtons : Math.Max(j.Buttons, 1);
        for (int b = 1; b <= buttons; b++) yield return $"Button{b}";
    }

    /// <summary>How far an input is pressed: 0 (not at all) to 1. An axis only counts past the threshold.</summary>
    public static float Amount(in JoyState s, string input, float threshold, bool pov8Way)
    {
        if (input.StartsWith("Button", StringComparison.Ordinal))
            return int.TryParse(input.AsSpan(6), out int b) && b is >= 1 and <= 32 && (s.Buttons & (1u << (b - 1))) != 0 ? 1 : 0;
        if (input.StartsWith("Axis", StringComparison.Ordinal) && input.Length >= 6)
        {
            int a = input[4] - '1';
            if (a is < 0 or > 5) return 0;
            float v = s.Axis(a) * (input[5] == '-' ? -1 : 1);
            if (v <= threshold) return 0;
            return threshold >= 1 ? 1 : (v - threshold) / (1 - threshold);
        }
        int d = Array.IndexOf(PovIds, input);
        if (d < 0 || s.Pov < 0) return 0;
        int sector = (int)Math.Round(s.Pov / 45.0) % 8;
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
