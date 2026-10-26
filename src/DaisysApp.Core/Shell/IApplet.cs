using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace DaisysApp.Shell;

/// <summary>
/// One applet = one tab. An applet lives in a module: a class library in its own folder under modules\ next to
/// DaisysApp.exe, with a class that implements this interface and carries an <see cref="AppletAttribute"/>. The app loads
/// every module it finds there (see src/Modules/Template/README.md), and each can be switched on or off in Settings →
/// General. The app supplies the tab, the Settings page, the tray, startup and updates. An applet keeps its own settings
/// with <see cref="Settings.JsonStore"/>, in a file named after its Id. Code several modules share lives in
/// DaisysApp.Core, never in another module.
/// </summary>
public interface IApplet : IDisposable
{
    /// <summary>The applet's name, icon, tab order and description, from its <see cref="AppletAttribute"/>.</summary>
    AppletAttribute Meta => AppletAttribute.Of(GetType());

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

    /// <summary>
    /// The applet's own first-run setup, if it has one: true when it has never been through it, or when something it
    /// needs is missing (the applet decides, from its own settings). Asked once the window is showing and the app's
    /// setup wizard (if due) is done; then <see cref="RunOnboarding"/> is called.
    /// </summary>
    bool NeedsOnboarding => false;

    /// <summary>Shows the applet's setup (e.g. its own wizard window, owned by <paramref name="owner"/>), on the UI
    /// thread. The applet records that it's done in its own settings, so <see cref="NeedsOnboarding"/> turns false.</summary>
    void RunOnboarding(Window owner) { }

    /// <summary>
    /// A page for the app's setup wizard, for what the user can set up or choose here (e.g. Gaming's permission to count
    /// frames); null when the applet has none. The full wizard (after installing, and from Settings → General) shows it
    /// whenever there is one; otherwise the wizard opens only for a <see cref="SetupRequired"/> applet. Asked each time
    /// the wizard runs, on the UI thread; the page does its work itself (buttons, checkboxes).
    /// </summary>
    FrameworkElement? SetupPage => null;

    /// <summary>
    /// A setting the applet can't do without is missing (e.g. Zabbix switched on but never set up or skipped). The app
    /// opens the setup wizard for it at start, with only the pages of the applets that need it
    /// (<see cref="RequiredSetupPage"/>); false for anything optional. Asked at start, on the UI thread.
    /// </summary>
    bool SetupRequired => false;

    /// <summary>The page for a wizard showing only what's required: <see cref="SetupPage"/> when <see cref="SetupRequired"/>.</summary>
    FrameworkElement? RequiredSetupPage => SetupRequired ? SetupPage : null;

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

    /// <summary>False for an applet that's off until it's switched on in Settings → General (e.g. the Template).</summary>
    public bool OnByDefault { get; init; } = true;

    /// <summary>
    /// True for an applet shown as a sub-tab at the end of the Settings tab instead of as a tab of its own (Logs was, before 0.39: it's on the Tools tab now).
    /// Its <see cref="IApplet.View"/> fills that sub-tab, which doesn't scroll: it gets the space to fill.
    /// </summary>
    public bool InSettings { get; init; }

    /// <summary>
    /// The id of a tab this applet shares with others, each on its own sub-tab (e.g. "GamingTools" for Joy2Key and
    /// ForzaWheel, "Video" for the Resizer); null for a tab of its own. The app builds that tab from whichever of its
    /// applets are installed: with two or more it's one tab of sub-tabs (named by the group's <see cref="TabGroupAttribute"/>,
    /// or a module's own <see cref="AppletGroup"/> with that id, which the others join), with only one the applet is a
    /// tab of its own (so the applet's DLL alone in the wrapper makes a tab with no sub-tabs).
    /// </summary>
    public string? Group { get; init; }

    /// <summary>Its sub-tab's place in its <see cref="Group"/>: lower comes first (an <see cref="AppletGroup"/>'s own tools are 10, 20, 30…).</summary>
    public int GroupOrder { get; init; } = 100;

    /// <summary>The attribute on an applet class.</summary>
    public static AppletAttribute Of(Type type) =>
        type.GetCustomAttribute<AppletAttribute>() ?? throw new InvalidOperationException($"{type.Name} needs an [Applet] attribute.");
}

/// <summary>
/// Describes a tab shared by applets from several modules (<see cref="AppletAttribute.Group"/>): its name, icon, place
/// and description, used when two or more of them are installed. Put the same attribute in each of those modules
/// (one file, linked into each project), so whichever are installed can make the tab. Assembly-level:
/// <c>[assembly: TabGroup("GamingTools", "Gaming", "", Order = 55, Description = "…")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class TabGroupAttribute(string id, string title, string icon) : Attribute
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public int Order { get; init; } = 100;
    public string Description { get; init; } = "";
    public bool OnByDefault { get; init; } = true;

    /// <summary>The tab as an applet's description (what the app's tabs, Settings and the setup wizard go by).</summary>
    public AppletAttribute ToApplet() => new(Id, Title, Icon) { Order = Order, Description = Description, OnByDefault = OnByDefault };
}
