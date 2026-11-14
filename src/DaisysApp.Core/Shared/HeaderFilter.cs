using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace DaisysApp.Shared;

/// <summary>
/// A table column's filter, set with a right-click on its header: a drop-down of the column's values, a checkbox each,
/// one or several picked (none: all), Clear, and (since 4.17.21) with more than 5 values a small box at Clear's right
/// that narrows the list as you type. A funnel in the header while one's picked. The column's Header is
/// <see cref="Header"/>; a table says what its columns can be filtered by with <see cref="Attach"/>. Listed A to Z, or
/// in <paramref name="order"/>. (SimEffects' since 4.13.0; in Core since 4.17.21, for every table.)
/// </summary>
public sealed class HeaderFilter(string name, Comparison<string>? order = null)
{
    private readonly HashSet<string> picked = new();
    private TextBlock? funnel;

    /// <summary>The values a drop-down has before it gets its search box.</summary>
    public const int SearchFrom = 5;

    /// <summary>What's picked changed.</summary>
    public event Action? Changed;

    /// <summary>What a value's line says, when it isn't just the value.</summary>
    public Func<string, string>? Label { get; set; }

    public bool On => picked.Count > 0;
    public bool Keeps(string value) => picked.Count == 0 || picked.Contains(value);

    /// <summary>A header for its column: made again when the table's columns are.</summary>
    public FrameworkElement Header()
    {
        funnel = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Visibility = On ? Visibility.Visible : Visibility.Collapsed,
        };
        funnel.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Tag = this, ToolTip = T("Right-click to filter"),
            Children = { new TextBlock { Text = name }, funnel },
        };
    }

    /// <summary>A right-click on a header of <paramref name="grid"/> made with <see cref="Header"/> opens its drop-down, with
    /// the values <paramref name="values"/> gives.</summary>
    public static void Attach(DataGrid grid, Func<HeaderFilter, IEnumerable<string>> values)
    {
        grid.PreviewMouseRightButtonUp += (_, e) =>
        {
            for (var d = e.OriginalSource as DependencyObject; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is not DataGridColumnHeader { Column.Header: FrameworkElement { Tag: HeaderFilter filter } } header) continue;
                filter.Open(header, values(filter));
                e.Handled = true;
                return;
            }
        };
    }

    private void Show()
    {
        if (funnel != null) funnel.Visibility = On ? Visibility.Visible : Visibility.Collapsed;
        Changed?.Invoke();
    }

    private void Open(FrameworkElement anchor, IEnumerable<string> values)
    {
        var boxes = new List<(CheckBox Box, string Line)>();
        var list = new StackPanel();
        // (what's picked stays listed, whatever the other columns' filters leave)
        foreach (var value in values.Concat(picked).Distinct().OrderBy(v => v, order != null ? Comparer<string>.Create(order) : StringComparer.CurrentCultureIgnoreCase))
        {
            var v = value;
            // (in a TextBlock: an underscore in a name isn't an access key)
            string line = Label?.Invoke(v) ?? v;
            var box = new CheckBox { Content = new TextBlock { Text = line }, IsChecked = picked.Contains(v), Margin = new Thickness(0, 2, 12, 2) };
            System.Windows.Automation.AutomationProperties.SetName(box, line);
            Ui.OnToggle(box, on =>
            {
                if (on) picked.Add(v); else picked.Remove(v);
                Show();
            });
            boxes.Add((box, line));
            list.Children.Add(box);
        }
        var clear = new Button { Content = T("Clear"), Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            picked.Clear();
            foreach (var (b, _) in boxes) b.IsChecked = false;
            Show();
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(clear, Dock.Left);
        top.Children.Add(clear);
        TextBox? search = null;
        if (boxes.Count > SearchFrom)
        {
            // (in what's left at Clear's right: the drop-down no wider for it)
            search = new TextBox { Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("Search") };
            System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
            search.TextChanged += (_, _) =>
            {
                string find = search.Text.Trim();
                foreach (var (b, line) in boxes)
                    b.Visibility = find.Length == 0 || line.Contains(find, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            };
            top.Children.Add(new NoWidth { Child = search });
        }
        var panel = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var frame = new Border { Child = panel, Padding = new Thickness(10, 8, 6, 8), BorderThickness = new Thickness(1), MinWidth = 150 };
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        frame.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, Child = frame };
        if (search != null) popup.Opened += (_, _) => search.Focus();
        popup.IsOpen = true;
    }

    /// <summary>Takes the width it's given but asks for none, so what's in it never widens the drop-down.</summary>
    private sealed class NoWidth : Decorator
    {
        protected override Size MeasureOverride(Size constraint)
        {
            if (Child == null) return default;
            Child.Measure(constraint);
            return new Size(0, Child.DesiredSize.Height);
        }
    }
}
