using Microsoft.Win32;

namespace DaisysApp.Settings;

/// <summary>Per-user "start at sign-in" via HKCU\...\Run, requiring no elevation.</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = AppPaths.ShortName;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>Adds or removes the Run entry. <paramref name="hidden"/> starts the app in the tray (--tray).</summary>
    public static void Set(bool enabled, bool hidden)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the running executable's path.");
            key.SetValue(ValueName, $"\"{exe}\"" + (hidden ? " --tray" : ""), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
