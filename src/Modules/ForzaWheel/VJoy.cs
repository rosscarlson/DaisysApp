using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// The vJoy driver (vjoystick.sourceforge.net 2.1.9.1, or the 2.2 builds): its feeder library, vJoyInterface.dll, loaded
/// from where vJoy is installed, and how a vJoy device is set up. The emulator writes the game's controller through it.
/// </summary>
internal static class VJoy
{
    public const uint X = 0x30, Y = 0x31, Z = 0x32, RX = 0x33, RY = 0x34, RZ = 0x35, SL0 = 0x36, SL1 = 0x37;
    /// <summary>The eight axes, in the order the emulator numbers them (0–7).</summary>
    public static readonly uint[] Axes = { X, Y, Z, RX, RY, RZ, SL0, SL1 };
    public static readonly string[] AxisNames = InputRef.AxisNames;
    public const int MaxButtons = GameControllers.MaxButtons;

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

    // ---------------------------------------------------------------- force feedback from the game (as EmuWheel reads it)

    /// <summary>What a force-feedback packet from the game is (vJoy's FFBPType).</summary>
    public enum FfbPacket { EffectReport = 1, Envelope = 2, Condition = 3, Periodic = 4, Constant = 5, Ramp = 6, Custom = 7, Sample = 8,
        EffectOperation = 10, BlockFree = 11, Control = 12, Gain = 13, SetCustom = 14, NewEffect = 17, BlockLoad = 18, Pool = 19 }

    /// <summary>The kind of effect (vJoy's FFBEType).</summary>
    public enum FfbEffect { None, Constant, Ramp, Square, Sine, Triangle, SawUp, SawDown, Spring, Damper, Inertia, Friction, Custom }

    public enum FfbOperation { Start = 1, Solo = 2, Stop = 3 }
    public enum FfbControl { EnableActuators = 1, DisableActuators = 2, StopAll = 3, Reset = 4, Pause = 5, Continue = 6 }

    // the layouts vJoy 2.1.9 hands them over in, exactly as vJoyInterfaceWrap (which EmuWheel uses) declares them.
    // The signed values (a force's magnitude, a condition's offset and coefficients, a wave's offset) are 16-bit numbers
    // that vJoy puts in 32-bit fields without carrying the sign into the top half, so they must be read as 16 bits:
    // read as 32 (0.59–0.60), a small push one way (-100, 0xFF9C) became +65436, full force the other way, and the
    // wheel was thrown from side to side.
    [StructLayout(LayoutKind.Explicit)]
    public struct EffectReport
    {
        [FieldOffset(0)] public byte BlockIndex;
        [FieldOffset(4)] public FfbEffect Type;
        [FieldOffset(8)] public ushort Duration;
        [FieldOffset(10)] public ushort TriggerRepeat;
        [FieldOffset(12)] public ushort SamplePeriod;
        [FieldOffset(14)] public byte Gain;
        [FieldOffset(15)] public byte TriggerButton;
        [FieldOffset(16)] public int Polar;
        [FieldOffset(20)] public byte Direction;
        [FieldOffset(21)] public byte DirY;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ConstantReport
    {
        [FieldOffset(0)] public byte BlockIndex;
        [FieldOffset(4)] public short Magnitude;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PeriodicReport
    {
        [FieldOffset(0)] public byte BlockIndex;
        [FieldOffset(4)] public uint Magnitude;
        [FieldOffset(8)] public short Offset;
        [FieldOffset(12)] public uint Phase;
        [FieldOffset(16)] public uint Period;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ConditionReport
    {
        [FieldOffset(0)] public byte BlockIndex;
        [FieldOffset(4)] public int IsY;
        [FieldOffset(8)] public short CenterPointOffset;
        [FieldOffset(12)] public short PosCoeff;
        [FieldOffset(16)] public short NegCoeff;
        [FieldOffset(20)] public uint PosSatur;
        [FieldOffset(24)] public uint NegSatur;
        [FieldOffset(28)] public int DeadBand;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct OperationReport
    {
        [FieldOffset(0)] public byte BlockIndex;
        [FieldOffset(4)] public FfbOperation Operation;
        [FieldOffset(8)] public byte LoopCount;
    }

    /// <summary>Called by vJoy (on its own thread) with each force-feedback packet the game sends to a vJoy device.</summary>
    public delegate void FfbCallback(IntPtr packet, IntPtr userData);

    private static FfbCallback? ffbCallback; // kept alive: vJoy holds a pointer to it

    /// <summary>Starts receiving the game's force feedback (one handler at a time: a new one replaces the last).</summary>
    public static void OnForceFeedback(FfbCallback callback)
    {
        ffbCallback = callback;
        FfbRegisterGenCB(callback, IntPtr.Zero);
    }

    public static bool TryType(IntPtr p, out FfbPacket type) { int t = 0; bool ok = Ffb_h_Type(p, ref t) == 0; type = (FfbPacket)t; return ok; }
    public static bool TryEffect(IntPtr p, out EffectReport r) { r = default; return Ffb_h_Eff_Report(p, ref r) == 0; }
    public static bool TryConstant(IntPtr p, out ConstantReport r) { r = default; return Ffb_h_Eff_Constant(p, ref r) == 0; }
    public static bool TryPeriodic(IntPtr p, out PeriodicReport r) { r = default; return Ffb_h_Eff_Period(p, ref r) == 0; }
    public static bool TryCondition(IntPtr p, out ConditionReport r) { r = default; return Ffb_h_Eff_Cond(p, ref r) == 0; }
    public static bool TryOperation(IntPtr p, out OperationReport r) { r = default; return Ffb_h_EffOp(p, ref r) == 0; }
    public static bool TryControl(IntPtr p, out FfbControl c) { int v = 0; bool ok = Ffb_h_DevCtrl(p, ref v) == 0; c = (FfbControl)v; return ok; }
    public static bool TryGain(IntPtr p, out byte g) { g = 0; return Ffb_h_DevGain(p, ref g) == 0; }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FfbRegisterGenCB(FfbCallback cb, IntPtr data);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_Type(IntPtr packet, ref int type);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_Eff_Report(IntPtr packet, ref EffectReport effect);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_Eff_Constant(IntPtr packet, ref ConstantReport effect);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_Eff_Period(IntPtr packet, ref PeriodicReport effect);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_Eff_Cond(IntPtr packet, ref ConditionReport effect);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_EffOp(IntPtr packet, ref OperationReport op);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_DevCtrl(IntPtr packet, ref int control);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint Ffb_h_DevGain(IntPtr packet, ref byte gain);

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
