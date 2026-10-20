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
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        var rows = new (string Label, string Tip, Func<string?> Get, Action<string?> Set)[]
        {
            (T("Start / stop the virtual webcam"), T("Starts it with the sound chosen last time"), () => s.WebcamHotkey, v => s.WebcamHotkey = v),
            (T("Mute / unmute the virtual webcam"), T("It keeps running but shows the VIDEO MUTED card"), () => s.WebcamMuteHotkey, v => s.WebcamMuteHotkey = v),
            (T("Cameras off / on"), T("Takes every camera off the picture: the webcam (and a recording) carry on with just the screen"), () => s.CamerasOffHotkey, v => s.CamerasOffHotkey = v),
        };
        void Check()
        {
            var failed = rows.Select(r => r.Get()).Where(h => h != null && service.FailedHotkeys.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
            warning.Text = failed.Count > 0 ? F("{0} is already used by another program, so it won't work. Pick another.", string.Join(", ", failed)) : "";
            warning.Visibility = failed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var row in rows)
        {
            var label = new TextBlock { Text = row.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
            var tip = new TextBlock { Text = row.Tip, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            tip.SetResourceReference(StyleProperty, "SecondaryText");
            var box = new ShortcutBox { AllowControllerButtons = true, Value = row.Get(), MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
            box.Attach(service.SuspendHotkeys, service.RegisterHotkeys);
            var set = row.Set;
            box.Changed += () =>
            {
                set(box.Value);
                s.Save();
                service.RegisterHotkeys();
                Check();
            };
            panel.Children.Add(label);
            panel.Children.Add(tip);
            panel.Children.Add(box);
        }
        panel.Children.Add(warning);
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
        Check();
    }
}
