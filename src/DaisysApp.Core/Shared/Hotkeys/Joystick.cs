using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// Windows' classic joystick API (winmm's joy* functions) and DirectInput's joystick configuration (joy.cpl's), declared
/// once for Core's controller buttons, Button2Key, Devices and Calibration (since Core 0.55: each had its own copy).
/// Every winmm call here is made under <see cref="ControllerButtons.JoystickApi"/>, every configuration call under
/// <see cref="ControllerButtons.DirectInput"/> (the caller's), as the rig's crashes taught (Core 0.38, 0.44).
/// </summary>
public static class Joystick
{
    static Joystick() => DirectInputFix.Apply();

    public const int Max = 16;
    public const int JOY_RETURNBUTTONS = 0x80, JOY_RETURNALL = 0xFF, JOY_RETURNPOVCTS = 0x200;
    public const int JOYCAPS_HASZ = 1, JOYCAPS_HASR = 2, JOYCAPS_HASU = 4, JOYCAPS_HASV = 8, JOYCAPS_HASPOV = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct JOYINFOEX
    {
        public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public uint dwButtons;
        public int dwButtonNumber, dwPOV, dwReserved1, dwReserved2;

        public static JOYINFOEX Asking(int flags) => new() { dwSize = Marshal.SizeOf<JOYINFOEX>(), dwFlags = flags };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct JOYCAPSW
    {
        public ushort wMid, wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax,
            wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }

    /// <summary>A controller's position and buttons; 0 when it answered.</summary>
    public static int GetPos(int id, ref JOYINFOEX info)
    {
        lock (ControllerButtons.JoystickApi) return joyGetPosEx(id, ref info);
    }

    /// <summary>A controller's capabilities; 0 when it answered.</summary>
    public static int Caps(int id, out JOYCAPSW caps)
    {
        lock (ControllerButtons.JoystickApi) return joyGetDevCapsW(id, out caps, Marshal.SizeOf<JOYCAPSW>());
    }

    /// <summary>Has Windows look at its controllers again (after a calibration or a renumbering).</summary>
    public static void ConfigChanged()
    {
        try { lock (ControllerButtons.JoystickApi) joyConfigChanged(0); } catch { }
    }

    /// <summary>The name Windows shows for a controller type ("VID_346E&amp;PID_0006"), as joy.cpl and "Set up USB game
    /// controllers" do; null if it has none.</summary>
    public static string? OemName(string type)
    {
        if (type.Length == 0) return null;
        string key = $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\{type}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = root.OpenSubKey(key);
                if (k?.GetValue("OEMName") is string s && s.Trim().Length > 0) return string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
            catch { }
        }
        return null;
    }

    public static string? OemName(int vid, int pid) => OemName($"VID_{vid:X4}&PID_{pid:X4}");

    [DllImport("winmm.dll")] private static extern int joyGetPosEx(int id, ref JOYINFOEX info);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int joyGetDevCapsW(nint id, out JOYCAPSW caps, int size);
    [DllImport("winmm.dll")] private static extern int joyConfigChanged(int flags);

    // ---------------------------------------------------------------- DirectInput's joystick configuration

    public const int ConfigSize = 1176, GuidOffset = 4, TypeOffset = 136, TypeChars = 256;
    public const uint DIJC_GUIDINSTANCE = 1, DIJC_REGHWCONFIGTYPE = 2;

    /// <summary>IDirectInputJoyConfig8 (no administrator rights needed): which number is which device, and setting it.</summary>
    [ComImport, Guid("EB0D7DFA-1990-4F27-B4D6-EDF2EEC4A44C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IJoyConfig
    {
        [PreserveSig] int Acquire();
        [PreserveSig] int Unacquire();
        [PreserveSig] int SetCooperativeLevel(IntPtr window, uint flags);
        [PreserveSig] int SendNotify();
        void EnumTypes();
        void GetTypeInfo();
        void SetTypeInfo();
        void DeleteType();
        [PreserveSig] int GetConfig(int id, [In, Out] byte[] config, uint flags);
        [PreserveSig] int SetConfig(int id, [In] byte[] config, uint flags);
    }

    /// <summary>The joystick configuration, or null (release it with Marshal.ReleaseComObject; under
    /// <see cref="ControllerButtons.DirectInput"/>).</summary>
    public static IJoyConfig? OpenConfig()
    {
        var iid = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");   // IID_IDirectInput8W
        try
        {
            if (DirectInput8Create(GetModuleHandleW(null), 0x0800, ref iid, out var raw, 0) != 0) return null;
            try { return (IJoyConfig)Marshal.GetObjectForIUnknown(raw); }
            finally { Marshal.Release(raw); }
        }
        catch (Exception ex)
        {
            Logging.Log.App.Warn($"Joystick configuration: {ex.Message}");
            return null;
        }
    }

    /// <summary>Number <paramref name="id"/>'s configuration (its DirectInput instance and type), or null if it has none.</summary>
    public static byte[]? Config(IJoyConfig joy, int id)
    {
        var config = new byte[ConfigSize];
        BitConverter.TryWriteBytes(config, ConfigSize);
        return joy.GetConfig(id, config, DIJC_GUIDINSTANCE | DIJC_REGHWCONFIGTYPE) == 0 ? config : null;
    }

    /// <summary>The DirectInput instance in a configuration.</summary>
    public static Guid InstanceIn(byte[] config) => new(config.AsSpan(GuidOffset, 16));

    /// <summary>The type ("VID_346E&amp;PID_0006") in a configuration.</summary>
    public static string TypeIn(byte[] config)
    {
        string type = Encoding.Unicode.GetString(config, TypeOffset, TypeChars * 2);
        int end = type.IndexOf('\0');
        return end >= 0 ? type[..end] : type;
    }

    [DllImport("dinput8.dll")] private static extern int DirectInput8Create(IntPtr instance, int version, ref Guid iid, out IntPtr result, IntPtr outer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
}
