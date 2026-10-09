using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming;

/// <summary>The benchmark's shortcut (start / stop) and how long a run lasts: a set time, or until it's stopped.</summary>
internal sealed class BenchmarkSettingsWindow : Window
{
    public BenchmarkSettingsWindow(GamingService service)
    {
        var s = service.Settings;
        Title = T("Benchmark settings");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = T("Start / stop shortcut"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var box = new ShortcutBox { Value = s.BenchmarkHotkey, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260 };
        box.Attach(service.SuspendHotkeys, service.RegisterHotkeys);
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        void Check()
        {
            bool failed = s.BenchmarkHotkey != null && service.FailedHotkeys.Contains(s.BenchmarkHotkey, StringComparer.OrdinalIgnoreCase);
            warning.Text = failed ? F("{0} is already used by another program, so it won't work. Pick another.", s.BenchmarkHotkey) : "";
            warning.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        }
        box.Changed += () =>
        {
            s.BenchmarkHotkey = box.Value;
            s.Save();
            service.RegisterHotkeys();
            Check();
        };
        panel.Children.Add(box);
        panel.Children.Add(warning);
        panel.Children.Add(Hint(T("Press it in the game to start measuring the game in front, and again to stop. A key combination, e.g. Ctrl+Alt+F12.")));

        panel.Children.Add(new TextBlock { Text = T("Each run lasts"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) });
        var timed = new RadioButton { GroupName = "BenchLength", IsChecked = s.BenchmarkTimed, VerticalAlignment = VerticalAlignment.Center };
        var seconds = new TextBox { Text = s.BenchmarkSeconds.ToString(), Width = 60, Margin = new Thickness(8, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(seconds, T("Seconds"));
        var timedLine = new StackPanel { Orientation = Orientation.Horizontal };
        timed.Content = T("A set time:");
        timedLine.Children.Add(timed);
        timedLine.Children.Add(seconds);
        timedLine.Children.Add(new TextBlock { Text = T("seconds"), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(timedLine);
        var untilStopped = new RadioButton { GroupName = "BenchLength", IsChecked = !s.BenchmarkTimed, Content = T("Until I stop it (the shortcut again, or Stop)"), Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(untilStopped);
        panel.Children.Add(Hint(T("Runs are only compared with runs of the same game and length, so a set time makes them easiest to compare.")));
        void SaveLength()
        {
            s.BenchmarkTimed = timed.IsChecked == true;
            if (int.TryParse(seconds.Text.Trim(), out int n)) s.BenchmarkSeconds = Math.Clamp(n, 5, 3600);
            s.Save();
            service.NotifyChanged();
        }
        timed.Checked += (_, _) => SaveLength();
        untilStopped.Checked += (_, _) => SaveLength();
        seconds.LostKeyboardFocus += (_, _) => { SaveLength(); seconds.Text = s.BenchmarkSeconds.ToString(); };
        seconds.GotKeyboardFocus += (_, _) => timed.IsChecked = true;

        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), IsCancel = true };
        close.Click += (_, _) => { SaveLength(); Close(); };
        panel.Children.Add(close);
        Content = panel;
        Check();
    }

    private static TextBlock Hint(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }
}
