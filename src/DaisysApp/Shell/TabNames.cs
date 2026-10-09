using DaisysApp.Settings;

namespace DaisysApp.Shell;

/// <summary>
/// The names the user gave the tabs (right-click a tab, or Settings → General → Tab names), by tab id. A tab without one
/// shows its applet's own (translated) title.
/// </summary>
internal static class TabNames
{
    private static AppSettings? settings;

    /// <summary>Raised on the UI thread with the id of a tab whose name changed.</summary>
    public static event Action<string>? Changed;

    public static void Init(AppSettings s) => settings = s;

    /// <summary>The tab's name: the user's, or <paramref name="defaultTitle"/>.</summary>
    public static string For(string id, string defaultTitle) =>
        settings?.TabNames.TryGetValue(id, out var name) == true && !string.IsNullOrWhiteSpace(name) ? name : defaultTitle;

    /// <summary>Whether the user has renamed the tab.</summary>
    public static bool IsRenamed(string id) => settings?.TabNames.ContainsKey(id) == true;

    /// <summary>Renames a tab; a blank name (or the default) puts the default back.</summary>
    public static void Set(string id, string? name, string defaultTitle)
    {
        if (settings == null) return;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name == defaultTitle) settings.TabNames.Remove(id);
        else settings.TabNames[id] = name;
        settings.Save();
        Changed?.Invoke(id);
    }
}
