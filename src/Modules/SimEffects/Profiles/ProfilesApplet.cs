using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// SimEffects → Profiles (since 3.18.0): the module's profiles. Left: the profile in use (<see cref="ProfileCard"/>)
/// and Game Switching; right: which sub-modules are on in each profile. The Profile drop-down at the right of the
/// sub-tabs (<see cref="Picker"/>) switches every tool.
/// </summary>
public sealed class ProfilesApplet : IApplet
{
    private readonly ProfilesView view = new();
    private readonly GameSwitcher switcher = new();

    public ProfilesApplet() => SimProfiles.Changed += view.Render;

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        view.Render();
        switcher.Start();
    }

    public void SaveSettings() => SimProfiles.Save();

    public void Dispose()
    {
        SimProfiles.Changed -= view.Render;
        switcher.Dispose();
        SimProfiles.Save();
    }

    /// <summary>"Profile:" and a drop-down of the profiles, at the right of the sub-tabs' row.</summary>
    public static FrameworkElement Picker()
    {
        var box = new ComboBox { MinWidth = 180, VerticalAlignment = VerticalAlignment.Center, DisplayMemberPath = nameof(SimProfile.Name) };
        AutomationProperties.SetName(box, T("Profile"));
        bool filling = false;
        void Fill()
        {
            filling = true;
            box.ItemsSource = null;
            box.ItemsSource = SimProfiles.All.ToList();
            box.SelectedItem = SimProfiles.Active;
            filling = false;
        }
        box.SelectionChanged += (_, _) => { if (!filling && box.SelectedItem is SimProfile p) SimProfiles.Use(p, "picked"); };
        SimProfiles.Changed += Fill;
        Fill();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = T("Profile:"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(box);
        return row;
    }
}

internal sealed class ProfilesView : UserControl
{
    private readonly StackPanel left = new(), right = new();

    public ProfilesView()
    {
        Content = Ui.Columns(left, right, 360);
        // (a tool switched on or off on its own tab shows here when this one shows again)
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
    }

    public void Render()
    {
        left.Children.Clear();
        left.Children.Add(ProfileCard.Build(SimProfiles.Active));
        left.Children.Add(SwitchingCard());
        right.Children.Clear();
        right.Children.Add(ToolsCard());
    }

    private static Border SwitchingCard()
    {
        var s = SimProfiles.Settings;
        var panel = new StackPanel();
        panel.Children.Add(ProfileCard.Header(T("Game Switching")));
        var on = new CheckBox { Content = T("Game Switching"), IsChecked = s.GameSwitching, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = T("A profile's program starting switches every tool to it") };
        on.Click += (_, _) => { s.GameSwitching = on.IsChecked == true; SimProfiles.Save(); };
        panel.Children.Add(on);
        var names = new List<string> { T("Stay") };
        names.AddRange(SimProfiles.All.Select(p => p.Name));
        var none = new ComboBox { ItemsSource = names, SelectedIndex = Math.Max(0, names.IndexOf(s.NoGame ?? "\0")), ToolTip = T("Used while none of the programs runs") };
        AutomationProperties.SetName(none, T("No Game"));
        none.SelectionChanged += (_, _) => { s.NoGame = none.SelectedIndex <= 0 ? null : names[none.SelectedIndex]; SimProfiles.Save(); };
        panel.Children.Add(ProfileCard.Field(T("No Game"), none));
        var card = ProfileCard.Card(panel);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    /// <summary>A row per sub-module, a column per profile: on in that profile or not.</summary>
    private static Border ToolsCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(ProfileCard.Header(T("Sub-modules")));
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        var profiles = SimProfiles.All;
        foreach (var _ in profiles) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < profiles.Count; c++)
        {
            var head = new TextBlock { Text = profiles[c].Name, Margin = new Thickness(8, 0, 8, 6), TextAlignment = TextAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = profiles[c] == SimProfiles.Active ? FontWeights.Bold : FontWeights.Normal, ToolTip = profiles[c].Name };
            Grid.SetColumn(head, c + 1);
            grid.Children.Add(head);
        }
        int r = 1;
        foreach (var t in SimProfiles.Tools.Where(t => t.IsOn != null))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = t.Title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(name, r);
            grid.Children.Add(name);
            for (int c = 0; c < profiles.Count; c++)
            {
                var p = profiles[c];
                var box = new CheckBox { IsChecked = SimProfiles.IsOn(p, t), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(box, $"{t.Title} · {p.Name}");
                box.Click += (_, _) => SimProfiles.SetOn(p, t, box.IsChecked == true);
                Grid.SetRow(box, r);
                Grid.SetColumn(box, c + 1);
                grid.Children.Add(box);
            }
            r++;
        }
        panel.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return ProfileCard.Card(panel);
    }
}
