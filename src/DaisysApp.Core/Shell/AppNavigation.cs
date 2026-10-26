namespace DaisysApp.Shell;

/// <summary>
/// Lets an applet take the user to a place in the app, e.g. its own page under Settings. The main window does the
/// switching; an applet just asks.
/// </summary>
public static class AppNavigation
{
    /// <summary>Raised with the applet's id and, optionally, the x:Name of an element on its Settings page to scroll to.</summary>
    public static event Action<string, string?>? SettingsRequested;

    /// <summary>
    /// Opens Settings at <paramref name="appletId"/>'s page, scrolled to the element named <paramref name="elementName"/>
    /// (an x:Name in its settings view) if there is one.
    /// </summary>
    public static void OpenSettings(string appletId, string? elementName = null) => SettingsRequested?.Invoke(appletId, elementName);

    /// <summary>A tab as the window shows it: its id, title (as the module names it, not renamed) and its sub-tabs' titles in order.</summary>
    public sealed record ShownTab(string Id, string Title, IReadOnlyList<string> Tools)
    {
        /// <summary>The name on the tab (the user's, if they renamed it; else <see cref="Title"/>).</summary>
        public string? Named { get; init; }
    }

    /// <summary>Set by the main window: the tabs in the order they show (any thread; e.g. the AI's Module Docs follow it).</summary>
    public static Func<IReadOnlyList<ShownTab>>? TabsShown { get; set; }

    /// <summary>Set by the main window: shows an applet's tab and says whether there is one (it may be switched off).</summary>
    public static Func<string, bool>? TabOpener { get; set; }

    /// <summary>Shows the tab of the applet with that id (a sub-tab's id too, or "Module/SubTab" where sub-tabs of two
    /// modules share an id); false if it isn't loaded.</summary>
    public static bool OpenTab(string appletId) => TabOpener?.Invoke(appletId) ?? false;

    /// <summary>
    /// Set by the main window, and by the setup wizard while it's open: switches an applet off (as in Settings → General
    /// → Applets), so it isn't loaded from the next start.
    /// </summary>
    public static Action<string>? AppletSwitcher { get; set; }

    /// <summary>Switches the applet with that id off (e.g. from its own setup, for someone who doesn't need it); false if nothing could.</summary>
    public static bool SwitchOff(string appletId)
    {
        if (AppletSwitcher is not { } s) return false;
        s(appletId);
        return true;
    }
}
