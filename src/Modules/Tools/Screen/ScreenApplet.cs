using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>
/// Screen, a sub-tab of the Tools tab: tools for the desktop's windows. <b>Spacebar Drag</b> (<see cref="SpaceDrag"/>),
/// on or off here or by its shortcut (Core's Hotkeys), from anywhere; <b>Always on top</b> (<see cref="TopMost"/>), an
/// item in every window's title bar menu, and a shortcut for the window in front; <b>Black screens</b>
/// (<see cref="Blackout"/>), every monitor (or those picked, <see cref="ScreenPicker"/>) black and back by its
/// shortcut, when switched on here. Each in a card of its own with its settings, the cards in rows of up to 5
/// (<see cref="Grid"/>). The shortcuts are Core's Hotkeys (keys and controller buttons); Spacebar Drag's and Always on
/// top's own hooks are for what they do (the held spacebar, the title bar menu), only while they're on.
/// </summary>
public sealed class ScreenApplet : IApplet
{
    private readonly ScreenSettings settings = ScreenSettings.Load();
    private readonly Logger log = Log.Here;
    private readonly SpaceDrag drag;
    private readonly TopMost topMost;
    private readonly Blackout blackout = new();
    private readonly Hotkey hotkey, topKey, blackKey;
    private readonly ToggleButton onOff, topOnOff, blackOnOff;
    private readonly ScreenPicker picker;
    private readonly FrameworkElement view;

    public ScreenApplet()
    {
        drag = new SpaceDrag(log);
        topMost = new TopMost(log, T("Always on &top"));
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Screen.SpaceDrag", Module = T("Screen"), Name = T("Spacebar Drag on / off"),
            Get = () => settings.SpaceDragKey, Set = v => { settings.SpaceDragKey = v; settings.Save(); },
        }, () => SetSpaceDrag(!settings.SpaceDrag));
        topKey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Screen.TopMost", Module = T("Screen"), Name = T("Always on top: the window in front"),
            Get = () => settings.TopMostKey, Set = v => { settings.TopMostKey = v; settings.Save(); },
        }, ToggleFront);
        blackKey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Screen.Blackout", Module = T("Screen"), Name = T("Black screens on / off"),
            Get = () => settings.BlackoutKey, Set = v => { settings.BlackoutKey = v; settings.Save(); },
        }, () => { if (settings.Blackout) blackout.Set(!blackout.On); });
        blackout.Covers = name => settings.BlackoutAll || settings.BlackoutScreens.Contains(name);
        blackout.Changed += () => log.Info(blackout.On ? "Black screens: monitors black" : "Black screens: monitors back");

        onOff = OnOff(T("Spacebar Drag on"), on => SetSpaceDrag(on));
        topOnOff = OnOff(T("Always on top on"), on => SetTopMost(on));
        blackOnOff = OnOff(T("Black screens on"), on => SetBlackout(on));
        ShowOnOff();

        var all = new CheckBox { Content = T("All screens"), IsChecked = settings.BlackoutAll, Margin = new Thickness(0, 10, 0, 0),
            ToolTip = T("Untick to pick the screens that go black") };
        picker = new ScreenPicker(name => settings.BlackoutScreens.Contains(name), PickScreen);
        picker.Visibility = settings.BlackoutAll ? Visibility.Collapsed : Visibility.Visible;
        Ui.OnToggle(all, SetAllScreens);

        view = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = Grid(
                Card(T("Spacebar Drag"), T("Hold the spacebar and drag a window from anywhere on it"), onOff, KeyButton(T("On / off shortcut"), () => hotkey)),
                Card(T("Always on top"), T("Always on top in a window's title bar menu (right-click its title bar)"), topOnOff, KeyButton(T("Always on top shortcut"), () => topKey)),
                Card(T("Black screens"), T("Its shortcut turns the screens black and back (a double-click on one also ends it)"), blackOnOff, KeyButton(T("Black screens shortcut"), () => blackKey), all, picker)),
        };
    }

    /// <summary>
    /// The cards side by side, as wide as the page allows: one row up to 5, then as few rows as that takes with the
    /// cards shared out evenly (6: 2 rows of 3; 7: 4 and 3).
    /// </summary>
    private static UniformGrid Grid(params Border[] cards)
    {
        int rows = (cards.Length + 4) / 5;
        var grid = new UniformGrid { Rows = rows, Columns = (cards.Length + rows - 1) / rows, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, -12, 0) };
        foreach (var c in cards) grid.Children.Add(c);
        return grid;
    }

    /// <summary>A feature's card: its name (its tooltip says what it does), On / Off, its shortcut's button and what it's
    /// bound to, then its settings.</summary>
    private static Border Card(string name, string tip, ToggleButton onOff, FrameworkElement key, params UIElement[] settings)
    {
        var panel = new StackPanel();
        var header = Ui.Header(name);
        header.ToolTip = tip;
        panel.Children.Add(header);
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(onOff, Dock.Left);
        row.Children.Add(onOff);
        row.Children.Add(key);
        panel.Children.Add(row);
        foreach (var s in settings) panel.Children.Add(s);
        var card = Ui.Card(panel, new Thickness(0, 0, 12, 12));
        card.ToolTip = tip;
        card.MinWidth = 220;
        return card;
    }

    private static ToggleButton OnOff(string name, Action<bool> set)
    {
        var b = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => set(b.IsChecked == true);
        return b;
    }

    /// <summary>The shortcut's joystick button, and beside it what it's bound to ("Unbound" when nothing is).</summary>
    private static FrameworkElement KeyButton(string name, Func<Hotkey> hotkey)
    {
        var b = HotkeyWindow.Button(name, () => hotkey());
        b.Margin = new Thickness(6, 0, 0, 0);
        var bound = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        void Show()
        {
            var text = hotkey().Text;
            bound.Text = text ?? T("Unbound");
            bound.ToolTip = text;
            bound.SetResourceReference(TextBlock.ForegroundProperty, text != null ? "TextBrush" : "TextSecondaryBrush");
        }
        bound.Loaded += (_, _) => { Hotkeys.Changed += Show; Show(); };
        bound.Unloaded += (_, _) => Hotkeys.Changed -= Show;
        var row = new DockPanel();
        DockPanel.SetDock(b, Dock.Left);
        row.Children.Add(b);
        row.Children.Add(bound);
        return row;
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        if (settings.SpaceDrag) drag.Start();
        if (settings.TopMost) topMost.Start();
    }

    private void SetTopMost(bool on)
    {
        if (settings.TopMost != on)
        {
            settings.TopMost = on;
            settings.Save();
            log.Info(on ? "Always on top on" : "Always on top off");
        }
        if (on) topMost.Start(); else topMost.Stop();
        ShowOnOff();
    }

    /// <summary>The shortcut: the window in front on top, or not (with the title bar item on or off).</summary>
    private void ToggleFront()
    {
        var w = TopMost.Front();
        if (w == IntPtr.Zero) return;
        var title = TopMost.TitleOf(w);
        if (TopMost.Toggle(w, out var on)) log.Info(on ? $"Always on top on: {title}" : $"Always on top off: {title}");
        else log.Warn($"Always on top: Windows didn't let it change {title} (run as administrator?)");
    }

    private void SetSpaceDrag(bool on)
    {
        if (settings.SpaceDrag != on)
        {
            settings.SpaceDrag = on;
            settings.Save();
            log.Info(on ? "Spacebar Drag on" : "Spacebar Drag off");
        }
        if (on) drag.Start(); else drag.Stop();
        ShowOnOff();
    }

    /// <summary>On lets the shortcut black the monitors (it never does it itself); Off also brings them back.</summary>
    private void SetBlackout(bool on)
    {
        if (settings.Blackout != on)
        {
            settings.Blackout = on;
            settings.Save();
            log.Info(on ? "Black screens on" : "Black screens off");
        }
        if (!on) blackout.Set(false);
        ShowOnOff();
    }

    /// <summary>All screens, or those picked (the first time: every screen there is now, picked to start with).</summary>
    private void SetAllScreens(bool all)
    {
        settings.BlackoutAll = all;
        if (!all && settings.BlackoutScreens.Count == 0) settings.BlackoutScreens.AddRange(Blackout.Displays().Select(d => d.Name));
        settings.Save();
        log.Info(all ? "Black screens: all screens" : $"Black screens: {settings.BlackoutScreens.Count} screens picked");
        picker.Visibility = all ? Visibility.Collapsed : Visibility.Visible;
        picker.Draw();
        blackout.Refit();
    }

    private void PickScreen(string name)
    {
        if (!settings.BlackoutScreens.Remove(name)) settings.BlackoutScreens.Add(name);
        settings.Save();
        log.Info($"Black screens: {name} {(settings.BlackoutScreens.Contains(name) ? "picked" : "left out")}");
        blackout.Refit();
    }

    private void ShowOnOff()
    {
        onOff.IsChecked = settings.SpaceDrag;
        onOff.Content = settings.SpaceDrag ? T("On") : T("Off");
        topOnOff.IsChecked = settings.TopMost;
        topOnOff.Content = settings.TopMost ? T("On") : T("Off");
        blackOnOff.IsChecked = settings.Blackout;
        blackOnOff.Content = settings.Blackout ? T("On") : T("Off");
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        hotkey.Dispose();
        topKey.Dispose();
        blackKey.Dispose();
        blackout.Dispose();
        picker.Dispose();
        drag.Dispose();
        topMost.Dispose();
    }
}
