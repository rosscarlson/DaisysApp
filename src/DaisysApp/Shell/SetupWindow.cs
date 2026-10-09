using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Settings;
using DaisysApp.Theming;

namespace DaisysApp.Shell;

/// <summary>
/// The setup wizard: a welcome, then every module with a switch, its one-line description and its place in the tab
/// row (drag a row to move it), then done. Runs once per user (when <see cref="Version"/> is newer than the one they've
/// been through: a new install, or an update that brings a newer wizard) and from Settings → General. Modules load
/// when the app starts, so changes to which are on, or to their order, restart it.
/// </summary>
internal sealed class SetupWindow : Window
{
    /// <summary>Raise it when the wizard gains something everyone should see after updating.</summary>
    public const int Version = 1;

    private readonly AppSettings settings;
    private readonly HashSet<string> loadedOn;
    private readonly List<string> loadedOrder;
    private readonly ContentControl page = new();
    private readonly Button back = new() { MinWidth = 90 }, next = new() { MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock stepText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel list = new();
    private readonly List<Row> rows;
    private int step;

    private sealed class Row
    {
        public required AppletCatalog.Entry Entry;
        public required Border Element;
        public required ToggleButton Toggle;
    }

    /// <summary>True when the choices differ from what's running, so the app has to restart.</summary>
    public bool NeedsRestart { get; private set; }

    public SetupWindow(AppSettings settings, IEnumerable<string> loadedIds)
    {
        this.settings = settings;
        loadedOrder = loadedIds.ToList();
        loadedOn = loadedOrder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Title = F("{0} setup", AppPaths.DisplayName);
        Width = 680;
        Height = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        rows = AppletCatalog.InTabOrder(settings).Select(MakeRow).ToList();
        foreach (var r in rows) list.Children.Add(r.Element);

        next.SetResourceReference(StyleProperty, "AccentButton");
        back.Content = T("Back");
        back.Click += (_, _) => Show(step - 1);
        next.Click += (_, _) => Next();
        stepText.SetResourceReference(StyleProperty, "SecondaryText");
        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(back);
        right.Children.Add(next);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(stepText);
        var dock = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(page);
        Content = dock;
        Show(0);
    }

    private void Show(int n)
    {
        step = n;
        stepText.Text = F("Step {0} of {1}", n + 1, 3);
        back.Visibility = n == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (n == 2) NeedsRestart = Changed();
        next.Content = n switch { 0 => T("Next"), 1 => T("Next"), _ => NeedsRestart ? T("Finish and restart") : T("Finish") };
        page.Content = n switch { 0 => WelcomePage(), 1 => ModulesPage(), _ => DonePage() };
    }

    private void Next()
    {
        if (step < 2) { Show(step + 1); return; }
        Save();
        DialogResult = true;
    }

    // ---- pages ----

    private FrameworkElement WelcomePage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(F("Welcome to {0}", AppPaths.DisplayName)));
        p.Children.Add(Text(T("Daisy's App is a set of small tools, each on its own tab. This quick setup lets you pick the ones you want and put them in the order you like.")));
        p.Children.Add(Text(T("A tool you switch off isn't loaded at all: no tab and nothing running in the background. You can change all of this later in Settings → General, where you can also run this setup again.")));
        return p;
    }

    private FrameworkElement ModulesPage()
    {
        var p = new DockPanel();
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(Heading(T("Your tools")));
        top.Children.Add(Text(T("Switch on the ones you want. Drag a row up or down to change where its tab goes (the top one is the first tab).")));
        p.Children.Add(top);
        if (list.Parent is Panel old) old.Children.Remove(list);
        if (list.Parent is ScrollViewer sv) sv.Content = null;
        p.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 0) });
        return p;
    }

    private FrameworkElement DonePage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("All set")));
        var on = rows.Where(r => r.Toggle.IsChecked == true).Select(r => TabNames.For(r.Entry.Meta.Id, Any(r.Entry.Meta.Title))).ToList();
        p.Children.Add(Text(on.Count == 0
            ? T("No tools are switched on, so only the Settings tab will show.")
            : F("Your tabs: {0}, then Settings.", string.Join(", ", on))));
        p.Children.Add(Text(NeedsRestart
            ? T("Daisy's App restarts to apply this when you click Finish and restart.")
            : T("Nothing needs a restart.")));
        return p;
    }

    // ---- the list ----

    private Row MakeRow(AppletCatalog.Entry e)
    {
        var grip = new TextBlock { Text = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = T("Drag to move") };
        grip.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        grip.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var icon = new TextBlock { Text = e.Meta.Icon, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Width = 22 };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        string title = TabNames.For(e.Meta.Id, Any(e.Meta.Title));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        var desc = new TextBlock { Text = Any(e.Meta.Description), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        desc.SetResourceReference(StyleProperty, "SecondaryText");
        text.Children.Add(desc);

        var toggle = new ToggleButton { IsChecked = AppletCatalog.IsOn(e.Meta, settings), MinWidth = 52, Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        toggle.SetResourceReference(StyleProperty, "ChipToggle");
        void Label() => toggle.Content = toggle.IsChecked == true ? T("On") : T("Off");
        Label();
        toggle.Checked += (_, _) => Label();
        toggle.Unchecked += (_, _) => Label();
        AutomationProperties.SetName(toggle, title);

        var line = new DockPanel();
        DockPanel.SetDock(grip, Dock.Left);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(toggle, Dock.Right);
        line.Children.Add(grip);
        line.Children.Add(icon);
        line.Children.Add(toggle);
        line.Children.Add(text);
        var border = new Border { Child = line, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Cursor = Cursors.SizeNS };
        border.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        AutomationProperties.SetName(border, title);
        var row = new Row { Entry = e, Element = border, Toggle = toggle };
        HookDrag(row);
        return row;
    }

    private Row? dragging;
    private Point pressAt;
    private bool moved;

    // drag anywhere on a row but its switch: it moves past a neighbour once the pointer passes that one's middle
    private void HookDrag(Row row)
    {
        var b = row.Element;
        b.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInToggle(e.OriginalSource as DependencyObject, row.Toggle)) return;
            dragging = row;
            moved = false;
            pressAt = e.GetPosition(list);
            b.CaptureMouse();
            e.Handled = true;
        };
        b.MouseMove += (_, e) =>
        {
            if (dragging != row || !b.IsMouseCaptured) return;
            var at = e.GetPosition(list);
            if (!moved && Math.Abs(at.Y - pressAt.Y) < 4) return;
            moved = true;
            b.Opacity = 0.7;
            // near the top or bottom of the list's view: scroll it, so a row can go anywhere in a long list
            if (list.Parent is ScrollViewer sv)
            {
                double y = e.GetPosition(sv).Y;
                if (y < 30) sv.ScrollToVerticalOffset(sv.VerticalOffset - 12);
                else if (y > sv.ActualHeight - 30) sv.ScrollToVerticalOffset(sv.VerticalOffset + 12);
            }
            int i = list.Children.IndexOf(b);
            if (i + 1 < list.Children.Count && list.Children[i + 1] is FrameworkElement below && at.Y > Middle(below))
            {
                list.Children.RemoveAt(i + 1);
                list.Children.Insert(i, below);
            }
            else if (i > 0 && list.Children[i - 1] is FrameworkElement above && at.Y < Middle(above))
            {
                list.Children.RemoveAt(i - 1);
                list.Children.Insert(i, above);
            }
        };
        b.MouseLeftButtonUp += (_, _) => EndDrag(row);
        b.LostMouseCapture += (_, _) => EndDrag(row);
    }

    private void EndDrag(Row row)
    {
        if (dragging != row) return;
        dragging = null;
        row.Element.Opacity = 1;
        if (row.Element.IsMouseCaptured) row.Element.ReleaseMouseCapture();
    }

    private double Middle(FrameworkElement e) => e.TranslatePoint(new Point(0, e.ActualHeight / 2), list).Y;

    private static bool IsInToggle(DependencyObject? d, DependencyObject toggle)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == toggle) return true;
        return false;
    }

    // ---- saving ----

    private List<Row> Ordered => list.Children.OfType<Border>().Select(b => rows.First(r => r.Element == b)).ToList();

    private bool Changed()
    {
        var ordered = Ordered;
        var on = ordered.Where(r => r.Toggle.IsChecked == true).Select(r => r.Entry.Meta.Id).ToList();
        // the tabs that would show, in order, against the ones showing now
        return !on.SequenceEqual(loadedOrder, StringComparer.OrdinalIgnoreCase);
    }

    private void Save()
    {
        var ordered = Ordered;
        AppletCatalog.SaveOnOff(settings, ordered.Select(r => (r.Entry.Meta, r.Toggle.IsChecked == true)));
        // the whole order, off ones too, so a module switched on later goes where it was put
        var ids = ordered.Select(r => r.Entry.Meta.Id).ToList();
        settings.TabOrder = ids.Concat(settings.TabOrder.Where(id => !ids.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
        settings.SetupVersion = Version;
        settings.Save();
    }

    private static TextBlock Heading(string s) => new() { Text = s, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };

    private static TextBlock Text(string s) => new() { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 13.5 };
}
