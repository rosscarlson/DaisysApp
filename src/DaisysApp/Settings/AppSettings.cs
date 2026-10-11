using DaisysApp.Theming;

namespace DaisysApp.Settings;

/// <summary>Settings of the shell itself (Settings → General, window placement). Tools keep their own files.</summary>
public sealed class AppSettings
{
    private const string FileName = "settings";

    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;
    /// <summary>The language's code ("en", "es"…; see Loc). Takes effect at the next start.</summary>
    public string Language { get; set; } = Loc.English;
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Debug logging (Settings → General → Logging): far more detail in the logs than normal.</summary>
    public bool DebugLogging { get; set; }

    /// <summary>Show a tray icon and keep running there when the window is closed.</summary>
    public bool RunInTray { get; set; } = true;
    /// <summary>When started at sign-in, stay hidden in the tray (needs <see cref="RunInTray"/>).</summary>
    public bool StartHidden { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>Ids of the applets switched off in Settings → General (they aren't loaded at all). Everything else is on.</summary>
    public List<string> DisabledApplets { get; set; } = new();
    /// <summary>Ids of applets that are off by default (e.g. the Template) and have been switched on.</summary>
    public List<string> EnabledApplets { get; set; } = new();

    /// <summary>Names the user gave tabs (right-click a tab), by tab id ("settings" for the Settings tab).</summary>
    public Dictionary<string, string> TabNames { get; set; } = new();

    /// <summary>The setup wizard's version this user last went through (0 = never): it runs again when it's newer.</summary>
    public int SetupVersion { get; set; }

    /// <summary>Applet tab ids in the order the user dragged them into (empty = each applet's own order).</summary>
    public List<string> TabOrder { get; set; } = new();

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
