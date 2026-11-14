using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// A game's block on the Profiles tab (since 4.17.17; one Profile card for the profile in use before): its checkbox
/// (the game's profile on or off), its name and Use, a line of what it's set to, then two parts that fold: Options
/// ((since 4.17.18) Short Name, Version, UDP Port, Auto Start, Processes, Defaults) and Modules (which sub-modules start with it, as chips; their
/// names on the fold's line). The Desktop block is the profile with no game.
/// </summary>
internal static class ProfileCard
{
    /// <summary>Analysis' units and temperature boxes (set by it; none while it's off).</summary>
    internal static Func<(ComboBox Units, ComboBox Temperature)>? UnitBoxes;

    /// <summary>The parts unfolded ("FH:Options"…), kept while the tab is drawn again.</summary>
    private static readonly HashSet<string> open = new();
    private const string Folded = "", Unfolded = "";

    /// <summary>The block of <paramref name="game"/> (null: Desktop), with its profile (null: none yet).</summary>
    public static Border Build(Game? game, SimProfile? p)
    {
        string key = game?.ToString() ?? "Desktop";
        string title = game is { } g ? Games.Title(g) : T("Desktop");
        bool on = p is { Enabled: true };
        bool active = on && p == SimProfiles.Active;
        var panel = new StackPanel();

        var check = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            ToolTip = game == null ? T("A profile for no game") : T("A profile for this game") };
        AutomationProperties.SetName(check, title);
        check.Checked += (_, _) => SimProfiles.SetEnabled(game, true);
        check.Unchecked += (_, _) => SimProfiles.SetEnabled(game, false);
        var name = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Opacity = on ? 1 : 0.6 };
        var head = new DockPanel { MinHeight = 30 };
        if (active)
        {
            var use = new TextBlock { Text = SimProfiles.Playing ? T("Playing") : T("In use"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            use.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            DockPanel.SetDock(use, Dock.Right);
            head.Children.Add(use);
        }
        else if (on)
        {
            var use = new Button { Content = T("Use"), Padding = new Thickness(10, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Use and play this profile") };
            AutomationProperties.SetName(use, F("Use {0}", title));
            use.Click += (_, _) => SimProfiles.Use(p!, "picked");
            DockPanel.SetDock(use, Dock.Right);
            head.Children.Add(use);
        }
        head.Children.Add(check);
        head.Children.Add(name);
        panel.Children.Add(head);

        if (p != null && on)
        {
            panel.Children.Add(Ui.Note(Summary(p), 6));
            panel.Children.Add(Fold(key + ":Options", T("Options"), null, () => Options(p)));
            var modules = SimProfiles.Tools.Where(t => t.IsOn != null).ToList();
            var ticked = modules.Where(t => SimProfiles.IsOn(p, t)).Select(t => t.Title).ToList();
            panel.Children.Add(Fold(key + ":Modules", T("Modules"), ticked.Count == 0 ? T("None") : string.Join(", ", ticked), () => Modules(p, modules)));
        }

        var card = Ui.Card(panel, new Thickness(0, 0, 12, 12));
        card.VerticalAlignment = VerticalAlignment.Top;
        if (active)
        {
            card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            card.BorderThickness = new Thickness(1.5);
        }
        return card;
    }

    /// <summary>What it's set to, in a line: (since 4.17.18) its short name, Forza's version, the port, Auto Start, how
    /// many processes.</summary>
    private static string Summary(SimProfile p)
    {
        var parts = new List<string> { SimProfiles.ShortOf(p) };
        if (p.Game is { } g && CarNames.Versions(g) is { Count: > 0 } versions)
            parts.Add(versions.FirstOrDefault(v => v.Version == p.Version).Title ?? T("Auto"));
        if (p.Game is { } u && Games.UsesPort(u)) parts.Add(F("Port {0}", p.Port));
        parts.Add(p.AutoStart == true ? T("Auto Start") : T("No Auto Start"));
        parts.Add(P(p.Programs.Count, "{0} process", "{0} processes"));
        return string.Join(" · ", parts);
    }

    /// <summary>A part that folds: a row (chevron, title, <paramref name="summary"/> at its right) and what
    /// <paramref name="body"/> makes under it while it's open.</summary>
    private static StackPanel Fold(string key, string title, string? summary, Func<FrameworkElement> body)
    {
        var s = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var chevron = Ui.Icon(open.Contains(key) ? Unfolded : Folded, 10);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        chevron.Margin = new Thickness(0, 0, 8, 0);
        var row = new DockPanel();
        row.Children.Add(chevron);
        var label = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        row.Children.Add(label);
        if (summary != null)
        {
            var more = Ui.Secondary(summary);
            more.VerticalAlignment = VerticalAlignment.Center;
            more.ToolTip = summary;
            row.Children.Add(more);
        }
        var button = new Button { Content = row, Template = Ui.FlatRow, Padding = new Thickness(4, 4, 4, 4), Background = System.Windows.Media.Brushes.Transparent,
            Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, title);
        button.MouseEnter += (_, _) => button.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        button.MouseLeave += (_, _) => button.Background = System.Windows.Media.Brushes.Transparent;
        s.Children.Add(button);
        var holder = new ContentControl { Margin = new Thickness(22, 4, 0, 0) };
        if (open.Contains(key)) holder.Content = body();
        s.Children.Add(holder);
        button.Click += (_, _) =>
        {
            bool now = open.Add(key);
            if (!now) open.Remove(key);
            chevron.Text = now ? Unfolded : Folded;
            holder.Content = now ? body() : null;
        };
        return s;
    }

    /// <summary>(Since 4.17.18) Short Name, Version (Forza's), UDP Port (Forza's), Auto Start, Processes, and Defaults (the
    /// game's port and processes).</summary>
    private static FrameworkElement Options(SimProfile p)
    {
        var panel = new StackPanel();
        // (since 4.17.18) the name in the title bar's SimEffects bubble; emptied, the game's again
        var shortName = new TextBox { Text = SimProfiles.ShortOf(p), Width = 90, MaxLength = 8, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = T("In the title bar") };
        AutomationProperties.SetName(shortName, T("Short Name"));
        void SetShort()
        {
            string s = shortName.Text.Trim();
            if (s.Length == 0) s = SimProfiles.DefaultShort(p);
            if (s != p.ShortName) { p.ShortName = s; SimProfiles.Edited(); }
            else shortName.Text = s;
        }
        shortName.LostFocus += (_, _) => SetShort();
        shortName.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetShort(); };
        panel.Children.Add(Field(T("Short Name"), shortName));
        if (p.Game is { } versioned && CarNames.Versions(versioned) is { Count: > 0 } versions)
        {
            var items = new List<KeyValuePair<int?, string>> { new(null, T("Auto")) };
            items.AddRange(versions.Select(v => new KeyValuePair<int?, string>(v.Version, v.Title)));
            var version = new ComboBox { ItemsSource = items, DisplayMemberPath = "Value", SelectedIndex = Math.Max(0, items.FindIndex(i => i.Key == p.Version)),
                ToolTip = T("For its cars' names and laps; Auto: the one running") };
            AutomationProperties.SetName(version, T("Version"));
            version.SelectionChanged += (_, _) =>
            {
                int? v = version.SelectedIndex <= 0 ? null : items[version.SelectedIndex].Key;
                if (v == p.Version) return;
                p.Version = v;
                SimProfiles.Edited();
            };
            panel.Children.Add(Field(T("Version"), version));
        }

        if (p.Game is { } used && Games.UsesPort(used))
        {
            var port = new TextBox { Text = p.Port.ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = T("The game's Data Out: IP 127.0.0.1 and this port") };
            AutomationProperties.SetName(port, T("UDP Port"));
            void SetPort()
            {
                if (int.TryParse(port.Text.Trim(), out int v) && v is >= 1024 and <= 65535 && v != p.Port) { p.Port = v; SimProfiles.Edited(); }
                else port.Text = p.Port.ToString();
            }
            port.LostFocus += (_, _) => SetPort();
            port.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetPort(); };
            panel.Children.Add(Field(T("UDP Port"), port));
        }

        var auto = new ComboBox { ItemsSource = new[] { T("Yes"), T("No") }, SelectedIndex = p.AutoStart == true ? 0 : 1, Width = 90, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = T("Plays while one of its programs runs") };
        AutomationProperties.SetName(auto, T("Auto Start"));
        auto.SelectionChanged += (_, _) => { p.AutoStart = auto.SelectedIndex == 0; SimProfiles.Edited(); };
        panel.Children.Add(Field(T("Auto Start"), auto));

        panel.Children.Add(new TextBlock { Text = T("Processes"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4),
            ToolTip = T("With Auto Start, one of these starting plays this profile") });
        panel.Children.Add(Ui.ProgramList(p.Programs, SimProfiles.Edited, addName: T("Process to add")));

        if (p.Game is { } g)
        {
            var defaults = DaisysApp.Shared.ConfirmButton.Make(T("Defaults"), () =>
            {
                p.Port = Games.Port(g);
                p.Programs = Games.Programs(g);
                SimProfiles.Edited();
            });
            defaults.Padding = new Thickness(10, 3, 10, 3);
            defaults.Margin = new Thickness(0, 10, 0, 0);
            defaults.HorizontalAlignment = HorizontalAlignment.Left;
            defaults.ToolTip = T("The game's own port and processes");
            panel.Children.Add(defaults);
        }
        return panel;
    }

    /// <summary>The sub-modules, a chip each: the ones on start when the profile does.</summary>
    private static FrameworkElement Modules(SimProfile p, List<ToolScope> modules)
    {
        var wrap = new WrapPanel();
        foreach (var t in modules)
        {
            var chip = Ui.Chip(t.Title, SimProfiles.IsOn(p, t), T("Starts with this profile"));
            chip.Margin = new Thickness(0, 0, 6, 6);
            AutomationProperties.SetName(chip, $"{t.Title} · {p.Name}");
            Ui.OnToggle(chip, on => { SimProfiles.SetOn(p, t, on); SimProfiles.Edited(); });
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    public static Grid Field(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }
}
