using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DaisysApp.Applets.SimEffects.BassShaker;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// The Profile card, the same in every tool that has profiles (the Shaker, Input Overlay, Arduino, AmbiLight): at the
/// top the profile in use and New (a game's defaults, or a copy of this one); then its name, Game, UDP Port (Forza,
/// where the tool reads the game), Programs, Game Switching and Switch On With Game, the tool's own buttons, and Delete.
/// </summary>
internal sealed class ProfileCard<T> where T : GameProfile, new()
{
    /// <summary>The tool, for the log ("Shaker").</summary>
    public required string Tool { get; init; }
    public required IProfileSettings<T> Settings { get; init; }
    /// <summary>Use a profile (picked here): the tool sets it active and takes it in.</summary>
    public required Action<T> Use { get; init; }
    /// <summary>Something changed (renamed, game, port, programs, added, deleted): the tool takes it in, saves, draws again.</summary>
    public required Action Changed { get; init; }
    /// <summary>Saves (the switches).</summary>
    public required Action Save { get; init; }
    /// <summary>A game's new profile (its defaults).</summary>
    public Func<Game, T> Create { get; init; } = GameProfiles.For<T>;
    /// <summary>A copy made with New → Copy of: anything in it that must be its own (the Shaker's effects' keys).</summary>
    public Action<T>? Copied { get; init; }
    /// <summary>The UDP port shows (for Forza) where the tool reads the game's telemetry.</summary>
    public bool ShowPort { get; init; } = true;
    /// <summary>The tool's own buttons, before Delete (the Shaker's Reset Effects).</summary>
    public Action<WrapPanel, T>? Buttons { get; init; }

    private IProfileSettings<T> settings => Settings;

    public Border Build(T p)
    {
        var panel = new StackPanel();
        // the profile in use, and New, at the top
        var profiles = new ComboBox { ItemsSource = settings.Profiles, DisplayMemberPath = nameof(GameProfile.Name), SelectedItem = p, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(profiles, T("Profile"));
        profiles.SelectionChanged += (_, _) => { if (profiles.SelectedItem is T picked && picked != p) Use(picked); };
        var newProfile = new Button { Content = T("New"), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
        newProfile.Click += (_, _) => NewProfileMenu(newProfile, p);
        var pick = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var title = Header(T("Profile"));
        title.Margin = new Thickness(0, 0, 10, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(title, Dock.Left);
        pick.Children.Add(title);
        DockPanel.SetDock(newProfile, Dock.Right);
        pick.Children.Add(newProfile);
        pick.Children.Add(profiles);
        panel.Children.Add(pick);

        var name = new TextBox { Text = p.Name };
        void Rename()
        {
            string n = name.Text.Trim();
            if (n.Length == 0 || n == p.Name) { name.Text = p.Name; return; }
            if (settings.Profiles.Any(o => o != p && o.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase))) { name.Text = p.Name; return; }
            bool active = settings.Active == p.Name || settings.Active() == p;
            p.Name = n;
            if (active) settings.Active = n;
            Changed();
        }
        name.LostFocus += (_, _) => Rename();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); else if (e.Key == Key.Escape) name.Text = p.Name; };
        panel.Children.Add(Field(T("Name"), name));

        var game = new ComboBox { ItemsSource = Games.All.Select(g => new KeyValuePair<Game, string>(g, Games.Title(g))).ToList(), DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = p.Game };
        game.SelectionChanged += (_, _) =>
        {
            if (game.SelectedValue is Game g && g != p.Game) { p.Game = g; Changed(); }
        };
        panel.Children.Add(Field(T("Game"), game));

        if (ShowPort && Games.UsesPort(p.Game))
        {
            var port = new TextBox { Text = p.Port.ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = T("The game's Data Out: IP 127.0.0.1 and this port") };
            void SetPort()
            {
                if (int.TryParse(port.Text.Trim(), out int v) && v is >= 1024 and <= 65535 && v != p.Port) { p.Port = v; Changed(); }
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
            x.Click += (_, _) => { p.Programs.Remove(prog); Changed(); };
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
            Changed();
        }
        addProg.Click += (_, _) => AddProg();
        newProg.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddProg(); };
        var addLine = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(addProg, Dock.Right);
        addLine.Children.Add(addProg);
        addLine.Children.Add(newProg);
        panel.Children.Add(addLine);

        var switching = new CheckBox { Content = T("Game Switching"), IsChecked = settings.GameSwitching, Margin = new Thickness(0, 10, 0, 0),
            ToolTip = T("Use a profile while one of its programs runs") };
        var switchOn = new CheckBox { Content = T("Switch On With Game"), IsChecked = settings.SwitchOnWithGame, Margin = new Thickness(0, 4, 0, 0),
            IsEnabled = settings.GameSwitching, ToolTip = T("On while a profile's program runs, off again when it closes") };
        switching.Click += (_, _) => { settings.GameSwitching = switching.IsChecked == true; switchOn.IsEnabled = settings.GameSwitching; Save(); };
        switchOn.Click += (_, _) => { settings.SwitchOnWithGame = switchOn.IsChecked == true; Save(); };
        panel.Children.Add(switching);
        panel.Children.Add(switchOn);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        Buttons?.Invoke(buttons, p);
        if (settings.Profiles.Count > 1)
            buttons.Children.Add(ConfirmButton(T("Delete"), () =>
            {
                settings.Profiles.Remove(p);
                settings.Active = settings.Profiles[0].Name;
                Changed();
            }));
        if (buttons.Children.Count > 0) panel.Children.Add(buttons);
        return Card(panel);
    }

    private void NewProfileMenu(Button anchor, T current)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var g in Games.All)
        {
            var item = new MenuItem { Header = Games.Title(g) };
            item.Click += (_, _) => AddProfile(Create(g));
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = F("Copy of {0}", current.Name) };
        copy.Click += (_, _) =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(current, DaisysApp.Settings.JsonStore.Options);
            var p = System.Text.Json.JsonSerializer.Deserialize<T>(json, DaisysApp.Settings.JsonStore.Options)!;
            Copied?.Invoke(p);
            AddProfile(p);
        };
        menu.Items.Add(copy);
        menu.IsOpen = true;
    }

    private void AddProfile(T p)
    {
        string baseName = p.Name;
        for (int i = 2; settings.Profiles.Any(o => o.Name.Equals(p.Name, StringComparison.CurrentCultureIgnoreCase)); i++) p.Name = $"{baseName} {i}";
        settings.Profiles.Add(p);
        settings.Active = p.Name;
        Logging.Log.Here.Info($"{Tool}: profile {p.Name} added");
        Changed();
    }

    // ---- pieces (the Shaker's) ----

    private static Grid Field(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static Border Card(UIElement child)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        return b;
    }

    private static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
        return t;
    }

    private static Button ConfirmButton(string text, Action confirmed, string? tip = null)
    {
        var b = DaisysApp.Shared.ConfirmButton.Make(text, confirmed, tip);
        b.Padding = new Thickness(8, 3, 8, 3);
        b.Margin = new Thickness(0, 0, 5, 5);
        return b;
    }
}
