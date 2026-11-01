using System.Runtime.InteropServices;
using Microsoft.Win32;
using DaisysApp.Shell;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// Giving devices the joystick numbers asked for. Windows has no sure way: <see cref="Assign"/> is DirectInput's own
/// (IDirectInputJoyConfig8, no administrator rights), and <see cref="ReorderTask"/> what's left when that's ignored. The
/// caller reads the numbers afterwards: only that says it worked.
/// </summary>
internal static class JoyOrder
{
    /// <summary>A device and the number (from 0) it's to have.</summary>
    internal sealed record Move(Guid Instance, string Path, int Id);

    private const int Numbers = 16, ConfigSize = 1176, GuidOffset = 4;
    private const uint GetInstanceAndType = 1 | 2, SetType = 2;   // DIJC_GUIDINSTANCE, DIJC_REGHWCONFIGTYPE
    private const uint Exclusive = 1, Background = 8;

    /// <summary>Asks DirectInput for each number. False if it wouldn't let us try at all.</summary>
    public static bool Assign(IEnumerable<Move> moves, IntPtr window)
    {
        var joy = Open();
        if (joy == null) return false;
        try
        {
            int hr = joy.SetCooperativeLevel(window, Exclusive | Background);
            if (hr == 0) hr = joy.Acquire();
            if (hr != 0)
            {
                Logging.Log.Here.Warn($"Joystick configuration: {hr:X8}");
                return false;
            }
            foreach (var m in moves)
            {
                var (from, config) = Find(joy, m.Instance);
                if (from < 0 || from == m.Id) continue;
                hr = joy.SetConfig(m.Id, config!, SetType);
                Logging.Log.Here.Info($"SetConfig {from} → {m.Id}: {hr:X8}");
            }
            joy.SendNotify();
            joy.Unacquire();
            return true;
        }
        finally { Marshal.ReleaseComObject(joy); }
    }

    /// <summary>Has Windows look at the devices again: numbers are given out when something asks.</summary>
    public static void Look()
    {
        try { joyConfigChanged(0); } catch { }
        if (Open() is not { } joy) return;
        Find(joy, Guid.Empty);
        Marshal.ReleaseComObject(joy);
    }

    private static (int Id, byte[]? Config) Find(IJoyConfig joy, Guid instance)
    {
        for (int id = 0; id < Numbers; id++)
        {
            var config = new byte[ConfigSize];
            BitConverter.TryWriteBytes(config, ConfigSize);
            if (joy.GetConfig(id, config, GetInstanceAndType) == 0 && new Guid(config.AsSpan(GuidOffset, 16)) == instance) return (id, config);
        }
        return (-1, null);
    }

    private static IJoyConfig? Open()
    {
        var input = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");   // IID_IDirectInput8W
        try
        {
            if (DirectInput8Create(GetModuleHandleW(null), 0x0800, ref input, out var raw, 0) != 0) return null;
            try { return (IJoyConfig)Marshal.GetObjectForIUnknown(raw); }
            finally { Marshal.Release(raw); }
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Joystick configuration: {ex.Message}");
            return null;
        }
    }

    /// <summary>A device's interface path as the instance id Windows' device tree knows it by.</summary>
    public static string InstanceId(string path)
    {
        string s = path.StartsWith(@"\\?\") ? path[4..] : path;
        int end = s.LastIndexOf("#{", StringComparison.Ordinal);
        return (end > 0 ? s[..end] : s).Replace('#', '\\');
    }

    [ComImport, Guid("EB0D7DFA-1990-4F27-B4D6-EDF2EEC4A44C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IJoyConfig
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

    [DllImport("dinput8.dll")] private static extern int DirectInput8Create(IntPtr instance, int version, ref Guid iid, out IntPtr result, IntPtr outer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("winmm.dll")] private static extern int joyConfigChanged(int flags);
}

/// <summary>
/// As administrator, the way plugging devices in one at a time would: the devices that have to move are switched off,
/// the numbers kept for them set, and they're switched on lowest first, each taking the lowest number free. Not lasting:
/// a device left off comes back with a replug or a restart. Arguments: instance|path|number, a device each.
/// </summary>
public sealed class ReorderTask : IModuleTask
{
    public const int Done = 0, Refused = 2, NotBack = 3;

    private const string Keys = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\DirectInput";
    private const int NoUi = 4, Absolute = 1;
    private const int Settle = 1500;

    public int Run(string[] args)
    {
        var moves = args.Select(a => a.Split('|')).Select(p => new JoyOrder.Move(Guid.Parse(p[0]), p[1], int.Parse(p[2])))
            .Where(m => m.Path.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) && m.Id is >= 0 and < 16).OrderBy(m => m.Id).ToList();
        var off = new List<(JoyOrder.Move Move, int Node)>();
        try
        {
            foreach (var m in moves)
            {
                if (CM_Locate_DevNodeW(out int node, m.Path, 0) != 0) return Refused;
                if (CM_Disable_DevNode(node, NoUi) != 0 && CM_Disable_DevNode(node, NoUi | Absolute) != 0) return Refused;
                off.Add((m, node));
            }
            Thread.Sleep(Settle);
            foreach (var m in moves) Keep(m);
            JoyOrder.Look();
            return Done;
        }
        finally
        {
            foreach (var (_, node) in off)
            {
                CM_Enable_DevNode(node, 0);
                Thread.Sleep(Settle);
                JoyOrder.Look();
            }
        }
    }

    /// <summary>The number kept for the device: on the key whose GUID value is its instance.</summary>
    private static void Keep(JoyOrder.Move m)
    {
        using var root = Registry.CurrentUser.OpenSubKey(Keys);
        if (root == null) return;
        foreach (var product in root.GetSubKeyNames())
        {
            using var calibration = root.OpenSubKey(product + @"\Calibration");
            if (calibration == null) continue;
            foreach (var n in calibration.GetSubKeyNames())
            {
                using var key = calibration.OpenSubKey(n, writable: true);
                if (key?.GetValue("GUID") is byte[] { Length: 16 } guid && new Guid(guid) == m.Instance)
                    key.SetValue("Joystick Id", BitConverter.GetBytes(m.Id), RegistryValueKind.Binary);
            }
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out int node, string id, int flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Disable_DevNode(int node, int flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Enable_DevNode(int node, int flags);
}
