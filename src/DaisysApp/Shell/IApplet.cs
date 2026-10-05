using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace DaisysApp.Shell;

/// <summary>
/// One applet = one tab. To add one: create a folder under Applets/ (e.g. Applets/MyThing/), add a class that implements
/// this interface, and tag it with <see cref="AppletAttribute"/>. It's found automatically (see <see cref="AppletCatalog"/>)
/// and can be switched on or off in Settings → General. The shell supplies the tab, the Settings page, the tray, startup
/// and updates. An applet keeps its own settings with <see cref="Settings.JsonStore"/>, in a file named after its Id.
/// Code shared by several applets lives in Shared/, never in another applet's folder.
/// </summary>
public interface IApplet : IDisposable
{
    /// <summary>The applet's name, icon, tab order and description, from its <see cref="AppletAttribute"/>.</summary>
    AppletAttribute Meta => AppletCatalog.MetaFor(GetType());

    /// <summary>The tab's content. Created once and kept alive while the app runs.</summary>
    FrameworkElement View { get; }

    /// <summary>Content of this applet's page under Settings, or null if it has no settings.</summary>
    FrameworkElement? SettingsView { get; }

    /// <summary>Called once at launch, also when the app starts hidden in the tray. Start background work here.</summary>
    void Start();

    /// <summary>Keyboard shortcuts: called for key presses in the main window while this applet's tab is open.</summary>
    void OnPreviewKeyDown(KeyEventArgs e) { }

    /// <summary>The window was hidden to the tray: stop anything that only makes sense while it's visible.</summary>
    void OnWindowHidden() { }

    /// <summary>Persist settings. Called when the window is hidden and before <see cref="IDisposable.Dispose"/> on exit.</summary>
    void SaveSettings();

    /// <summary>Items for a submenu (named after the applet) in the tray icon's menu, or null for none.
    /// Read each time the menu opens, on the UI thread.</summary>
    IReadOnlyList<AppletMenuItem>? TrayMenu => null;
}

/// <summary>One tray menu entry from an applet: a command, a submenu (<see cref="Children"/>), or a separator.</summary>
public sealed record AppletMenuItem(string Text, Action? Click = null, IReadOnlyList<AppletMenuItem>? Children = null)
{
    public bool Enabled { get; init; } = true;
    public bool Checked { get; init; }
    /// <summary>Shown right-aligned, e.g. a hotkey.</summary>
    public string? Hint { get; init; }
    public bool IsSeparator { get; init; }

    public static AppletMenuItem Separator { get; } = new("") { IsSeparator = true };
}

/// <summary>Describes an applet. Everything here is available without creating the applet (e.g. for a disabled one).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AppletAttribute(string id, string title, string icon) : Attribute
{
    /// <summary>Stable key: settings file name, remembered tab and enable/disable setting. Never change it once released.</summary>
    public string Id { get; } = id;

    /// <summary>Tab caption, also used for its Settings page.</summary>
    public string Title { get; } = title;

    /// <summary>Segoe Fluent Icons glyph shown next to the caption, e.g. "".</summary>
    public string Icon { get; } = icon;

    /// <summary>Tab position: lower comes first.</summary>
    public int Order { get; init; } = 100;

    /// <summary>One line for Settings → General → Applets.</summary>
    public string Description { get; init; } = "";
}

/// <summary>Finds every applet compiled into the app: classes that implement <see cref="IApplet"/> and carry an <see cref="AppletAttribute"/>.</summary>
public static class AppletCatalog
{
    public sealed record Entry(Type Type, AppletAttribute Meta)
    {
        public IApplet Create() => (IApplet)Activator.CreateInstance(Type)!;
    }

    /// <summary>All applets, in tab order.</summary>
    public static IReadOnlyList<Entry> All { get; } = typeof(AppletCatalog).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IApplet).IsAssignableFrom(t))
        .Select(t => (Type: t, Meta: t.GetCustomAttribute<AppletAttribute>()))
        .Where(x => x.Meta != null)
        .Select(x => new Entry(x.Type, x.Meta!))
        .OrderBy(e => e.Meta.Order).ThenBy(e => e.Meta.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public static AppletAttribute MetaFor(Type type) =>
        All.FirstOrDefault(e => e.Type == type)?.Meta
        ?? type.GetCustomAttribute<AppletAttribute>()
        ?? throw new InvalidOperationException($"{type.Name} needs an [Applet] attribute.");
}
