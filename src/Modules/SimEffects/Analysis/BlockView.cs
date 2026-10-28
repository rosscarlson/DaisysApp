using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// A block on the tab (every row) and in its widget (the rows not hidden): its cells line by line, a label and a value
/// each, in columns (or, for Tires, the car drawing with a temperature at each wheel). While the widget is being edited
/// every cell shows its rows as checkboxes, in place of its label and value (Tires: beside each temperature): unticked,
/// the row is hidden once Done is pressed.
/// </summary>
internal sealed class BlockView : Grid
{
    private readonly Block block;
    private readonly ISet<string>? hidden;
    private readonly Action? changed;
    private readonly List<Cell> cells = new();
    private bool editing;

    private sealed class Cell
    {
        public required (BlockRow Row, string Sep, CheckBox Box)[] Parts;
        public TextBlock? Label;
        public required TextBlock Value;
        public required FrameworkElement[] Shown;
    }

    /// <param name="hidden">The widget's hidden rows (null: the tab's, every row, no checkboxes).</param>
    /// <param name="changed">A row ticked or unticked.</param>
    public BlockView(Block block, ISet<string>? hidden = null, Action? changed = null, bool title = false)
    {
        this.block = block;
        this.hidden = hidden;
        this.changed = changed;
        if (title)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = Ui.Header(block.Title);
            t.Margin = new Thickness(0, 0, 0, 4);
            Children.Add(t);
        }
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        FrameworkElement body = block.Lines == null ? CarBody() : LinesBody(block.Lines);
        SetRow(body, title ? 1 : 0);
        Children.Add(body);
        ShowRows();
    }

    public Block Block => block;

    /// <summary>Its widget is being edited: every cell, with its checkboxes.</summary>
    public bool Editing
    {
        get => editing;
        set { editing = value; ShowRows(); }
    }

    // ---------------------------------------------------------------- building

    private BlockRow RowOf(string key) => block.Rows.First(r => r.Key == key);

    private CheckBox Box(BlockRow row, bool withLabel)
    {
        var box = new CheckBox { IsChecked = hidden?.Contains(row.Key) != true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            Visibility = Visibility.Collapsed, ToolTip = T("Show in the widget"), Content = withLabel ? row.Label : null };
        AutomationProperties.SetName(box, F("Show {0}", row.Label));
        // (Checked / Unchecked, not Click: a toggle through UI Automation raises no Click)
        box.Checked += (_, _) => { if (hidden?.Remove(row.Key) == true) this.changed?.Invoke(); };
        box.Unchecked += (_, _) => { if (hidden?.Add(row.Key) == true) this.changed?.Invoke(); };
        return box;
    }

    private static TextBlock ValueText() =>
        new() { Text = Blocks.None, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>The cells in a grid: per column a label and a value, with a gap between columns.</summary>
    private Grid LinesBody(BlockCell[][] lines)
    {
        var grid = new Grid();
        int columns = lines.Max(l => l.Length);
        for (int c = 0; c < columns; c++)
        {
            if (c > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (int r = 0; r < lines.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < lines[r].Length; c++)
            {
                var parts = lines[r][c].Parts.Select(p => { var row = RowOf(p.Key); return (row, p.Sep, Box(row, withLabel: true)); }).ToArray();
                var label = new TextBlock { Margin = new Thickness(0, 1, 10, 1), VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                var boxes = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 4, 1) };
                foreach (var p in parts) boxes.Children.Add(p.Item3);
                var head = new Grid { Children = { label, boxes } };
                var value = ValueText();
                SetRow(head, r); SetColumn(head, c * 3);
                SetRow(value, r); SetColumn(value, c * 3 + 1);
                grid.Children.Add(head); grid.Children.Add(value);
                cells.Add(new Cell { Parts = parts, Label = label, Value = value, Shown = new FrameworkElement[] { head, value } });
            }
        }
        return grid;
    }

    /// <summary>Tires: the car from above, each wheel's temperature beside it.</summary>
    private Viewbox CarBody()
    {
        var canvas = CarDrawing.Make();
        foreach (var (key, left, top) in new[] { ("FL", true, CarDrawing.FrontY), ("FR", false, CarDrawing.FrontY), ("RL", true, CarDrawing.RearY), ("RR", false, CarDrawing.RearY) })
        {
            var row = RowOf(key);
            var box = Box(row, withLabel: false);
            var value = ValueText();
            value.FontSize = 21;
            AutomationProperties.SetName(value, row.Label);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = CarDrawing.TextWidth, Height = 30,
                FlowDirection = left ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            value.FlowDirection = box.FlowDirection = FlowDirection.LeftToRight;
            if (left) { panel.Children.Add(value); panel.Children.Add(box); }
            else { panel.Children.Add(box); panel.Children.Add(value); }
            Canvas.SetLeft(panel, left ? 0 : CarDrawing.Width - CarDrawing.TextWidth);
            Canvas.SetTop(panel, top - 15);
            canvas.Children.Add(panel);
            cells.Add(new Cell { Parts = new[] { (row, "", box) }, Value = value, Shown = new FrameworkElement[] { value } });
        }
        return new Viewbox { Child = canvas, Stretch = System.Windows.Media.Stretch.Uniform, MaxHeight = hidden == null ? 200 : double.PositiveInfinity };
    }

    // ---------------------------------------------------------------- showing

    private bool Ticked(BlockRow row) => hidden?.Contains(row.Key) != true;

    private void ShowRows()
    {
        foreach (var cell in cells)
        {
            bool any = cell.Parts.Any(p => Ticked(p.Row));
            foreach (var e in cell.Shown) e.Visibility = editing || any ? Visibility.Visible : Visibility.Collapsed;
            bool boxes = editing && hidden != null;
            foreach (var p in cell.Parts) p.Box.Visibility = boxes ? Visibility.Visible : Visibility.Collapsed;
            // (a cell's checkboxes, its rows' labels, stand in for its label and value: the widget keeps its size)
            if (cell.Label != null)
            {
                cell.Label.Visibility = boxes ? Visibility.Hidden : Visibility.Visible;
                if (boxes) cell.Value.Visibility = Visibility.Collapsed;
            }
            cell.Value.Opacity = editing && !any ? 0.45 : 1;
        }
    }

    public void Update(Reading reading)
    {
        foreach (var cell in cells)
        {
            if (cell.Value.Visibility != Visibility.Visible) continue;
            var ticked = cell.Parts.Where(p => Ticked(p.Row)).Select(p => (p.Row, p.Sep, Text: p.Row.Show(reading))).ToList();
            var shown = ticked.Where(p => p.Text != Blocks.None).ToList();
            string value = shown.Count > 0 ? Join(shown.Select(p => (p.Sep, p.Text))) : Blocks.None;
            if (cell.Value.Text != value) cell.Value.Text = value;
            if (cell.Label != null)
            {
                string label = Join((shown.Count > 0 ? shown : ticked).Select(p => (p.Sep, p.Row.Label)));
                if (cell.Label.Text != label) cell.Label.Text = label;
            }
            cell.Value.Opacity = editing && ticked.Count == 0 ? 0.45 : 1;
        }
        if (editing) ShowRows();
    }

    private static string Join(IEnumerable<(string Sep, string Text)> parts)
    {
        var s = new System.Text.StringBuilder();
        foreach (var (sep, text) in parts) s.Append(s.Length > 0 ? sep : "").Append(text);
        return s.ToString();
    }
}
