using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Record → the shortcut beside Mute virtual webcam: sets the mute shortcut (<see cref="GamingSettings.WebcamMuteHotkey"/>),
/// a key combination or a controller / button box button. Saved as it's set; the same one is in Settings → Video → Overlay.
/// </summary>
internal sealed class WebcamMuteWindow : Window
{
    public WebcamMuteWindow(GamingSettings s, GamingService service)
    {
        Title = T("Mute shortcut");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = T("Mute / unmute the virtual webcam"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var box = new ShortcutBox { AllowControllerButtons = true, Value = s.WebcamMuteHotkey, MinWidth = 300 };
        box.Attach(service.SuspendHotkeys, service.RegisterHotkeys);
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        void Check()
        {
            bool failed = s.WebcamMuteHotkey != null && service.FailedHotkeys.Contains(s.WebcamMuteHotkey, StringComparer.OrdinalIgnoreCase);
            warning.Text = failed ? F("{0} is already used by another program, so it won't work. Pick another.", s.WebcamMuteHotkey) : "";
            warning.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        }
        box.Changed += () =>
        {
            s.WebcamMuteHotkey = box.Value;
            s.Save();
            service.RegisterHotkeys();
            Check();
        };
        panel.Children.Add(box);
        panel.Children.Add(warning);
        var hint = new TextBlock
        {
            Text = T("Click the box, then press a key combination (with Ctrl, Alt or Shift) or a controller / button box button; × clears it. It works anywhere, in games too. To hide one camera instead, give it a hide shortcut in Add Camera."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        };
        hint.SetResourceReference(StyleProperty, "SecondaryText");
        panel.Children.Add(hint);

        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
        Check();
    }
}
