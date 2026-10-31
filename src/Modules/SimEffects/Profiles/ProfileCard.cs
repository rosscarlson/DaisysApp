using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DaisysApp.Applets.SimEffects.BassShaker;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// The Profile card (the Shaker's until 3.18.0, now the whole module's, on the Profiles tab): the profile in use (Name:
/// a drop-down of the profiles, a box while it's being named), Game (or none), Version (Forza's), UDP Port (Forza), Auto Start and
/// (since 3.28.0) Units (Analysis', the module's own: the same in every profile) and Processes (Programs before), with
/// Edit / Save, New and Delete at the bottom.
/// </summary>
internal static class ProfileCard
{
    /// <summary>Analysis' units and temperature boxes (set by it; none while it's off).</summary>
    internal static Func<(ComboBox Units, ComboBox Temperature)>? UnitBoxes;

    /// <summary>The card shows the name in a box (Edit, or a profile just made by New) until Save.</summary>
    private static bool naming;

    public static Border Build(SimProfile p)
    {
        var panel = new StackPanel();
        panel.Children.Add(Header(T("Profile")));

        var edit = new Button { Content = naming ? T("Save") : T("Edit"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        FrameworkElement nameControl;
        if (naming)
        {
            var name = new TextBox { Text = p.Name };
            AutomationProperties.SetName(name, T("Profile name"));
            void Save()
            {
                naming = false;
                if (!SimProfiles.Rename(p, name.Text.Trim())) { naming = true; name.Focus(); name.SelectAll(); }
            }
            void Cancel() { naming = false; SimProfiles.Edited(); }
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); else if (e.Key == Key.Escape) Cancel(); };
            name.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
            edit.Click += (_, _) => Save();
            nameControl = name;
        }
        else
        {
            var box = new ComboBox { ItemsSource = SimProfiles.All.ToList(), DisplayMemberPath = nameof(SimProfile.Name), SelectedItem = p };
            AutomationProperties.SetName(box, T("Profile"));
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is SimProfile o && o != p) SimProfiles.Use(o, "picked"); };
            edit.Click += (_, _) => { naming = true; SimProfiles.Edited(); };
            nameControl = box;
        }
        panel.Children.Add(Field(T("Profile"), nameControl));

        var games = new List<KeyValuePair<Game?, string>> { new(null, T("None")) };
        games.AddRange(Games.All.Select(g => new KeyValuePair<Game?, string>(g, Games.Title(g))));
        var game = new ComboBox { ItemsSource = games, DisplayMemberPath = "Value", SelectedIndex = p.Game is { } pg ? Array.IndexOf(Games.All, pg) + 1 : 0,
            ToolTip = T("The game every tool reads") };
        AutomationProperties.SetName(game, T("Game"));
        game.SelectionChanged += (_, _) =>
        {
            var g = game.SelectedIndex <= 0 ? (Game?)null : Games.All[game.SelectedIndex - 1];
            if (g == p.Game) return;
            p.Game = g;
            if (g == Telemetry.Game.ForzaMotorsport && p.Port == 8000) p.Port = 5301;
            SimProfiles.Edited();
        };
        panel.Children.Add(Field(T("Game"), game));

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
        auto.SelectionChanged += (_, _) => { p.AutoStart = auto.SelectedIndex == 0; SimProfiles.Save(); };
        panel.Children.Add(Field(T("Auto Start"), auto));

        // (since 3.28.0) the units, then the processes, each its own part with room between
        if (UnitBoxes?.Invoke() is var (units, temperature))
        {
            panel.Children.Add(Section(T("Units"), null));
            panel.Children.Add(Field(T("System"), units));
            panel.Children.Add(Field(T("Temperature"), temperature));
        }
        panel.Children.Add(Section(T("Processes"), T("With Auto Start, one of these starting plays this profile")));
        foreach (var prog in p.Programs.ToList())
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var x = VolumeControl.IconButton("");
            x.ToolTip = T("Remove");
            AutomationProperties.SetName(x, F("Remove {0}", prog));
            x.Click += (_, _) => { p.Programs.Remove(prog); SimProfiles.Edited(); };
            DockPanel.SetDock(x, Dock.Right);
            line.Children.Add(x);
            line.Children.Add(new TextBlock { Text = prog, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(line);
        }
        var newProg = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(newProg, T("Process to add"));
        var addProg = new Button { Content = T("Add"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        void AddProg()
        {
            string s = System.IO.Path.GetFileName(newProg.Text.Trim().Trim('"'));
            if (s.Length == 0) return;
            if (!s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s += ".exe";
            if (!p.Programs.Contains(s, StringComparer.OrdinalIgnoreCase)) p.Programs.Add(s);
            SimProfiles.Edited();
        }
        addProg.Click += (_, _) => AddProg();
        newProg.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddProg(); };
        var addLine = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(addProg, Dock.Right);
        addLine.Children.Add(addProg);
        addLine.Children.Add(newProg);
        panel.Children.Add(addLine);

        var newProfile = new Button { Content = T("New"), Padding = new Thickness(10, 3, 10, 3) };
        newProfile.Click += (_, _) => NewProfileMenu(newProfile, p);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(edit);
        buttons.Children.Add(newProfile);
        if (SimProfiles.All.Count > 1)
        {
            var delete = DaisysApp.Shared.ConfirmButton.Make(T("Delete"), () => { naming = false; SimProfiles.Remove(p); });
            delete.Padding = new Thickness(10, 3, 10, 3);
            delete.Margin = new Thickness(6, 0, 0, 0);
            buttons.Children.Add(delete);
        }
        panel.Children.Add(buttons);
        return Card(panel);
    }

    /// <summary>New: a game's profile, one with no game, or a copy of the one in use; then its name is asked for.</summary>
    private static void NewProfileMenu(Button anchor, SimProfile current)
    {
        void Add(SimProfile p) { naming = true; SimProfiles.Add(p); }
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var g in Games.All)
        {
            var item = new MenuItem { Header = Games.Title(g) };
            item.Click += (_, _) => Add(new SimProfile { Name = Games.Title(g), Game = g, Port = g == Telemetry.Game.ForzaMotorsport ? 5301 : 8000, Programs = Games.Programs(g) });
            menu.Items.Add(item);
        }
        var none = new MenuItem { Header = T("No game") };
        none.Click += (_, _) => Add(new SimProfile { Name = T("Desktop") });
        menu.Items.Add(none);
        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = F("Copy of {0}", current.Name) };
        copy.Click += (_, _) => Add(new SimProfile { Name = current.Name, Game = current.Game, Version = current.Version, Port = current.Port, Programs = current.Programs.ToList(), AutoStart = current.AutoStart });
        menu.Items.Add(copy);
        menu.IsOpen = true;
    }

    // ---- pieces (the Shaker's) ----

    /// <summary>A part's title, with room above it.</summary>
    private static TextBlock Section(string text, string? tip) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 4), ToolTip = tip };

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

    public static Border Card(UIElement child)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        return b;
    }

    public static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
        return t;
    }
}
