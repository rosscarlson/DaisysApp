using DaisysApp.Theming;

namespace DaisysApp.Settings;

/// <summary>Settings of the shell itself (Settings → General, window placement). Tools keep their own files.</summary>
public sealed class AppSettings
{
    private const string FileName = "settings";

    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Show a tray icon and keep running there when the window is closed.</summary>
    public bool RunInTray { get; set; } = true;
    /// <summary>When started at sign-in, stay hidden in the tray (needs <see cref="RunInTray"/>).</summary>
    public bool StartHidden { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>Ids of the applets switched off in Settings → General (they aren't loaded at all). Everything else is on.</summary>
    public List<string> DisabledApplets { get; set; } = new();

    /// <summary>Id of the tab that was open last ("settings" for the Settings tab).</summary>
    public string? LastTab { get; set; }

    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public static AppSettings Load() => JsonStore.Load<AppSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
