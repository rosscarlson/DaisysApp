using System.IO;

namespace DaisysApp;

/// <summary>
/// The app's name and per-user folders, shared by the shell and every tool. The same program is Daisy's App
/// (DaisysApp.exe, with every module) and each tool's own app (e.g. Joy2Key.exe, with just that module): the name comes
/// from the program's file name, so each has its own settings, logs, tray icon and updates.
/// </summary>
public static class AppPaths
{
    /// <summary>The full app's program name (DaisysApp.exe).</summary>
    public const string DaisysAppName = "DaisysApp";

    /// <summary>The program's file name without .exe: "DaisysApp", or a one-tool app's ("Joy2Key"). Names the settings
    /// and log folders, the single-instance lock, the sign-in entry, the update tasks and the release's installer.</summary>
    public static string ShortName { get; } = ProgramName();

    /// <summary>The name people see: "Daisy's App", or a one-tool app's own ("Joy2Key").</summary>
    public static string DisplayName { get; } = IsDaisysApp ? "Daisy's App" : ShortName;

    /// <summary>True for Daisy's App itself, false for a one-tool app.</summary>
    public static bool IsDaisysApp => ShortName.Equals(DaisysAppName, StringComparison.OrdinalIgnoreCase);

    private static string ProgramName()
    {
        string? name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        // (run by dotnet, or in a designer: Daisy's App)
        return string.IsNullOrWhiteSpace(name) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) || name.StartsWith("XDesProc", StringComparison.OrdinalIgnoreCase)
            ? DaisysAppName : name;
    }

    /// <summary>%APPDATA%\DaisysApp (or the one-tool app's name): one JSON file for the shell and one per tool.</summary>
    public static string SettingsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ShortName);

    /// <summary>Documents\Daisy's App: the default place for files the user saves (levels, delays).</summary>
    public static string DocumentsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DisplayName);

    /// <summary>%LOCALAPPDATA%\DaisysApp\logs</summary>
    public static string LogFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShortName, "logs");
}
