using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// (since 4.17.18) The title bar's one SimEffects bubble: the profile in use's short name, then each tool running, a
/// line between them ("FH: Shaker | Overlay | Wheel"); clicking it opens Profiles. The Shaker, Overlays, Wheel,
/// AmbiLight and Arduino had a bubble each before. Callable from any thread.
/// </summary>
public static class SimBadges
{
    private const string Id = "SimEffects";
    /// <summary>The tools in the bubble's order, by their profile id.</summary>
    private static readonly string[] order = { "BassShaker", "InputOverlay", "Fanatec", "AmbiLight", "Arduino" };
    private static readonly object gate = new();
    private static readonly Dictionary<string, (string Text, string Tip)> parts = new(StringComparer.OrdinalIgnoreCase);
    private static bool following;

    /// <summary>A tool's part (its id, as <see cref="ToolScope.Id"/>): shown, or gone (null text).</summary>
    public static void Set(string tool, string? text, string? tip = null)
    {
        lock (gate)
        {
            if (!following) { following = true; SimProfiles.Changed += Show; }
            if (string.IsNullOrEmpty(text)) parts.Remove(tool);
            else parts[tool] = (text, tip ?? text);
        }
        Show();
    }

    private static void Show()
    {
        List<(string Text, string Tip)> shown;
        lock (gate)
            shown = parts.OrderBy(p => Array.IndexOf(order, p.Key) is var i and >= 0 ? i : int.MaxValue).Select(p => p.Value).ToList();
        if (shown.Count == 0) { TitleBadges.Set(Id, null); return; }
        string text = SimProfiles.ShortOf(SimProfiles.Active) + ": " + string.Join(" | ", shown.Select(s => s.Text));
        TitleBadges.Set(Id, text, string.Join("\n", shown.Select(s => s.Tip)), TitleBadges.Green, Id + "/" + SimEffectsApplet.Profiles);
    }
}
