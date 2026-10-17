namespace DaisysApp.Shell;

/// <summary>
/// A notification from a module (Claude Code: "your plan's limit resets at 2:40am"): the shell shows it as a Windows
/// notification from the tray icon, or, without one, as a notice at the top of the window. Callable from any thread.
/// </summary>
public static class Notifications
{
    /// <summary>Raised for each notification (the shell shows it).</summary>
    public static event Action<string, string>? Requested;

    public static void Show(string title, string text)
    {
        Logging.Log.App.Info($"Notification: {title}: {text}");
        Requested?.Invoke(title, text);
    }
}
