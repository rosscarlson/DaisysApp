using System.IO;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// Hush, as EmuWheel's: hides the real wheel, pedals, shifter and button boxes from the game, so it sees only the vJoy
/// controller and binding isn't confused by the same press arriving twice. Done with HidHide (Nefarius'
/// driver, which ViGEm and DS4Windows use): the devices are hidden from every program except the ones on its list, and
/// Daisy's App puts itself on that list first so it can still read them. Unmute shows them again.
/// </summary>
internal static class Hush
{
    public const string Download = "https://github.com/nefarius/HidHide/releases/latest";

    /// <summary>HidHideCLI.exe, or null if HidHide isn't installed.</summary>
    public static string? Cli { get; } = FindCli();

    private static string? FindCli()
    {
        var folders = new List<string>();
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
                    if (k?.GetValue("DisplayName") is string dn && dn.Contains("HidHide", StringComparison.OrdinalIgnoreCase)
                        && k.GetValue("InstallLocation") is string loc && loc.Length > 0)
                        folders.Add(loc);
                }
            }
            catch { }
        }
        try
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var d in Directory.EnumerateDirectories(pf, "Nefarius*"))
                folders.Add(Path.Combine(d, "HidHide"));
        }
        catch { }
        foreach (var f in folders)
            foreach (var cli in new[] { Path.Combine(f, "x64", "HidHideCLI.exe"), Path.Combine(f, "HidHideCLI.exe") })
                if (File.Exists(cli)) return cli;
        return null;
    }

    public static bool Installed => Cli != null;

    /// <summary>
    /// Hides the devices (HID instance IDs) from games: Daisy's App on HidHide's allowed list, each device hidden, hiding
    /// switched on. One permission prompt.
    /// </summary>
    public static Task<Elevated.Outcome> MuteAsync(IReadOnlyList<string> devices)
    {
        var cmds = new List<(string, string)> { (Cli!, $"--app-reg \"{Environment.ProcessPath}\"") };
        cmds.AddRange(devices.Select(d => (Cli!, $"--dev-hide \"{d}\"")));
        cmds.Add((Cli!, "--cloak-on"));
        return Elevated.RunAsync(cmds);
    }

    /// <summary>Shows the devices again (HidHide stays installed and Daisy's App on its list; other hidden devices stay hidden).</summary>
    public static Task<Elevated.Outcome> UnmuteAsync(IReadOnlyList<string> devices) =>
        Elevated.RunAsync(devices.Select(d => (Cli!, $"--dev-unhide \"{d}\"")).ToList());
}
