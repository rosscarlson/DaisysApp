using System.Runtime.InteropServices;

namespace DaisysApp.Applets.Joy2Key;

public enum KeyGroup
{
    Modifiers, Common, Navigation, Function, Function13, Numpad, Letters, Digits, Symbols, Lock, Media, Browser, Launch,
    Mouse, International, Other,
}

/// <summary>A key (or mouse button) an input can press. <see cref="Id"/> is what profiles store.</summary>
public sealed record KeyDef(string Id, string Name, int Vk, KeyGroup Group, bool Extended = false)
{
    /// <summary>Sent as a virtual key rather than a scan code (keys with no fixed scan code).</summary>
    public bool ByVk => Group is KeyGroup.Media or KeyGroup.Browser or KeyGroup.Launch or KeyGroup.International or KeyGroup.Other
        || Vk is 0x13 or 0x2C or 0x90 or 0x03 or 0x5F;

    public bool IsMouse => Group == KeyGroup.Mouse;
}

/// <summary>Every key Joy 2 Key can press, by group (the groups of the key menu).</summary>
public static class KeyCatalog
{
    private static List<KeyDef>? all;
    private static Dictionary<string, KeyDef>? byId;

    public static IReadOnlyList<KeyDef> All => all ??= Build();

    public static KeyDef? Find(string id)
    {
        byId ??= All.ToDictionary(k => k.Id, StringComparer.OrdinalIgnoreCase);
        return byId.TryGetValue(id, out var k) ? k : null;
    }

    /// <summary>The key for a virtual-key code (generic Ctrl / Shift / Alt mean the left ones).</summary>
    public static KeyDef? FromVk(int vk, bool extended = false)
    {
        vk = vk switch { 0x10 => 0xA0, 0x11 => 0xA2, 0x12 => 0xA4, _ => vk };
        if (vk == 0x0D && extended) return Find("NumEnter");
        return All.FirstOrDefault(k => k.Vk == vk && !k.IsMouse && k.Id != "NumEnter")
            ?? (vk is 1 or 2 or 4 or 5 or 6 ? All.FirstOrDefault(k => k.IsMouse && k.Vk == vk) : null);
    }

    public static string NameOf(string id) => Find(id)?.Name ?? id;

    public static string Describe(IEnumerable<string> ids)
    {
        var s = string.Join(" + ", ids.Select(NameOf));
        return s.Length == 0 ? T("Nothing") : s;
    }

    public static string GroupName(KeyGroup g) => g switch
    {
        KeyGroup.Modifiers => T("Ctrl, Shift, Alt and Windows"),
        KeyGroup.Common => T("Enter, Esc, Tab, Space…"),
        KeyGroup.Navigation => T("Arrows and navigation"),
        KeyGroup.Function => T("F1–F12"),
        KeyGroup.Function13 => T("F13–F24"),
        KeyGroup.Numpad => T("Number pad"),
        KeyGroup.Letters => T("Letters"),
        KeyGroup.Digits => T("Number row"),
        KeyGroup.Symbols => T("Symbols"),
        KeyGroup.Lock => T("Lock, Print Screen and Pause"),
        KeyGroup.Media => T("Media"),
        KeyGroup.Browser => T("Browser"),
        KeyGroup.Launch => T("Launch apps"),
        KeyGroup.Mouse => T("Mouse buttons and wheel"),
        KeyGroup.International => T("International and IME"),
        _ => T("Rare and old keys"),
    };

    private static List<KeyDef> Build()
    {
        var k = new List<KeyDef>();
        void Add(string id, string name, int vk, KeyGroup g, bool ext = false) => k.Add(new KeyDef(id, name, vk, g, ext));

        Add("LCtrl", T("Left Ctrl"), 0xA2, KeyGroup.Modifiers);
        Add("RCtrl", T("Right Ctrl"), 0xA3, KeyGroup.Modifiers, true);
        Add("LShift", T("Left Shift"), 0xA0, KeyGroup.Modifiers);
        Add("RShift", T("Right Shift"), 0xA1, KeyGroup.Modifiers);
        Add("LAlt", T("Left Alt"), 0xA4, KeyGroup.Modifiers);
        Add("RAlt", T("Right Alt (AltGr)"), 0xA5, KeyGroup.Modifiers, true);
        Add("LWin", T("Left Windows"), 0x5B, KeyGroup.Modifiers, true);
        Add("RWin", T("Right Windows"), 0x5C, KeyGroup.Modifiers, true);
        Add("Apps", T("Menu (Apps)"), 0x5D, KeyGroup.Modifiers, true);

        Add("Enter", T("Enter"), 0x0D, KeyGroup.Common);
        Add("Esc", T("Esc"), 0x1B, KeyGroup.Common);
        Add("Tab", T("Tab"), 0x09, KeyGroup.Common);
        Add("Space", T("Space"), 0x20, KeyGroup.Common);
        Add("Backspace", T("Backspace"), 0x08, KeyGroup.Common);

        Add("Up", T("Up arrow"), 0x26, KeyGroup.Navigation, true);
        Add("Down", T("Down arrow"), 0x28, KeyGroup.Navigation, true);
        Add("Left", T("Left arrow"), 0x25, KeyGroup.Navigation, true);
        Add("Right", T("Right arrow"), 0x27, KeyGroup.Navigation, true);
        Add("Home", T("Home"), 0x24, KeyGroup.Navigation, true);
        Add("End", T("End"), 0x23, KeyGroup.Navigation, true);
        Add("PageUp", T("Page Up"), 0x21, KeyGroup.Navigation, true);
        Add("PageDown", T("Page Down"), 0x22, KeyGroup.Navigation, true);
        Add("Insert", T("Insert"), 0x2D, KeyGroup.Navigation, true);
        Add("Delete", T("Delete"), 0x2E, KeyGroup.Navigation, true);

        for (int i = 1; i <= 24; i++) Add($"F{i}", $"F{i}", 0x70 + i - 1, i <= 12 ? KeyGroup.Function : KeyGroup.Function13);

        for (int i = 0; i <= 9; i++) Add($"Num{i}", F("Num {0}", i), 0x60 + i, KeyGroup.Numpad);
        Add("NumDecimal", T("Num ."), 0x6E, KeyGroup.Numpad);
        Add("NumAdd", T("Num +"), 0x6B, KeyGroup.Numpad);
        Add("NumSubtract", T("Num −"), 0x6D, KeyGroup.Numpad);
        Add("NumMultiply", T("Num *"), 0x6A, KeyGroup.Numpad);
        Add("NumDivide", T("Num /"), 0x6F, KeyGroup.Numpad, true);
        Add("NumEnter", T("Num Enter"), 0x0D, KeyGroup.Numpad, true);
        Add("NumLock", T("Num Lock"), 0x90, KeyGroup.Numpad);
        Add("NumClear", T("Num 5 (Num Lock off)"), 0x0C, KeyGroup.Numpad);
        Add("NumSeparator", T("Num separator"), 0x6C, KeyGroup.Numpad);

        for (char c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c.ToString(), c, KeyGroup.Letters);
        for (char c = '0'; c <= '9'; c++) Add("D" + c, c.ToString(), c, KeyGroup.Digits);

        Add("Semicolon", "; :", 0xBA, KeyGroup.Symbols);
        Add("Equals", "= +", 0xBB, KeyGroup.Symbols);
        Add("Comma", ", <", 0xBC, KeyGroup.Symbols);
        Add("Minus", "- _", 0xBD, KeyGroup.Symbols);
        Add("Period", ". >", 0xBE, KeyGroup.Symbols);
        Add("Slash", "/ ?", 0xBF, KeyGroup.Symbols);
        Add("Backquote", "` ~", 0xC0, KeyGroup.Symbols);
        Add("LBracket", "[ {", 0xDB, KeyGroup.Symbols);
        Add("Backslash", "\\ |", 0xDC, KeyGroup.Symbols);
        Add("RBracket", "] }", 0xDD, KeyGroup.Symbols);
        Add("Quote", "' \"", 0xDE, KeyGroup.Symbols);
        Add("Oem102", T("\\ | (next to Left Shift)"), 0xE2, KeyGroup.Symbols);
        Add("Oem8", T("OEM 8"), 0xDF, KeyGroup.Symbols);

        Add("CapsLock", T("Caps Lock"), 0x14, KeyGroup.Lock);
        Add("ScrollLock", T("Scroll Lock"), 0x91, KeyGroup.Lock);
        Add("PrintScreen", T("Print Screen"), 0x2C, KeyGroup.Lock, true);
        Add("Pause", T("Pause"), 0x13, KeyGroup.Lock);
        Add("Break", T("Break (Ctrl+Pause)"), 0x03, KeyGroup.Lock, true);

        Add("PlayPause", T("Play / pause"), 0xB3, KeyGroup.Media, true);
        Add("MediaStop", T("Stop"), 0xB2, KeyGroup.Media, true);
        Add("NextTrack", T("Next track"), 0xB0, KeyGroup.Media, true);
        Add("PrevTrack", T("Previous track"), 0xB1, KeyGroup.Media, true);
        Add("VolumeUp", T("Volume up"), 0xAF, KeyGroup.Media, true);
        Add("VolumeDown", T("Volume down"), 0xAE, KeyGroup.Media, true);
        Add("VolumeMute", T("Mute"), 0xAD, KeyGroup.Media, true);

        Add("BrowserBack", T("Back"), 0xA6, KeyGroup.Browser, true);
        Add("BrowserForward", T("Forward"), 0xA7, KeyGroup.Browser, true);
        Add("BrowserRefresh", T("Refresh"), 0xA8, KeyGroup.Browser, true);
        Add("BrowserStop", T("Stop loading"), 0xA9, KeyGroup.Browser, true);
        Add("BrowserSearch", T("Search"), 0xAA, KeyGroup.Browser, true);
        Add("BrowserFavorites", T("Favorites"), 0xAB, KeyGroup.Browser, true);
        Add("BrowserHome", T("Browser home"), 0xAC, KeyGroup.Browser, true);

        Add("LaunchMail", T("Mail"), 0xB4, KeyGroup.Launch, true);
        Add("LaunchMedia", T("Media player"), 0xB5, KeyGroup.Launch, true);
        Add("LaunchApp1", T("App 1 (usually This PC)"), 0xB6, KeyGroup.Launch, true);
        Add("LaunchApp2", T("App 2 (usually Calculator)"), 0xB7, KeyGroup.Launch, true);
        Add("Sleep", T("Sleep"), 0x5F, KeyGroup.Launch, true);

        Add("MouseLeft", T("Left click"), 1, KeyGroup.Mouse);
        Add("MouseRight", T("Right click"), 2, KeyGroup.Mouse);
        Add("MouseMiddle", T("Middle click"), 4, KeyGroup.Mouse);
        Add("MouseBack", T("Mouse back (X1)"), 5, KeyGroup.Mouse);
        Add("MouseForward", T("Mouse forward (X2)"), 6, KeyGroup.Mouse);
        Add("WheelUp", T("Wheel up"), 0, KeyGroup.Mouse);
        Add("WheelDown", T("Wheel down"), 0, KeyGroup.Mouse);
        Add("WheelLeft", T("Wheel left"), 0, KeyGroup.Mouse);
        Add("WheelRight", T("Wheel right"), 0, KeyGroup.Mouse);

        Add("Kana", T("Kana / Hangul"), 0x15, KeyGroup.International);
        Add("ImeOn", T("IME on"), 0x16, KeyGroup.International);
        Add("Junja", T("Junja"), 0x17, KeyGroup.International);
        Add("Final", T("Final"), 0x18, KeyGroup.International);
        Add("Kanji", T("Kanji / Hanja"), 0x19, KeyGroup.International);
        Add("ImeOff", T("IME off"), 0x1A, KeyGroup.International);
        Add("Convert", T("Convert"), 0x1C, KeyGroup.International);
        Add("NonConvert", T("Non-convert"), 0x1D, KeyGroup.International);
        Add("Accept", T("Accept"), 0x1E, KeyGroup.International);
        Add("ModeChange", T("Mode change"), 0x1F, KeyGroup.International);
        Add("AbntC1", T("ABNT C1 (/ ?)"), 0xC1, KeyGroup.International);
        Add("AbntC2", T("ABNT C2 (Num .)"), 0xC2, KeyGroup.International);
        Add("Process", T("IME process"), 0xE5, KeyGroup.International);

        Add("Help", T("Help"), 0x2F, KeyGroup.Other);
        Add("Select", T("Select"), 0x29, KeyGroup.Other);
        Add("Print", T("Print"), 0x2A, KeyGroup.Other);
        Add("Execute", T("Execute"), 0x2B, KeyGroup.Other);
        Add("Attn", T("Attn"), 0xF6, KeyGroup.Other);
        Add("CrSel", T("CrSel"), 0xF7, KeyGroup.Other);
        Add("ExSel", T("ExSel"), 0xF8, KeyGroup.Other);
        Add("EraseEof", T("Erase EOF"), 0xF9, KeyGroup.Other);
        Add("Play", T("Play"), 0xFA, KeyGroup.Other);
        Add("Zoom", T("Zoom"), 0xFB, KeyGroup.Other);
        Add("Pa1", T("PA1"), 0xFD, KeyGroup.Other);
        Add("OemClear", T("OEM Clear"), 0xFE, KeyGroup.Other);
        return k;
    }
}

/// <summary>Presses and lets go of keys and mouse buttons with SendInput, as scan codes where it can (games read those).</summary>
public static class KeySender
{
    public static void Down(KeyDef k) => Send(k, true);
    public static void Up(KeyDef k) => Send(k, false);

    public static void Send(KeyDef k, bool down)
    {
        var input = new INPUT();
        if (k.IsMouse)
        {
            input.type = INPUT_MOUSE;
            switch (k.Id)
            {
                case "MouseLeft": input.u.mi.dwFlags = down ? 0x0002u : 0x0004u; break;
                case "MouseRight": input.u.mi.dwFlags = down ? 0x0008u : 0x0010u; break;
                case "MouseMiddle": input.u.mi.dwFlags = down ? 0x0020u : 0x0040u; break;
                case "MouseBack": input.u.mi.dwFlags = down ? 0x0080u : 0x0100u; input.u.mi.mouseData = 1; break;
                case "MouseForward": input.u.mi.dwFlags = down ? 0x0080u : 0x0100u; input.u.mi.mouseData = 2; break;
                default:
                    // a wheel "key" turns one notch when it's pressed
                    if (!down) return;
                    bool horizontal = k.Id is "WheelLeft" or "WheelRight";
                    input.u.mi.dwFlags = horizontal ? 0x01000u : 0x0800u;
                    input.u.mi.mouseData = unchecked((uint)(k.Id is "WheelUp" or "WheelRight" ? 120 : -120));
                    break;
            }
        }
        else
        {
            input.type = INPUT_KEYBOARD;
            uint flags = down ? 0 : KEYEVENTF_KEYUP;
            uint scan = k.ByVk ? 0 : MapVirtualKeyW((uint)k.Vk, MAPVK_VK_TO_VSC_EX);
            if (scan != 0)
            {
                flags |= KEYEVENTF_SCANCODE;
                if ((scan & 0xFF00) == 0xE000 || k.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
                input.u.ki.wScan = (ushort)(scan & 0xFF);
            }
            else
            {
                input.u.ki.wVk = (ushort)k.Vk;
                if (k.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
            }
            input.u.ki.dwFlags = flags;
        }
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Moves the mouse pointer by this many pixels.</summary>
    public static void MoveMouse(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var input = new INPUT { type = INPUT_MOUSE };
        input.u.mi.dx = dx;
        input.u.mi.dy = dy;
        input.u.mi.dwFlags = 0x0001; // MOUSEEVENTF_MOVE
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;
    private const uint MAPVK_VK_TO_VSC_EX = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);
}
