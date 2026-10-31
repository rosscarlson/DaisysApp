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
/// minute per row, with its value and peak. Timing: lap times in large type. Big (Position): large "3 / 8"s side by side.
/// Board (Leaderboard): the field around you, a column a row.
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

    /// <summary>A tyre's temperature band (<see cref="AnalysisSettings.TyreBand"/>): green, orange, red.</summary>
    private static readonly Brush[] TyreBrushes =
    {
        Frozen(Color.FromRgb(0x3D, 0xDC, 0x84)), Frozen(Color.FromRgb(0xFF, 0x9F, 0x1A)), Frozen(Color.FromRgb(0xF0, 0x3E, 0x3E)),
    };

    private readonly Block block;
    private readonly ISet<string>? hidden;
    private readonly Action? changed;
    private readonly List<Cell> cells = new();
    private readonly List<Line> lines = new();
    private bool editing;
    /// <summary>The Leaderboard's lines, filled on every update (its columns hide with their headers).</summary>
    private Action<Reading>? board;

    /// <summary>The most cars the Leaderboard shows.</summary>
    public const int MostBoardRows = 20;

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
    /// (A widget's title is its <see cref="OverlayFrame"/>'s.)
    public BlockView(Block block, ISet<string>? hidden = null, Action? changed = null)
    {
        this.block = block;
        this.hidden = hidden;
        this.changed = changed;
        FrameworkElement body = block.Kind switch
        {
            BlockKind.Tires => CarBody(),
            BlockKind.Graphs => GraphsBody(),
            BlockKind.Timing => TimingBody(),
            BlockKind.Big => BigBody(),
            BlockKind.Board => BoardBody(),
            _ => LinesBody(block.Lines!),
        };
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

    /// <summary>Tires: the car from above, each wheel's temperature beside it, large; the tyre and its temperature green,
    /// orange or red by how hot it is (the bands in Settings).</summary>
    private Viewbox CarBody()
    {
        var canvas = CarDrawing.Make(out var tyres);
        int wheel = 0;
        foreach (var (key, left, top) in new[] { ("FL", true, CarDrawing.FrontY), ("FR", false, CarDrawing.FrontY), ("RL", true, CarDrawing.RearY), ("RR", false, CarDrawing.RearY) })
        {
            int w = wheel++;
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
            int band = -1;
            lines.Add(new Line(row, null, value, r =>
            {
                int b = r.Frame is { HasRace: true } f ? r.Settings.TyreBand(f.TireTemp[w]) : 0;
                if (b == band) return;
                band = b;
                var brush = b > 0 ? TyreBrushes[b - 1] : null;
                CarDrawing.Paint(tyres[w], brush);
                if (brush != null) value.Foreground = brush; else value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }));
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
        return Scaled(grid, 240);
    }

    /// <summary>Position, and Lap beside it in a race with laps (else Position alone, in the middle): "3 / 8", the "of"
    /// smaller, under its name.</summary>
    private Viewbox BigBody()
    {
        var panel = new Grid { MinWidth = 180 };
        var parts = new List<FrameworkElement>();
        foreach (var row in block.Rows)
        {
            var text = RaceText(64);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            AutomationProperties.SetName(text, row.Label);
            var box = hidden == null ? null : Box(row, withLabel: false);
            var caption = Caption(row.Label, 15);
            var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (box != null) head.Children.Add(box);
            head.Children.Add(caption);
            var one = new StackPanel { Margin = new Thickness(10, 0, 10, 0), MinWidth = 150, Children = { head, text } };
            parts.Add(one);
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SetColumn(one, parts.Count - 1);
            panel.Children.Add(one);
            string shown = "";
            bool lap = row.Key == "LapOf", done = row.Key == "DoneOf";
            lines.Add(new Line(row, box, one, r =>
            {
                // (a sprint has no laps: Position alone, or with how much is done once that's known, the Grid's other
                // columns empty, centred by the Viewbox)
                if (lap || done)
                {
                    bool on = lap ? Blocks.HasLaps(r) : r.Progress >= 0 && !Blocks.HasLaps(r);
                    var v = on || editing ? Visibility.Visible : Visibility.Collapsed;
                    if (one.Visibility != v && Ticked(row)) one.Visibility = v;
                }
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
        }
        return Scaled(panel, 90);
    }

    /// <summary>Leaderboard: a caption over each column (place, number, driver, gap), then a line a car; yours on the
    /// accent, one in the pits faded. Each gap is the car's to the one ahead of it.</summary>
    private Viewbox BoardBody()
    {
        var grid = new Grid { Width = 320 };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        for (int i = 0; i <= MostBoardRows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var mine = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.35, Visibility = Visibility.Collapsed };
        mine.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        SetColumnSpan(mine, 4);
        grid.Children.Add(mine);
        var empty = Caption(T("No field from this game"), 15);
        empty.Margin = new Thickness(6, 4, 6, 4);
        SetRow(empty, 1);
        SetColumnSpan(empty, 4);
        grid.Children.Add(empty);
        var heads = new FrameworkElement[block.Rows.Length];
        var cells = new TextBlock[block.Rows.Length][];
        for (int c = 0; c < block.Rows.Length; c++)
        {
            var row = block.Rows[c];
            var box = hidden == null ? null : Box(row, withLabel: false);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 2),
                HorizontalAlignment = c == 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            if (box != null) head.Children.Add(box);
            head.Children.Add(Caption(row.Label));
            SetColumn(head, c);
            grid.Children.Add(head);
            heads[c] = head;
            lines.Add(new Line(row, box, head, _ => { }));
            cells[c] = new TextBlock[MostBoardRows];
            for (int i = 0; i < MostBoardRows; i++)
            {
                var t = RaceText(20);
                t.Text = "";
                t.Margin = new Thickness(6, 1, 6, 1);
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                if (c == 3) t.HorizontalAlignment = HorizontalAlignment.Right;
                t.Visibility = Visibility.Collapsed;
                AutomationProperties.SetName(t, $"{row.Label} {i + 1}");
                SetRow(t, i + 1);
                SetColumn(t, c);
                grid.Children.Add(t);
                cells[c][i] = t;
            }
        }
        static void Set(TextBlock t, string s) { if (t.Text != s) t.Text = s; }
        board = r =>
        {
            var field = r.Frame?.Field;
            var cars = field == null ? Array.Empty<Racer>() : Blocks.Around(field, Math.Clamp(r.Settings.BoardRows, 3, MostBoardRows));
            empty.Visibility = field == null ? Visibility.Visible : Visibility.Collapsed;
            mine.Visibility = Visibility.Collapsed;
            for (int i = 0; i < MostBoardRows; i++)
            {
                bool on = i < cars.Count;
                for (int c = 0; c < cells.Length; c++)
                {
                    var t = cells[c][i];
                    var v = on && heads[c].Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
                    if (t.Visibility != v) t.Visibility = v;
                    t.Opacity = heads[c].Opacity * (on && cars[i].InPits ? 0.5 : 1);
                }
                if (!on) continue;
                var car = cars[i];
                Set(cells[0][i], car.Place.ToString(System.Globalization.CultureInfo.CurrentCulture));
                Set(cells[1][i], car.Number.Length > 0 ? "#" + car.Number : "");
                Set(cells[2][i], car.Name);
                Set(cells[3][i], Blocks.GapText(car, field!.Race));
                if (car.Me)
                {
                    mine.Visibility = Visibility.Visible;
                    SetRow(mine, i + 1);
                }
            }
        };
        return Scaled(grid, 420);
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
        // (a ticked one even while it's collapsed: Position's Lap shows itself again when a race has laps)
        foreach (var line in lines)
            if (editing || Ticked(line.Row)) line.Update(reading);
        if (editing) ShowRows();
        board?.Invoke(reading);
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
