using System.IO;
using System.Text.Json.Nodes;
using DaisysApp.Settings;

namespace DaisysApp.Shell;

/// <summary>
/// Applets that became sub-tabs of another (0.22: Resizer, Mini Mirror and Gaming went into Video; Performance, USB
/// Monitor and Sensors into Hardware; 0.39: Zabbix and Logs into Network; 0.53: Joy2Key into Gaming, whose id is
/// GamingTools as Video's Gaming tool already has the id Gaming). Once, at startup, the settings that name tabs by id follow them: the tab
/// order (the group goes where the first of its tools was), the switched-off list (the group is off only if all its
/// tools were), the last tab open (and which sub-tab), and tab names (dropped). The tools' own settings files are
/// unchanged, except that Zabbix switched off on its own (with Network on, for Logs) is switched off in Zabbix.json,
/// which hides its sub-tab.
/// </summary>
internal static class ModuleMoves
{
    private const int Version = 4;

    private static readonly (string Group, string[] Tools)[] Moves =
    [
        ("Video", ["Resizer", "MiniMirror", "Gaming"]),
        ("Hardware", ["Performance", "UsbMonitor", "Sensors"]),
        ("Network", ["Zabbix", "Logs"]),
        ("GamingTools", ["Joy2Key"]),
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
            if (group == "Network" && !allOff && s.DisabledApplets.Contains("Zabbix", cmp)) SwitchOffZabbix();
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
        // 1.8: Claude Code became AI (its sessions, settings and tab carry over)
        foreach (var list in new[] { s.TabOrder, s.DisabledApplets })
        {
            int at = list.FindIndex(id => cmp.Equals(id, "ClaudeCode"));
            if (at >= 0) { list[at] = "AI"; moved = true; }
        }
        if (cmp.Equals(s.LastTab, "ClaudeCode")) s.LastTab = "AI";
        if (s.TabNames.Remove("ClaudeCode", out var named)) s.TabNames["AI"] = named;
        s.ModuleMovesVersion = Version;
        s.Save();
        if (moved) Logging.Log.App.Info("Settings moved to the Video, Hardware, Network and Gaming tabs (their tools were tabs of their own before 0.22, 0.39 and 0.53)");
    }

    /// <summary>
    /// 3.12.9: Logs went from the Network tab to the Tools tab (its Logs.json is unchanged). Its sub-module switch follows
    /// it, and if it was the sub-tab showing last, the app opens on Tools → Logs. For Daisy's App and the wrapper alike;
    /// changes nothing once done, so it runs at every start.
    /// </summary>
    public static void LogsToTools(AppSettings s)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        bool changed = false;
        int off = s.DisabledTools.FindIndex(k => cmp.Equals(k, "Network/Logs"));
        if (off >= 0)
        {
            if (s.DisabledTools.Contains("Tools/Logs", cmp)) s.DisabledTools.RemoveAt(off);
            else s.DisabledTools[off] = "Tools/Logs";
            changed = true;
        }
        if (cmp.Equals(s.LastTab, "Network") && File.Exists(JsonStore.PathFor("Network"))
            && JsonStore.Load<AppletGroup.GroupSettings>("Network").Tool is { } tool && cmp.Equals(tool, "Logs"))
        {
            s.LastTab = "Tools";
            JsonStore.Save("Tools", new AppletGroup.GroupSettings { Tool = "Logs" });
            JsonStore.Save("Network", new AppletGroup.GroupSettings());
            changed = true;
        }
        if (!changed) return;
        s.Save();
        Logging.Log.App.Info("Logs is on the Tools tab now (it was on the Network tab before 3.12.9)");
    }

    /// <summary>Zabbix's own switch (ZabbixSettings.Off, in the Network module), set without loading the module.</summary>
    private static void SwitchOffZabbix()
    {
        try
        {
            string path = JsonStore.PathFor("Zabbix");
            var json = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
            json ??= new JsonObject();
            json["Off"] = true;
            File.WriteAllText(path, json.ToJsonString(JsonStore.Options));
        }
        catch (Exception ex) { Logging.Log.App.Error("Couldn't switch Zabbix off in Zabbix.json", ex); }
    }
}
