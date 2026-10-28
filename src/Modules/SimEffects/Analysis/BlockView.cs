using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// A block on the tab (every row) and in its widget (the rows not hidden). Cells: line by line, a label and a value
/// each, in columns. Tires: the car drawing with a temperature at each wheel. Graphs (Performance): a graph of the last
/// minute per row, with its value and peak. Timing: lap times in large type. Big (Position, Lap): one large "3 / 8".
/// The driving ones scale with their widget. While the widget is being edited every row shows a checkbox (a cell's in
/// place of its label and value; Tires: beside each temperature): unticked, the row is hidden once Done is pressed.
/// </summary>
internal sealed class BlockView : Grid
{
    /// <summary>The type the driving blocks use, to read at a glance: Bahnschrift (Windows' own, a DIN as Forza's
    /// screens use), semi-condensed and semi-bold.</summary>
    public static readonly FontFamily RaceFont = new("Bahnschrift, Segoe UI");

    /// <summary>A lap as fast as your best ever (purple, as in racing's timing screens).</summary>
    private static readonly Brush PersonalBestBrush = Frozen(Color.FromRgb(0xB3, 0x6B, 0xFF));

    private readonly Block block;
    private readonly ISet<string>? hidden;
    private readonly Action? changed;
    private readonly List<Cell> cells = new();
    private readonly List<Line> lines = new();
    private bool editing;

    private sealed class Cell
    {
        public required (BlockRow Row, string Sep, CheckBox Box)[] Parts;
        public TextBlock? Label;
        public required TextBlock Value;
        public required FrameworkElement[] Shown;
    }

    /// <summary>A row of a driving block: its checkbox, what shows it, and how it's updated.</summary>
    private sealed record Line(BlockRow Row, CheckBox? Box, FrameworkElement Element, Action<Reading> Update);

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
        FrameworkElement body = block.Kind switch
        {
            BlockKind.Tires => CarBody(),
            BlockKind.Graphs => GraphsBody(),
            BlockKind.Timing => TimingBody(),
            BlockKind.Big => BigBody(),
            _ => LinesBody(block.Lines!),
        };
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

    private static TextBlock RaceText(double size)
    {
        var t = new TextBlock { Text = Blocks.None, FontFamily = RaceFont, FontStretch = FontStretches.SemiCondensed, FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return t;
    }

    /// <summary>A small capitals label over or beside a driving block's value.</summary>
    private static TextBlock Caption(string text, double size = 13)
    {
        var t = new TextBlock { Text = text.ToUpperInvariant(), FontFamily = RaceFont, FontStretch = FontStretches.SemiCondensed, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return t;
    }

    /// <summary>A driving block scales with its widget; on the tab, up to <paramref name="tabHeight"/>.</summary>
    private Viewbox Scaled(UIElement child, double tabHeight) =>
        new() { Child = child, Stretch = Stretch.Uniform, MaxHeight = hidden == null ? tabHeight : double.PositiveInfinity };

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

    /// <summary>Tires: the car from above, each wheel's temperature beside it, large.</summary>
    private Viewbox CarBody()
    {
        var canvas = CarDrawing.Make();
        foreach (var (key, left, top) in new[] { ("FL", true, CarDrawing.FrontY), ("FR", false, CarDrawing.FrontY), ("RL", true, CarDrawing.RearY), ("RR", false, CarDrawing.RearY) })
        {
            var row = RowOf(key);
            var box = Box(row, withLabel: false);
            var value = RaceText(30);
            AutomationProperties.SetName(value, row.Label);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = CarDrawing.TextWidth, Height = 40,
                FlowDirection = left ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            value.FlowDirection = box.FlowDirection = FlowDirection.LeftToRight;
            if (left) { panel.Children.Add(value); panel.Children.Add(box); }
            else { panel.Children.Add(box); panel.Children.Add(value); }
            Canvas.SetLeft(panel, left ? CarDrawing.LeftText : CarDrawing.RightText);
            Canvas.SetTop(panel, top - 20);
            canvas.Children.Add(panel);
            cells.Add(new Cell { Parts = new[] { (row, "", box) }, Value = value, Shown = new FrameworkElement[] { value } });
        }
        return Scaled(canvas, 170);
    }

    /// <summary>Performance: a graph of the last minute per row (power, torque, boost), its value now and its peak.</summary>
    private UniformGrid GraphsBody()
    {
        var grid = new UniformGrid { Columns = 1 };
        for (int i = 0; i < block.Rows.Length; i++)
        {
            var row = block.Rows[i];
            int measure = i;
            var box = hidden == null ? null : Box(row, withLabel: false);
            var value = RaceText(22);
            AutomationProperties.SetName(value, row.Label);
            var peak = Caption("", 11);
            peak.Margin = new Thickness(10, 0, 0, 0);
            var head = new DockPanel { LastChildFill = false };
            if (box != null) head.Children.Add(box);
            head.Children.Add(Caption(row.Label));
            DockPanel.SetDock(peak, Dock.Right);
            DockPanel.SetDock(value, Dock.Right);
            head.Children.Add(peak);
            head.Children.Add(value);
            var graph = new Graph(measure) { MinHeight = hidden == null ? 54 : 24, Margin = new Thickness(0, 2, 0, 0) };
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(head, Dock.Top);
            line.Children.Add(head);
            line.Children.Add(graph);
            grid.Children.Add(line);
            lines.Add(new Line(row, box, line, r =>
            {
                value.Text = row.Show(r);
                graph.Show(r);
                peak.Text = graph.PeakText is { } p ? T("Peak") + " " + p : "";
            }));
        }
        return grid;
    }

    /// <summary>Timing: each lap time large, its name small beside it; the current lap the largest.</summary>
    private Viewbox TimingBody()
    {
        var grid = new UniformGrid { Columns = 1, Width = 300 };
        foreach (var row in block.Rows)
        {
            bool current = row.Key == "TimeCurr";
            var box = hidden == null ? null : Box(row, withLabel: false);
            var value = RaceText(current ? 46 : 34);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            AutomationProperties.SetName(value, row.Label);
            var label = Caption(row.Label, 15);
            label.Width = 80;
            var line = new DockPanel();
            if (box != null) line.Children.Add(box);
            line.Children.Add(label);
            line.Children.Add(value);
            grid.Children.Add(line);
            lines.Add(new Line(row, box, line, r =>
            {
                value.Text = row.Show(r);
                // the last lap: purple as fast as your best, green as the session's
                if (row.Key == "TimeLast" && r.Frame is { LastLap: > 0 } f)
                {
                    if (r.PersonalBest > 0 && f.LastLap <= r.PersonalBest + 0.0005f) value.Foreground = PersonalBestBrush;
                    else if (r.SessionBest > 0 && f.LastLap <= r.SessionBest + 0.0005f) value.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
                    else value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                }
            }));
        }
        return Scaled(grid, 210);
    }

    /// <summary>Position, Lap: "3 / 8", the "of" smaller, under its name.</summary>
    private Viewbox BigBody()
    {
        var row = block.Rows[0];
        var text = RaceText(64);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(text, row.Label);
        var panel = new StackPanel { MinWidth = 180 };
        var caption = Caption(row.Label, 15);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(caption);
        panel.Children.Add(text);
        string shown = "";
        lines.Add(new Line(row, null, panel, r =>
        {
            string s = row.Show(r);
            if (s == shown) return;
            shown = s;
            text.Inlines.Clear();
            int of = s.IndexOf(" / ", StringComparison.Ordinal);
            text.Inlines.Add(new Run(of < 0 ? s : s[..of]));
            if (of >= 0)
            {
                var rest = new Run(s[of..]) { FontSize = 38 };
                rest.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
                text.Inlines.Add(rest);
            }
        }));
        return Scaled(panel, 90);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ---------------------------------------------------------------- showing

    private bool Ticked(BlockRow row) => hidden?.Contains(row.Key) != true;

    private void ShowRows()
    {
        bool boxes = editing && hidden != null;
        foreach (var cell in cells)
        {
            bool any = cell.Parts.Any(p => Ticked(p.Row));
            foreach (var e in cell.Shown) e.Visibility = editing || any ? Visibility.Visible : Visibility.Collapsed;
            foreach (var p in cell.Parts) p.Box.Visibility = boxes ? Visibility.Visible : Visibility.Collapsed;
            // (a cell's checkboxes, its rows' labels, stand in for its label and value: the widget keeps its size)
            if (cell.Label != null)
            {
                cell.Label.Visibility = boxes ? Visibility.Hidden : Visibility.Visible;
                if (boxes) cell.Value.Visibility = Visibility.Collapsed;
            }
            cell.Value.Opacity = editing && !any ? 0.45 : 1;
        }
        foreach (var line in lines)
        {
            bool ticked = Ticked(line.Row);
            line.Element.Visibility = editing || ticked ? Visibility.Visible : Visibility.Collapsed;
            line.Element.Opacity = editing && !ticked ? 0.45 : 1;
            if (line.Box != null) line.Box.Visibility = boxes ? Visibility.Visible : Visibility.Collapsed;
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
        foreach (var line in lines)
            if (line.Element.Visibility == Visibility.Visible) line.Update(reading);
        if (editing) ShowRows();
    }

    private static string Join(IEnumerable<(string Sep, string Text)> parts)
    {
        var s = new System.Text.StringBuilder();
        foreach (var (sep, text) in parts) s.Append(s.Length > 0 ? sep : "").Append(text);
        return s.ToString();
    }

    // ---------------------------------------------------------------- a graph

    /// <summary>
    /// One measure of <see cref="PerfHistory"/> over the last minute, newest at the right: a 2 px line in the accent
    /// over a fade of it, a faint line at its peak and (boost) at 0, scaled from its lowest to a little over its peak.
    /// </summary>
    private sealed class Graph : FrameworkElement
    {
        private readonly int measure;
        private Reading? reading;

        public Graph(int measure)
        {
            this.measure = measure;
            SnapsToDevicePixels = true;
        }

        /// <summary>Its peak in the units shown ("512 hp"), or null with no history.</summary>
        public string? PeakText { get; private set; }

        public void Show(Reading r)
        {
            reading = r;
            var h = r.History;
            PeakText = h is { Count: > 0 } ? Text(r.Settings, h.Peak[measure]) : null;
            InvalidateVisual();
        }

        private float In(AnalysisSettings s, float v) => measure switch
        {
            0 => Blocks.Hp(v),
            1 => Blocks.TorqueIn(s, v),
            _ => Blocks.BoostIn(s, v),
        };

        private string Text(AnalysisSettings s, float v) => measure switch
        {
            0 => $"{Blocks.Hp(v):0} hp",
            1 => Blocks.TorqueText(s, v),
            _ => Blocks.BoostText(s, v),
        };

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, ht = ActualHeight;
            if (w < 4 || ht < 4) return;
            var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x3D, 0x8B, 0xFD);
            var grid = (TryFindResource("TextSecondaryBrush") as SolidColorBrush)?.Color ?? Colors.Gray;
            var faint = new Pen(new SolidColorBrush(Color.FromArgb(0x40, grid.R, grid.G, grid.B)), 1);
            // (an empty plot still shows its floor)
            dc.DrawLine(faint, new Point(0, ht - 0.5), new Point(w, ht - 0.5));
            if (reading?.History is not { Count: > 1 } h) return;
            var s = reading.Settings;
            double lo = Math.Min(0, In(s, h.Low[measure])), hi = Math.Max(In(s, h.Peak[measure]), lo + 0.001);
            hi += (hi - lo) * 0.1;
            double Y(float v) => ht - (In(s, v) - lo) / (hi - lo) * ht;
            double X(int k) => w * (PerfHistory.Size - h.Count + k) / (PerfHistory.Size - 1);

            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(X(0), Y(h.At(measure, 0))), false, false);
                for (int k = 1; k < h.Count; k++) g.LineTo(new Point(X(k), Y(h.At(measure, k))), true, true);
            }
            double zero = ht - (0 - lo) / (hi - lo) * ht;
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(X(0), zero), true, true);
                for (int k = 0; k < h.Count; k++) g.LineTo(new Point(X(k), Y(h.At(measure, k))), false, false);
                g.LineTo(new Point(X(h.Count - 1), zero), false, false);
            }
            var fade = new LinearGradientBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B), Color.FromArgb(0x00, accent.R, accent.G, accent.B), 90);
            dc.DrawGeometry(fade, null, area);
            // its peak, and (boost) 0 when there's vacuum below it
            double peak = Y(h.Peak[measure]);
            var dashed = new Pen(faint.Brush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(dashed, new Point(0, Math.Round(peak) + 0.5), new Point(w, Math.Round(peak) + 0.5));
            if (lo < 0) dc.DrawLine(faint, new Point(0, Math.Round(zero) + 0.5), new Point(w, Math.Round(zero) + 0.5));
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 2) { LineJoin = PenLineJoin.Round }, line);
            // now: a dot at the line's end
            var end = new Point(X(h.Count - 1), Y(h.At(measure, h.Count - 1)));
            dc.DrawEllipse(new SolidColorBrush(accent), null, end, 4, 4);
        }
    }
}
