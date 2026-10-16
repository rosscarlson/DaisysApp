using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DaisysApp.Shell;

/// <summary>
/// The tab pattern used for the main tabs and the Settings sub-tabs: a row of "TabButton" RadioButtons over a Grid
/// that holds every page, with only the selected page visible. Pages stay alive when you switch tabs.
/// Renamable tabs show the user's name for them (<see cref="TabNames"/>); right-click one to type a new name (Enter
/// saves, Esc or clicking elsewhere cancels). Movable tabs (the applets') can be dragged to another place, as in a browser.
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
        public bool Renamable, Movable;
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

    public void Add(string id, string title, string? iconGlyph, FrameworkElement page, bool renamable = true, bool movable = false, string? tip = null)
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
        var tab = new Tab { Id = id, DefaultTitle = title, Button = button, Title = titleText, Page = page, Renamable = renamable, Movable = movable };
        button.Checked += (_, _) => Show(id);
        if (tip != null) button.ToolTip = tip;
        if (renamable)
        {
            button.ToolTip = movable ? T("Right-click to rename. Drag it to move it.") : T("Right-click to rename");
            button.MouseRightButtonUp += (_, e) => { BeginRename(tab); e.Handled = true; };
        }
        if (movable)
        {
            button.PreviewMouseLeftButtonDown += (_, e) => PressStart(tab, e);
            button.PreviewMouseMove += (_, e) => DragMove(tab, e);
            button.PreviewMouseLeftButtonUp += (_, e) => DragEnd(e);
            button.LostMouseCapture += (_, _) => { if (dragging && pressed == tab) Drop(); };
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

    // ---- drag a tab to move it, as in a browser: it follows the pointer and the others slide out of its way ----

    private const double SlideMs = 140;
    private Tab? pressed;
    private Point pressPoint;
    private double grabOffset; // where in the tab it was grabbed, from the tab's left edge
    private bool dragging;

    /// <summary>Raised with the movable tabs' ids in their new order after one is dragged somewhere else.</summary>
    public event Action<List<string>>? Reordered;

    private void PressStart(Tab tab, MouseButtonEventArgs e)
    {
        if (tab.Editor != null) return;
        pressed = tab;
        pressPoint = e.GetPosition(strip);
    }

    private void DragMove(Tab tab, MouseEventArgs e)
    {
        if (pressed != tab) return;
        var at = e.GetPosition(strip);
        if (!dragging)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { pressed = null; return; }
            // a few pixels sideways makes it a drag; less is still a click
            if (Math.Abs(at.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance * 2) return;
            dragging = true;
            grabOffset = pressPoint.X - SlotLeft(tab.Button);
            tab.Button.IsChecked = true; // a dragged tab is the one showing, as in a browser
            tab.Button.CaptureMouse();
            Panel.SetZIndex(tab.Button, 1);
            tab.Button.Opacity = 0.9;
            tab.Button.RenderTransform = new TranslateTransform();
        }

        // keep it within the movable tabs
        var movable = strip.Children.OfType<RadioButton>().Where(IsMovable).ToList();
        double min = SlotLeft(movable[0]), max = SlotLeft(movable[^1]) + movable[^1].ActualWidth - tab.Button.ActualWidth;
        double left = Math.Clamp(at.X - grabOffset, min, Math.Max(min, max));

        // past the middle of a neighbour: it moves to the other side (the dragged tab stays put in the tree, so it keeps
        // the mouse) and slides there
        int i = strip.Children.IndexOf(tab.Button);
        if (i + 1 < strip.Children.Count && strip.Children[i + 1] is RadioButton right && IsMovable(right)
            && left + tab.Button.ActualWidth > SlotLeft(right) + right.ActualWidth / 2)
        {
            double from = SlotLeft(right);
            strip.Children.RemoveAt(i + 1);
            strip.Children.Insert(i, right);
            strip.UpdateLayout();
            Slide(right, from - SlotLeft(right));
        }
        else if (i > 0 && strip.Children[i - 1] is RadioButton leftTab && IsMovable(leftTab)
                 && left < SlotLeft(leftTab) + leftTab.ActualWidth / 2)
        {
            double from = SlotLeft(leftTab);
            strip.Children.RemoveAt(i - 1);
            strip.Children.Insert(i, leftTab);
            strip.UpdateLayout();
            Slide(leftTab, from - SlotLeft(leftTab));
        }
        ((TranslateTransform)tab.Button.RenderTransform).X = left - SlotLeft(tab.Button);

        // near the edge of a row that scrolls: scroll it
        if (strip.Parent is ScrollViewer sv && sv.ScrollableWidth > 0)
        {
            double x = e.GetPosition(sv).X;
            if (x < 24) sv.ScrollToHorizontalOffset(sv.HorizontalOffset - 10);
            else if (x > sv.ViewportWidth - 24) sv.ScrollToHorizontalOffset(sv.HorizontalOffset + 10);
        }
        e.Handled = true;
    }

    /// <summary>Where a tab's place in the row starts (its layout position, without any slide or drag offset).</summary>
    private double SlotLeft(FrameworkElement b) => VisualTreeHelper.GetOffset(b).X;

    /// <summary>Animates a tab from <paramref name="offset"/> back to its place.</summary>
    private static void Slide(FrameworkElement b, double offset, Action? done = null)
    {
        var move = new TranslateTransform(offset, 0);
        b.RenderTransform = move;
        var anim = new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(SlideMs)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        if (done != null) anim.Completed += (_, _) => done();
        move.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void DragEnd(MouseButtonEventArgs e)
    {
        if (dragging)
        {
            Drop();
            e.Handled = true; // no click: the drag doesn't also switch tabs
        }
        pressed = null;
    }

    private void Drop()
    {
        var tab = pressed;
        dragging = false;
        pressed = null;
        if (tab == null) return;
        if (tab.Button.IsMouseCaptured) tab.Button.ReleaseMouseCapture();
        // into its place
        double offset = tab.Button.RenderTransform is TranslateTransform t ? t.X : 0;
        var button = tab.Button;
        Slide(button, offset, () => { Panel.SetZIndex(button, 0); button.Opacity = 1; });
        var order = strip.Children.OfType<RadioButton>().Select(b => tabs.FirstOrDefault(t => t.Button == b)).OfType<Tab>()
            .Where(t => t.Movable).Select(t => t.Id).ToList();
        Reordered?.Invoke(order);
    }

    private bool IsMovable(RadioButton b) => tabs.FirstOrDefault(t => t.Button == b)?.Movable == true;

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
