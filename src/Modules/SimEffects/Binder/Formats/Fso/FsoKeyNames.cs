namespace DaisysApp.Applets.SimEffects.Binder.Formats.Fso;

/// <summary>
/// FSO keyboard names. FSO key codes are DirectInput scancodes (Windows scancode, plus 0x80 for extended
/// keys), so a captured WM_KEYDOWN maps straight onto them.
/// </summary>
public static class KeyNames
{
    private static readonly (string Name, int Code, string Label)[] Keys =
    [
        ("KEY_0", 0x0B, "0"), ("KEY_1", 0x02, "1"), ("KEY_2", 0x03, "2"), ("KEY_3", 0x04, "3"), ("KEY_4", 0x05, "4"),
        ("KEY_5", 0x06, "5"), ("KEY_6", 0x07, "6"), ("KEY_7", 0x08, "7"), ("KEY_8", 0x09, "8"), ("KEY_9", 0x0A, "9"),
        ("KEY_A", 0x1E, "A"), ("KEY_B", 0x30, "B"), ("KEY_C", 0x2E, "C"), ("KEY_D", 0x20, "D"), ("KEY_E", 0x12, "E"),
        ("KEY_F", 0x21, "F"), ("KEY_G", 0x22, "G"), ("KEY_H", 0x23, "H"), ("KEY_I", 0x17, "I"), ("KEY_J", 0x24, "J"),
        ("KEY_K", 0x25, "K"), ("KEY_L", 0x26, "L"), ("KEY_M", 0x32, "M"), ("KEY_N", 0x31, "N"), ("KEY_O", 0x18, "O"),
        ("KEY_P", 0x19, "P"), ("KEY_Q", 0x10, "Q"), ("KEY_R", 0x13, "R"), ("KEY_S", 0x1F, "S"), ("KEY_T", 0x14, "T"),
        ("KEY_U", 0x16, "U"), ("KEY_V", 0x2F, "V"), ("KEY_W", 0x11, "W"), ("KEY_X", 0x2D, "X"), ("KEY_Y", 0x15, "Y"),
        ("KEY_Z", 0x2C, "Z"),
        ("KEY_MINUS", 0x0C, "-"), ("KEY_EQUAL", 0x0D, "="), ("KEY_DIVIDE", 0x35, "/"), ("KEY_SLASH", 0x2B, "\\"),
        ("KEY_SLASH_UK", 0x56, "\\ (UK)"), ("KEY_COMMA", 0x33, ","), ("KEY_PERIOD", 0x34, "."), ("KEY_SEMICOL", 0x27, ";"),
        ("KEY_LBRACKET", 0x1A, "["), ("KEY_RBRACKET", 0x1B, "]"), ("KEY_RAPOSTRO", 0x28, "'"), ("KEY_LAPOSTRO", 0x29, "`"),
        ("KEY_ESC", 0x01, "Esc"), ("KEY_ENTER", 0x1C, "Enter"), ("KEY_BACKSP", 0x0E, "Backspace"), ("KEY_TAB", 0x0F, "Tab"),
        ("KEY_SPACEBAR", 0x39, "Space"),
        ("KEY_NUMLOCK", 0x45, "Num Lock"), ("KEY_SCROLLOCK", 0x46, "Scroll Lock"), ("KEY_CAPSLOCK", 0x3A, "Caps Lock"),
        ("KEY_LSHIFT", 0x2A, "Left Shift"), ("KEY_RSHIFT", 0x36, "Right Shift"),
        ("KEY_LALT", 0x38, "Left Alt"), ("KEY_RALT", 0xB8, "Right Alt"),
        ("KEY_LCTRL", 0x1D, "Left Ctrl"), ("KEY_RCTRL", 0x9D, "Right Ctrl"),
        ("KEY_F1", 0x3B, "F1"), ("KEY_F2", 0x3C, "F2"), ("KEY_F3", 0x3D, "F3"), ("KEY_F4", 0x3E, "F4"),
        ("KEY_F5", 0x3F, "F5"), ("KEY_F6", 0x40, "F6"), ("KEY_F7", 0x41, "F7"), ("KEY_F8", 0x42, "F8"),
        ("KEY_F9", 0x43, "F9"), ("KEY_F10", 0x44, "F10"), ("KEY_F11", 0x57, "F11"), ("KEY_F12", 0x58, "F12"),
        ("KEY_PAD0", 0x52, "Numpad 0"), ("KEY_PAD1", 0x4F, "Numpad 1"), ("KEY_PAD2", 0x50, "Numpad 2"),
        ("KEY_PAD3", 0x51, "Numpad 3"), ("KEY_PAD4", 0x4B, "Numpad 4"), ("KEY_PAD5", 0x4C, "Numpad 5"),
        ("KEY_PAD6", 0x4D, "Numpad 6"), ("KEY_PAD7", 0x47, "Numpad 7"), ("KEY_PAD8", 0x48, "Numpad 8"),
        ("KEY_PAD9", 0x49, "Numpad 9"), ("KEY_PADMINUS", 0x4A, "Numpad −"), ("KEY_PADPLUS", 0x4E, "Numpad +"),
        ("KEY_PADPERIOD", 0x53, "Numpad ."), ("KEY_PADDIVIDE", 0xB5, "Numpad /"), ("KEY_PADMULTIPLY", 0x37, "Numpad *"),
        ("KEY_PADENTER", 0x9C, "Numpad Enter"),
        ("KEY_INSERT", 0xD2, "Insert"), ("KEY_HOME", 0xC7, "Home"), ("KEY_PAGEUP", 0xC9, "Page Up"),
        ("KEY_DELETE", 0xD3, "Delete"), ("KEY_END", 0xCF, "End"), ("KEY_PAGEDOWN", 0xD1, "Page Down"),
        ("KEY_UP", 0xC8, "Up"), ("KEY_DOWN", 0xD0, "Down"), ("KEY_LEFT", 0xCB, "Left"), ("KEY_RIGHT", 0xCD, "Right"),
        ("KEY_PRINT_SCRN", 0xB7, "Print Screen"), ("KEY_PAUSE", 0x45, "Pause"), ("KEY_BREAK", 0xC6, "Break"),
    ];

    private static readonly Dictionary<string, string> LabelByName = Keys.ToDictionary(k => k.Name, k => k.Label);
    private static readonly Dictionary<int, string> NameByCode = Keys
        .GroupBy(k => k.Code).ToDictionary(g => g.Key, g => g.First().Name);

    public static bool IsModifierName(string name) =>
        name is "KEY_LSHIFT" or "KEY_RSHIFT" or "KEY_LALT" or "KEY_RALT" or "KEY_LCTRL" or "KEY_RCTRL";

    /// <summary>Key name for a DirectInput-style scancode, or null if FSO has no name for it.</summary>
    public static string? FromScancode(int code) => NameByCode.TryGetValue(code, out var n) ? n : null;

    /// <summary>Accepts "KEY_T", "SHIFT-KEY_T", "ALT-KEY_T" and "ALT-SHIFT-KEY_T" (KeyboardToVal's order).</summary>
    public static bool IsValidKeyInput(string input)
    {
        var s = input;
        if (s.StartsWith("ALT-")) s = s[4..];
        if (s.StartsWith("SHIFT-")) s = s[6..];
        return LabelByName.ContainsKey(s);
    }

    public static string Display(string input)
    {
        var parts = new List<string>();
        var s = input;
        if (s.StartsWith("ALT-")) { parts.Add("Alt"); s = s[4..]; }
        if (s.StartsWith("SHIFT-")) { parts.Add("Shift"); s = s[6..]; }
        parts.Add(LabelByName.TryGetValue(s, out var label) ? label : s);
        return string.Join(" + ", parts);
    }
}
