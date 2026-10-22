using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>
/// The Notepad as an app for text files, in Windows (this user's, HKCU): a ProgID that starts the app with
/// "--open file", listed for each type (OpenWithProgids) and as a registered app with those types (Capabilities). Windows
/// doesn't let a program make itself the default (the choice is signed, and guarded by its UCPD driver), so the
/// choice is the user's, in Windows' Default apps, opened at the app's page; and putting back what was there before is
/// Windows' Open with, for each type.
/// </summary>
internal static class DefaultApp
{
    /// <summary>The types offered, and the ones ticked at first.</summary>
    public static readonly string[] Types = [".txt", ".log", ".md", ".ini", ".cfg", ".conf", ".nfo", ".json", ".xml", ".yaml", ".yml", ".csv"];
    public static readonly string[] Usual = [".txt", ".log", ".md", ".ini", ".cfg", ".conf", ".nfo"];

    private static string ProgId => AppPaths.ShortName + ".Notepad";
    /// <summary>The name in Windows' lists of apps.</summary>
    public static string AppName => AppPaths.DisplayName + " Notepad";
    private static string CapabilitiesKey => $@"Software\{AppPaths.ShortName}\Notepad\Capabilities";
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\";

    public static bool Registered
    {
        get { using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command"); return k != null; }
    }

    /// <summary>Listed for these types (the ones not in the list no longer).</summary>
    public static void Register(IReadOnlyCollection<string> types)
    {
        Unregister(notify: false);
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppPaths.ShortName + ".exe");
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue("", F("Text file ({0})", AppPaths.DisplayName));
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using (var app = prog.CreateSubKey("Application")) app.SetValue("ApplicationName", AppName);
            using var open = prog.CreateSubKey(@"shell\open");
            open.SetValue("FriendlyAppName", AppName);
            using var command = open.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" {Shell.OpenFiles.Argument} \"%1\"");
        }
        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", T("Notes in tabs, shared between your PCs"));
            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (var t in types) assoc.SetValue(t, ProgId);
        }
        using (var apps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications")) apps.SetValue(AppName, CapabilitiesKey);
        foreach (var t in types)
        {
            using var with = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{t}\OpenWithProgids");
            with.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        Notify();
        Logging.Log.Here.Info($"Notepad: listed as an app for {string.Join(" ", types)}");
    }

    /// <summary>Everything <see cref="Register"/> wrote taken away.</summary>
    public static void Unregister(bool notify = true)
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\{AppPaths.ShortName}\Notepad", false);
        using (var apps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true)) apps?.DeleteValue(AppName, false);
        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes"))
            foreach (var t in Types)
            {
                using var with = classes?.OpenSubKey($@"{t}\OpenWithProgids", true);
                with?.DeleteValue(ProgId, false);
            }
        if (notify)
        {
            Notify();
            Logging.Log.Here.Info("Notepad: no longer listed as an app for text files");
        }
    }

    /// <summary>Windows' Default apps, at this app's page.</summary>
    public static void OpenWindowsSettings() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName)) { UseShellExecute = true });

    /// <summary>The ProgID Windows uses for the type (the user's choice), or "".</summary>
    public static string ChoiceOf(string type)
    {
        using (var latest = Registry.CurrentUser.OpenSubKey(Explorer + type + @"\UserChoiceLatest\ProgId"))
            if (latest?.GetValue("ProgId") is string p && p.Length > 0) return p;
        using var choice = Registry.CurrentUser.OpenSubKey(Explorer + type + @"\UserChoice");
        return choice?.GetValue("ProgId") as string ?? "";
    }

    public static bool IsOurs(string type) => ChoiceOf(type).Equals(ProgId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name of the app that opens the type now ("" if none).</summary>
    public static string AppOf(string type)
    {
        uint size = 512;
        var sb = new StringBuilder((int)size);
        return AssocQueryString(AssocInitIgnoreUnknown, AssocFriendlyAppName, type, null, sb, ref size) == 0 ? sb.ToString() : "";
    }

    /// <summary>The app's name, or else the program or ProgID the choice names (one Windows can't find any more has no name).</summary>
    public static string NameOf(string type)
    {
        if (AppOf(type) is { Length: > 0 } name) return name;
        string choice = ChoiceOf(type);
        return choice.StartsWith(@"Applications\", StringComparison.OrdinalIgnoreCase) ? choice[13..] : choice;
    }

    /// <summary>Windows' Open with for the type ("Always" makes the app picked its default).</summary>
    public static void OpenWith(string type, IntPtr owner)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), AppPaths.ShortName + "-Types")).FullName;
        string file = Path.Combine(folder, "example" + type);
        if (!File.Exists(file)) File.WriteAllText(file, "");
        var info = new OpenAsInfo { File = file, Flags = 0x1 | 0x2 }; // OAIF_ALLOW_REGISTRATION | OAIF_REGISTER_EXT
        SHOpenWithDialog(owner, ref info);
    }

    private static void Notify() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED

    private const int AssocInitIgnoreUnknown = 0x400, AssocFriendlyAppName = 4;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(int flags, int str, string assoc, string? extra, StringBuilder? output, ref uint size);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        public string File;
        public string? Class;
        public int Flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);
}
