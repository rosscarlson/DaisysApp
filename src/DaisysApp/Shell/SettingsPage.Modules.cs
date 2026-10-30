using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Shell;

/// <summary>
/// Settings → General → Modules: a row per module (its tab, in tab order), ticked if it's on, with its sub-modules
/// ticked under it: its own sub-tabs (Hardware's Sensors; <see cref="ToolSwitches"/>, as the module last declared them)
/// and the modules that share its tab (Gaming's Joy2Key, Video's Resizer). Switching the last sub-module off switches
/// the module off; switching one on switches its module on. A sub-module that needs another (Recording, the Overlay)
/// can't be on without it. Everything takes effect at the next start (Restart now).
/// </summary>
public partial class SettingsPage
{
    private sealed class ModuleRow
    {
        public required AppletAttribute Meta { get; init; }
        public required CheckBox Box { get; init; }
        public List<SubRow> Subs { get; } = new();
    }

    /// <summary>A sub-module: another module that shares the tab (<see cref="Module"/>), or one of the module's own tools.</summary>
    private sealed class SubRow
    {
        public required CheckBox Box { get; init; }
        public AppletAttribute? Module { get; init; }
        public string? Tool { get; init; }
        public string? Needs { get; init; }
        public required int Order { get; init; }
    }

    private readonly List<ModuleRow> moduleRows = new();
    private string loadedModules = ""; // what this run started with
    private bool syncing;

    private void BuildModules()
    {
        foreach (var e in AppletCatalog.InTabOrder(settings))
        {
            var row = new ModuleRow
            {
                Meta = e.Meta,
                Box = new CheckBox { Content = TabNames.For(e.Meta.Id, Any(e.Meta.Title)), FontWeight = FontWeights.SemiBold },
            };
            // its own sub-tabs, and other modules' applets on its tab, in sub-tab order
            int order = 0;
            foreach (var t in ModuleTools.Of(settings, e.Meta.Id))
                row.Subs.Add(new SubRow { Box = new CheckBox { Content = Any(t.Title) }, Tool = t.Id, Needs = t.Needs, Order = order += 10 });
            foreach (var m in e.Tools)
                row.Subs.Add(new SubRow { Box = new CheckBox { Content = Any(m.Meta.Title), ToolTip = Any(m.Meta.Description) is { Length: > 0 } d ? d : null }, Module = m.Meta, Order = m.Meta.GroupOrder });
            row.Subs.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var s in row.Subs)
                s.Box.IsChecked = s.Module != null ? AppletCatalog.IsOn(s.Module, settings) : ModuleTools.IsOn(settings, e.Meta.Id, s.Tool!);
            // (a tab made only of other modules' applets, Gaming, is on while one of them is)
            row.Box.IsChecked = AppletCatalog.IsOn(e.Meta, settings) && (e.Type != null || row.Subs.Count == 0 || row.Subs.Any(s => s.Box.IsChecked == true));

            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(row.Box);
            if (row.Subs.Count > 0)
            {
                panel.Tag = "subs";
                var subs = new WrapPanel { Margin = new Thickness(26, 6, 0, 0) };
                foreach (var s in row.Subs)
                {
                    s.Box.Margin = new Thickness(0, 0, 22, 4);
                    if (s.Needs != null && row.Subs.FirstOrDefault(n => n.Tool == s.Needs) is { } needed)
                        s.Box.ToolTip = F("Needs {0}", needed.Box.Content);
                    s.Box.Checked += (_, _) => SubChanged(row, s);
                    s.Box.Unchecked += (_, _) => SubChanged(row, s);
                    subs.Children.Add(s.Box);
                }
                panel.Children.Add(subs);
            }
            row.Box.Checked += (_, _) => ModuleChanged(row);
            row.Box.Unchecked += (_, _) => ModuleChanged(row);
            moduleRows.Add(row);
            ModuleList.Children.Add(panel);
            Enable(row);
        }
        loadedModules = State();
    }

    /// <summary>A module switched: on with none of its sub-modules on puts them all on.</summary>
    private void ModuleChanged(ModuleRow row)
    {
        if (syncing) return;
        syncing = true;
        if (row.Box.IsChecked == true && row.Subs.Count > 0 && !row.Subs.Any(s => s.Box.IsChecked == true))
            foreach (var s in row.Subs) s.Box.IsChecked = true;
        syncing = false;
        Enable(row);
        SaveModules();
    }

    /// <summary>A sub-module switched: its module goes on with it, or off with the last one.</summary>
    private void SubChanged(ModuleRow row, SubRow sub)
    {
        if (syncing) return;
        syncing = true;
        if (sub.Box.IsChecked == true) row.Box.IsChecked = true;
        else if (!row.Subs.Any(s => s.Box.IsChecked == true)) row.Box.IsChecked = false;
        syncing = false;
        Enable(row);
        SaveModules();
    }

    /// <summary>Sub-modules can be switched while their module is on (and what they need is).</summary>
    private static void Enable(ModuleRow row)
    {
        foreach (var s in row.Subs)
            s.Box.IsEnabled = row.Box.IsChecked == true
                && (s.Needs == null || row.Subs.FirstOrDefault(n => n.Tool == s.Needs)?.Box.IsChecked != false);
    }

    private void SaveModules()
    {
        if (updating) return;
        AppletCatalog.SaveOnOff(settings, moduleRows.Select(r => (r.Meta, r.Box.IsChecked == true))
            .Concat(moduleRows.SelectMany(r => r.Subs).Where(s => s.Module != null).Select(s => (s.Module!, s.Box.IsChecked == true))));
        foreach (var r in moduleRows)
            foreach (var s in r.Subs.Where(s => s.Tool != null))
                ModuleTools.Set(settings, r.Meta.Id, s.Tool!, s.Box.IsChecked == true);
        settings.Save();
        // takes effect on the next start; offer a restart while it differs from what's loaded
        RestartRow.Visibility = State() != loadedModules ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>What's ticked, to compare with what this run started with.</summary>
    private string State() => string.Join("|", moduleRows.Select(r =>
        $"{r.Meta.Id}={r.Box.IsChecked == true}:" + string.Join(",", r.Subs.Select(s => s.Box.IsChecked == true ? "1" : "0"))));
}
