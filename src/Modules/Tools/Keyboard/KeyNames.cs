using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace DaisysApp.Applets.Tools.Keyboards;

/// <summary>Ctrl, Shift, Alt and Win, either side.</summary>
[Flags]
public enum Mods { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

/// <summary>
/// A key as the keyboard sends it (its virtual-key code, whether it's an extended key, its scan code), with the
/// modifiers it comes with: what a key on the keyboard sends (Win + Shift + F23 for the Copilot key) or what it's
/// made to send. <see cref="Vk"/> 0 is nothing.
/// </summary>
public sealed class KeyCombo
{
    public Mods Mods { get; set; }
    public int Vk { get; set; }
    public bool Ext { get; set; }
    public int Scan { get; set; }

    [JsonIgnore] public bool Nothing => Vk == 0;

    /// <summary>The same key: code, extended or not (Enter or the number pad's), the scan code where the code says
    /// nothing (0xFF), and the modifiers.</summary>
    public bool Same(KeyCombo o) => Mods == o.Mods && Vk == o.Vk && Ext == o.Ext && (Vk != KeyNames.NoVk || Scan == o.Scan);

    public KeyCombo Copy() => new() { Mods = Mods, Vk = Vk, Ext = Ext, Scan = Scan };

    public override string ToString() => KeyNames.Describe(this);
}

/// <summary>A key and what it sends instead (saved in Keyboard.json).</summary>
public sealed class Remap
{
    public KeyCombo From { get; set; } = new();
    public KeyCombo To { get; set; } = new();
    public bool On { get; set; } = true;
}

/// <summary>Keys' names, the list to pick from, and which are modifiers.</summary>
public static class KeyNames
{
    /// <summary>The code of a key Windows has no code for (only its scan code says which).</summary>
    public const int NoVk = 0xFF;

    public const int LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5, LWin = 0x5B, RWin = 0x5C;

    /// <summary>The modifier a key is (left, right or either), or none.</summary>
    public static Mods ModOf(int vk) => vk switch
    {
        0x10 or LShift or RShift => Mods.Shift,
        0x11 or LCtrl or RCtrl => Mods.Ctrl,
        0x12 or LAlt or RAlt => Mods.Alt,
        LWin or RWin => Mods.Win,
        _ => Mods.None,
    };

    /// <summary>The left key of a modifier, the one sent for it.</summary>
    public static int VkOf(Mods m) => m switch { Mods.Ctrl => LCtrl, Mods.Shift => LShift, Mods.Alt => LAlt, _ => LWin };

    public static readonly Mods[] Each = [Mods.Win, Mods.Ctrl, Mods.Alt, Mods.Shift];

    /// <summary>Every key to pick from, by group (the number pad's Enter is Enter, extended); made when first shown,
    /// not by the hook.</summary>
    public static (string Group, int Vk, bool Ext, string Name)[] List => list.Value;

    private static readonly Lazy<(string, int, bool, string)[]> list = new(MakeList);

    private static (string, int, bool, string)[] MakeList()
    {
        var l = new List<(string, int, bool, string)>();
        string g = T("Keys");
        for (char c = 'A'; c <= 'Z'; c++) l.Add((g, c, false, c.ToString()));
        for (char c = '0'; c <= '9'; c++) l.Add((g, c, false, c.ToString()));
        l.AddRange(new (string, int, bool, string)[]
        {
            (g, 0x20, false, T("Space")), (g, 0x0D, false, T("Enter")), (g, 0x09, false, T("Tab")), (g, 0x08, false, T("Backspace")),
            (g, 0x1B, false, T("Esc")), (g, 0x14, false, T("Caps Lock")), (g, 0x2E, true, T("Delete")), (g, 0x2D, true, T("Insert")),
            (g, 0x24, true, T("Home")), (g, 0x23, true, T("End")), (g, 0x21, true, T("Page Up")), (g, 0x22, true, T("Page Down")),
            (g, 0x26, true, T("Up")), (g, 0x28, true, T("Down")), (g, 0x25, true, T("Left")), (g, 0x27, true, T("Right")),
            (g, 0x2C, true, T("Print Screen")), (g, 0x91, false, T("Scroll Lock")), (g, 0x13, false, T("Pause")), (g, 0x5D, true, T("Menu")),
            (g, 0xBA, false, ";"), (g, 0xBB, false, "="), (g, 0xBC, false, ","), (g, 0xBD, false, "-"), (g, 0xBE, false, "."),
            (g, 0xBF, false, "/"), (g, 0xC0, false, "`"), (g, 0xDB, false, "["), (g, 0xDC, false, "\\"), (g, 0xDD, false, "]"), (g, 0xDE, false, "'"),
        });
        g = T("Modifiers");
        l.AddRange(new (string, int, bool, string)[]
        {
            (g, LCtrl, false, T("Left Ctrl")), (g, RCtrl, true, T("Right Ctrl")), (g, LShift, false, T("Left Shift")), (g, RShift, false, T("Right Shift")),
            (g, LAlt, false, T("Left Alt")), (g, RAlt, true, T("Right Alt")), (g, LWin, true, T("Left Win")), (g, RWin, true, T("Right Win")),
        });
        g = T("Function keys");
        for (int i = 1; i <= 24; i++) l.Add((g, 0x6F + i, false, "F" + i));
        g = T("Media");
        l.AddRange(new (string, int, bool, string)[]
        {
            (g, 0xB3, true, T("Play / Pause")), (g, 0xB2, true, T("Stop")), (g, 0xB0, true, T("Next track")), (g, 0xB1, true, T("Previous track")),
            (g, 0xAF, true, T("Volume up")), (g, 0xAE, true, T("Volume down")), (g, 0xAD, true, T("Mute")),
            (g, 0xA6, true, T("Browser back")), (g, 0xA7, true, T("Browser forward")), (g, 0xA8, true, T("Browser refresh")),
            (g, 0xAA, true, T("Search")), (g, 0xAB, true, T("Favourites")), (g, 0xAC, true, T("Browser home")),
            (g, 0xB4, true, T("Mail")), (g, 0xB5, true, T("Media player")), (g, 0xB6, true, T("This PC")), (g, 0xB7, true, T("Calculator")),
            (g, 0x5F, true, T("Sleep")),
        });
        g = T("Number pad");
        for (int i = 0; i <= 9; i++) l.Add((g, 0x60 + i, false, T("Num") + " " + i));
        l.AddRange(new (string, int, bool, string)[]
        {
            (g, 0x6A, false, T("Num") + " *"), (g, 0x6B, false, T("Num") + " +"), (g, 0x6D, false, T("Num") + " -"),
            (g, 0x6E, false, T("Num") + " ."), (g, 0x6F, true, T("Num") + " /"), (g, 0x0D, true, T("Num Enter")), (g, 0x90, true, T("Num Lock")),
        });
        return l.ToArray();
    }

    /// <summary>A key's name: from the list, else Windows' name for its scan code, else its code.</summary>
    public static string Name(int vk, bool ext, int scan)
    {
        foreach (var k in List) if (k.Vk == vk && k.Ext == ext) return k.Name;
        foreach (var k in List) if (k.Vk == vk && vk != 0x0D) return k.Name; // (a keyboard's own extended flag)
        if (scan != 0)
        {
            var sb = new StringBuilder(64);
            if (GetKeyNameText((scan & 0xFF) << 16 | (ext ? 1 << 24 : 0), sb, sb.Capacity) > 0) return sb.ToString();
        }
        return F("Key {0}", vk == NoVk ? $"#{scan:X2}" : $"0x{vk:X2}");
    }

    /// <summary>"Ctrl + Shift + F2"; the Copilot key by its name; "Nothing".</summary>
    public static string Describe(KeyCombo k)
    {
        if (k.Nothing) return T("Nothing");
        var parts = Each.Where(m => k.Mods.HasFlag(m)).Select(m => m.ToString()).ToList();
        parts.Add(Name(k.Vk, k.Ext, k.Scan));
        var text = string.Join(" + ", parts);
        return IsCopilot(k) ? F("Copilot key ({0})", text) : text;
    }

    /// <summary>The Copilot key: Windows' keyboards send Win + Shift + F23 for it.</summary>
    public static bool IsCopilot(KeyCombo k) => k.Vk == 0x86 && k.Mods == (Mods.Win | Mods.Shift);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetKeyNameText(int lParam, StringBuilder name, int size);
}
