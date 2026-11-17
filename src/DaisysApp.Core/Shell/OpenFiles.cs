namespace DaisysApp.Shell;

/// <summary>
/// Files opened with the app ("DaisysApp.exe --open file", as Windows starts it for a file type it's the default app
/// for): the shell hands each one to the module that opens files (Tools → Notepad), in the running copy. Since 4.18.0
/// also links ("daisysapp://session/…", from a web page's Open in Daisy's App): the shell registers the scheme for this
/// user, hands a link to the running copy the same way, and gives it to the module whose <see cref="Links"/> has its
/// kind (the part after "daisysapp://"). Called on the UI thread.
/// </summary>
public static class OpenFiles
{
    public const string Argument = "--open";

    /// <summary>(since 4.18.0) The app's links' scheme.</summary>
    public const string Scheme = "daisysapp";

    /// <summary>Opens a file (its full path); set by the module that opens them, null while none is loaded.</summary>
    public static Action<string>? Handler { get; set; }

    /// <summary>(since 4.18.0) A link's kind ("session") → what opens it (the whole link), set by the module it's for.</summary>
    public static Dictionary<string, Action<Uri>> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>(since 4.18.0) The text is one of the app's links.</summary>
    public static bool IsLink(string text) => text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);
}
