using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// System-wide hotkeys via RegisterHotKey on a hidden message window. Shortcuts are strings in Resize Rabbit's format,
/// e.g. "Ctrl+Alt+K", "Ctrl+Shift+F1", "Alt+Left" — at least one of Ctrl/Alt/Shift plus one key.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource window;
    private readonly Dictionary<int, string> registered = new();
    private int nextId = 1;

    /// <summary>Raised on the UI thread with the shortcut string that was pressed.</summary>
    public event Action<string>? Pressed;

    public HotkeyManager(string name = "DaisysApp.Hotkeys")
    {
        // HWND_MESSAGE parent: a message-only window, enough to receive WM_HOTKEY
        window = new HwndSource(new HwndSourceParameters(name) { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        window.AddHook(WndProc);
    }

    /// <summary>Replaces every registration. Returns the shortcuts that couldn't be registered (in use elsewhere or invalid).</summary>
    public List<string> RegisterAll(IEnumerable<string> shortcuts)
    {
        UnregisterAll();
        var failed = new List<string>();
        foreach (string s in shortcuts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParse(s, out uint mods, out uint vk) || !RegisterHotKey(window.Handle, nextId, mods | MOD_NOREPEAT, vk))
            {
                failed.Add(s);
                continue;
            }
            registered[nextId++] = s;
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (int id in registered.Keys) UnregisterHotKey(window.Handle, id);
        registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && registered.TryGetValue(wParam.ToInt32(), out var shortcut))
        {
            handled = true;
            Pressed?.Invoke(shortcut);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        window.RemoveHook(WndProc);
        window.Dispose();
    }

    // ---------------------------------------------------------------- shortcut strings

    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["Escape"] = 0x1B,
        ["Return"] = 0x0D, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Backspace"] = 0x08, ["Delete"] = 0x2E, ["Home"] = 0x24,
        ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Insert"] = 0x2D,
    };

    public static bool TryParse(string shortcut, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        foreach (string m in parts[..^1])
        {
            if (m.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || m.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
            else if (m.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
            else if (m.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
            else return false;
        }
        string key = parts[^1];
        if (key.Length == 0 && shortcut.EndsWith("++")) key = "+"; // e.g. "Ctrl++"
        if (NamedKeys.TryGetValue(key, out vk)) return true;
        if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f') && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
            short scan = VkKeyScan(key[0]);
            if (scan != -1) { vk = (uint)(scan & 0xFF); return true; }
        }
        return false;
    }

    /// <summary>Builds a shortcut string from a key press, or null if it's only a modifier or has no Ctrl/Alt/Shift.</summary>
    public static string? FromKeyEvent(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None) return null;

        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (parts.Count == 0) return null;

        string? name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space => "Space", Key.Left => "Left", Key.Right => "Right", Key.Up => "Up", Key.Down => "Down",
            Key.Return => "Return", Key.Tab => "Tab", Key.Back => "Backspace", Key.Delete => "Delete",
            Key.Home => "Home", Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown", Key.Insert => "Insert",
            _ => null,
        };
        if (name == null)
        {
            // punctuation etc.: the character the key types
            uint ch = MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(key), 2 /* MAPVK_VK_TO_CHAR */) & 0x7FFF;
            if (ch == 0) return null;
            name = char.ToUpperInvariant((char)ch).ToString();
        }
        parts.Add(name);
        return string.Join("+", parts);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
}
