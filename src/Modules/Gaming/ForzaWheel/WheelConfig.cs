using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>What a role drives on the vJoy device.</summary>
public enum RoleKind { Axis, CombinedPedals, Button, DPad }

/// <summary>
/// One thing to bind: a control of the game's controller, which the emulator drives on the vJoy device. Axis roles drive
/// one vJoy axis (Output = 0–7: X, Y, Z, Rx, Ry, Rz, slider 1, slider 2); button roles one vJoy button (Output = 1–128);
/// d-pad roles a direction of the vJoy hat (Output = 0 up, 1 right, 2 down, 3 left). The layout is Forza EmuWheel's,
/// which Windows is told about (<see cref="GameInputRegistry"/>) so that Forza sees the vJoy device as a racing wheel:
/// steering X; throttle, brake and clutch Z, Rx and Ry, sent reversed (they read full when released, as Windows is
/// told); handbrake Rz; Menu, View, A and B buttons 1–4; wheel buttons 5–16; shift down and up 17 and 18; reverse 19;
/// gears 1–7 20–26; the d-pad the hat. Forza sees nothing else of it.
/// </summary>
public sealed record Role(string Id, string Name, RoleKind Kind, int Output, string Group, bool Centred = false, bool Reversed = false, bool ForzaSees = true);

/// <summary>Every role: the driving axes, then every button the game's controller can have, up to vJoy's 128.</summary>
public static class Roles
{
    public static readonly IReadOnlyList<Role> All = Make();

    public static Role? Find(string id) => All.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The group that's full width at the top (the rest go in two columns, the spare buttons last).</summary>
    public static string AxesGroup => All[0].Group;

    /// <summary>The spare buttons' group (last, folded away).</summary>
    public static string Extras => All[^1].Group;

    private static List<Role> Make()
    {
        string axes = T("Steering and Pedals"), shifting = T("Shifting"), gears = T("H-pattern shifter"), menu = T("Menu buttons"),
            wheel = T("Wheel buttons (bind each to any action in Forza)"), dpad = T("D-pad"),
            extra = T("Extra buttons (Forza sees buttons 1–26)");
        var list = new List<Role>
        {
            new("Steering", T("Steering"), RoleKind.Axis, 0, axes, Centred: true),
            new("Throttle", T("Throttle"), RoleKind.Axis, 2, axes, Reversed: true),
            new("Brake", T("Brake"), RoleKind.Axis, 3, axes, Reversed: true),
            new("Clutch", T("Clutch"), RoleKind.Axis, 4, axes, Reversed: true),
            new("Handbrake", T("Handbrake"), RoleKind.Axis, 5, axes),
            new("Combined", T("Combined throttle / brake (one axis: up throttle, down brake)"), RoleKind.CombinedPedals, 2, axes),
        };
        void Button(string id, string name, int output, string group, bool forza = true) => list.Add(new(id, name, RoleKind.Button, output, group, ForzaSees: forza));
        Button("Start", T("Start / Menu"), 1, menu);
        Button("Back", T("Back / View"), 2, menu);
        Button("A", T("A (accept)"), 3, menu);
        Button("B", T("B (back)"), 4, menu);
        Button("X", T("X"), 5, wheel);
        Button("Y", T("Y"), 6, wheel);
        Button("LB", T("LB (left bumper)"), 7, wheel);
        Button("RB", T("RB (right bumper)"), 8, wheel);
        Button("LS", T("Left stick click"), 9, wheel);
        Button("RS", T("Right stick click"), 10, wheel);
        // the rest are numbered: what each does is chosen in Forza (their ids are the names they had before 0.58)
        string[] numbered = { "PageUp", "PageDown", "Horn", "LookBack", "Camera", "Rewind" };
        for (int n = 0; n < numbered.Length; n++) Button(numbered[n], F("Wheel button {0}", 11 + n), 11 + n, wheel);
        Button("ShiftDown", T("Shift down"), 17, shifting);
        Button("ShiftUp", T("Shift up"), 18, shifting);
        Button("GearR", T("Reverse gear"), 19, gears);
        for (int g = 1; g <= 7; g++) Button($"Gear{g}", F("Gear {0}", g), 19 + g, gears);
        for (int b = 27; b <= VJoy.MaxButtons; b++) Button($"Button{b}", F("Button {0}", b), b, extra, forza: false);
        list.Add(new("DpadUp", T("D-pad up"), RoleKind.DPad, 0, dpad));
        list.Add(new("DpadRight", T("D-pad right"), RoleKind.DPad, 1, dpad));
        list.Add(new("DpadDown", T("D-pad down"), RoleKind.DPad, 2, dpad));
        list.Add(new("DpadLeft", T("D-pad left"), RoleKind.DPad, 3, dpad));
        // in this order on the tab; the 102 spare buttons last
        string[] order = { axes, shifting, gears, menu, dpad, wheel, extra };
        return list.OrderBy(r => Array.IndexOf(order, r.Group)).ToList();
    }
}

/// <summary>A real control: an axis, a button or a hat direction of one controller.</summary>
public sealed class InputRef
{
    public enum InputKind { Axis, Button, Pov }

    /// <summary>The controller's DirectInput instance (stable on this PC), product, and name (to find it again if the instance changed).</summary>
    public Guid Device { get; set; }
    public Guid Product { get; set; }
    public string DeviceName { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InputKind Kind { get; set; }
    /// <summary>Axis 0–7, button 0–127, hat 0–3.</summary>
    public int Index { get; set; }
    /// <summary>For a hat: 0 up, 1 right, 2 down, 3 left. For an axis used as a button: 1 pushed up, -1 pushed down.</summary>
    public int Direction { get; set; }

    public string Describe()
    {
        string what = Kind switch
        {
            InputKind.Axis => Direction == 0 ? F("axis {0}", VJoy.AxisNames[Index]) : F("axis {0} {1}", VJoy.AxisNames[Index], Direction > 0 ? "+" : "−"),
            InputKind.Button => F("button {0}", Index + 1),
            _ => F("hat {0} {1}", Index + 1, new[] { T("up"), T("right"), T("down"), T("left") }[Math.Clamp(Direction, 0, 3)]),
        };
        return $"{DeviceName} · {what}";
    }

    public bool SameControl(InputRef o) => o.Device == Device && o.Kind == Kind && o.Index == Index && o.Direction == Direction;
}

/// <summary>A role and the real controls that drive it (any one of them pressed presses it; an axis role takes the first).</summary>
public sealed class Binding
{
    public string Role { get; set; } = "";
    public List<InputRef> Inputs { get; set; } = new();
    /// <summary>Axes: reversed (a pedal that reads full when released).</summary>
    public bool Invert { get; set; }
    /// <summary>Axes: the part ignored, 0–0.5: at the released end of a pedal, around the centre of steering.</summary>
    public double Deadzone { get; set; }
    /// <summary>Steering: how much further the game's wheel turns than the real one (1–4). Forza's wheel is 450°, so a
    /// 1080° wheel at 240% turns the car as far as the hands.</summary>
    public double Sensitivity { get; set; } = 1;
}

/// <summary>
/// The bindings: which real control drives which part of the game's controller. Kept in %APPDATA%\DaisysApp\ForzaWheel.json
/// (with the tool's settings), and saved to or loaded from a .json file of its own with Save / Load, like EmuWheel's
/// configuration.json.
/// </summary>
public sealed class WheelConfig
{
    public int Version { get; set; } = 1;
    /// <summary>Which vJoy device (1–16) the game sees.</summary>
    public uint VJoyDevice { get; set; } = 1;
    public List<Binding> Bindings { get; set; } = new();

    public Binding? For(string role) => Bindings.FirstOrDefault(b => b.Role.Equals(role, StringComparison.OrdinalIgnoreCase));

    public Binding Ensure(string role)
    {
        if (For(role) is { } b) return b;
        b = new Binding { Role = role };
        Bindings.Add(b);
        return b;
    }

    /// <summary>The highest vJoy button bound, for checking the device has that many.</summary>
    [JsonIgnore]
    public int HighestButton => Bindings.Where(b => b.Inputs.Count > 0).Select(b => Roles.Find(b.Role))
        .Where(r => r?.Kind == RoleKind.Button).Select(r => r!.Output).DefaultIfEmpty(0).Max();

    /// <summary>The controllers the bindings use.</summary>
    [JsonIgnore]
    public IEnumerable<(Guid Device, Guid Product, string Name)> Devices =>
        Bindings.SelectMany(b => b.Inputs).GroupBy(i => i.Device).Select(g => (g.Key, g.First().Product, g.First().DeviceName));

    public void SaveTo(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonStore.Options));

    public static WheelConfig LoadFrom(string path) =>
        JsonSerializer.Deserialize<WheelConfig>(File.ReadAllText(path), JsonStore.Options) ?? throw new InvalidDataException(T("That file has no bindings in it."));
}

/// <summary>ForzaWheel's settings, in %APPDATA%\DaisysApp\ForzaWheel.json: the bindings and how it starts.</summary>
public sealed class ForzaWheelSettings
{
    private const string FileName = "ForzaWheel";

    public WheelConfig Config { get; set; } = new();
    /// <summary>Start the emulator when Daisy's App starts.</summary>
    public bool StartWithApp { get; set; }

    public static ForzaWheelSettings Load()
    {
        // WheelEmu.json until 0.58 (the tool was called WheelEmu): the bindings move to ForzaWheel.json
        try
        {
            if (!JsonStore.Exists(FileName) && JsonStore.Exists("WheelEmu"))
            {
                File.Move(JsonStore.PathFor("WheelEmu"), JsonStore.PathFor(FileName));
                Logging.Log.Here.Info("ForzaWheel: settings moved from WheelEmu.json");
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("ForzaWheel settings move", ex); }
        return JsonStore.Load<ForzaWheelSettings>(FileName);
    }
    public void Save() => JsonStore.Save(FileName, this);
}
