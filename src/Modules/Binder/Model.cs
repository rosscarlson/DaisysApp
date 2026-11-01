namespace DaisysApp.Applets.Binder;

/// <summary>What a binding is pressed or moved with.</summary>
public enum BindKind { None, Key, Mouse, MouseAxis, Button, Hat, Axis, Other }

/// <summary>Shift, Ctrl and Alt held with a key.</summary>
[Flags]
public enum Mods { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

/// <summary>
/// A controller as the bindings name it: its USB vendor and product, and which of several alike it is (0 for the first).
/// </summary>
public readonly record struct DeviceId(int Vid, int Pid, int Nth = 0)
{
    public override string ToString() => Nth == 0 ? $"{Vid:X4}:{Pid:X4}" : $"{Vid:X4}:{Pid:X4}#{Nth + 1}";
}

/// <summary>
/// One input bound to an action, the same for every game (each format turns its own into this and back):
/// <list type="bullet">
/// <item>Key: <see cref="Code"/> is the key's scancode as DirectInput numbers them (0x80 added for the extended ones), <see cref="Mods"/> held with it</item>
/// <item>Mouse: 0 left, 1 right, 2 middle, 3 and 4 the side buttons, 5–8 the wheel up, down, left, right</item>
/// <item>MouseAxis: 0 X, 1 Y, 2 the wheel</item>
/// <item>Button: a controller's button, from 0</item>
/// <item>Hat: <see cref="Hat"/> (from 0) pushed one way (<see cref="Code"/>: 0 up, 1 right, 2 down, 3 left)</item>
/// <item>Axis: an axis as the title's input numbers them (DirectInput: 0 X, 1 Y, 2 Z, 3–5 the rotations, 6 and 7 the sliders;
/// SDL, for FSO: the device's axes in its own order); <see cref="Half"/> +1 or -1 is half of it used as a button</item>
/// <item>Other: something only its own game knows (a gamepad's button in Elite), kept as it was (<see cref="Raw"/>)</item>
/// </list>
/// </summary>
public sealed record Bind(BindKind Kind, int Code = 0, DeviceId? Device = null, int Hat = 0, Mods Mods = Mods.None,
    bool Inverted = false, int Half = 0, string? Raw = null)
{
    public static readonly Bind None = new(BindKind.None);

    public bool IsNone => Kind == BindKind.None;
    public bool IsAxis => Kind is BindKind.Axis or BindKind.MouseAxis && Half == 0;
    public bool IsController => Kind is BindKind.Button or BindKind.Hat or BindKind.Axis;

    public static Bind Key(int code, Mods mods = Mods.None) => new(BindKind.Key, code, Mods: mods);
    public static Bind Button(DeviceId d, int button) => new(BindKind.Button, button, d);
    public static Bind HatDir(DeviceId d, int hat, int dir) => new(BindKind.Hat, dir, d, hat);
    public static Bind Axis(DeviceId d, int axis, bool inverted = false) => new(BindKind.Axis, axis, d, Inverted: inverted);

    /// <summary>True when both react to the same thing pressed or moved (inverting doesn't matter).</summary>
    public bool SameInput(Bind o) =>
        !IsNone && Kind == o.Kind && Code == o.Code && Device == o.Device && Hat == o.Hat && Mods == o.Mods && Half == o.Half && Raw == o.Raw;

    public Bind WithInverted(bool on) => IsAxis ? this with { Inverted = on } : this;
}

/// <summary>Something a game does that can be bound: its id in the game's file, its name, where it's listed, its defaults.</summary>
public sealed class BinderAction
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Group { get; init; } = "";
    /// <summary>Takes an axis (steering, pitch, throttle…); else buttons and keys.</summary>
    public bool IsAxis { get; init; }
    /// <summary>An axis that rests in the middle (steering, pitch); false: from one end (throttle, a pedal).</summary>
    public bool Centred { get; init; } = true;
    /// <summary>What it does in words every title shares ("pitch", "gear.up"…): how bindings are copied from another title,
    /// and what the example device shows.</summary>
    public string? Role { get; init; }
    /// <summary>The game's own bindings for it, a slot each.</summary>
    public Bind[] Defaults { get; init; } = [];
    /// <summary>The game's own name, where it differs from <see cref="Label"/>.</summary>
    public string? GameLabel { get; init; }
    /// <summary>When it works (Elite's ship, SRV or on foot): a binding only conflicts with another in the same one.</summary>
    public string Context { get; init; } = "";
    /// <summary>Its order in its group.</summary>
    public int Order { get; init; }
}

/// <summary>What's wrong with a title's folder or files, or a binding that can't be.</summary>
public sealed class BinderException(string message) : Exception(message);

/// <summary>
/// Keys by their scancode (as DirectInput numbers them), with each format's name for them: FSO's and Elite Dangerous'. Assetto
/// Corsa uses Windows' virtual key codes (<see cref="ToVk"/>, <see cref="FromVk"/>).
/// </summary>
public static class Keys
{
    public sealed record KeyName(int Code, string Label, string? Fso, string? Elite);

    public static readonly KeyName[] All =
    [
        new(0x01, "Esc", "KEY_ESC", "Key_Escape"),
        new(0x02, "1", "KEY_1", "Key_1"), new(0x03, "2", "KEY_2", "Key_2"), new(0x04, "3", "KEY_3", "Key_3"), new(0x05, "4", "KEY_4", "Key_4"),
        new(0x06, "5", "KEY_5", "Key_5"), new(0x07, "6", "KEY_6", "Key_6"), new(0x08, "7", "KEY_7", "Key_7"), new(0x09, "8", "KEY_8", "Key_8"),
        new(0x0A, "9", "KEY_9", "Key_9"), new(0x0B, "0", "KEY_0", "Key_0"),
        new(0x0C, "-", "KEY_MINUS", "Key_Minus"), new(0x0D, "=", "KEY_EQUAL", "Key_Equals"), new(0x0E, "Backspace", "KEY_BACKSP", "Key_Backspace"),
        new(0x0F, "Tab", "KEY_TAB", "Key_Tab"),
        new(0x10, "Q", "KEY_Q", "Key_Q"), new(0x11, "W", "KEY_W", "Key_W"), new(0x12, "E", "KEY_E", "Key_E"), new(0x13, "R", "KEY_R", "Key_R"),
        new(0x14, "T", "KEY_T", "Key_T"), new(0x15, "Y", "KEY_Y", "Key_Y"), new(0x16, "U", "KEY_U", "Key_U"), new(0x17, "I", "KEY_I", "Key_I"),
        new(0x18, "O", "KEY_O", "Key_O"), new(0x19, "P", "KEY_P", "Key_P"),
        new(0x1A, "[", "KEY_LBRACKET", "Key_LeftBracket"), new(0x1B, "]", "KEY_RBRACKET", "Key_RightBracket"), new(0x1C, "Enter", "KEY_ENTER", "Key_Enter"),
        new(0x1D, "Left Ctrl", "KEY_LCTRL", "Key_LeftControl"),
        new(0x1E, "A", "KEY_A", "Key_A"), new(0x1F, "S", "KEY_S", "Key_S"), new(0x20, "D", "KEY_D", "Key_D"), new(0x21, "F", "KEY_F", "Key_F"),
        new(0x22, "G", "KEY_G", "Key_G"), new(0x23, "H", "KEY_H", "Key_H"), new(0x24, "J", "KEY_J", "Key_J"), new(0x25, "K", "KEY_K", "Key_K"),
        new(0x26, "L", "KEY_L", "Key_L"),
        new(0x27, ";", "KEY_SEMICOL", "Key_SemiColon"), new(0x28, "'", "KEY_RAPOSTRO", "Key_Apostrophe"), new(0x29, "`", "KEY_LAPOSTRO", "Key_Grave"),
        new(0x2A, "Left Shift", "KEY_LSHIFT", "Key_LeftShift"), new(0x2B, "\\", "KEY_SLASH", "Key_BackSlash"),
        new(0x2C, "Z", "KEY_Z", "Key_Z"), new(0x2D, "X", "KEY_X", "Key_X"), new(0x2E, "C", "KEY_C", "Key_C"), new(0x2F, "V", "KEY_V", "Key_V"),
        new(0x30, "B", "KEY_B", "Key_B"), new(0x31, "N", "KEY_N", "Key_N"), new(0x32, "M", "KEY_M", "Key_M"),
        new(0x33, ",", "KEY_COMMA", "Key_Comma"), new(0x34, ".", "KEY_PERIOD", "Key_Period"), new(0x35, "/", "KEY_DIVIDE", "Key_Slash"),
        new(0x36, "Right Shift", "KEY_RSHIFT", "Key_RightShift"), new(0x37, "Numpad *", "KEY_PADMULTIPLY", "Key_Numpad_Multiply"),
        new(0x38, "Left Alt", "KEY_LALT", "Key_LeftAlt"), new(0x39, "Space", "KEY_SPACEBAR", "Key_Space"), new(0x3A, "Caps Lock", "KEY_CAPSLOCK", "Key_CapsLock"),
        new(0x3B, "F1", "KEY_F1", "Key_F1"), new(0x3C, "F2", "KEY_F2", "Key_F2"), new(0x3D, "F3", "KEY_F3", "Key_F3"), new(0x3E, "F4", "KEY_F4", "Key_F4"),
        new(0x3F, "F5", "KEY_F5", "Key_F5"), new(0x40, "F6", "KEY_F6", "Key_F6"), new(0x41, "F7", "KEY_F7", "Key_F7"), new(0x42, "F8", "KEY_F8", "Key_F8"),
        new(0x43, "F9", "KEY_F9", "Key_F9"), new(0x44, "F10", "KEY_F10", "Key_F10"),
        new(0x45, "Num Lock", "KEY_NUMLOCK", "Key_NumLock"), new(0x46, "Scroll Lock", "KEY_SCROLLOCK", "Key_ScrollLock"),
        new(0x47, "Numpad 7", "KEY_PAD7", "Key_Numpad_7"), new(0x48, "Numpad 8", "KEY_PAD8", "Key_Numpad_8"), new(0x49, "Numpad 9", "KEY_PAD9", "Key_Numpad_9"),
        new(0x4A, "Numpad -", "KEY_PADMINUS", "Key_Numpad_Subtract"),
        new(0x4B, "Numpad 4", "KEY_PAD4", "Key_Numpad_4"), new(0x4C, "Numpad 5", "KEY_PAD5", "Key_Numpad_5"), new(0x4D, "Numpad 6", "KEY_PAD6", "Key_Numpad_6"),
        new(0x4E, "Numpad +", "KEY_PADPLUS", "Key_Numpad_Add"),
        new(0x4F, "Numpad 1", "KEY_PAD1", "Key_Numpad_1"), new(0x50, "Numpad 2", "KEY_PAD2", "Key_Numpad_2"), new(0x51, "Numpad 3", "KEY_PAD3", "Key_Numpad_3"),
        new(0x52, "Numpad 0", "KEY_PAD0", "Key_Numpad_0"), new(0x53, "Numpad .", "KEY_PADPERIOD", "Key_Numpad_Decimal"),
        new(0x56, "\\ (UK)", "KEY_SLASH_UK", "Key_OEM_102"), new(0x57, "F11", "KEY_F11", "Key_F11"), new(0x58, "F12", "KEY_F12", "Key_F12"),
        new(0x64, "F13", null, "Key_F13"), new(0x65, "F14", null, "Key_F14"), new(0x66, "F15", null, "Key_F15"),
        new(0x8D, "Numpad =", null, "Key_Numpad_Equals"),
        new(0x9C, "Numpad Enter", "KEY_PADENTER", "Key_Numpad_Enter"), new(0x9D, "Right Ctrl", "KEY_RCTRL", "Key_RightControl"),
        new(0xB3, "Numpad ,", null, "Key_Numpad_Comma"), new(0xB5, "Numpad /", "KEY_PADDIVIDE", "Key_Numpad_Divide"),
        new(0xB7, "Print Screen", "KEY_PRINT_SCRN", "Key_SYSRQ"), new(0xB8, "Right Alt", "KEY_RALT", "Key_RightAlt"),
        new(0xC5, "Pause", "KEY_PAUSE", "Key_Pause"), new(0xC6, "Break", "KEY_BREAK", null),
        new(0xC7, "Home", "KEY_HOME", "Key_Home"), new(0xC8, "Up", "KEY_UP", "Key_UpArrow"), new(0xC9, "Page Up", "KEY_PAGEUP", "Key_PageUp"),
        new(0xCB, "Left", "KEY_LEFT", "Key_LeftArrow"), new(0xCD, "Right", "KEY_RIGHT", "Key_RightArrow"), new(0xCF, "End", "KEY_END", "Key_End"),
        new(0xD0, "Down", "KEY_DOWN", "Key_DownArrow"), new(0xD1, "Page Down", "KEY_PAGEDOWN", "Key_PageDown"),
        new(0xD2, "Insert", "KEY_INSERT", "Key_Insert"), new(0xD3, "Delete", "KEY_DELETE", "Key_Delete"),
        new(0xDB, "Left Windows", null, "Key_LeftWin"), new(0xDC, "Right Windows", null, "Key_RightWin"), new(0xDD, "Menu", null, "Key_Apps"),
    ];

    private static readonly Dictionary<int, KeyName> ByCode = All.ToDictionary(k => k.Code);
    private static readonly Dictionary<string, KeyName> ByFso = All.Where(k => k.Fso != null).ToDictionary(k => k.Fso!);
    private static readonly Dictionary<string, KeyName> ByElite = All.Where(k => k.Elite != null).ToDictionary(k => k.Elite!, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, KeyName> ByLabel = All.GroupBy(k => k.Label, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public const int LeftShift = 0x2A, RightShift = 0x36, LeftCtrl = 0x1D, RightCtrl = 0x9D, LeftAlt = 0x38, RightAlt = 0xB8;

    public static bool IsModifier(int code) => code is LeftShift or RightShift or LeftCtrl or RightCtrl or LeftAlt or RightAlt;

    public static string Label(int code) => ByCode.TryGetValue(code, out var k) ? k.Label : $"Key 0x{code:X2}";
    public static bool Known(int code) => ByCode.ContainsKey(code);
    public static string? Fso(int code) => ByCode.TryGetValue(code, out var k) ? k.Fso : null;
    public static int? FromFso(string name) => ByFso.TryGetValue(name, out var k) ? k.Code : null;
    public static string? Elite(int code) => ByCode.TryGetValue(code, out var k) ? k.Elite : null;
    public static int? FromElite(string name) => ByElite.TryGetValue(name, out var k) ? k.Code : null;

    /// <summary>A key written as people do in the titles' files: "Up", "Shift+T", "Ctrl+Numpad 5".</summary>
    public static Bind? Parse(string text)
    {
        var mods = Mods.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        // ("Numpad +" ends with a + of its own)
        if (text.EndsWith("Numpad +", StringComparison.OrdinalIgnoreCase))
            parts = text[..^"Numpad +".Length].Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Append("Numpad +").ToArray();
        foreach (var p in parts[..^1])
        {
            if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= Mods.Shift;
            else if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= Mods.Ctrl;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= Mods.Alt;
            else return null;
        }
        return ByLabel.TryGetValue(parts[^1], out var k) ? Bind.Key(k.Code, mods) : null;
    }

    public static string Text(int code, Mods mods)
    {
        var s = Label(code);
        if (mods.HasFlag(Mods.Alt)) s = "Alt+" + s;
        if (mods.HasFlag(Mods.Ctrl)) s = "Ctrl+" + s;
        if (mods.HasFlag(Mods.Shift)) s = "Shift+" + s;
        return s;
    }

    // ---------------------------------------------------------------- Windows' virtual keys (Assetto Corsa)

    /// <summary>Each key's virtual key, a table of its own: Windows' MapVirtualKey gives a numpad key for an arrow and Page
    /// Down for Numpad 3 (the US layout's keys for the punctuation).</summary>
    private static readonly (int Code, int Vk)[] VkTable =
    [
        (0x01, 0x1B), (0x0E, 0x08), (0x0F, 0x09), (0x1C, 0x0D), (0x39, 0x20), (0x3A, 0x14), (0x45, 0x90), (0x46, 0x91), (0xC5, 0x13), (0xB7, 0x2C),
        (0x2A, 0xA0), (0x36, 0xA1), (0x1D, 0xA2), (0x9D, 0xA3), (0x38, 0xA4), (0xB8, 0xA5), (0xDB, 0x5B), (0xDC, 0x5C), (0xDD, 0x5D),
        (0xC8, 0x26), (0xD0, 0x28), (0xCB, 0x25), (0xCD, 0x27), (0xD2, 0x2D), (0xD3, 0x2E), (0xC7, 0x24), (0xCF, 0x23), (0xC9, 0x21), (0xD1, 0x22),
        (0x52, 0x60), (0x4F, 0x61), (0x50, 0x62), (0x51, 0x63), (0x4B, 0x64), (0x4C, 0x65), (0x4D, 0x66), (0x47, 0x67), (0x48, 0x68), (0x49, 0x69),
        (0x37, 0x6A), (0x4E, 0x6B), (0x4A, 0x6D), (0x53, 0x6E), (0xB5, 0x6F),
        (0x27, 0xBA), (0x0D, 0xBB), (0x33, 0xBC), (0x0C, 0xBD), (0x34, 0xBE), (0x35, 0xBF), (0x29, 0xC0), (0x1A, 0xDB), (0x2B, 0xDC), (0x1B, 0xDD), (0x28, 0xDE), (0x56, 0xE2),
        (0x3B, 0x70), (0x3C, 0x71), (0x3D, 0x72), (0x3E, 0x73), (0x3F, 0x74), (0x40, 0x75), (0x41, 0x76), (0x42, 0x77), (0x43, 0x78), (0x44, 0x79),
        (0x57, 0x7A), (0x58, 0x7B), (0x64, 0x7C), (0x65, 0x7D), (0x66, 0x7E),
        (0x0B, '0'), (0x02, '1'), (0x03, '2'), (0x04, '3'), (0x05, '4'), (0x06, '5'), (0x07, '6'), (0x08, '7'), (0x09, '8'), (0x0A, '9'),
        (0x1E, 'A'), (0x30, 'B'), (0x2E, 'C'), (0x20, 'D'), (0x12, 'E'), (0x21, 'F'), (0x22, 'G'), (0x23, 'H'), (0x17, 'I'), (0x24, 'J'), (0x25, 'K'), (0x26, 'L'), (0x32, 'M'),
        (0x31, 'N'), (0x18, 'O'), (0x19, 'P'), (0x10, 'Q'), (0x13, 'R'), (0x1F, 'S'), (0x14, 'T'), (0x16, 'U'), (0x2F, 'V'), (0x11, 'W'), (0x2D, 'X'), (0x15, 'Y'), (0x2C, 'Z'),
    ];

    private static readonly Dictionary<int, int> VkByCode = VkTable.ToDictionary(k => k.Code, k => k.Vk);
    // (Shift, Ctrl and Alt as Windows' own VKs too, and Enter for the numpad's)
    private static readonly Dictionary<int, int> CodeByVk = VkTable.GroupBy(k => k.Vk).ToDictionary(g => g.Key, g => g.First().Code)
        .Concat(new Dictionary<int, int> { [0x10] = LeftShift, [0x11] = LeftCtrl, [0x12] = LeftAlt }).ToDictionary(p => p.Key, p => p.Value);

    /// <summary>A key's virtual key, or 0 if it hasn't one here.</summary>
    public static int ToVk(int code) => VkByCode.TryGetValue(code, out int vk) ? vk : code == 0x9C ? 0x0D : 0;

    public static int? FromVk(int vk) => CodeByVk.TryGetValue(vk, out int code) ? code : null;
}
