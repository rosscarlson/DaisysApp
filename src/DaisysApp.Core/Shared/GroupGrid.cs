using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Shared;

/// <summary>
/// Groups of boxes packed into one grid, as Zabbix's hosts and Home Assistant's devices are: a group takes as many
/// cells as it has boxes (the rest of the row if it has more, going on at the start of the next), and its title drags
/// it to another group's place. The columns are the setting, else as many ~250-wide boxes as fit.
/// </summary>
public sealed class GroupGrid(Grid layout, ScrollViewer scroller, string dragFormat, Func<int> columnsSet, Func<int> rowsSet, Func<IEnumerable<string>> order, Action<List<string>> reordered)
{
    private const double BoxWidth = 250;
    private int laidColumns;

    private int Columns() => columnsSet() > 0 ? columnsSet() : Math.Max(1, (int)((scroller.ActualWidth - 10) / BoxWidth));

    /// <summary>Whether a new width means laying the groups out again.</summary>
    public bool ColumnsChanged => Columns() != laidColumns;

    /// <summary>Packs the groups in order. <paramref name="all"/> is every group, shown or not.</summary>
    public void Lay(IEnumerable<GroupSection> all, List<(GroupSection Sec, List<UIElement> Boxes)> placed)
    {
        foreach (var sec in all) sec.Detach();
        layout.Children.Clear();
        layout.RowDefinitions.Clear();
        layout.ColumnDefinitions.Clear();
        int columns = laidColumns = Columns();
        for (int c = 0; c < columns; c++) layout.ColumnDefinitions.Add(new ColumnDefinition());
        int row = 0, col = 0;
        foreach (var (sec, boxes) in placed)
        {
            for (int at = 0; at < boxes.Count;)
            {
                int take = Math.Min(columns - col, boxes.Count - at);
                if (layout.RowDefinitions.Count <= row) layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (piece, head) = sec.Piece(boxes.GetRange(at, take), first: at == 0, more: at + take < boxes.Count);
                if (sec.Movable)
                {
                    piece.AllowDrop = true;
                    piece.DragOver += (_, e) => { e.Effects = CanDrop(e, sec) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                    piece.Drop += (_, e) => { if (CanDrop(e, sec)) Move((string)e.Data.GetData(dragFormat), sec.Key); e.Handled = true; };
                    DragFrom(head, sec.Key);
                }
                Grid.SetRow(piece, row);
                Grid.SetColumn(piece, col);
                Grid.SetColumnSpan(piece, take);
                layout.Children.Add(piece);
                at += take;
                col += take;
                if (col == columns) { col = 0; row++; }
            }
        }
        FitRows();
    }

    /// <summary>With a number of rows set: each row a share of the tab's height (never less than its boxes need).</summary>
    public void FitRows()
    {
        int rows = rowsSet();
        double each = rows > 0 && scroller.ViewportHeight > 0 ? Math.Floor(scroller.ViewportHeight / rows) : 0;
        foreach (var r in layout.RowDefinitions) r.MinHeight = each;
    }

    private bool CanDrop(DragEventArgs e, GroupSection target) =>
        e.Data.GetDataPresent(dragFormat) && (string)e.Data.GetData(dragFormat) != target.Key;

    /// <summary>A group's title starts a drag; dropped on another group, it takes that one's place.</summary>
    private void DragFrom(FrameworkElement head, string key)
    {
        Point? down = null;
        head.Cursor = Cursors.SizeAll;
        head.ToolTip = T("Drag to move");
        head.MouseLeftButtonDown += (_, e) => down = e.GetPosition(head);
        head.MouseLeftButtonUp += (_, _) => down = null;
        head.MouseMove += (_, e) =>
        {
            if (down is not { } d || e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
            var v = e.GetPosition(head) - d;
            if (Math.Abs(v.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(v.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            DragDrop.DoDragDrop(head, new DataObject(dragFormat, key), DragDropEffects.Move);
        };
    }

    /// <summary>Puts a group where another is (the others move along).</summary>
    private void Move(string key, string onto)
    {
        var keys = order().ToList();
        int from = keys.IndexOf(key), to = keys.IndexOf(onto);
        if (from < 0 || to < 0) return;
        keys.RemoveAt(from);
        keys.Insert(to, key);
        reordered(keys);
    }
}

/// <summary>One group of a <see cref="GroupGrid"/>: its title and coloured counts, drawn as a piece per grid row it's on.</summary>
public class GroupSection(string key)
{
    public string Key { get; } = key;
    public string Title { get; set; } = "";
    public virtual bool Movable => true;
    public List<((string Color, string Name) Status, int Count)> Counts { get; set; } = new();
    private readonly List<UniformGrid> grids = new();
    private StackPanel? countsPanel;

    /// <summary>Takes its boxes out of the pieces they were in, to be laid out again.</summary>
    public void Detach()
    {
        foreach (var g in grids) g.Children.Clear();
        grids.Clear();
        countsPanel = null;
    }

    /// <summary>A box round some of its boxes on one grid row (and its title line): the first piece with the name and
    /// counts, a later one with the name faded; square corners where it goes on from the row before or to the next.</summary>
    public (Border Piece, FrameworkElement Head) Piece(List<UIElement> boxes, bool first, bool more)
    {
        var name = Ui.Header(Title);
        name.Margin = new Thickness(0);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 10), Background = Brushes.Transparent, Children = { name } };
        if (first)
        {
            countsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            ShowCounts(Counts);
            head.Children.Add(countsPanel);
        }
        else name.Opacity = 0.55;
        var grid = new UniformGrid { Rows = 1, Columns = boxes.Count, Margin = new Thickness(-6, 0, -6, -12) };
        foreach (var b in boxes) grid.Children.Add(b);
        grids.Add(grid);
        DockPanel.SetDock(head, Dock.Top);
        double l = first ? 10 : 2, r = more ? 2 : 10;
        var piece = new Border
        {
            CornerRadius = new CornerRadius(l, r, r, l), BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(6, 0, 6, 12),
            Child = new DockPanel { Children = { head, grid } },
        };
        piece.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        piece.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return (piece, head);
    }

    public void ShowCounts(List<((string Color, string Name) Status, int Count)> counts)
    {
        Counts = counts;
        if (countsPanel == null) return;
        countsPanel.Children.Clear();
        foreach (var ((color, what), n) in counts) countsPanel.Children.Add(Dot(color, $"{n} {what}", n));
    }

    /// <summary>A circle with a count in it (3 in green: three fine).</summary>
    private static Border Dot(string color, string tip, int n)
    {
        var dot = new Border
        {
            MinWidth = 22, Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 5, 0),
            ToolTip = tip,
            Child = new TextBlock { Text = n.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Ui.SetBrush(dot, Border.BackgroundProperty, color);
        return dot;
    }
}
