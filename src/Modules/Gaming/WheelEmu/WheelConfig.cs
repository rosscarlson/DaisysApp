using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>What a role drives on the vJoy device.</summary>
public enum RoleKind { Axis, CombinedPedals, Button, DPad }

/// <summary>
/// One thing to bind: a control of the game's controller, which the emulator drives on the vJoy device. Axis roles drive
/// one vJoy axis (Output = 0–7: X, Y, Z, Rx, Ry, Rz, slider 1, slider 2); button roles one vJoy button (Output = 1–128);
/// d-pad roles a direction of the vJoy hat (Output = 0 up, 1 right, 2 down, 3 left). Forza then has these bound to its
/// actions in its own Controls → custom layout, where pressing the real control shows the vJoy one.
/// </summary>
public sealed record Role(string Id, string Name, RoleKind Kind, int Output, string Group, bool Centred = false);

/// <summary>Every role: the driving axes, then every button the game's controller can have, up to vJoy's 128.</summary>
public static class Roles
{
    public static readonly IReadOnlyList<Role> All = Make();

    public static Role? Find(string id) => All.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static List<Role> Make()
    {
        string axes = T("Steering and pedals"), driving = T("Driving"), gears = T("H-pattern shifter"),
            pad = T("Controller buttons (menus)"), dpad = T("D-pad"), actions = T("Game actions"), extra = T("Extra buttons"), extraAxes = T("Extra axes");
        var list = new List<Role>
        {
            new("Steering", T("Steering"), RoleKind.Axis, 0, axes, Centred: true),
            new("Throttle", T("Throttle"), RoleKind.Axis, 1, axes),
            new("Brake", T("Brake"), RoleKind.Axis, 2, axes),
            new("Clutch", T("Clutch"), RoleKind.Axis, 3, axes),
            new("Handbrake", T("Handbrake"), RoleKind.Axis, 4, axes),
            new("Combined", T("Combined throttle / brake (one axis: up throttle, down brake)"), RoleKind.CombinedPedals, 1, axes),
        };
        int b = 1;
        void Button(string id, string name, string group) => list.Add(new(id, name, RoleKind.Button, b++, group));
        Button("ShiftUp", T("Shift up"), driving);
        Button("ShiftDown", T("Shift down"), driving);
        Button("HandbrakeButton", T("Handbrake (button)"), driving);
        Button("ClutchButton", T("Clutch (button)"), driving);
        for (int g = 1; g <= 10; g++) Button($"Gear{g}", F("Gear {0}", g), gears);
        Button("GearR", T("Reverse gear"), gears);
        Button("A", T("A (accept)"), pad);
        Button("B", T("B (back)"), pad);
        Button("X", T("X"), pad);
        Button("Y", T("Y"), pad);
        Button("LB", T("LB (left bumper)"), pad);
        Button("RB", T("RB (right bumper)"), pad);
        Button("Start", T("Start / Menu"), pad);
        Button("Back", T("Back / View"), pad);
        Button("LS", T("Left stick click"), pad);
        Button("RS", T("Right stick click"), pad);
        Button("PageUp", T("Page up (left trigger in menus)"), pad);
        Button("PageDown", T("Page down (right trigger in menus)"), pad);
        Button("Horn", T("Horn"), actions);
        Button("LookBack", T("Look back"), actions);
        Button("Camera", T("Change camera"), actions);
        Button("Rewind", T("Rewind"), actions);
        Button("Telemetry", T("Telemetry"), actions);
        Button("Anna", T("ANNA / navigation"), actions);
        Button("Photo", T("Photo mode"), actions);
        while (b <= VJoy.MaxButtons) Button($"Button{b}", F("Button {0}", b), extra);
        list.Add(new("DpadUp", T("D-pad up"), RoleKind.DPad, 0, dpad));
        list.Add(new("DpadRight", T("D-pad right"), RoleKind.DPad, 1, dpad));
        list.Add(new("DpadDown", T("D-pad down"), RoleKind.DPad, 2, dpad));
        list.Add(new("DpadLeft", T("D-pad left"), RoleKind.DPad, 3, dpad));
        list.Add(new("AxisRz", T("Extra axis (Rz)"), RoleKind.Axis, 5, extraAxes));
        list.Add(new("AxisSl0", T("Extra axis (slider 1)"), RoleKind.Axis, 6, extraAxes));
        list.Add(new("AxisSl1", T("Extra axis (slider 2)"), RoleKind.Axis, 7, extraAxes));
        // the d-pad and the gear / button rows first, the 128-button tail last
        string[] order = { axes, driving, gears, pad, dpad, actions, extraAxes, extra };
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
}

/// <summary>
/// The bindings: which real control drives which part of the game's controller. Kept in %APPDATA%\DaisysApp\WheelEmu.json
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

/// <summary>WheelEmu's settings, in %APPDATA%\DaisysApp\WheelEmu.json: the bindings and how it starts.</summary>
public sealed class WheelEmuSettings
{
    private const string FileName = "WheelEmu";

    public WheelConfig Config { get; set; } = new();
    /// <summary>Start the emulator when Daisy's App starts.</summary>
    public bool StartWithApp { get; set; }
    /// <summary>The devices Hush hid (HID instance IDs), so Unmute shows them again.</summary>
    public List<string> Hidden { get; set; } = new();
    public DateTime? HiddenAt { get; set; }

    public static WheelEmuSettings Load() => JsonStore.Load<WheelEmuSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
