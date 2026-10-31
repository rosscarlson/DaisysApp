using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>
/// Screen, a sub-tab of the Tools tab: tools for the desktop's windows. For now <b>Spacebar Drag</b> (<see
/// cref="SpaceDrag"/>), on or off here or by its shortcut (Core's Hotkeys), from anywhere.
/// </summary>
public sealed class ScreenApplet : IApplet
{
    private readonly ScreenSettings settings = ScreenSettings.Load();
    private readonly Logger log = Log.Here;
    private readonly SpaceDrag drag;
    private readonly Hotkey hotkey;
    private readonly ToggleButton onOff;
    private readonly Button key;
    private readonly FrameworkElement view;

    public ScreenApplet()
    {
        drag = new SpaceDrag(log);
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Screen.SpaceDrag", Module = T("Screen"), Name = T("Spacebar Drag on / off"),
            Get = () => settings.SpaceDragKey, Set = v => { settings.SpaceDragKey = v; settings.Save(); },
        }, () => SetSpaceDrag(!settings.SpaceDrag));

        onOff = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        onOff.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
        AutomationProperties.SetName(onOff, T("Spacebar Drag on"));
        onOff.Click += (_, _) => SetSpaceDrag(onOff.IsChecked == true);
        ShowOnOff();
        key = Ui.IconButton("", "");
        key.Margin = new Thickness(6, 0, 0, 0);
        key.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(key, T("On / off shortcut"));
        key.Click += (_, _) => { HotkeyWindow.Open(T("On / off shortcut"), Window.GetWindow(key), hotkey); ShowKey(); };
        hotkey.Changed += ShowKey;
        ShowKey();

        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var name = new TextBlock { Text = T("Spacebar Drag"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = T("Hold the spacebar and drag a window from anywhere on it") };
        row.Children.Add(name);
        row.Children.Add(onOff);
        row.Children.Add(key);
        row.LastChildFill = false;
        var card = new StackPanel();
        card.Children.Add(Ui.Header(T("Windows")));
        card.Children.Add(row);
        var box = Ui.Card(card);
        box.VerticalAlignment = VerticalAlignment.Top;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.MinWidth = 320;
        view = new ScrollViewer { Content = box, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        if (settings.SpaceDrag) drag.Start();
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
    }

    private void ShowKey()
    {
        ((TextBlock)((StackPanel)key.Content).Children[0]).SetResourceReference(TextBlock.ForegroundProperty, hotkey.Value != null ? "AccentBrush" : "TextBrush");
        key.ToolTip = hotkey.Value is { } k ? F("On / off shortcut: {0}", k) : T("On / off shortcut");
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        hotkey.Dispose();
        drag.Dispose();
    }
}
