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
/// item in every window's title bar menu, and a shortcut for the window in front.
/// </summary>
public sealed class ScreenApplet : IApplet
{
    private readonly ScreenSettings settings = ScreenSettings.Load();
    private readonly Logger log = Log.Here;
    private readonly SpaceDrag drag;
    private readonly TopMost topMost;
    private readonly Hotkey hotkey, topKey;
    private readonly ToggleButton onOff, topOnOff;
    private readonly Button key, topKeyButton;
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

        onOff = OnOff(T("Spacebar Drag on"), on => SetSpaceDrag(on));
        key = KeyButton(T("On / off shortcut"), () => hotkey);
        topOnOff = OnOff(T("Always on top on"), on => SetTopMost(on));
        topKeyButton = KeyButton(T("Always on top shortcut"), () => topKey);
        ShowOnOff();

        var card = new StackPanel();
        card.Children.Add(Ui.Header(T("Windows")));
        card.Children.Add(Row(T("Spacebar Drag"), T("Hold the spacebar and drag a window from anywhere on it"), onOff, key));
        card.Children.Add(Row(T("Always on top"), T("Always on top in a window's title bar menu (right-click its title bar)"), topOnOff, topKeyButton));
        var box = Ui.Card(card);
        box.VerticalAlignment = VerticalAlignment.Top;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.MinWidth = 320;
        view = new ScrollViewer { Content = box, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static ToggleButton OnOff(string name, Action<bool> set)
    {
        var b = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => set(b.IsChecked == true);
        return b;
    }

    private static Button KeyButton(string name, Func<Hotkey> hotkey)
    {
        var b = HotkeyWindow.Button(name, () => hotkey());
        b.Margin = new Thickness(6, 0, 0, 0);
        return b;
    }

    private static DockPanel Row(string name, string tip, ToggleButton onOff, Button key)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 100, ToolTip = tip });
        row.Children.Add(onOff);
        row.Children.Add(key);
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

    private void ShowOnOff()
    {
        onOff.IsChecked = settings.SpaceDrag;
        onOff.Content = settings.SpaceDrag ? T("On") : T("Off");
        topOnOff.IsChecked = settings.TopMost;
        topOnOff.Content = settings.TopMost ? T("On") : T("Off");
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        hotkey.Dispose();
        topKey.Dispose();
        drag.Dispose();
        topMost.Dispose();
    }
}
