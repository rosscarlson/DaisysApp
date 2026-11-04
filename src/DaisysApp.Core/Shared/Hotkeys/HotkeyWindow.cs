using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Shared.Hotkeys;

/// <summary>
/// The small window a module's shortcut button opens (the joystick beside On / Off, a volume's keys): a <see
/// cref="ShortcutBox"/> per binding, saved as it's set and shown until Done. A set one stays until it's cleared with
/// the ×, which then listens; with one binding and none set, it listens as soon as it opens. Several get a row each,
/// named by the binding. A module adds anything of its
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
            var box = new ShortcutBox(hotkeys[0]) { ClearFirst = true };
            panel.Children.Add(box);
            Loaded += (_, _) => { if (hotkeys[0].Value == null) box.Listen(); };
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
                var box = new ShortcutBox(h) { ClearFirst = true, Margin = new Thickness(0, 0, 0, 10) };
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

    /// <summary>
    /// The joystick button that opens it for a binding (found when it's clicked: null, nothing to bind): in the accent
    /// colour while one is set, which its tooltip names. (Since Core 0.53.)
    /// </summary>
    public static Button Button(string title, Func<Hotkey?> hotkey)
    {
        var glyph = new TextBlock { Text = "", FontSize = 14 };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var b = new Button { Content = glyph, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(b, title);
        void Show()
        {
            var text = hotkey()?.Text;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, text != null ? "AccentBrush" : "TextBrush");
            b.ToolTip = text != null ? $"{title}: {text}" : title;
        }
        b.Click += (_, _) =>
        {
            if (hotkey() is { } h) Open(title, Window.GetWindow(b), h);
            Show();
        };
        b.Loaded += (_, _) => { Hotkeys.Changed += Show; Show(); };
        b.Unloaded += (_, _) => Hotkeys.Changed -= Show;
        Show();
        return b;
    }
}
