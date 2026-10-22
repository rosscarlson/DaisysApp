namespace DaisysApp.Shell;

/// <summary>
/// Small icon buttons a module puts at the top right of the main window, after Update (Tools → Notepad's pop-out). The
/// main window draws them, in the order they're added. Called on the UI thread.
/// </summary>
public static class QuickButtons
{
    /// <summary>One button: its id, icon (a Segoe Fluent Icons glyph), what pointing at it says, and what it does.</summary>
    public sealed record Button(string Id, string Glyph, string Tip, Action Click);

    private static readonly List<Button> buttons = new();

    /// <summary>Raised when one is added or removed.</summary>
    public static event Action? Changed;

    /// <summary>Adds a button (by id: one with the same id is replaced).</summary>
    public static void Add(string id, string glyph, string tip, Action click)
    {
        int i = buttons.FindIndex(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var b = new Button(id, glyph, tip, click);
        if (i < 0) buttons.Add(b); else buttons[i] = b;
        Changed?.Invoke();
    }

    public static void Remove(string id)
    {
        if (buttons.RemoveAll(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0) Changed?.Invoke();
    }

    public static IReadOnlyList<Button> All => buttons.ToList();
}
