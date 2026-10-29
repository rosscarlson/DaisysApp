namespace DaisysApp.Shell;

/// <summary>
/// "DaisysApp.exe --say text" (run by an AI session to speak to you): the shell hands the text to the running copy
/// (over the same pipe as OpenFiles) and gives it to the module that speaks (AI, with the voice picked in Settings → AI).
/// Called on the UI thread.
/// </summary>
public static class Speech
{
    public const string Argument = "--say";

    /// <summary>Speaks the text; set by the module that speaks, null while none is loaded.</summary>
    public static Action<string>? Say { get; set; }

    /// <summary>The text after --say (everything after it, joined), if there is one.</summary>
    public static string? TextIn(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return null;
        string text = string.Join(" ", args.Skip(i + 1)).ReplaceLineEndings(" ").Trim();
        return text.Length > 0 ? text : null;
    }
}
