using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using DaisysApp.Shared.Hotkeys;
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

    private const int Numbers = Joystick.Max;
    private const uint SetType = Joystick.DIJC_REGHWCONFIGTYPE;
    private const uint Exclusive = 1, Background = 8;

    /// <summary>Asks DirectInput for each number. False if it wouldn't let us try at all.</summary>
    public static bool Assign(IEnumerable<Move> moves, IntPtr window)
    {
        var joy = Joystick.OpenConfig();
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
        Joystick.ConfigChanged();
        if (Joystick.OpenConfig() is not { } joy) return;
        Find(joy, Guid.Empty);
        Marshal.ReleaseComObject(joy);
    }

    private static (int Id, byte[]? Config) Find(Joystick.IJoyConfig joy, Guid instance)
    {
        for (int id = 0; id < Numbers; id++)
            if (Joystick.Config(joy, id) is { } config && Joystick.InstanceIn(config) == instance) return (id, config);
        return (-1, null);
    }

    /// <summary>A device's interface path as the instance id Windows' device tree knows it by.</summary>
    public static string InstanceId(string path)
    {
        string s = path.StartsWith(@"\\?\") ? path[4..] : path;
        int end = s.LastIndexOf("#{", StringComparison.Ordinal);
        return (end > 0 ? s[..end] : s).Replace('#', '\\');
    }
}

/// <summary>
/// As administrator, the way plugging devices in one at a time would: the devices that have to move are switched off,
/// the numbers kept for them set, and they're switched on lowest first, each taking the lowest number free. Not lasting:
/// a device left off comes back with a replug or a restart. Arguments: the file to write what went wrong to, "force"
/// (the programs keeping a device on are closed, "Closed: name (id)" lines) or "ask", then instance|path|number|name, a
/// device each.
/// </summary>
public sealed class ReorderTask : IModuleTask
{
    public const int Done = 0, Refused = 2, NotBack = 3;

    private const string Keys = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\DirectInput";
    private const int NoUi = 4, Absolute = 1;
    private const int Settle = 1500;

    public int Run(string[] args)
    {
        string report = args[0];
        bool force = args[1] == "force";
        var lines = new List<string>();
        var moves = args.Skip(2).Select(a => a.Split('|', 4)).Select(p => (Move: new JoyOrder.Move(Guid.Parse(p[0]), p[1], int.Parse(p[2])), Name: p.Length > 3 ? p[3] : p[1]))
            .Where(m => m.Move.Path.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) && m.Move.Id is >= 0 and < 16).OrderBy(m => m.Move.Id).ToList();
        var off = new List<(string Name, int Node)>();
        var since = DateTime.Now.AddSeconds(-1);
        int result = Done;
        try
        {
            // every one found before any is switched off
            var nodes = new List<int>();
            foreach (var (m, name) in moves)
            {
                int cr = CM_Locate_DevNodeW(out int node, m.Path, 0);
                if (cr != 0)
                {
                    lines.Add($"{name}: not found ({m.Path}, CR {cr:X})");
                    return result = Refused;
                }
                nodes.Add(node);
            }
            for (int i = 0; i < moves.Count; i++)
            {
                var (m, name) = moves[i];
                int cr = Disable(nodes[i]);
                // forced: the programs Windows says keep it on are closed, and it's tried again
                for (int round = 0; cr == VetoedCode && force && round < 3; round++)
                {
                    var tried = DateTime.Now;
                    if (Close(Vetoes(since), lines) == 0) break;
                    Thread.Sleep(500);
                    since = tried;
                    cr = Disable(nodes[i]);
                }
                if (cr != 0)
                {
                    lines.Add($"{name}: refused (CR {cr:X}{(cr == VetoedCode ? ", vetoed" : "")})");
                    return result = Refused;
                }
                lines.Add($"{name}: off");
                off.Add((name, nodes[i]));
            }
            Thread.Sleep(Settle);
            foreach (var (m, _) in moves) Keep(m);
            JoyOrder.Look();
            return Done;
        }
        catch (Exception ex)
        {
            lines.Add(ex.Message);
            throw;
        }
        finally
        {
            foreach (var (name, node) in off)
            {
                int cr = CM_Enable_DevNode(node, 0);
                if (cr != 0) lines.Add($"{name}: not switched back on (CR {cr:X})");
                Thread.Sleep(Settle);
                JoyOrder.Look();
            }
            if (result == Refused) lines.AddRange(Vetoes(since));
            try { File.WriteAllLines(report, lines); } catch { }
        }
    }

    private const int VetoedCode = 0x17;   // CR_REMOVE_VETOED

    private static int Disable(int node)
    {
        int cr = CM_Disable_DevNode(node, NoUi);
        return cr == 0 ? 0 : CM_Disable_DevNode(node, NoUi | Absolute);
    }

    /// <summary>Windows' own and Daisy's App are never closed.</summary>
    private static readonly HashSet<string> Kept = new(StringComparer.OrdinalIgnoreCase)
        { "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer", "fontdrvhost", "Registry", "audiodg" };

    /// <summary>Closes the programs in these "Vetoed:" lines; how many were.</summary>
    private static int Close(IEnumerable<string> vetoes, List<string> lines)
    {
        int n = 0;
        string me = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        foreach (var id in vetoes.Select(v => System.Text.RegularExpressions.Regex.Match(v, @"process id (\d+)")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Distinct())
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(id);
                string name = p.ProcessName;
                if (id == Environment.ProcessId || id <= 4 || Kept.Contains(name) || name.Equals(me, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("DaisysApp", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add($"Not closed: {name} ({id})");
                    continue;
                }
                p.Kill();
                p.WaitForExit(3000);
                lines.Add($"Closed: {name} ({id})");
                n++;
            }
            catch (Exception ex) { lines.Add($"Couldn't close {id}: {ex.Message}"); }
        }
        return n;
    }

    /// <summary>The programs Windows says stopped a device being switched off since then (Kernel-PnP's event 225).</summary>
    private static IEnumerable<string> Vetoes(DateTime since)
    {
        var found = new List<string>();
        try
        {
            string query = $"*[System[Provider[@Name='Microsoft-Windows-Kernel-PnP'] and (EventID=225) and TimeCreated[@SystemTime>='{since.ToUniversalTime():o}']]]";
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(new System.Diagnostics.Eventing.Reader.EventLogQuery("System", System.Diagnostics.Eventing.Reader.PathType.LogName, query));
            for (var e = reader.ReadEvent(); e != null && found.Count < 10; e = reader.ReadEvent())
                using (e)
                {
                    string text = "";
                    try { text = e.FormatDescription() ?? ""; } catch { }
                    if (text.Length == 0) text = string.Join(", ", e.Properties.Select(p => p.Value?.ToString()));
                    found.Add("Vetoed: " + text.Trim());
                }
        }
        catch (Exception ex) { found.Add("Couldn't read the vetoes: " + ex.Message); }
        return found;
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
