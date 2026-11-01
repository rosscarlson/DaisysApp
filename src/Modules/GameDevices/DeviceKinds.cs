using System.Text.RegularExpressions;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.GameDevices;

/// <summary>What a device is: which picture it gets and what its axes do.</summary>
public enum DeviceKind
{
    Other, Joystick, Hotas, Throttle, FlightPedals, Yoke, WheelBase, AllInOne, RacingPedals, HShifter, SeqShifter,
    Handbrake, Xbox, Gamepad, ButtonBox,
}

/// <summary>A part of the picture an axis drives; <see cref="Prefer"/>: the axes it's usually on, best first.</summary>
internal sealed record Role(string Id, string Name, params int[] Prefer);

internal static class DeviceKinds
{
    private const int X = 0, Y = 1, Z = 2, Rx = 3, Ry = 4, Rz = 5, S1 = 6, S2 = 7;

    public const string Throttle = "Throttle", Brake = "Brake", Clutch = "Clutch", Steering = "Steering", Rudder = "Rudder",
        LeftBrake = "LeftBrake", RightBrake = "RightBrake", StickX = "StickX", StickY = "StickY", Twist = "Twist",
        Throttle2 = "Throttle2", Slider = "Slider", Handbrake = "Handbrake", LeftX = "LeftX", LeftY = "LeftY",
        RightX = "RightX", RightY = "RightY", Triggers = "Triggers";

    /// <summary>The types in the order they're offered.</summary>
    public static readonly DeviceKind[] All =
    {
        DeviceKind.FlightPedals, DeviceKind.RacingPedals, DeviceKind.Throttle, DeviceKind.Joystick, DeviceKind.Hotas, DeviceKind.Yoke,
        DeviceKind.WheelBase, DeviceKind.AllInOne, DeviceKind.HShifter, DeviceKind.SeqShifter, DeviceKind.Handbrake,
        DeviceKind.Xbox, DeviceKind.Gamepad, DeviceKind.ButtonBox, DeviceKind.Other,
    };

    public static string Name(DeviceKind kind) => kind switch
    {
        DeviceKind.FlightPedals => T("Flight pedals"),
        DeviceKind.RacingPedals => T("Racing pedals"),
        DeviceKind.Throttle => T("Throttle"),
        DeviceKind.Joystick => T("Joystick"),
        DeviceKind.Hotas => T("Stick and throttle (HOTAS)"),
        DeviceKind.Yoke => T("Yoke"),
        DeviceKind.WheelBase => T("Wheel base"),
        DeviceKind.AllInOne => T("Wheel, pedals and H-shifter"),
        DeviceKind.HShifter => T("H-pattern shifter"),
        DeviceKind.SeqShifter => T("Sequential shifter"),
        DeviceKind.Handbrake => T("Handbrake"),
        DeviceKind.Xbox => T("Xbox controller"),
        DeviceKind.Gamepad => T("Gamepad"),
        DeviceKind.ButtonBox => T("Button box"),
        _ => T("Other"),
    };

    public static string AxisName(int axis) => axis switch
    {
        X => T("X Axis"), Y => T("Y Axis"), Z => T("Z Axis"), Rx => T("X Rotation"), Ry => T("Y Rotation"), Rz => T("Z Rotation"),
        S1 => T("Slider 1"), _ => T("Slider 2"),
    };

    public static string AxisShort(int axis) => axis switch { X => "X", Y => "Y", Z => "Z", Rx => "Rx", Ry => "Ry", Rz => "Rz", S1 => "S1", _ => "S2" };

    /// <summary>What an axis of this type can drive.</summary>
    public static Role[] Roles(DeviceKind kind) => kind switch
    {
        DeviceKind.FlightPedals => new[] { new Role(Rudder, T("Rudder"), Rz, X, Z), new Role(LeftBrake, T("Left brake"), X, Rx, Y), new Role(RightBrake, T("Right brake"), Y, Ry, Z) },
        DeviceKind.RacingPedals => new[] { new Role(Throttle, T("Throttle")), new Role(Brake, T("Brake")), new Role(Clutch, T("Clutch")) },
        DeviceKind.Throttle => new[] { new Role(Throttle, T("Left throttle"), Z, X), new Role(Throttle2, T("Right throttle"), Rz, Y), new Role(Slider, T("Slider"), S1, S2) },
        DeviceKind.Joystick => new[] { new Role(StickX, T("Stick left / right"), X), new Role(StickY, T("Stick up / down"), Y), new Role(Twist, T("Twist"), Rz, Rx), new Role(Throttle, T("Throttle"), S1, Z, S2) },
        DeviceKind.Hotas => new[] { new Role(StickX, T("Stick left / right"), X), new Role(StickY, T("Stick up / down"), Y), new Role(Twist, T("Twist"), Rz, Rx), new Role(Throttle, T("Left throttle"), Z, S1), new Role(Throttle2, T("Right throttle"), S2, Ry) },
        DeviceKind.Yoke => new[] { new Role(StickX, T("Roll"), X), new Role(StickY, T("Pitch"), Y), new Role(Throttle, T("Throttle"), Z, S1) },
        DeviceKind.WheelBase => new[] { new Role(Steering, T("Steering"), X) },
        DeviceKind.AllInOne => new[] { new Role(Steering, T("Steering"), X), new Role(Throttle, T("Throttle"), Y, Z), new Role(Brake, T("Brake"), Rz, Z), new Role(Clutch, T("Clutch"), S1, Z) },
        DeviceKind.Handbrake => new[] { new Role(Handbrake, T("Handbrake")) },
        DeviceKind.Xbox or DeviceKind.Gamepad => new[]
        {
            new Role(LeftX, T("Left stick left / right"), X), new Role(LeftY, T("Left stick up / down"), Y),
            new Role(RightX, T("Right stick left / right"), Rx, Z), new Role(RightY, T("Right stick up / down"), Ry, Rz),
            new Role(Triggers, T("Triggers"), Z),
        },
        _ => Array.Empty<Role>(),
    };

    /// <summary>
    /// A control devices send lowest at its top: a throttle forward, a stick pushed away (the Y of DirectInput's axes), so
    /// more of it is a lower reading. Pedals' Throttle isn't one: it goes up as it's pressed.
    /// </summary>
    public static bool Reversed(DeviceKind kind, string role) =>
        role is StickY or LeftY or RightY || kind is not (DeviceKind.RacingPedals or DeviceKind.AllInOne) && role is Throttle or Throttle2 or Slider;

    /// <summary>
    /// Which axis drives each role (-1: none): the user's where they've said, else the axis the role is usually on, else
    /// the next one free (in X, Y, Z… order).
    /// </summary>
    public static Dictionary<string, int> Assign(DeviceKind kind, bool[] hasAxis, IReadOnlyDictionary<string, int> chosen)
    {
        var roles = Roles(kind);
        var result = new Dictionary<string, int>();
        var used = new HashSet<int>();
        foreach (var r in roles)
            if (chosen.TryGetValue(r.Id, out int a) && (a < 0 || hasAxis[a]))
            {
                result[r.Id] = a;
                if (a >= 0) used.Add(a);
            }
        foreach (var r in roles)
        {
            if (result.ContainsKey(r.Id)) continue;
            int pick = r.Prefer.Where(a => hasAxis[a] && !used.Contains(a)).DefaultIfEmpty(-1).First();
            if (pick < 0) pick = Enumerable.Range(0, 8).Where(a => hasAxis[a] && !used.Contains(a)).DefaultIfEmpty(-1).First();
            result[r.Id] = pick;
            if (pick >= 0) used.Add(pick);
        }
        return result;
    }

    /// <summary>The usual first-gear button (0 first): Logitech's wheels have their shifter from button 13.</summary>
    public static int FirstGear(DeviceKind kind, string name) =>
        kind != DeviceKind.AllInOne ? 0 : Has(name, @"\bg2[57]\b") ? 8 : 12;

    /// <summary>A guess from the name, then from what Windows says it is, then from what it has.</summary>
    public static DeviceKind Detect(ControllerInfo c)
    {
        string n = c.Name;
        if (c.VJoy) return DeviceKind.Other;
        if (Has(n, @"xbox|xinput|x-box")) return DeviceKind.Xbox;
        if (Has(n, @"hand ?brake|e-?brake|pxn-hb")) return DeviceKind.Handbrake;
        if (Has(n, @"rudder|\btpr\b|crosswind|flight pedal|combat pedal|\bmfg\b")) return DeviceKind.FlightPedals;
        if (Has(n, @"yoke")) return DeviceKind.Yoke;
        if (Has(n, @"hotas|\bx5[26]\b") && !Has(n, @"throttle|stick")) return DeviceKind.Hotas;
        if (Has(n, @"throttle|twcs|quadrant")) return DeviceKind.Throttle;
        if (Has(n, @"sequential|\bsq\b")) return DeviceKind.SeqShifter;
        if (Has(n, @"shifter|\bth8[as]?\b")) return DeviceKind.HShifter;
        if (Has(n, @"pedal|simjack|\bhpp\b|t-lcm|\bt3pa\b|\blc-?\d|heusinkveld")) return DeviceKind.RacingPedals;
        if (Has(n, @"\bg2[579]\b|\bg92[03]\b|driving force|\bmomo\b")) return DeviceKind.AllInOne;
        if (Has(n, @"wheel|fanatec|podium|gt dd|simucube|moza|simagic|accuforce|\bt[0-9]{3}\b|\bts-(pc|xw)\b|\btx\b")) return DeviceKind.WheelBase;
        if (Has(n, @"button ?box|arduino|bodnar")) return DeviceKind.ButtonBox;
        if (Has(n, @"stick|gladiator|virpil|\bvkb\b|t16000|warthog")) return DeviceKind.Joystick;
        if (Has(n, @"game ?pad|dualsense|dualshock|wireless controller")) return DeviceKind.Gamepad;
        int axes = c.HasAxis.Count(a => a);
        switch (c.DeviceType)
        {
            case 0x17: return DeviceKind.WheelBase;   // DI8DEVTYPE_DRIVING
            case 0x16: return DeviceKind.Gamepad;
            case 0x15 or 0x18 when axes >= 2 && c.Buttons > 0: return DeviceKind.Joystick;
        }
        if (axes == 0) return DeviceKind.ButtonBox;
        if (axes <= 3 && c.Buttons == 0) return DeviceKind.RacingPedals;
        if (axes <= 1) return c.Buttons >= 12 ? DeviceKind.ButtonBox : DeviceKind.Handbrake;
        return DeviceKind.Joystick;
    }

    private static bool Has(string name, string pattern) => Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase);
}
