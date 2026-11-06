using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// (since 4.6.2; a line over Races before) Racing Statistics: over every race finished, a tile each for the races, wins,
/// podiums and top fives (each with its share and a bar), the average and best finish, the average start, the places
/// gained a race and how often you finished ahead of where you started; beside them the finishing places as bars (gold,
/// silver and bronze for the podium; since 4.6.4 as tall as the tiles) and the last races' places, newest right (since
/// 4.6.4 a click opens one in the session analysis).
/// </summary>
internal sealed class RaceStatsView : Grid
{
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xC8, 0x3D), Silver = Color.FromRgb(0xC4, 0xCC, 0xD6), Bronze = Color.FromRgb(0xD9, 0x8C, 0x4E);
    private static readonly Color Good = Color.FromRgb(0x3D, 0xDC, 0x84), Bad = Color.FromRgb(0xF0, 0x3E, 0x3E);
    private readonly UniformGrid tiles = new() { Columns = 4 };
    private readonly Grid bars = new() { MinHeight = 120 };
    private readonly Action<RaceRecord> open;
    private readonly WrapPanel form = new();
    private readonly TextBlock none = Ui.Note(T("A race is added when it finishes, with where you started and finished."));

    /// <param name="open">A last race clicked: its session analysed.</param>
    public RaceStatsView(Action<RaceRecord> open)
    {
        this.open = open;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // (the tiles' two rows set the height; Finishes fills it beside them)
        var right = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var barsHead = Caption(T("Finishes"));
        barsHead.Margin = new Thickness(0, 0, 0, 4);
        DockPanel.SetDock(barsHead, Dock.Top);
        right.Children.Add(barsHead);
        right.Children.Add(bars);
        Grid.SetColumn(right, 2);
        var formHead = Caption(T("Last races"));
        formHead.Margin = new Thickness(0, 6, 0, 4);
        var last = new StackPanel { Children = { formHead, form } };
        Grid.SetRow(last, 1);
        Grid.SetColumnSpan(last, 3);
        Grid.SetColumnSpan(none, 3);
        Children.Add(tiles);
        Children.Add(right);
        Children.Add(last);
        Children.Add(none);
    }

    private static TextBlock Caption(string text)
    {
        var t = new TextBlock { Text = text.ToUpperInvariant(), FontFamily = BlockView.RaceFont, FontStretch = FontStretches.SemiCondensed, FontSize = 12 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return t;
    }

    public void Render(IReadOnlyList<RaceRecord> all)
    {
        tiles.Children.Clear();
        bars.Children.Clear();
        bars.ColumnDefinitions.Clear();
        bars.RowDefinitions.Clear();
        form.Children.Clear();
        bool any = all.Count > 0;
        none.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        foreach (UIElement c in Children) if (c != none) c.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!any) return;
        var placed = all.Where(r => r.Finish > 0).ToList();
        var both = placed.Where(r => r.Start > 0).ToList();
        int n = placed.Count, wins = placed.Count(r => r.Finish == 1), podiums = placed.Count(r => r.Finish <= 3), top5 = placed.Count(r => r.Finish <= 5);
        double Share(int k) => n == 0 ? 0 : (double)k / n;
        string Pct(double v) => (v * 100).ToString("0", CultureInfo.CurrentCulture) + "%";

        Tile(all.Count.ToString(CultureInfo.CurrentCulture), T("Races"), P(all.Select(r => r.Track).Distinct().Count(), "{0} track", "{0} tracks"), null, null);
        Tile(wins.ToString(CultureInfo.CurrentCulture), T("Wins"), F("{0} of races", Pct(Share(wins))), Share(wins), Gold);
        Tile(podiums.ToString(CultureInfo.CurrentCulture), T("Podiums"), F("{0} of races", Pct(Share(podiums))), Share(podiums), Silver);
        Tile(top5.ToString(CultureInfo.CurrentCulture), T("Top 5"), F("{0} of races", Pct(Share(top5))), Share(top5), Bronze);
        Tile(n > 0 ? placed.Average(r => r.Finish).ToString("0.0", CultureInfo.CurrentCulture) : Blocks.None, T("Average finish"),
            n > 0 ? F("Best {0}", Ordinal(placed.Min(r => r.Finish))) : "", null, null);
        Tile(both.Count > 0 ? both.Average(r => r.Start).ToString("0.0", CultureInfo.CurrentCulture) : Blocks.None, T("Average start"),
            both.Count > 0 ? F("Best {0}", Ordinal(both.Min(r => r.Start))) : "", null, null);
        double gained = both.Count > 0 ? both.Average(r => r.Start - r.Finish) : double.NaN;
        Tile(double.IsNaN(gained) ? Blocks.None : gained.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture), T("Places gained a race"),
            both.Count > 0 ? F("{0} up · {1} down", both.Count(r => r.Finish < r.Start), both.Count(r => r.Finish > r.Start)) : "", null,
            double.IsNaN(gained) ? null : gained > 0 ? Good : gained < 0 ? Bad : null);
        double ahead = both.Count == 0 ? 0 : (double)both.Count(r => r.Finish < r.Start) / both.Count;
        Tile(both.Count > 0 ? Pct(ahead) : Blocks.None, T("Finished ahead"), T("of where you started"), both.Count > 0 ? ahead : null, Good);

        // the finishing places: 1 to 10, then 11+
        var counts = new int[11];
        foreach (var r in placed) counts[Math.Min(r.Finish, 11) - 1]++;
        int most = Math.Max(1, counts.Max());
        bars.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bars.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < counts.Length; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var colour = PlaceColour(i + 1);
            // (a column of its own: room for the count over the bar, then the bar, by stars so it fills the height)
            var column = new Grid { Margin = new Thickness(2, 0, 2, 0) };
            double share = Math.Max(0.015, (double)counts[i] / most);
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0001 - share, GridUnitType.Star) });
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(share, GridUnitType.Star) });
            var bar = new Border
            {
                CornerRadius = new CornerRadius(3, 3, 0, 0), Background = Ui.Brush(Color.FromArgb(counts[i] == 0 ? (byte)0x30 : (byte)0xFF, colour.R, colour.G, colour.B)),
                ToolTip = F("{0}: {1}", Ordinal(i + 1) + (i == 10 ? "+" : ""), P(counts[i], "{0} race", "{0} races")),
            };
            Grid.SetRow(bar, 2);
            var count = new TextBlock { Text = counts[i] > 0 ? counts[i].ToString(CultureInfo.CurrentCulture) : "", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };
            Grid.SetRow(count, 1);
            // (the count's height kept above the tallest bar)
            column.Children.Add(new TextBlock { Text = " ", FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
            column.Children.Add(count);
            column.Children.Add(bar);
            var label = new TextBlock { Text = i == 10 ? "11+" : (i + 1).ToString(CultureInfo.CurrentCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(column, i);
            bars.Children.Add(column);
            Grid.SetColumn(label, i);
            Grid.SetRow(label, 1);
            bars.Children.Add(label);
        }

        // the last races, oldest left
        foreach (var r in all.OrderByDescending(r => r.At).Take(15).Reverse())
        {
            var colour = r.Finish > 0 ? PlaceColour(r.Finish) : Colors.Gray;
            var chip = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 5, 5), BorderThickness = new Thickness(2),
                BorderBrush = Ui.Brush(colour), Background = Ui.Brush(Color.FromArgb(r.Finish is > 0 and <= 3 ? (byte)0x50 : (byte)0x18, colour.R, colour.G, colour.B)),
                ToolTip = r.At.ToString("g", CultureInfo.CurrentCulture) + (r.Start > 0 ? " · " + F("started {0}", Ordinal(r.Start)) : ""),
                Child = new TextBlock { Text = r.Finish > 0 ? r.Finish.ToString(CultureInfo.CurrentCulture) : Blocks.None, FontFamily = BlockView.RaceFont, FontSize = 13,
                    FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            // (since 4.6.4) a click: its race in the session analysis
            if (r.Session != null)
            {
                var race = r;
                chip.Cursor = System.Windows.Input.Cursors.Hand;
                chip.MouseLeftButtonUp += (_, _) => open(race);
            }
            form.Children.Add(chip);
        }
    }

    private static Color PlaceColour(int place) => place switch { 1 => Gold, 2 => Silver, 3 => Bronze, _ => Color.FromRgb(0x4D, 0xA3, 0xFF) };

    private static string Ordinal(int n)
    {
        string suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.CurrentCulture) + suffix;
    }

    /// <summary>A tile: its number large (in <paramref name="colour"/>, if any), its name, a line under it and, for a
    /// share, a bar filled that far.</summary>
    private void Tile(string value, string name, string sub, double? share, Color? colour)
    {
        var s = new StackPanel();
        var big = new TextBlock { Text = value, FontFamily = BlockView.RaceFont, FontStretch = FontStretches.SemiCondensed, FontSize = 32, FontWeight = FontWeights.SemiBold };
        if (colour is { } c) big.Foreground = Ui.Brush(c); else big.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        s.Children.Add(big);
        s.Children.Add(Caption(name));
        if (share is { } f && colour is { } bc)
        {
            var track = new Grid { Height = 5, Margin = new Thickness(0, 6, 0, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, f), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - f), GridUnitType.Star) });
            var fill = new Border { Background = Ui.Brush(bc), CornerRadius = new CornerRadius(2.5) };
            var rest = new Border { Background = Ui.Brush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), CornerRadius = new CornerRadius(2.5) };
            Grid.SetColumn(rest, 1);
            track.Children.Add(fill);
            track.Children.Add(rest);
            s.Children.Add(track);
        }
        var line = new TextBlock { Text = sub, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(line);
        var tile = new Border { Child = s, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 10), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1) };
        tile.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        if (colour is { } accent) tile.BorderBrush = Ui.Brush(Color.FromArgb(0x70, accent.R, accent.G, accent.B));
        System.Windows.Automation.AutomationProperties.SetName(tile, name + " " + value);
        tiles.Children.Add(tile);
    }
}
