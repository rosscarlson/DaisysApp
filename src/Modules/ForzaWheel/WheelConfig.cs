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
/// steering X; throttle, brake and clutch Z, Rx and Ry (as how far they're pressed, as EmuWheel sends them); handbrake Rz; Menu, View, A and B buttons 1–4; wheel buttons 5–16; shift down and up 17 and 18; reverse 19;
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
            // the pedals go to vJoy as how far they're pressed (0 released), as EmuWheel sends them: reversed (0.53–0.59)
            // Forza read them backwards
            new("Throttle", T("Throttle"), RoleKind.Axis, 2, axes),
            new("Brake", T("Brake"), RoleKind.Axis, 3, axes),
            new("Clutch", T("Clutch"), RoleKind.Axis, 4, axes),
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

    /// <summary>What an EmuWheel configuration.json became: the bindings, its force feedback tuning (if any), and notes.</summary>
    public sealed record EmuWheelImport(List<Binding> Bindings, EmuWheelFfb? Tuning, List<string> Notes);

    /// <summary>
    /// Forza EmuWheel's configuration.json as bindings. It lists each controller (by its DirectInput instance, as here)
    /// with the axes it drives (Steering, Throttle, Brake, Clutch, Handbrake, Combined; AxisIndex counts X, Y, Z, Rx,
    /// Ry, Rz, slider 1, slider 2, as here), its buttons (Id: the vJoy button, from 0; Index: the controller's button,
    /// from 0) and its d-pad (EmuWheel reads the controller's first hat), and the wheel's force feedback tuning.
    /// </summary>
    public static EmuWheelImport FromEmuWheel(string path)
    {
        var bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        EmuWheelFfb? tuning = null;
        Binding For(string role) => bindings.TryGetValue(role, out var b) ? b : bindings[role] = new Binding { Role = role };
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException(T("That isn't an EmuWheel configuration.json (it should be a list of controllers)."));
        static JsonElement? Get(JsonElement e, string name) =>
            e.EnumerateObject().Where(p => p.NameEquals(name) || p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault() is { ValueKind: not JsonValueKind.Null } v ? v : null;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            if (Get(c, "InstanceGuid")?.GetString() is not { } instance || !Guid.TryParse(instance, out var device)) continue;
            Guid.TryParse(Get(c, "ProductGuid")?.GetString(), out var product);
            string name = Get(c, "InstanceName")?.GetString() ?? Get(c, "ProductName")?.GetString() ?? "";
            InputRef Ref(InputRef.InputKind kind, int index, int direction = 0) => new()
            {
                Device = device, Product = product, DeviceName = name, Kind = kind, Index = index, Direction = direction,
            };
            if (Get(c, "Axes") is { ValueKind: JsonValueKind.Array } axes)
                foreach (var a in axes.EnumerateArray())
                {
                    string id = Get(a, "Id")?.GetString() ?? "";
                    if (Roles.Find(id) is not { Kind: RoleKind.Axis or RoleKind.CombinedPedals } role) { notes.Add(F("{0}: axis {1} isn't a control here, skipped.", name, id)); continue; }
                    int index = Get(a, "AxisIndex")?.GetInt32() ?? -1;
                    if (index is < 0 or > 7) { notes.Add(F("{0}: {1} is on an axis that can't be read, skipped.", name, role.Name)); continue; }
                    var b = For(role.Id);
                    b.Inputs = new List<InputRef> { Ref(InputRef.InputKind.Axis, index) };
                    b.Invert = (Get(a, "Inverted")?.GetInt32() ?? 0) == 1;
                    b.Deadzone = Math.Clamp(Get(a, "Deadzone")?.GetDouble() ?? 0, 0, 0.3);
                }
            if (Get(c, "Buttons") is { ValueKind: JsonValueKind.Array } buttons)
                foreach (var btn in buttons.EnumerateArray())
                {
                    int vjoy = Get(btn, "Id")?.GetInt32() ?? -1, index = Get(btn, "Index")?.GetInt32() ?? -1;
                    if (vjoy < 0 || index < 0) continue;
                    if (Roles.All.FirstOrDefault(r => r.Kind == RoleKind.Button && r.Output == vjoy + 1) is not { } role) { notes.Add(F("{0}: vJoy button {1} isn't a control here, skipped.", name, vjoy + 1)); continue; }
                    var b = For(role.Id);
                    var input = Ref(InputRef.InputKind.Button, index);
                    if (!b.Inputs.Any(input.SameControl)) b.Inputs.Add(input);
                }
            if (Get(c, "DPad") is { ValueKind: JsonValueKind.Object })
                foreach (var (role, dir) in new[] { ("DpadUp", 0), ("DpadRight", 1), ("DpadDown", 2), ("DpadLeft", 3) })
                    For(role).Inputs = new List<InputRef> { Ref(InputRef.InputKind.Pov, 0, dir) };
            if (Get(c, "FFBParameters") is { ValueKind: JsonValueKind.Object } ffb)
                tuning = ffb.Deserialize<EmuWheelFfb>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        // shift paddles first, the rest in the tab's order
        var ordered = Roles.All.Where(r => bindings.ContainsKey(r.Id)).Select(r => bindings[r.Id]).ToList();
        return new EmuWheelImport(ordered, tuning, notes);
    }

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

    /// <summary>The emulator was running (started by hand) when the app closed: it starts again at launch.</summary>
    public bool WasRunning { get; set; }

    /// <summary>Programs (e.g. "forza_gaming.desktop.x64_release_final.exe") that start the emulator while they run and
    /// stop it when they close.</summary>
    public List<string> Games { get; set; } = new();

    /// <summary>A green ForzaWheel bubble in the window's title bar while the emulator runs.</summary>
    public bool TitleBadge { get; set; } = true;

    /// <summary>The shortcut that starts and stops the emulator: a key combination or a controller button (null: none).</summary>
    public string? ToggleHotkey { get; set; }

    /// <summary>Force feedback from the game to the wheel (the one Steering is bound to).</summary>
    public FfbSettings Ffb { get; set; } = new();

    /// <summary>The input overlay: the pedals' and wheel's history over the game.</summary>
    public OverlaySettings Overlay { get; set; } = new();

    /// <summary>Hush the bound devices when the emulator starts, and unhush them when it stops.</summary>
    public bool AutoHush { get; set; } = true;

    /// <summary>The tab's blocks folded up (by id), kept for next time.</summary>
    public List<string> Folded { get; set; } = new();

    /// <summary>1 once the pedals' Invert has been put right for 0.60 (they were sent to vJoy reversed before).</summary>
    public int PedalFix { get; set; }

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
        var s = JsonStore.Load<ForzaWheelSettings>(FileName);
        // until 0.60 the pedals went to vJoy reversed, so Forza read them backwards unless Invert was ticked to make up
        // for it (which made the tab's bars backwards): now they go as they are, so each pedal's Invert flips once,
        // which keeps what Forza gets and puts the bars right
        if (s.PedalFix < 1)
        {
            foreach (var b in s.Config.Bindings.Where(b => b.Inputs.Count > 0 && b.Role is "Throttle" or "Brake" or "Clutch"))
                b.Invert = !b.Invert;
            s.PedalFix = 1;
            if (JsonStore.Exists(FileName)) s.Save();
        }
        return s;
    }
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>Force feedback: on or off, overall strength and direction, and EmuWheel's tuning.</summary>
public sealed class FfbSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Overall strength of the steering force and vibrations (1 = as EmuWheel), 0–2.</summary>
    public double Strength { get; set; } = 1;
    /// <summary>Pushes the other way (EmuWheel leaves this to Forza's Invert Force Feedback option).</summary>
    public bool Reverse { get; set; }
    /// <summary>EmuWheel's tuning, as in its configuration.json's FFBParameters (its Configurator's defaults until imported).</summary>
    public EmuWheelFfb Tuning { get; set; } = new();
    /// <summary>Where the tuning was imported from, if it was.</summary>
    public string? TuningFrom { get; set; }
}

/// <summary>
/// Forza EmuWheel's force-feedback tuning, with its names and its Configurator's defaults, so a configuration.json's
/// FFBParameters reads straight in. Constant: the steering force; Sine: vibrations (engine and gear shifts are told
/// apart by the gain Forza gives them); Spring and Damper are off (0) unless tuned.
/// </summary>
public sealed class EmuWheelFfb
{
    public sealed class ConstantParams
    {
        public float Magnitude { get; set; } = 1;
        public float MaximumForce { get; set; } = 1;
        public float MinimumForce { get; set; }
        public float FilterThreshold { get; set; } = 1;
        public float MinimumCoefficient { get; set; } = 1;
    }

    public sealed class VibrationParams
    {
        public float Frequency { get; set; } = 1;
        public float Strength { get; set; }
    }

    public sealed class PeriodicParams
    {
        public float Magnitude { get; set; } = 1;
        public float Frequency { get; set; } = 1;
        public float MaximumForce { get; set; } = 1;
        public float MinimumForce { get; set; }
        public float Phase { get; set; } = 0.375f;
        public VibrationParams EngineVibrations { get; set; } = new() { Strength = 0.03f };
        public VibrationParams GearShiftVibrations { get; set; } = new() { Strength = 0.1f };
    }

    public sealed class ConditionParams
    {
        public float Coefficient { get; set; }
        public float Saturation { get; set; }
    }

    public ConstantParams Const { get; set; } = new();
    public PeriodicParams Sine { get; set; } = new();
    public ConditionParams Spring { get; set; } = new();
    public ConditionParams Damper { get; set; } = new();

    /// <summary>The FFBParameters of the controller that has them in an EmuWheel configuration.json.</summary>
    public static EmuWheelFfb FromConfiguration(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var controller in doc.RootElement.EnumerateArray())
            foreach (var p in controller.EnumerateObject())
                if (p.NameEquals("FFBParameters") && p.Value.ValueKind == JsonValueKind.Object)
                    return p.Value.Deserialize<EmuWheelFfb>(options) ?? new EmuWheelFfb();
        throw new InvalidDataException(T("That configuration.json has no force feedback settings in it."));
    }
}
