using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// The vJoy driver (vjoystick.sourceforge.net 2.1.9.1, or the 2.2 builds): its feeder library, vJoyInterface.dll, loaded
/// from where vJoy is installed, and how a vJoy device is set up. The emulator writes the game's controller through it.
/// </summary>
internal static class VJoy
{
    public const uint X = 0x30, Y = 0x31, Z = 0x32, RX = 0x33, RY = 0x34, RZ = 0x35, SL0 = 0x36, SL1 = 0x37;
    /// <summary>The eight axes, in the order the emulator numbers them (0–7).</summary>
    public static readonly uint[] Axes = { X, Y, Z, RX, RY, RZ, SL0, SL1 };
    public static readonly string[] AxisNames = { "X", "Y", "Z", "Rx", "Ry", "Rz", "Slider 1", "Slider 2" };
    public const int MaxButtons = 128;

    public enum Status { Own = 0, Free = 1, Busy = 2, Missing = 3, Unknown = 4 }

    /// <summary>Where vJoy is installed (the folder with x64\vJoyInterface.dll), or null.</summary>
    public static string? InstallFolder { get; } = FindInstall();

    private static string? FindInstall()
    {
        var candidates = new List<string>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var k = uninstall.OpenSubKey(name);
                    if (k?.GetValue("DisplayName") is string dn && dn.Contains("vJoy", StringComparison.OrdinalIgnoreCase)
                        && k.GetValue("InstallLocation") is string loc && loc.Length > 0)
                        candidates.Add(loc);
                }
            }
            catch { }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "vJoy"));
        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "x64", "vJoyInterface.dll")));
    }

    /// <summary>vJoyConfig.exe (sets up devices from the command line), or null.</summary>
    public static string? ConfigTool => InstallFolder is { } f && File.Exists(Path.Combine(f, "x64", "vJoyConfig.exe")) ? Path.Combine(f, "x64", "vJoyConfig.exe") : null;

    /// <summary>vJoyConf.exe, the "Configure vJoy" window, or null.</summary>
    public static string? ConfigWindow => InstallFolder is { } f && File.Exists(Path.Combine(f, "x64", "vJoyConf.exe")) ? Path.Combine(f, "x64", "vJoyConf.exe") : null;

    private static bool resolverSet;
    private static bool? loaded;

    /// <summary>Whether vJoyInterface.dll could be loaded (vJoy is installed).</summary>
    public static bool Available
    {
        get
        {
            if (loaded is { } l) return l;
            if (InstallFolder == null) return (loaded = false).Value;
            if (!resolverSet)
            {
                resolverSet = true;
                NativeLibrary.SetDllImportResolver(typeof(VJoy).Assembly, (name, _, _) =>
                    name == Dll && InstallFolder is { } f && NativeLibrary.TryLoad(Path.Combine(f, "x64", Dll), out var h) ? h : IntPtr.Zero);
            }
            try { _ = vJoyEnabled(); loaded = true; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { loaded = false; }
            return loaded.Value;
        }
    }

    public static bool Enabled => Available && vJoyEnabled();

    /// <summary>The driver's version as text (e.g. 2.1.9), or null.</summary>
    public static string? Version
    {
        get
        {
            if (!Available) return null;
            short v = GetvJoyVersion();
            return v == 0 ? null : $"{(v >> 8) & 0xF}.{(v >> 4) & 0xF}.{v & 0xF}";
        }
    }

    /// <summary>How device <paramref name="id"/> is set up, read from the driver.</summary>
    public sealed record DeviceSetup(uint Id, Status Status, bool[] Axes, int Buttons, int DiscretePovs, int ContinuousPovs, bool Ffb, long AxisMin, long AxisMax);

    public static DeviceSetup? Read(uint id)
    {
        if (!Available || !vJoyEnabled()) return null;
        var status = (Status)GetVJDStatus(id);
        if (status == Status.Missing || !isVJDExists(id)) return new DeviceSetup(id, Status.Missing, new bool[8], 0, 0, 0, false, 0, 0);
        var axes = Axes.Select(a => GetVJDAxisExist(id, a)).ToArray();
        long min = 0, max = 0x8000;
        uint first = Axes.FirstOrDefault(a => GetVJDAxisExist(id, a));
        if (first != 0)
        {
            if (GetVJDAxisMin(id, first, out int mn)) min = mn;
            if (GetVJDAxisMax(id, first, out int mx)) max = mx;
        }
        bool ffb = false;
        try { ffb = IsDeviceFfb(id); } catch (EntryPointNotFoundException) { }
        return new DeviceSetup(id, status, axes, GetVJDButtonNumber(id), GetVJDDiscPovNumber(id), GetVJDContPovNumber(id), ffb, min, max);
    }

    /// <summary>One thing the emulator needs from the vJoy device, and whether it has it.</summary>
    public sealed record Check(string What, bool Ok, bool Required);

    /// <summary>
    /// What the emulator (like Forza EmuWheel) needs of the device: all eight axes, the buttons (128, so everything can be
    /// bound), one 4-direction POV hat for the d-pad and no continuous one, and force feedback switched on (Forza only
    /// sends force feedback to a device that has it). vJoy's own default device has 8 buttons and a continuous hat.
    /// </summary>
    public static List<Check> Checks(DeviceSetup? d, int buttonsNeeded)
    {
        var list = new List<Check>();
        if (d == null) { list.Add(new(T("vJoy is installed and switched on"), false, true)); return list; }
        list.Add(new(F("vJoy device {0} exists", d.Id), d.Status != Status.Missing, true));
        if (d.Status == Status.Missing) return list;
        list.Add(new(d.Status == Status.Busy ? F("vJoy device {0} is free (another program is feeding it)", d.Id) : F("vJoy device {0} is free", d.Id), d.Status != Status.Busy, true));
        int missing = d.Axes.Count(a => !a);
        list.Add(new(missing == 0 ? T("All 8 axes") : F("All 8 axes ({0} missing)", missing), missing == 0, true));
        list.Add(new(F("{0} buttons (has {1}; {2} are bound)", MaxButtons, d.Buttons, buttonsNeeded), d.Buttons >= Math.Max(buttonsNeeded, 1) && d.Buttons >= MaxButtons, d.Buttons < buttonsNeeded));
        list.Add(new(T("One 4-direction POV hat (for the d-pad), no continuous hats"), d.DiscretePovs >= 1 && d.ContinuousPovs == 0, false));
        list.Add(new(T("Force feedback switched on"), d.Ffb, false));
        list.Add(new(T("Registered with Windows as a racing wheel (EmuWheel's vjoy.reg), so Forza takes it for one"), GameInputRegistry.VJoyIsWheel(), true));
        return list;
    }

    /// <summary>vJoyConfig's arguments that set a device up as the emulator needs it.</summary>
    public static string FixArguments(uint id) =>
        $"{id} -f -a x y z rx ry rz sl0 sl1 -b {MaxButtons} -p 1 -s 0 -e Const Ramp Sqr Sine Tr StUp StDn Spr Dm Inr Fric";

    // ---------------------------------------------------------------- feeding

    public static bool Acquire(uint id) => AcquireVJD(id);
    public static void Relinquish(uint id) { try { RelinquishVJD(id); } catch { } }
    public static void Reset(uint id) { try { ResetVJD(id); } catch { } }
    public static bool SetAxisValue(uint id, uint axis, int value) => SetAxis(value, id, axis);
    public static bool SetButton(uint id, int button, bool down) => SetBtn(down, id, (byte)button);
    /// <summary>A 4-direction hat: -1 centred, 0 up, 1 right, 2 down, 3 left.</summary>
    public static bool SetDiscretePov(uint id, int pov, int value) => SetDiscPov(value, id, (byte)pov);
    /// <summary>A continuous hat: hundredths of a degree, or -1 centred.</summary>
    public static bool SetContinuousPov(uint id, int pov, int value) => SetContPov(value == -1 ? uint.MaxValue : (uint)value, id, (byte)pov);

    private const string Dll = "vJoyInterface.dll";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool vJoyEnabled();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern short GetvJoyVersion();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int GetVJDStatus(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool isVJDExists(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool AcquireVJD(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void RelinquishVJD(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool ResetVJD(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int GetVJDButtonNumber(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int GetVJDDiscPovNumber(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int GetVJDContPovNumber(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool GetVJDAxisExist(uint rID, uint axis);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool GetVJDAxisMax(uint rID, uint axis, out int max);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool GetVJDAxisMin(uint rID, uint axis, out int min);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool IsDeviceFfb(uint rID);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool SetAxis(int value, uint rID, uint axis);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool SetBtn(bool value, uint rID, byte nBtn);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool SetDiscPov(int value, uint rID, byte nPov);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern bool SetContPov(uint value, uint rID, byte nPov);
}
