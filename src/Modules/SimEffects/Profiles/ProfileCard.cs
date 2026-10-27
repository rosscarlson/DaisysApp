using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DaisysApp.Applets.SimEffects.BassShaker;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// The Profile card (the Shaker's until 3.18.0, now the whole module's, on the Profiles tab): the profile in use with
/// New and Delete, its name, Game (or none), UDP Port (Forza) and Programs.
/// </summary>
internal static class ProfileCard
{
    public static Border Build(SimProfile p)
    {
        var panel = new StackPanel();
        var newProfile = new Button { Content = T("New"), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
        newProfile.Click += (_, _) => NewProfileMenu(newProfile, p);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(newProfile, Dock.Right);
        head.Children.Add(newProfile);
        var title = Header(T("Profile"));
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        panel.Children.Add(head);

        var name = new TextBox { Text = p.Name };
        AutomationProperties.SetName(name, T("Name"));
        void Rename()
        {
            if (!SimProfiles.Rename(p, name.Text.Trim())) name.Text = p.Name;
        }
        name.LostFocus += (_, _) => Rename();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); else if (e.Key == Key.Escape) name.Text = p.Name; };
        panel.Children.Add(Field(T("Name"), name));

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

        panel.Children.Add(new TextBlock { Text = T("Programs"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4),
            ToolTip = T("With Game Switching on, one of these running picks this profile") });
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
        AutomationProperties.SetName(newProg, T("Program to add"));
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

        if (SimProfiles.All.Count > 1)
        {
            var delete = DaisysApp.Shared.ConfirmButton.Make(T("Delete"), () => SimProfiles.Remove(p));
            delete.Padding = new Thickness(8, 3, 8, 3);
            delete.Margin = new Thickness(0, 10, 0, 0);
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(delete);
        }
        return Card(panel);
    }

    private static void NewProfileMenu(Button anchor, SimProfile current)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var g in Games.All)
        {
            var item = new MenuItem { Header = Games.Title(g) };
            item.Click += (_, _) => SimProfiles.Add(new SimProfile { Name = Games.Title(g), Game = g, Port = g == Telemetry.Game.ForzaMotorsport ? 5301 : 8000, Programs = Games.Programs(g) });
            menu.Items.Add(item);
        }
        var none = new MenuItem { Header = T("No game") };
        none.Click += (_, _) => SimProfiles.Add(new SimProfile { Name = T("Desktop") });
        menu.Items.Add(none);
        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = F("Copy of {0}", current.Name) };
        copy.Click += (_, _) => SimProfiles.Add(new SimProfile { Name = current.Name, Game = current.Game, Port = current.Port, Programs = current.Programs.ToList() });
        menu.Items.Add(copy);
        menu.IsOpen = true;
    }

    // ---- pieces (the Shaker's) ----

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
