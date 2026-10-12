using System.IO;
using DaisysApp.Settings;

namespace DaisysApp.Shell;

/// <summary>
/// Applets that became sub-tabs of another (0.22: Resizer, Mini Mirror and Gaming went into Video; Performance, USB
/// Monitor and Sensors into Hardware). Once, at startup, the settings that name tabs by id follow them: the tab
/// order (the group goes where the first of its tools was), the switched-off list (the group is off only if all its
/// tools were), the last tab open (and which sub-tab), and tab names (dropped). The tools' own settings files are
/// unchanged.
/// </summary>
internal static class ModuleMoves
{
    private const int Version = 1;

    private static readonly (string Group, string[] Tools)[] Moves =
    [
        ("Video", ["Resizer", "MiniMirror", "Gaming"]),
        ("Hardware", ["Performance", "UsbMonitor", "Sensors"]),
    ];

    public static void Apply(AppSettings s)
    {
        if (s.ModuleMovesVersion >= Version) return;
        var cmp = StringComparer.OrdinalIgnoreCase;
        bool moved = false;
        foreach (var (group, tools) in Moves)
        {
            bool Is(string id) => tools.Contains(id, cmp);
            moved |= s.TabOrder.Any(Is) || s.DisabledApplets.Any(Is) || (s.LastTab is { } l && Is(l));

            int at = s.TabOrder.FindIndex(Is);
            if (at >= 0 && !s.TabOrder.Contains(group, cmp)) s.TabOrder.Insert(at, group);
            s.TabOrder.RemoveAll(Is);

            bool allOff = tools.All(t => s.DisabledApplets.Contains(t, cmp));
            s.DisabledApplets.RemoveAll(Is);
            if (allOff && !s.DisabledApplets.Contains(group, cmp)) s.DisabledApplets.Add(group);

            foreach (var t in tools) s.TabNames.Remove(t);

            if (s.LastTab is { } last && Is(last))
            {
                s.LastTab = group;
                // open on that tool's sub-tab (unless the group already remembers one)
                if (!File.Exists(JsonStore.PathFor(group)))
                    JsonStore.Save(group, new AppletGroup.GroupSettings { Tool = tools.First(t => t.Equals(last, StringComparison.OrdinalIgnoreCase)) });
            }
        }
        s.ModuleMovesVersion = Version;
        s.Save();
        if (moved) Logging.Log.App.Info("Settings moved to the Video and Hardware tabs (their tools were tabs of their own before 0.22)");
    }
}
