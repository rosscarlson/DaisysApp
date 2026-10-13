using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Hardware.UsbMonitor.ViewModels;

/// <summary>
/// Right-click a column's header for a menu that sorts by it and filters it: every value in that column with a tick
/// box (untick to hide those rows), Show all and Hide all. A filtered column's header shows a filter mark; <see
/// cref="Reset"/> clears every filter (the view's Reset filters button). Left-clicking a header still sorts as usual.
/// Works on any DataGrid whose columns are bound to properties of its items.
/// </summary>
internal sealed class GridColumnFilter
{
    private const int MaxValues = 300;
    private readonly DataGrid grid;
    private readonly Dictionary<DataGridColumn, HashSet<string>> hidden = new();
    private readonly Dictionary<DataGridColumn, object> headers = new();

    public GridColumnFilter(DataGrid grid)
    {
        this.grid = grid;
        grid.PreviewMouseRightButtonUp += OnRightClick;
    }

    /// <summary>Raised when a filter is set or cleared.</summary>
    public event Action? Changed;

    public bool IsActive => hidden.Values.Any(h => h.Count > 0);

    private ICollectionView? View => grid.ItemsSource == null ? null : CollectionViewSource.GetDefaultView(grid.ItemsSource);

    private static string? PathOf(DataGridColumn c) =>
        c is DataGridBoundColumn { Binding: Binding b } ? b.Path.Path : string.IsNullOrEmpty(c.SortMemberPath) ? null : c.SortMemberPath;

    private static string ValueOf(object item, string path) => item.GetType().GetProperty(path)?.GetValue(item)?.ToString() ?? "";

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d != null && d is not DataGridColumnHeader) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        if (d is not DataGridColumnHeader { Column: { } column } header || PathOf(column) is not { } path) return;
        e.Handled = true;
        var menu = Menu(column, path);
        menu.PlacementTarget = header;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu Menu(DataGridColumn column, string path)
    {
        var menu = new ContextMenu { MaxHeight = SystemParameters.WorkArea.Height * 0.7 };
        MenuItem Item(string text, Action click, bool staysOpen = false)
        {
            var m = new MenuItem { Header = text, StaysOpenOnClick = staysOpen };
            m.Click += (_, _) => click();
            menu.Items.Add(m);
            return m;
        }
        Item(T("Sort A to Z"), () => Sort(column, path, ListSortDirection.Ascending));
        Item(T("Sort Z to A"), () => Sort(column, path, ListSortDirection.Descending));
        menu.Items.Add(new Separator());

        if (!hidden.TryGetValue(column, out var set)) hidden[column] = set = new HashSet<string>(StringComparer.Ordinal);
        var values = (grid.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Select(i => ValueOf(i, path))
            .Distinct().OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList() ?? new();
        var boxes = new List<(MenuItem Item, string Value)>();
        Item(T("Show all"), () => { set.Clear(); foreach (var b in boxes) b.Item.IsChecked = true; Apply(); }, staysOpen: true);
        Item(T("Hide all"), () => { foreach (var b in boxes) { set.Add(b.Value); b.Item.IsChecked = false; } Apply(); }, staysOpen: true);
        menu.Items.Add(new Separator());
        foreach (var v in values.Take(MaxValues))
        {
            var m = new MenuItem { Header = v.Length == 0 ? T("(blank)") : v, IsCheckable = true, IsChecked = !set.Contains(v), StaysOpenOnClick = true };
            m.Click += (_, _) =>
            {
                if (m.IsChecked) set.Remove(v); else set.Add(v);
                Apply();
            };
            boxes.Add((m, v));
            menu.Items.Add(m);
        }
        if (values.Count > MaxValues)
            menu.Items.Add(new MenuItem { Header = F("…and {0} more", values.Count - MaxValues), IsEnabled = false });
        return menu;
    }

    private void Sort(DataGridColumn column, string path, ListSortDirection direction)
    {
        if (View is not { } view) return;
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(column.SortMemberPath is { Length: > 0 } s ? s : path, direction));
        foreach (var c in grid.Columns) c.SortDirection = null;
        column.SortDirection = direction;
    }

    private bool Passes(object item)
    {
        foreach (var (column, set) in hidden)
            if (set.Count > 0 && PathOf(column) is { } path && set.Contains(ValueOf(item, path))) return false;
        return true;
    }

    private void Apply()
    {
        if (View is { } view)
        {
            view.Filter = IsActive ? Passes : null;
            view.Refresh();
        }
        // a filtered column's header gets a filter mark
        foreach (var column in grid.Columns)
        {
            bool on = hidden.TryGetValue(column, out var set) && set.Count > 0;
            if (!headers.ContainsKey(column)) headers[column] = column.Header;
            if (!on) { column.Header = headers[column]; continue; }
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = headers[column]?.ToString() ?? "" });
            var mark = new TextBlock { Text = "", FontSize = 11, Margin = new Thickness(5, 2, 0, 0) };
            mark.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            mark.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            panel.Children.Add(mark);
            column.Header = panel;
        }
        Changed?.Invoke();
    }

    public void Reset()
    {
        foreach (var set in hidden.Values) set.Clear();
        Apply();
    }
}
