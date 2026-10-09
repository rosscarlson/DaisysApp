using System.Runtime.InteropServices;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// Xbox-style pads through XInput, numbered as JoyToKey numbers them: A B X Y LB RB View Menu, the stick clicks, then the
/// triggers as buttons 11 and 12 and the Xbox button as 13; axes 1–2 the left stick, 3–4 the right one (up and left
/// negative), 5–6 the triggers.
/// </summary>
internal static class XInput
{
    private const int TriggerButton = 30; // of 255: how far a trigger goes before it's "button" 11 / 12

    private static bool? hasEx;

    public static bool TryGet(int slot, out XINPUT_STATE state)
    {
        state = default;
        try
        {
            if (hasEx != false)
            {
                try
                {
                    // the undocumented one also reports the Xbox button
                    int r = XInputGetStateEx(slot, out state);
                    hasEx = true;
                    return r == 0;
                }
                catch (EntryPointNotFoundException) { hasEx = false; }
            }
            return XInputGetState(slot, out state) == 0;
        }
        catch (DllNotFoundException) { return false; }
    }

    public static bool Read(int slot, out JoyState s)
    {
        s = default;
        if (!TryGet(slot, out var x)) return false;
        var g = x.Gamepad;
        s.X = Stick(g.sThumbLX);
        s.Y = -Stick(g.sThumbLY);
        s.Z = Stick(g.sThumbRX);
        s.R = -Stick(g.sThumbRY);
        s.U = g.bLeftTrigger / 255f;
        s.V = g.bRightTrigger / 255f;
        uint b = 0;
        void Bit(bool on, int n) { if (on) b |= 1u << (n - 1); }
        ushort w = g.wButtons;
        Bit((w & 0x1000) != 0, 1);
        Bit((w & 0x2000) != 0, 2);
        Bit((w & 0x4000) != 0, 3);
        Bit((w & 0x8000) != 0, 4);
        Bit((w & 0x0100) != 0, 5);
        Bit((w & 0x0200) != 0, 6);
        Bit((w & 0x0020) != 0, 7);
        Bit((w & 0x0010) != 0, 8);
        Bit((w & 0x0040) != 0, 9);
        Bit((w & 0x0080) != 0, 10);
        Bit(g.bLeftTrigger > TriggerButton, 11);
        Bit(g.bRightTrigger > TriggerButton, 12);
        Bit((w & 0x0400) != 0, 13);
        s.Buttons = b;

        bool up = (w & 1) != 0, down = (w & 2) != 0, left = (w & 4) != 0, right = (w & 8) != 0;
        s.Pov = (up, down, left, right) switch
        {
            (true, false, false, false) => 0,
            (true, false, false, true) => 45,
            (false, false, false, true) => 90,
            (false, true, false, true) => 135,
            (false, true, false, false) => 180,
            (false, true, true, false) => 225,
            (false, false, true, false) => 270,
            (true, false, true, false) => 315,
            _ => -1,
        };
        return true;
    }

    private static float Stick(short v) => Math.Clamp(v / 32767f, -1, 1);

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll")] private static extern int XInputGetState(int slot, out XINPUT_STATE state);
    [DllImport("xinput1_4.dll", EntryPoint = "#100")] private static extern int XInputGetStateEx(int slot, out XINPUT_STATE state);
}
