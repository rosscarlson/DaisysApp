using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// SimEffects → Profiles (since 3.18.0): the module's profiles, a block per game (since 4.17.17: <see cref="ProfileCard"/>,
/// its Options with Auto Start: <see cref="GameSwitcher"/>, and its Modules). The Profile drop-down at the right of the
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

    private static Hotkey? playKey;
    private static readonly SolidColorBrush PlayingBrush = Ui.Brush(Color.FromRgb(0x2F, 0x6F, 0xEB));


    /// <summary>
    /// "Profile:", a drop-down of the profiles, Play / Stop (the whole profile) and its shortcut, at the right of the
    /// sub-tabs' row.
    /// </summary>
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

        playKey ??= Hotkeys.Add(new HotkeySpec
        {
            Id = "SimEffects.Play", Module = T("SimEffects"), Name = T("Play / Stop"),
            Get = () => SimProfiles.Settings.PlayKey, Set = v => { SimProfiles.Settings.PlayKey = v; SimProfiles.Save(); },
        }, () => SimProfiles.Play(!SimProfiles.Playing, "shortcut"));
        var glyph = new TextBlock { FontSize = 12 };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var play = new Button { Content = glyph, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0) };
        play.Click += (_, _) => SimProfiles.Play(!SimProfiles.Playing, "Play / Stop");
        var key = HotkeyWindow.Button(T("Play / Stop shortcut"), () => playKey);
        key.Margin = new Thickness(2, 0, 0, 0);
        void ShowPlay()
        {
            // (since 3.24.3) stopped: white, green once the game runs; playing: blue with the game running, red without
            bool on = SimProfiles.Playing, game = SimData.GameRunning;
            glyph.Text = on ? "\uE71A" : "\uE768";
            if (on && game) play.Background = PlayingBrush;
            else if (on || game) play.SetResourceReference(Control.BackgroundProperty, on ? "DangerBrush" : "SuccessBrush");
            else play.Background = Brushes.White;
            glyph.Foreground = on || game ? Brushes.White : Brushes.Black;
            play.ToolTip = on ? T("Stop the profile") : T("Play the profile");
            AutomationProperties.SetName(play, on ? T("Stop") : T("Play"));
        }
        SimProfiles.Changed += ShowPlay;
        SimData.GameRunningChanged += ShowPlay;
        ShowPlay();
        row.Children.Add(play);
        row.Children.Add(key);
        return row;
    }
}

internal sealed class ProfilesView : UserControl
{
    /// <summary>A block's narrowest: fewer columns when the tab is narrower than this many.</summary>
    private const double BlockWidth = 300;
    private readonly StackPanel page = new();
    private readonly Grid blocks = new();
    private int columns;

    public ProfilesView()
    {
        Content = SimUi.Scroll(page);
        // (a tool switched on or off on its own tab shows here when this one shows again)
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
        SizeChanged += (_, _) => { if (Columns() != columns) Place(); };
    }

    /// <summary>(since 4.17.17) A block per game (and Desktop, no game), 3 or 4 across, as fits their number and the
    /// width; Analysis' units above them (the same in every profile).</summary>
    public void Render()
    {
        page.Children.Clear();
        if (ProfileCard.UnitBoxes?.Invoke() is var (units, temperature))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new TextBlock { Text = T("Units"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            foreach (var (label, box) in new[] { (T("System"), units), (T("Temperature"), temperature) })
            {
                row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                box.MinWidth = 110;
                box.Margin = new Thickness(0, 0, 16, 0);
                row.Children.Add(box);
            }
            page.Children.Add(row);
        }
        blocks.Children.Clear();
        var active = SimProfiles.Active;
        foreach (var g in Games.All.Cast<Game?>().Append(null))
            if ((active.Game == g ? active : SimProfiles.All.FirstOrDefault(p => p.Game == g)) is { } p)
                blocks.Children.Add(ProfileCard.Build(g, p));
        Place();
        page.Children.Add(blocks);
    }

    /// <summary>3 across, or 4 where that fills the rows better (4, 7, 8 blocks), fewer when it's narrow.</summary>
    private int Columns()
    {
        int n = Math.Max(1, blocks.Children.Count);
        int wanted = n % 3 == 0 ? 3 : n % 4 == 0 || n > 6 ? 4 : 3;
        double width = ActualWidth > 0 ? ActualWidth : wanted * BlockWidth;
        return Math.Clamp((int)(width / BlockWidth), 1, wanted);
    }

    private void Place()
    {
        columns = Columns();
        blocks.ColumnDefinitions.Clear();
        blocks.RowDefinitions.Clear();
        for (int c = 0; c < columns; c++) blocks.ColumnDefinitions.Add(new ColumnDefinition());
        int i = 0;
        foreach (UIElement b in blocks.Children)
        {
            if (i % columns == 0) blocks.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(b, i / columns);
            Grid.SetColumn(b, i % columns);
            i++;
        }
    }
}
