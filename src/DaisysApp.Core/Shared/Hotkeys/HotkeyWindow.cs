using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// The small window a module's shortcut button opens (the joystick beside On / Off, a volume's keys): a <see
/// cref="ShortcutBox"/> per binding, saved as it's set. With one binding it listens as soon as it opens and closes once
/// one is pressed (unless it's taken); with several, a row each, named by the binding. A module adds anything of its
/// own (a checkbox) to <see cref="Extra"/>.
/// </summary>
public sealed class HotkeyWindow : Window
{
    private readonly StackPanel panel = new() { Margin = new Thickness(20) };

    /// <summary>Under the boxes, above Done.</summary>
    public StackPanel Extra { get; } = new();

    public HotkeyWindow(string title, IReadOnlyList<Hotkey> hotkeys, Window? owner = null)
    {
        Title = title;
        Width = hotkeys.Count == 1 ? 440 : 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Owner = owner;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        if (hotkeys.Count == 1)
        {
            var box = new ShortcutBox(hotkeys[0]);
            box.Changed += () => { if (hotkeys[0].Value != null && !hotkeys[0].Failed) Close(); };
            panel.Children.Add(box);
            Loaded += (_, _) => box.Listen();
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (var h in hotkeys)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = h.Spec.Name, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 12, 10) };
                Grid.SetRow(label, row);
                grid.Children.Add(label);
                var box = new ShortcutBox(h) { Margin = new Thickness(0, 0, 0, 10) };
                Grid.SetRow(box, row);
                Grid.SetColumn(box, 1);
                grid.Children.Add(box);
            }
            panel.Children.Add(grid);
        }
        panel.Children.Add(Extra);
        panel.Children.Add(new Button { Content = T("Done"), MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) });
        Content = panel;
    }

    /// <summary>Opens it and waits until it's closed.</summary>
    public static void Open(string title, Window? owner, params Hotkey[] hotkeys) => new HotkeyWindow(title, hotkeys, owner).ShowDialog();
}
