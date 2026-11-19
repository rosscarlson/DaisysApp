using System.IO;

namespace DaisysApp;

/// <summary>
/// The app's name and per-user folders, shared by the shell and every tool. The same program is Daisy's App
/// (DaisysApp.exe, with every module) and the wrapper (Wrapper.exe, which downloads the modules you pick): the name
/// comes from the program's file name, so each has its own settings, logs, tray icon and updates.
/// </summary>
public static class AppPaths
{
    /// <summary>The full app's program name (DaisysApp.exe).</summary>
    public const string DaisysAppName = "DaisysApp";

    /// <summary>The program's file name without .exe: "DaisysApp", or the wrapper's ("Wrapper"). Names the settings
    /// and log folders, the single-instance lock, the sign-in entry, the update tasks and the release's installer.</summary>
    public static string ShortName { get; } = ProgramName();

    /// <summary>
    /// An app of its own made from this program with some modules (build.ps1 -Product, from products\&lt;Name&gt;):
    /// app.json beside the program gives its title and its releases' tag prefix (SimEffects Analysis: "sea-v"). Null
    /// for Daisy's App and the wrapper.
    /// </summary>
    public static ProductInfo? Product { get; } = ReadProduct();

    /// <summary>The name people see: "Daisy's App", a product's title, or the wrapper's own name ("Wrapper").</summary>
    public static string DisplayName { get; } = IsDaisysApp ? "Daisy's App" : Product?.Title ?? ShortName;

    /// <summary>True for Daisy's App itself, false for the wrapper or a product (or any copy under another name).</summary>
    public static bool IsDaisysApp => ShortName.Equals(DaisysAppName, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the wrapper, which downloads the modules picked; false for Daisy's App and a product (theirs are
    /// installed beside the program).</summary>
    public static bool IsWrapper => !IsDaisysApp && Product == null;

    /// <summary>A product's app.json: its title, its releases' tag prefix, and its icon (beside the program).</summary>
    public sealed record ProductInfo(string Title, string TagPrefix)
    {
        /// <summary>The product's icon file beside the program (app.ico), for the tray.</summary>
        public string Icon => Path.Combine(AppContext.BaseDirectory, "app.ico");
    }

    private static ProductInfo? ReadProduct()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "app.json");
            if (IsDaisysApp || !File.Exists(path)) return null;
            var p = System.Text.Json.JsonSerializer.Deserialize<ProductInfo>(File.ReadAllText(path));
            return string.IsNullOrWhiteSpace(p?.Title) || string.IsNullOrWhiteSpace(p.TagPrefix) ? null : p;
        }
        catch { return null; }
    }

    private static string ProgramName()
    {
        string? name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        // (run by dotnet, or in a designer: Daisy's App)
        return string.IsNullOrWhiteSpace(name) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) || name.StartsWith("XDesProc", StringComparison.OrdinalIgnoreCase)
            ? DaisysAppName : name;
    }

    /// <summary>%APPDATA%\DaisysApp (or the wrapper's name): one JSON file for the shell and one per tool.</summary>
    public static string SettingsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ShortName);

    /// <summary>Documents\Daisy's App: the default place for files the user saves (levels, delays).</summary>
    public static string DocumentsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DisplayName);

    /// <summary>
    /// The modules the wrapper downloads: %LOCALAPPDATA%\Wrapper\modules (the program's name), one folder each, as in
    /// the modules folder beside the program. Daisy's App has all of its own beside it and doesn't use this.
    /// </summary>
    public static string UserModulesFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShortName, "modules");

    /// <summary>Where modules are loaded from: modules\ beside the program and, for the wrapper, <see cref="UserModulesFolder"/>.</summary>
    public static IReadOnlyList<string> ModuleFolders { get; } = !IsWrapper
        ? new[] { Path.Combine(AppContext.BaseDirectory, "modules") }
        : new[] { Path.Combine(AppContext.BaseDirectory, "modules"), UserModulesFolder };

    /// <summary>%LOCALAPPDATA%\DaisysApp\logs</summary>
    public static string LogFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShortName, "logs");
}
