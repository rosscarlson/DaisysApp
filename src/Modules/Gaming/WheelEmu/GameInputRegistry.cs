using System.IO;
using System.Text;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// What Windows tells games that read controllers through Windows.Gaming.Input / GameInput (Forza) about each device:
/// HKLM\SYSTEM\CurrentControlSet\Control\GameInput\Devices\&lt;VID&gt;&lt;PID&gt;&lt;usage page&gt;&lt;usage&gt;. EmuWheel uses it
/// twice, and so does WheelEmu:
/// <list type="bullet">
/// <item><b>Hush</b> (EmuWheel's Hush.exe): Mute gives each real device a <c>Disabled</c> = 1 there, so Forza (which reads
/// controllers this way) doesn't see it; other games, which read them through DirectInput, still do. Unmute removes it
/// (and the device's key if that was all it had). Nothing to install.</item>
/// <item><b>vJoy as a racing wheel</b> (EmuWheel's registry\vjoy.reg, which Hush imports): the vJoy device (1234:BEAD,
/// a joystick) described as a wheel, which axis and button is which, so Forza takes it for one. The emulator's layout
/// (<see cref="Roles"/>) is this one.</item>
/// </list>
/// Reading needs nothing; writing HKLM needs administrator, so it's done with reg.exe through <see cref="Elevated"/>.
/// </summary>
internal static class GameInputRegistry
{
    private const string Root = @"SYSTEM\CurrentControlSet\Control\GameInput\Devices";
    private const string HklmRoot = @"HKLM\" + Root;
    /// <summary>vJoy's devices (all of them share the ids), as a joystick.</summary>
    public const string VJoyKey = "1234BEAD00010004";

    /// <summary>A device's key name: VID, PID, usage page and usage, four hex digits each.</summary>
    public static string KeyFor(int vid, int pid, int usagePage, int usage) => $"{vid:X4}{pid:X4}{usagePage:X4}{usage:X4}";

    private static string Reg => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe");

    // ---------------------------------------------------------------- Hush

    /// <summary>Whether a device is muted (hidden from Forza).</summary>
    public static bool IsMuted(string key)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"{Root}\{key}");
            return k?.GetValue("Disabled") is int d && d != 0;
        }
        catch { return false; }
    }

    /// <summary>What mutes the devices; with <paramref name="registerVJoy"/>, also tells Windows vJoy is a racing wheel (Hush imports vjoy.reg each time too).</summary>
    public static List<(string Exe, string Args)> MuteCommands(IEnumerable<string> keys, bool registerVJoy)
    {
        var cmds = keys.Select(k => (Reg, $"add \"{HklmRoot}\\{k}\" /v Disabled /t REG_DWORD /d 1 /f")).ToList();
        if (registerVJoy) cmds.Add(RegisterVJoyCommand());
        return cmds;
    }

    /// <summary>Unmutes the devices: removes Disabled, and the device's key if nothing else is in it.</summary>
    public static Task<Elevated.Outcome> UnmuteAsync(IEnumerable<string> keys)
    {
        var cmds = new List<(string, string)>();
        foreach (var k in keys)
        {
            bool onlyDisabled = false;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{Root}\{k}");
                if (key == null) continue;
                onlyDisabled = key.SubKeyCount == 0 && key.GetValueNames().All(n => n.Equals("Disabled", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            cmds.Add(onlyDisabled ? (Reg, $"delete \"{HklmRoot}\\{k}\" /f") : (Reg, $"delete \"{HklmRoot}\\{k}\" /v Disabled /f"));
        }
        return Elevated.RunAsync(cmds);
    }

    // ---------------------------------------------------------------- vJoy as a racing wheel

    /// <summary>The racing wheel's layout: (subkey under the vJoy key, value, DWORD or string). As EmuWheel's vjoy.reg.</summary>
    private static readonly (string Key, string Name, object Value)[] Wheel = MakeWheel();

    private static (string, string, object)[] MakeWheel()
    {
        var w = new List<(string, string, object)>
        {
            ("", "Description", "Daisy's App WheelEmu (vJoy)"),
            (@"RacingWheel", "MaxWheelAngle", 450),
            (@"RacingWheel\Wheel", "AxisIndex", 0), (@"RacingWheel\Wheel", "Invert", 0),
            (@"RacingWheel\Throttle", "AxisIndex", 2), (@"RacingWheel\Throttle", "Invert", 1),
            (@"RacingWheel\Brake", "AxisIndex", 3), (@"RacingWheel\Brake", "Invert", 1),
            (@"RacingWheel\Clutch", "AxisIndex", 4), (@"RacingWheel\Clutch", "Invert", 1),
            (@"RacingWheel\Handbrake", "AxisIndex", 5), (@"RacingWheel\Handbrake", "Invert", 0),
            (@"RacingWheel\PreviousGear", "ButtonIndex", 16),
            (@"RacingWheel\NextGear", "ButtonIndex", 17),
            (@"RacingWheel\ReverseGear", "ButtonIndex", 18),
        };
        string[] gears = { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh" };
        for (int g = 0; g < 7; g++) w.Add(($@"RacingWheel\{gears[g]}Gear", "ButtonIndex", 19 + g));
        for (int b = 1; b <= 16; b++) w.Add(($@"RacingWheel\Button{b}", "ButtonIndex", b - 1));
        foreach (var (name, dir) in new[] { ("Up", "Up"), ("Down", "Down"), ("Left", "Left"), ("Right", "Right") })
        {
            w.Add(($@"RacingWheel\DPad{name}", "SwitchIndex", 0));
            w.Add(($@"RacingWheel\DPad{name}", "SwitchPosition", dir));
            w.Add(($@"RacingWheel\DPad{name}", "IncludeAdjacent", 1));
            w.Add(($@"UINavigation\Primary{name}", "SwitchIndex", 0));
            w.Add(($@"UINavigation\Primary{name}", "SwitchPosition", dir));
            w.Add(($@"UINavigation\Primary{name}", "IncludeAdjacent", 1));
        }
        w.Add((@"UINavigation\Menu", "ButtonIndex", 0));
        w.Add((@"UINavigation\View", "ButtonIndex", 1));
        w.Add((@"UINavigation\Accept", "ButtonIndex", 2));
        w.Add((@"UINavigation\Cancel", "ButtonIndex", 3));
        w.Add((@"Labels\Buttons", "Button0", "Menu"));
        w.Add((@"Labels\Buttons", "Button1", "View"));
        // (as vjoy.reg labels them)
        w.Add((@"Labels\Buttons", "Button3", "XboxA"));
        w.Add((@"Labels\Buttons", "Button4", "XboxB"));
        w.Add((@"Labels\Buttons", "Button5", "XboxX"));
        w.Add((@"Labels\Buttons", "Button6", "XboxY"));
        foreach (var dir in new[] { "Up", "Down", "Left", "Right" }) w.Add((@"Labels\Switches", $"Switch0{dir}", dir));
        return w.ToArray();
    }

    /// <summary>Whether Windows is told the vJoy device is a racing wheel with this layout (the axis and button numbers that matter).</summary>
    public static bool VJoyIsWheel()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey($@"{Root}\{VJoyKey}");
            if (root == null) return false;
            foreach (var (key, name, value) in Wheel)
            {
                if (value is not int want || name == "MaxWheelAngle" || key.StartsWith("Labels")) continue;
                using var k = key.Length == 0 ? root : root.OpenSubKey(key);
                if (k?.GetValue(name) is not int have || have != want) return false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Tells Windows the vJoy device is a racing wheel: writes the layout as a .reg file and imports it (as vjoy.reg is).</summary>
    public static (string Exe, string Args) RegisterVJoyCommand()
    {
        var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        foreach (var group in Wheel.GroupBy(w => w.Key))
        {
            sb.Append($"\r\n[HKEY_LOCAL_MACHINE\\{Root}\\{VJoyKey}{(group.Key.Length > 0 ? "\\" + group.Key : "")}]\r\n");
            foreach (var (_, name, value) in group)
                sb.Append(value is int i ? $"\"{name}\"=dword:{i:x8}\r\n" : $"\"{name}\"=\"{value}\"\r\n");
        }
        string dir = Path.Combine(Path.GetTempPath(), "DaisysApp");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "wheelemu-vjoy.reg");
        File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
        return (Reg, $"import \"{file}\"");
    }
}
