using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DaisysApp.Applets.Performance;

/// <summary>
/// The Hardware sensors card: a filter chip per kind of sensor and a block per piece of hardware with its sensors'
/// value, minimum and maximum. The blocks are rebuilt only when the filter or the set of sensors changes; otherwise
/// just the numbers are updated, so it doesn't flicker.
/// </summary>
internal sealed class SensorPanel
{
    public sealed record Kind(string Label, Func<string, bool> Matches);

    public static readonly IReadOnlyList<Kind> Kinds = new Kind[]
    {
        new("Temperatures", t => t == "Temperature"),
        new("Fans", t => t is "Fan" or "Control"),
        new("Power", t => t is "Power" or "Current" or "Energy"),
        new("Voltages", t => t == "Voltage"),
        new("Clocks", t => t is "Clock" or "Frequency"),
        new("Load", t => t == "Load"),
        new("Other", t => t is not ("Temperature" or "Fan" or "Control" or "Power" or "Current" or "Energy" or "Voltage" or "Clock" or "Frequency" or "Load")),
    };

    /// <summary>Fixed values (thresholds, resolutions) and headroom figures that aren't worth a row.</summary>
    public static bool Hidden(HwSensor s) =>
        new[] { "Limit", "Resolution", "Warning Temperature", "Critical Temperature", "Threshold", "Distance to TjMax" }.Any(w => s.Name.Contains(w, StringComparison.OrdinalIgnoreCase));

    private readonly WrapPanel filters, groups;
    private readonly Action<IReadOnlyList<HwSensor>, string> open;
    private readonly Dictionary<string, (TextBlock Value, TextBlock Min, TextBlock Max)> cells = new();
    private readonly Dictionary<Kind, RadioButton> chips = new();
    private Kind kind = Kinds[0];
    private string layout = "";

    /// <param name="open">Opens the history of some sensors, with a title.</param>
    public SensorPanel(WrapPanel filters, WrapPanel groups, Action<IReadOnlyList<HwSensor>, string> open)
    {
        this.filters = filters;
        this.groups = groups;
        this.open = open;
        foreach (var k in Kinds)
        {
            var chip = new RadioButton { GroupName = "SensorKind", Margin = new Thickness(0, 0, 8, 0), IsChecked = k == kind };
            chip.SetResourceReference(FrameworkElement.StyleProperty, "ChipToggle");
            chip.Checked += (_, _) => { kind = k; layout = ""; Update(last); };
            chips[k] = chip;
            filters.Children.Add(chip);
        }
    }

    private HwSnapshot? last;

    public void Update(HwSnapshot? snapshot)
    {
        last = snapshot;
        if (snapshot == null) return;
        var visible = snapshot.Sensors.Where(s => !Hidden(s)).ToList();
        foreach (var k in Kinds)
        {
            int n = visible.Count(s => k.Matches(s.Type));
            chips[k].Content = n > 0 ? $"{k.Label}  {n}" : k.Label;
            chips[k].IsEnabled = n > 0;
        }

        var shown = visible.Where(s => kind.Matches(s.Type)).ToList();
        string newLayout = string.Join("\n", shown.Select(s => s.Key));
        if (newLayout != layout)
        {
            layout = newLayout;
            Build(shown);
        }
        foreach (var s in shown)
        {
            if (!cells.TryGetValue(s.Key, out var c)) continue;
            c.Value.Text = s.Text(s.Value);
            c.Min.Text = s.Text(s.Min);
            c.Max.Text = s.Text(s.Max);
        }
    }

    private void Build(List<HwSensor> shown)
    {
        groups.Children.Clear();
        cells.Clear();
        foreach (var hw in shown.GroupBy(s => s.Hardware))
        {
            var list = hw.ToList();
            var block = new StackPanel { Width = 330, Margin = new Thickness(0, 0, 24, 16) };

            var title = new TextBlock
            {
                Text = hw.Key,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = Cursors.Hand,
                ToolTip = $"All of {hw.Key}'s {kind.Label.ToLowerInvariant()} on one graph",
                Margin = new Thickness(0, 0, 0, 6),
            };
            title.MouseLeftButtonUp += (_, _) => open(list, $"{hw.Key} — {kind.Label.ToLowerInvariant()}");
            block.Children.Add(title);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (int w in new[] { 76, 68, 68 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            AddRow(grid, new[] { "", "Now", "Min", "Max" }, header: true, sensor: null);
            foreach (var s in list) cells[s.Key] = AddRow(grid, new[] { s.Name, "", "", "" }, header: false, sensor: s);
            block.Children.Add(grid);
            groups.Children.Add(block);
        }
    }

    private (TextBlock, TextBlock, TextBlock) AddRow(Grid grid, string[] texts, bool header, HwSensor? sensor)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var blocks = new TextBlock[4];
        for (int c = 0; c < 4; c++)
        {
            var t = new TextBlock { Text = texts[c], Margin = new Thickness(0, 0, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis };
            if (header || c >= 2) t.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
            if (c > 0) t.HorizontalAlignment = HorizontalAlignment.Right;
            if (c == 1 && !header) t.FontWeight = FontWeights.SemiBold;
            if (sensor != null)
            {
                t.Cursor = Cursors.Hand;
                t.ToolTip = $"{sensor.Name} — click for its graph";
                t.MouseLeftButtonUp += (_, _) => open(new[] { sensor }, $"{sensor.Hardware} — {sensor.Name}");
            }
            Grid.SetRow(t, row);
            Grid.SetColumn(t, c);
            grid.Children.Add(t);
            blocks[c] = t;
        }
        return (blocks[1], blocks[2], blocks[3]);
    }
}
