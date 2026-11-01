namespace DaisysApp.Applets.Binder;

/// <summary>A binding as text: "Shift+T", "Mouse Left", "VKB Gladiator Button 4", "Warthog Hat 1 Up".</summary>
public static class Describe
{
    private static string MouseName(int code) => code switch
    {
        0 => T("Left"), 1 => T("Right"), 2 => T("Middle"), 3 => T("Side 1"), 4 => T("Side 2"),
        5 => T("Wheel Up"), 6 => T("Wheel Down"), 7 => T("Wheel Left"), 8 => T("Wheel Right"), _ => code.ToString(),
    };

    private static string HatDir(int dir) => (dir & 3) switch { 0 => T("Up"), 1 => T("Right"), 2 => T("Down"), _ => T("Left") };

    /// <param name="device">A controller's name, or null if it isn't known.</param>
    /// <param name="sdl">The title numbers axes as SDL does (Axis 1, 2…), not X, Y, Z.</param>
    public static string Text(Bind b, Func<DeviceId, string?> device, bool sdl = false)
    {
        string Dev() => b.Device is { } d ? device(d) ?? (d.Vid == 0 && d.Pid == 0 ? F("Joystick {0}", d.Nth) : d.ToString()) : "";
        return b.Kind switch
        {
            BindKind.None => "",
            BindKind.Key => Keys.Text(b.Code, b.Mods),
            BindKind.Mouse => F("Mouse {0}", MouseName(b.Code)),
            BindKind.MouseAxis => F("Mouse {0}", b.Code switch { 0 => "X", 1 => "Y", _ => T("Wheel") }) + Inv(b),
            BindKind.Button => F("{0} Button {1}", Dev(), b.Code + 1),
            BindKind.Hat => F("{0} Hat {1} {2}", Dev(), b.Hat + 1, HatDir(b.Code)),
            BindKind.Axis => $"{Dev()} {AxisName(b.Code, sdl)}" + (b.Half > 0 ? " +" : b.Half < 0 ? " −" : "") + Inv(b),
            _ => T("(the game's own)"),
        };
    }

    public static string AxisName(int axis, bool sdl) =>
        sdl ? F("Axis {0}", axis + 1) : axis switch
        {
            0 => "X", 1 => "Y", 2 => "Z", 3 => "Rx", 4 => "Ry", 5 => "Rz", 6 => T("Slider 1"), 7 => T("Slider 2"), _ => F("Axis {0}", axis + 1),
        };

    private static string Inv(Bind b) => b.Inverted ? " " + T("(inverted)") : "";
}
