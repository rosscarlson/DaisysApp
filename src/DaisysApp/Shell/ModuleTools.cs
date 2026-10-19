using DaisysApp.Settings;

namespace DaisysApp.Shell;

/// <summary>
/// Each module's own sub-tabs (its tools), as the modules declared them this run (<see cref="ToolSwitches"/>), kept in
/// settings.json, so Settings → General → Modules can list a module's tools to switch while the module itself is off.
/// </summary>
public static class ModuleTools
{
    /// <summary>One tool: its id, title, and the tool it needs (Video's Recording needs the Overlay).</summary>
    public sealed record Known(string Id, string Title, string? Needs = null);

    /// <summary>Keeps what the modules made this run declared (a module that wasn't made keeps what it had).</summary>
    public static void Remember(AppSettings settings, IReadOnlyList<ToolSwitches.Tool> declared)
    {
        bool changed = false;
        foreach (var module in declared.GroupBy(t => t.Module, StringComparer.OrdinalIgnoreCase))
        {
            var tools = module.Select(t => new Known(t.Id, t.Title, t.Needs)).ToList();
            var key = settings.ModuleTools.Keys.FirstOrDefault(k => k.Equals(module.Key, StringComparison.OrdinalIgnoreCase)) ?? module.Key;
            if (settings.ModuleTools.TryGetValue(key, out var old) && old.SequenceEqual(tools)) continue;
            settings.ModuleTools[key] = tools;
            changed = true;
        }
        if (changed) settings.Save();
    }

    /// <summary>A module's tools, as last declared (none if it has no sub-tabs of its own, or never ran).</summary>
    public static IReadOnlyList<Known> Of(AppSettings settings, string module) =>
        settings.ModuleTools.FirstOrDefault(p => p.Key.Equals(module, StringComparison.OrdinalIgnoreCase)).Value ?? [];

    /// <summary>Whether a module's tool is switched on.</summary>
    public static bool IsOn(AppSettings settings, string module, string tool) =>
        !settings.DisabledTools.Contains(ToolSwitches.Key(module, tool), StringComparer.OrdinalIgnoreCase);

    /// <summary>Switches a module's tool on or off (from the next start).</summary>
    public static void Set(AppSettings settings, string module, string tool, bool on)
    {
        string key = ToolSwitches.Key(module, tool);
        settings.DisabledTools.RemoveAll(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (!on) settings.DisabledTools.Add(key);
    }
}
