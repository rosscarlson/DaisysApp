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
}
