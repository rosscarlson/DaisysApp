using DaisysApp.Controllers;

namespace DaisysApp.Applets.GameDevices;

/// <summary>Pretend devices moving by themselves, one of each type, for trying the tab on a PC with none
/// (DAISYSAPP_FAKE_CONTROLLER=1).</summary>
internal static class FakeDevices
{
    public static bool On => Environment.GetEnvironmentVariable("DAISYSAPP_FAKE_CONTROLLER") == "1";

    private static readonly (string Name, int Pid, int Axes, int Buttons, int Povs)[] Table =
    {
        ("Test Pedals", 0xFA01, 3, 0, 0), ("Test Wheel", 0xFA02, 1, 24, 1), ("Test TPR Rudder", 0xFA03, 3, 0, 0), ("Test Xbox Controller", 0xFA04, 5, 10, 1),
        ("Test Stick", 0xFA05, 4, 12, 1), ("Test Throttle", 0xFA06, 3, 16, 0), ("Test G29 Driving", 0xFA07, 4, 24, 1), ("Test Shifter", 0xFA08, 0, 8, 0),
        ("Test Handbrake", 0xFA09, 1, 0, 0), ("Test Yoke", 0xFA0A, 3, 8, 1), ("Test HOTAS", 0xFA0B, 5, 20, 1), ("Test Button Box", 0xFA0C, 0, 32, 2),
        ("Test Sequential Shifter", 0xFA0D, 0, 2, 0),
    };

    public static List<ControllerInfo> List() => Table.Select((t, i) =>
        new ControllerInfo(new Guid($"6a1c0de0-0000-4000-8000-00000000{t.Pid:x4}"), Guid.Empty, t.Name, 0x1209, t.Pid, 1, 4, t.Buttons, t.Povs,
            Enumerable.Range(0, 8).Select(a => a < t.Axes).ToArray()) { JoystickId = i, ForceFeedback = i == 1 }).ToList();

    public static bool Is(ControllerInfo c) => c.Vid == 0x1209 && c.Pid is >= 0xFA01 and <= 0xFA0D;

    public static void Read(ControllerInfo c, ControllerState s)
    {
        double t = Environment.TickCount64 / 1000.0;
        for (int i = 0; i < 8; i++) s.Axes[i] = c.HasAxis[i] ? 0.5 + 0.5 * Math.Sin(t * (0.7 + 0.31 * i) + i) : double.NaN;
        int step = (int)(t / 1.5);
        for (int i = 0; i < c.Buttons; i++) s.Buttons[i] = i == step % c.Buttons && (int)(t / 0.75) % 2 == 0;
        if (c.Povs > 0) s.Povs[0] = step % 3 == 0 ? step % 8 * 4500 : -1;
    }
}
