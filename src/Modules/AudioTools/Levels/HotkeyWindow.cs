using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>A device's or app's three shortcuts: volume up, volume down and mute. Saved as they're set.</summary>
internal sealed class HotkeyWindow : Window
{
    public HotkeyWindow(AudioLevelsSettings settings, VolumeHotkeyService service, VolumeRow row)
    {
        Title = F("Shortcuts — {0}", row.Name);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        if (!settings.Hotkeys.TryGetValue(row.Key, out var h)) h = new VolumeHotkeys();
        h.Name = row.Name;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = row.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis });
        var hint = new TextBlock
        {
            Text = F("A key combination (with Ctrl, Alt or Shift) or a controller / wheel button. Each press changes the volume by {0}; change that at the top of the tab.", settings.Step),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
        };
        hint.SetResourceReference(StyleProperty, "SecondaryText");
        panel.Children.Add(hint);

        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        void ShowWarning()
        {
            var failed = new[] { h.Up, h.Down, h.Mute }.Where(s => s != null && service.Failed.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            warning.Text = failed.Count > 0 ? F("{0} is already used by another program, so it won't work. Pick another.", string.Join(", ", failed)) : "";
            warning.Visibility = failed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var (label, get, set) in new (string, Func<string?>, Action<string?>)[]
        {
            (T("Volume up"), () => h.Up, v => h.Up = v),
            (T("Volume down"), () => h.Down, v => h.Down = v),
            (T("Mute on / off"), () => h.Mute, v => h.Mute = v),
        })
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            line.Children.Add(new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center });
            var box = new ShortcutBox { AllowControllerButtons = true, Value = get() };
            box.Attach(service.Suspend, service.Resume);
            box.Changed += () =>
            {
                set(box.Value);
                if (h.Any) settings.Hotkeys[row.Key] = h; else settings.Hotkeys.Remove(row.Key);
                settings.Save();
                service.Register();
                row.HotkeyText = service.Summary(row.Key);
                ShowWarning();
            };
            line.Children.Add(box);
            panel.Children.Add(line);
        }
        panel.Children.Add(warning);

        var close = new Button { Content = T("Close"), MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
        ShowWarning();
    }
}
