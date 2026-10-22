namespace DaisysApp.Shell;

/// <summary>
/// Files opened with the app ("DaisysApp.exe --open file", as Windows starts it for a file type it's the default app
/// for): the shell hands each one to the module that opens files (Tools → Notepad), in the running copy. Called on the
/// UI thread.
/// </summary>
public static class OpenFiles
{
    public const string Argument = "--open";

    /// <summary>Opens a file (its full path); set by the module that opens them, null while none is loaded.</summary>
    public static Action<string>? Handler { get; set; }
}
