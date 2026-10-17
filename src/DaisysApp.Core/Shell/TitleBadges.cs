namespace DaisysApp.Shell;

/// <summary>
/// Status bubbles in the main window's title bar, right after the app's name and version: a module shows one while
/// something of its own is running (ForzaWheel while its emulator runs, Joy2Key while a profile is in use), with its
/// own setting to leave it out. The shell draws them (Shell/TitleBadgeStrip). Callable from any thread.
/// </summary>
public static class TitleBadges
{
    /// <summary>One bubble: its text, what pointing at it says (or null), and its colour ("#E3B341"; null = green).</summary>
    public sealed record Badge(string Id, string Text, string? Tip, string? Color = null);

    private static readonly object gate = new();
    private static readonly List<Badge> badges = new();

    /// <summary>Raised (on whichever thread changed them) when a bubble appears, changes or goes.</summary>
    public static event Action? Changed;

    /// <summary>Shows a bubble (by id: one per module, in the order they first appear), or removes it (null text).</summary>
    public static void Set(string id, string? text, string? tip = null, string? color = null)
    {
        bool changed;
        lock (gate)
        {
            int i = badges.FindIndex(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(text))
            {
                changed = i >= 0;
                if (changed) badges.RemoveAt(i);
            }
            else
            {
                var badge = new Badge(id, text, tip, color);
                changed = i < 0 || badges[i] != badge;
                if (i < 0) badges.Add(badge); else badges[i] = badge;
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>The bubbles showing now, in order.</summary>
    public static IReadOnlyList<Badge> All { get { lock (gate) return badges.ToList(); } }
}
