using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Shell;

/// <summary>
/// The tab pattern used for the main tabs and the Settings sub-tabs: a row of "TabButton" RadioButtons over a Grid
/// that holds every page, with only the selected page visible. Pages stay alive when you switch tabs.
/// Renamable tabs show the user's name for them (<see cref="TabNames"/>); right-click one to type a new name (Enter
/// saves, Esc or clicking elsewhere cancels).
/// </summary>
internal sealed class TabStrip
{
    private sealed class Tab
    {
        public required string Id;
        public required string DefaultTitle;
        public required RadioButton Button;
        public required TextBlock Title;
        public required FrameworkElement Page;
        public bool Renamable;
        public TextBox? Editor;
    }

    private readonly Panel strip, host;
    private readonly List<Tab> tabs = new();

    public TabStrip(Panel strip, Panel host)
    {
        this.strip = strip;
        this.host = host;
        // a single row that scrolls sideways when the tabs don't fit: the mouse wheel scrolls it too
        if (strip.Parent is ScrollViewer scroller)
            scroller.PreviewMouseWheel += (_, e) =>
            {
                if (scroller.ScrollableWidth <= 0) return;
                scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta / 2.0);
                e.Handled = true;
            };
        TabNames.Changed += id => { if (tabs.FirstOrDefault(t => t.Id == id) is { } tab) ShowTitle(tab); };
    }

    /// <summary>Raised with the tab id after a tab is shown.</summary>
    public event Action<string>? Selected;

    public void Add(string id, string title, string? iconGlyph, FrameworkElement page, bool renamable = true)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (iconGlyph != null)
        {
            header.Children.Add(new TextBlock
            {
                Text = iconGlyph,
                FontFamily = (FontFamily)strip.FindResource("IconFont"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
        }
        var titleText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(titleText);

        // No GroupName: RadioButtons that share a parent panel form their own group.
        var button = new RadioButton { Style = (Style)strip.FindResource("TabButton"), Content = header };
        var tab = new Tab { Id = id, DefaultTitle = title, Button = button, Title = titleText, Page = page, Renamable = renamable };
        button.Checked += (_, _) => Show(id);
        if (renamable)
        {
            button.ToolTip = T("Right-click to rename");
            button.MouseRightButtonUp += (_, e) => { BeginRename(tab); e.Handled = true; };
        }
        strip.Children.Add(button);
        ShowTitle(tab);

        page.Visibility = Visibility.Collapsed;
        host.Children.Add(page);
        tabs.Add(tab);
    }

    private void ShowTitle(Tab tab)
    {
        string title = tab.Renamable ? TabNames.For(tab.Id, tab.DefaultTitle) : tab.DefaultTitle;
        tab.Title.Text = title;
        AutomationProperties.SetName(tab.Button, title);
    }

    /// <summary>Swaps the tab's name for a text box with the name selected.</summary>
    private void BeginRename(Tab tab)
    {
        if (tab.Editor != null) return;
        var header = (StackPanel)tab.Button.Content;
        var editor = new TextBox
        {
            Text = tab.Title.Text,
            MinWidth = Math.Max(80, tab.Title.ActualWidth + 24),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0),
        };
        AutomationProperties.SetName(editor, T("Tab name"));
        tab.Editor = editor;
        tab.Title.Visibility = Visibility.Collapsed;
        header.Children.Add(editor);
        bool done = false;
        void Finish(bool save)
        {
            if (done) return;
            done = true;
            if (save) TabNames.Set(tab.Id, editor.Text, tab.DefaultTitle);
            header.Children.Remove(editor);
            tab.Editor = null;
            tab.Title.Visibility = Visibility.Visible;
            ShowTitle(tab);
        }
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Finish(save: true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Finish(save: false); e.Handled = true; }
        };
        editor.LostKeyboardFocus += (_, _) => Finish(save: false);
        editor.Loaded += (_, _) =>
        {
            editor.Focus();
            editor.SelectAll();
        };
    }

    /// <summary>Selects the tab with this id, or the first tab if there's no such tab.</summary>
    public void Select(string? id)
    {
        if (tabs.Count == 0) return;
        var tab = tabs.FirstOrDefault(t => t.Id == id) ?? tabs[0];
        tab.Button.IsChecked = true;
    }

    private void Show(string id)
    {
        foreach (var tab in tabs)
        {
            tab.Page.Visibility = tab.Id == id ? Visibility.Visible : Visibility.Collapsed;
            // scrolled to, if the row is wider than the window (after layout, so it works at startup too)
            if (tab.Id == id) { var b = tab.Button; b.Dispatcher.BeginInvoke(() => b.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded); }
        }
        Selected?.Invoke(id);
    }
}
