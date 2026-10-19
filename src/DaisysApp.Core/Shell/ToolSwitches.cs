namespace DaisysApp.Shell;

/// <summary>
/// Which of a module's own sub-tabs (its tools: Hardware's Sensors, Video's Recording…) are switched on, in Settings →
/// General → Modules. A module asks <see cref="IsOn"/> before making each tool, and makes only the ones that are on (a
/// tool that's off isn't loaded at all), and says what its tools are with <see cref="Declare"/>, on or off, so the app
/// can list them there (remembered, for when the module itself is off). The app sets <see cref="Off"/> at startup.
/// Takes effect at the next start, like switching a module.
/// </summary>
public static class ToolSwitches
{
    /// <summary>One of a module's tools: the module's id, the tool's id and title, and the tool it needs (if any).</summary>
    public sealed record Tool(string Module, string Id, string Title, string? Needs = null);

    private static readonly List<Tool> declared = new();

    /// <summary>The tools switched off, as "module/tool" keys (set by the app at startup).</summary>
    public static ISet<string> Off { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static string Key(string module, string tool) => module + "/" + tool;

    /// <summary>Whether the module's tool is switched on (and the tool it needs, if any).</summary>
    public static bool IsOn(string module, string tool, string? needs = null) =>
        !Off.Contains(Key(module, tool)) && (needs == null || !Off.Contains(Key(module, needs)));

    /// <summary>A module's tool, on or off (as the module is made), in sub-tab order.</summary>
    public static void Declare(string module, string id, string title, string? needs = null)
    {
        lock (declared)
            if (!declared.Any(t => t.Module.Equals(module, StringComparison.OrdinalIgnoreCase) && t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                declared.Add(new Tool(module, id, title, needs));
    }

    /// <summary>The tools the modules made this run have declared.</summary>
    public static IReadOnlyList<Tool> Declared { get { lock (declared) return declared.ToList(); } }
}
