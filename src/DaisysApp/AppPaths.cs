using System.IO;

namespace DaisysApp;

/// <summary>Names and per-user folders shared by the shell and every tool.</summary>
public static class AppPaths
{
    public const string ShortName = "DaisysApp";
    public const string DisplayName = "Daisy's App";

    /// <summary>%APPDATA%\DaisysApp: one JSON file for the shell and one per tool.</summary>
    public static string SettingsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ShortName);

    /// <summary>Documents\Daisy's App: the default place for files the user saves (levels, delays).</summary>
    public static string DocumentsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DisplayName);

    /// <summary>%LOCALAPPDATA%\DaisysApp\logs</summary>
    public static string LogFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShortName, "logs");
}
