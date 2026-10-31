using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// SimEffects → Profiles (since 3.18.0): the module's profiles. Left: the profile in use (<see cref="ProfileCard"/>,
/// with its Auto Start: <see cref="GameSwitcher"/>); right: which sub-modules start with it (Auto Start Modules, a tile each). The Profile drop-down at the right of the
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
    private static readonly SolidColorBrush PlayingBrush = Frozen(Color.FromRgb(0x2F, 0x6F, 0xEB));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

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
        var key = VolumeControl.IconButton("\uE7FC");
        key.Margin = new Thickness(2, 0, 0, 0);
        AutomationProperties.SetName(key, T("Play / Stop shortcut"));
        key.Click += (_, _) => { HotkeyWindow.Open(T("Play / Stop shortcut"), Window.GetWindow(key), playKey); ShowPlay(); };
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
            ((TextBlock)key.Content).SetResourceReference(TextBlock.ForegroundProperty, playKey!.Value != null ? "AccentBrush" : "TextBrush");
            key.ToolTip = playKey.Value is { } k ? F("Play / Stop shortcut: {0}", k) : T("Play / Stop shortcut");
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
    private readonly StackPanel left = new(), right = new();

    public ProfilesView()
    {
        Content = SimUi.Columns(left, right, 360);
        // (a tool switched on or off on its own tab shows here when this one shows again)
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
    }

    public void Render()
    {
        left.Children.Clear();
        left.Children.Add(ProfileCard.Build(SimProfiles.Active));
        right.Children.Clear();
        right.Children.Add(ToolsCard());
    }

    /// <summary>The profile in use's sub-modules, a tile each with its checkbox: started when the profile is (by a game or by hand) or not.</summary>
    private static Border ToolsCard()
    {
        var panel = new StackPanel();
        var p = SimProfiles.Active;
        panel.Children.Add(Ui.Header(T("Auto Start Modules")));
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 4, -5, 0) };
        foreach (var t in SimProfiles.Tools.Where(t => t.IsOn != null))
        {
            bool on = SimProfiles.IsOn(p, t);
            var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsHitTestVisible = false, Focusable = false };
            var name = new TextBlock { Text = t.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var tile = new ToggleButton
            {
                IsChecked = on, Margin = new Thickness(5), MinHeight = 64, Padding = new Thickness(14, 10, 14, 10), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left, Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, name } },
                Template = TileTemplate(),
            };
            AutomationProperties.SetName(tile, $"{t.Title} · {p.Name}");
            // (Checked / Unchecked, not Click: a toggle through UI Automation raises no Click)
            tile.Checked += (_, _) => { box.IsChecked = true; SimProfiles.SetOn(p, t, true); };
            tile.Unchecked += (_, _) => { box.IsChecked = false; SimProfiles.SetOn(p, t, false); };
            tiles.Children.Add(tile);
        }
        panel.Children.Add(tiles);
        return Ui.Card(panel);
    }

    /// <summary>A tile: a rounded square, the accent's edge and a tint of it while it's ticked.</summary>
    private static ControlTemplate TileTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Tile");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        var tint = new FrameworkElementFactory(typeof(Border), "Tint");
        tint.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        tint.SetValue(UIElement.OpacityProperty, 0.0);
        tint.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(tint);
        grid.AppendChild(border);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = grid };
        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.16, "Tint"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "Tile"));
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent, "Tile"));
        template.Triggers.Add(checkedTrigger);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ControlBorderStrongBrush"), "Tile"));
        template.Triggers.Add(hover);
        return template;
    }
}
