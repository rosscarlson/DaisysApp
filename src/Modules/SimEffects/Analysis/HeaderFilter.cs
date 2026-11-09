using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// (since 4.13.0) A table column's filter, set with a right-click on its header: a drop-down of the column's values, a
/// checkbox each, one or several picked (none: all). A funnel in the header while one's picked. The column's Header is
/// <see cref="Header"/>; a table says what its columns can be filtered by with <see cref="Attach"/>.
/// </summary>
internal sealed class HeaderFilter(string name)
{
    private readonly HashSet<string> picked = new();
    private TextBlock? funnel;

    /// <summary>What's picked changed.</summary>
    public event Action? Changed;

    public bool On => picked.Count > 0;
    public bool Keeps(string value) => picked.Count == 0 || picked.Contains(value);

    /// <summary>A header for its column: made again when the table's columns are.</summary>
    public FrameworkElement Header()
    {
        funnel = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
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
        var boxes = new List<CheckBox>();
        var list = new StackPanel();
        // (what's picked stays listed, whatever the other columns' filters leave)
        foreach (var value in values.Concat(picked).Distinct().OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase))
        {
            var v = value;
            // (in a TextBlock: an underscore in a name isn't an access key)
            var box = new CheckBox { Content = new TextBlock { Text = v }, IsChecked = picked.Contains(v), Margin = new Thickness(0, 2, 12, 2) };
            System.Windows.Automation.AutomationProperties.SetName(box, v);
            Ui.OnToggle(box, on =>
            {
                if (on) picked.Add(v); else picked.Remove(v);
                Show();
            });
            boxes.Add(box);
            list.Children.Add(box);
        }
        var clear = new Button { Content = T("Clear"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            picked.Clear();
            foreach (var b in boxes) b.IsChecked = false;
            Show();
        };
        var panel = new DockPanel();
        DockPanel.SetDock(clear, Dock.Top);
        panel.Children.Add(clear);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var frame = new Border { Child = panel, Padding = new Thickness(10, 8, 6, 8), BorderThickness = new Thickness(1), MinWidth = 150 };
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        frame.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, Child = frame, IsOpen = true };
    }
}
