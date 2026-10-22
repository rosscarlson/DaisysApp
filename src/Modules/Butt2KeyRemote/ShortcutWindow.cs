using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// The on / off shortcut, in a small window of its own (the joystick button beside Status): it listens straight away
/// for keys (with Ctrl, Alt or Shift) or a controller / button box button, and closes once one is pressed; × clears it.
/// </summary>
internal sealed class ShortcutWindow : Window
{
    public ShortcutWindow(ButtHub hub)
    {
        Title = T("On / off shortcut");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = T("Press keys (with Ctrl, Alt or Shift) or a controller or button box button: it switches Button2Key Remote on and off from anywhere."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var box = new ShortcutBox { AllowControllerButtons = true, Value = hub.Settings.Hotkey };
        AutomationProperties.SetName(box, T("On / off shortcut"));
        box.Attach(hub.SuspendHotkey, hub.RegisterHotkey);
        box.Changed += () =>
        {
            hub.Settings.Hotkey = box.Value;
            hub.Save();
            hub.RegisterHotkey();
            hub.Publish();
            if (box.Value != null) Close();
        };
        panel.Children.Add(box);
        var done = new Button { Content = T("Done"), MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(done);
        Content = panel;
        // (listening as soon as it opens: its capture button pressed for you)
        Loaded += (_, _) =>
        {
            if (LogicalTreeHelper.FindLogicalNode(box, "CaptureButton") is Button capture)
                capture.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        };
    }
}
