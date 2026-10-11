using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Logging;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Logs;

/// <summary>Picks any Windows event log on this PC that has entries, with a search box.</summary>
internal sealed class WindowsLogPicker : Window
{
    private readonly TextBox search = new() { Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox list = new();
    private readonly Button open = new() { Content = T("Open"), IsDefault = true, MinWidth = 80, IsEnabled = false };
    private List<string> names = new();

    public string? Chosen { get; private set; }

    public WindowsLogPicker()
    {
        Title = T("Open a Windows log");
        Width = 560;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var hint = new TextBlock { Text = T("Every event log on this PC that has entries. Some (such as Security) need administrator rights."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        hint.SetResourceReference(StyleProperty, "SecondaryText");
        var cancel = new Button { Content = T("Cancel"), IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(open);
        buttons.Children.Add(cancel);

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(search, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(hint);
        layout.Children.Add(search);
        layout.Children.Add(buttons);
        layout.Children.Add(list);
        Content = layout;

        list.Items.Add(T("Loading…"));
        search.TextChanged += (_, _) => Fill();
        list.SelectionChanged += (_, _) => open.IsEnabled = list.SelectedItem is string s && names.Contains(s);
        list.MouseDoubleClick += (_, _) => Accept();
        open.Click += (_, _) => Accept();
        Loaded += async (_, _) =>
        {
            search.Focus();
            try { names = await Task.Run(WindowsEvents.LogNames); }
            catch (Exception ex) { Log.Here.Warn("Couldn't list the Windows logs", ex); }
            Fill();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && search.IsKeyboardFocused && list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
                (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        };
    }

    private void Fill()
    {
        string q = search.Text.Trim();
        list.Items.Clear();
        foreach (var n in names.Where(n => q.Length == 0 || n.Contains(q, StringComparison.OrdinalIgnoreCase))) list.Items.Add(n);
        if (list.Items.Count == 1) list.SelectedIndex = 0;
    }

    private void Accept()
    {
        if (list.SelectedItem is not string s || !names.Contains(s)) return;
        Chosen = s;
        DialogResult = true;
    }
}
