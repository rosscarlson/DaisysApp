namespace DaisysApp.Applets.SimEffects.Binder.Formats.Fso;

/// <summary>Controller ids, in the same order as FSO's CID enum.</summary>
public enum Cid { None, Keyboard, Mouse, Joy0, Joy1, Joy2, Joy3 }

/// <summary>Binding flags; values match FSO's CCF_* defines.</summary>
[Flags]
public enum Ccf
{
    None = 0x00,
    Button = 0x01,
    Ball = 0x02,
    Hat = 0x04,
    Axis = 0x08,
    Inverted = 0x10,
    Relative = 0x20,
    AxisBtn = 0x40,
}

/// <summary>
/// One input bound to an action, stored the way FSO's preset files store it: a controller id, an input
/// string (key enum name, mouse button/axis name, joystick button number or axis name) and flags.
/// </summary>
public sealed record FsoBind(Cid Cid, string Input, Ccf Flags)
{
    public static readonly FsoBind None = new(Cid.None, "NONE", Ccf.None);

    public static FsoBind Key(string name) => new(Cid.Keyboard, name, Ccf.None);
    public static FsoBind JoyButton(Cid cid, int button) => new(cid, button.ToString(), Ccf.None);
    public static FsoBind Axis(Cid cid, string axis) => new(cid, axis, Ccf.Axis);
    public static FsoBind MouseButton(string name) => new(Cid.Mouse, name, Ccf.None);

    public bool IsNone => Cid == Cid.None;
    public bool IsAxis => (Flags & (Ccf.Axis | Ccf.Ball)) != 0;
    public bool IsJoystick => Cid is Cid.Joy0 or Cid.Joy1 or Cid.Joy2 or Cid.Joy3;
    public int JoySlot => IsJoystick ? Cid - Cid.Joy0 : -1;

    /// <summary>Applies the same clean-up as CC_bind::validate().</summary>
    public FsoBind Normalized()
    {
        if (Cid == Cid.None || string.IsNullOrEmpty(Input) || Input == "NONE") return None;
        if (Cid == Cid.Keyboard) return Flags == Ccf.None ? this : this with { Flags = Ccf.None };
        if (Cid == Cid.Mouse) return this with { Flags = Flags & ~(Ccf.Ball | Ccf.Hat | Ccf.AxisBtn) };
        return this;
    }

    /// <summary>True when both binds react to the same physical input (inversion doesn't matter).</summary>
    public bool SameInput(FsoBind other) =>
        !IsNone && Cid == other.Cid && Input == other.Input && IsAxis == other.IsAxis;

    public FsoBind WithInverted(bool inverted) =>
        IsNone ? this : this with { Flags = inverted ? Flags | Ccf.Inverted : Flags & ~Ccf.Inverted };

    // ---- FSO string conversions (presets.cpp / controlsconfigcommon.cpp) ----

    public string CidName => Cid switch
    {
        Cid.Keyboard => "CID_KEYBOARD",
        Cid.Mouse => "CID_MOUSE",
        Cid.Joy0 => "CID_JOY0",
        Cid.Joy1 => "CID_JOY1",
        Cid.Joy2 => "CID_JOY2",
        Cid.Joy3 => "CID_JOY3",
        _ => "CID_NONE",
    };

    public static Cid ParseCid(string? s) => s?.Trim() switch
    {
        "CID_KEYBOARD" => Cid.Keyboard,
        "CID_MOUSE" => Cid.Mouse,
        "CID_JOY0" => Cid.Joy0,
        "CID_JOY1" => Cid.Joy1,
        "CID_JOY2" => Cid.Joy2,
        "CID_JOY3" => Cid.Joy3,
        _ => Cid.None,
    };

    /// <summary>Same order and separators as ValToCCF().</summary>
    public string FlagsText
    {
        get
        {
            var parts = new List<string>();
            if (Flags.HasFlag(Ccf.AxisBtn)) parts.Add("AXIS_BTN");
            if (Flags.HasFlag(Ccf.Relative)) parts.Add("RELATIVE");
            if (Flags.HasFlag(Ccf.Inverted)) parts.Add("INVERTED");
            if (Flags.HasFlag(Ccf.Axis)) parts.Add("AXIS");
            if (Flags.HasFlag(Ccf.Hat)) parts.Add("HAT");
            if (Flags.HasFlag(Ccf.Ball)) parts.Add("BALL");
            return parts.Count == 0 ? "NONE" : string.Join(", ", parts);
        }
    }

    /// <summary>Same substring matching as CCFToVal(), including "AXIS_BTN" also setting AXIS.</summary>
    public static Ccf ParseFlags(string? s)
    {
        if (string.IsNullOrEmpty(s)) return Ccf.None;
        var f = Ccf.None;
        if (s.Contains("AXIS_BTN")) f |= Ccf.AxisBtn;
        if (s.Contains("RELATIVE")) f |= Ccf.Relative;
        if (s.Contains("INVERTED")) f |= Ccf.Inverted;
        if (s.Contains("AXIS")) f |= Ccf.Axis;
        if (s.Contains("HAT")) f |= Ccf.Hat;
        if (s.Contains("BALL")) f |= Ccf.Ball;
        return f;
    }

    /// <summary>
    /// True if FSO can load this input without raising a fatal error. Unknown mouse or joystick inputs
    /// abort the game on load, so anything read from disk is checked before it is written back.
    /// </summary>
    public bool IsLoadable => Normalized() is var n && (n.IsNone || n.Cid switch
    {
        Cid.Keyboard => KeyNames.IsValidKeyInput(n.Input),
        Cid.Mouse => n.IsAxis ? MouseAxes.Contains(n.Input) : MouseButtons.Contains(n.Input),
        _ => n.IsAxis ? JoyAxes.Contains(n.Input) : int.TryParse(n.Input, out var b) && b >= 0 && b < 132,
    });

    public static readonly string[] JoyAxes = ["X_AXIS", "Y_AXIS", "Z_AXIS", "RX_AXIS", "RY_AXIS", "RZ_AXIS", "U_AXIS", "V_AXIS"];
    public static readonly string[] MouseAxes = ["X_AXIS", "Y_AXIS", "Z_AXIS"];
    public static readonly string[] MouseButtons =
        ["LEFT_BUTTON", "RIGHT_BUTTON", "MIDDLE_BUTTON", "X1_BUTTON", "X2_BUTTON", "WHEEL_UP", "WHEEL_DOWN", "WHEEL_LEFT", "WHEEL_RIGHT"];

    // ---- Display text, matching CC_bind::textify() where it makes sense ----

    public string Display
    {
        get
        {
            var n = Normalized();
            if (n.IsNone) return "";
            string prefix = n.Cid switch
            {
                Cid.Mouse => "Mouse ",
                Cid.Joy0 => "Joy-0 ",
                Cid.Joy1 => "Joy-1 ",
                Cid.Joy2 => "Joy-2 ",
                Cid.Joy3 => "Joy-3 ",
                _ => "",
            };
            string body;
            if (n.IsAxis)
            {
                body = n.Input switch
                {
                    "X_AXIS" => "X Axis", "Y_AXIS" => "Y Axis", "Z_AXIS" => n.Cid == Cid.Mouse ? "Wheel Axis" : "Z Axis",
                    "RX_AXIS" => "rX Axis", "RY_AXIS" => "rY Axis", "RZ_AXIS" => "rZ Axis",
                    "U_AXIS" => "U Axis", "V_AXIS" => "V Axis", _ => n.Input,
                };
                if (n.Flags.HasFlag(Ccf.Inverted)) body += " (inverted)";
            }
            else if (n.Cid == Cid.Keyboard) body = KeyNames.Display(n.Input);
            else if (n.Cid == Cid.Mouse)
            {
                body = n.Input switch
                {
                    "LEFT_BUTTON" => "Left Button", "RIGHT_BUTTON" => "Right Button", "MIDDLE_BUTTON" => "Mid Button",
                    "X1_BUTTON" => "X1 Button", "X2_BUTTON" => "X2 Button", "WHEEL_UP" => "Wheel Up",
                    "WHEEL_DOWN" => "Wheel Down", "WHEEL_LEFT" => "Wheel Left", "WHEEL_RIGHT" => "Wheel Right", _ => n.Input,
                };
            }
            else
            {
                body = int.TryParse(n.Input, out var b) && b >= 128
                    ? b switch { 128 => "Hat Up", 129 => "Hat Right", 130 => "Hat Down", _ => "Hat Left" }
                    : "Button " + n.Input;
            }
            return prefix + body;
        }
    }
}
