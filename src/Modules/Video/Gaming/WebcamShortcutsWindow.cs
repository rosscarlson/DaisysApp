using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Record → the shortcuts button beside the webcam's: its three shortcuts, each a key combination or a controller /
/// button box button: start / stop the virtual webcam (<see cref="GamingSettings.WebcamHotkey"/>), mute / unmute it
/// (<see cref="GamingSettings.WebcamMuteHotkey"/>) and cameras off / on (<see cref="GamingSettings.CamerasOffHotkey"/>),
/// and the title bar bubble. Saved as they're set; the same shortcuts are in Settings → Video → Overlay.
/// </summary>
internal sealed class WebcamShortcutsWindow : Window
{
    public WebcamShortcutsWindow(GamingSettings s, GamingService service)
    {
        Title = T("Virtual webcam shortcuts");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var panel = new StackPanel { Margin = new Thickness(20) };
        var rows = new (string Label, string Tip, string Name)[]
        {
            (T("Start / stop the virtual webcam"), T("Starts it with the sound chosen last time"), "Webcam"),
            (T("Mute / unmute the virtual webcam"), T("It keeps running but shows the VIDEO MUTED card"), "WebcamMute"),
            (T("Cameras off / on"), T("Takes every camera off the picture: the webcam (and a recording) carry on with just the screen"), "CamerasOff"),
        };
        foreach (var row in rows)
        {
            var label = new TextBlock { Text = row.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
            var tip = new TextBlock { Text = row.Tip, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            tip.SetResourceReference(StyleProperty, "SecondaryText");
            var box = new ShortcutBox(service.Hotkey(row.Name)) { MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(label);
            panel.Children.Add(tip);
            panel.Children.Add(box);
        }
        var hint = new TextBlock
        {
            Text = T("Click a box, then press a key combination (with Ctrl, Alt or Shift) or a controller / button box button; × clears it. They work anywhere, in games too. To hide one camera instead, give it a hide shortcut in Add Camera."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        };
        hint.SetResourceReference(StyleProperty, "SecondaryText");
        panel.Children.Add(hint);

        var badge = new CheckBox
        {
            Content = T("Show Webcam in the title bar while it's running"), IsChecked = s.WebcamTitleBadge, Margin = new Thickness(0, 12, 0, 0),
            ToolTip = T("A green Webcam bubble after the app's name and version, at the top left of the window (red while it's muted)"),
        };
        badge.Click += (_, _) => { s.WebcamTitleBadge = badge.IsChecked == true; s.Save(); service.NotifyChanged(); };
        panel.Children.Add(badge);

        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }
}
