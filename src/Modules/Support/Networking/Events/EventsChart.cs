using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Events → the chart beside the list (since 3.34.0): how many events in each minute (the last hour), 10 minutes (the
/// last day), hour (the last week) or day (the last 30 days), or all of them by hour of the day or day of the week, to
/// see when problems come. Each bar is the problems (red, at the bottom) and the other events (grey) above them; the
/// bar under the pointer says what's in it.
/// </summary>
internal sealed class EventsChart : FrameworkElement
{
    public const string Minute = "Minute", TenMinutes = "TenMinutes", Hour = "Hour", Day = "Day", HourOfDay = "HourOfDay", DayOfWeek = "DayOfWeek";

    /// <summary>The choices, for the picker.</summary>
    public static (string Text, string Value)[] Choices =>
    [
        (T("By minute"), Minute), (T("By 10 minutes"), TenMinutes), (T("By hour"), Hour), (T("By day"), Day),
        (T("Hour of the day"), HourOfDay), (T("Day of the week"), DayOfWeek),
    ];

    private sealed record Bucket(string Label, string Title, int Bad, int Other);

    private List<Bucket> buckets = new();
    private string by = Hour;
    private int? hover;

    public EventsChart()
    {
        MouseMove += (_, e) => SetHover(BucketAt(e.GetPosition(this)));
        MouseLeave += (_, _) => SetHover(null);
    }

    /// <summary>The events counted, by <paramref name="mode"/>.</summary>
    public void Show(IEnumerable<NetEvent> events, string mode)
    {
        by = mode;
        buckets = Count(events.ToList(), mode, DateTime.Now);
        InvalidateVisual();
    }

    private static List<Bucket> Count(List<NetEvent> events, string mode, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        if (mode == HourOfDay)
        {
            var list = Enumerable.Range(0, 24).Select(h => new Bucket(h.ToString(culture), F("{0:00}:00–{1:00}:00", h, (h + 1) % 24), 0, 0)).ToList();
            foreach (var e in events) Tally(list, e.Time.Hour, e);
            return list;
        }
        if (mode == DayOfWeek)
        {
            int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
            var list = Enumerable.Range(0, 7).Select(i => (System.DayOfWeek)((first + i) % 7))
                .Select(d => new Bucket(culture.DateTimeFormat.GetAbbreviatedDayName(d), culture.DateTimeFormat.GetDayName(d), 0, 0)).ToList();
            foreach (var e in events) Tally(list, ((int)e.Time.DayOfWeek - first + 7) % 7, e);
            return list;
        }
        var (size, count) = mode switch
        {
            Minute => (TimeSpan.FromMinutes(1), 60),
            TenMinutes => (TimeSpan.FromMinutes(10), 144),
            Day => (TimeSpan.FromDays(1), 30),
            _ => (TimeSpan.FromHours(1), 168),
        };
        var last = mode == Day ? now.Date : new DateTime(now.Ticks - now.Ticks % size.Ticks, now.Kind);
        var start = last - size * (count - 1);
        var buckets = Enumerable.Range(0, count).Select(i =>
        {
            var t = start + size * i;
            return mode switch
            {
                Day => new Bucket(t.ToString("d MMM", culture), t.ToString("D", culture), 0, 0),
                Hour => new Bucket(t.Hour == 0 ? t.ToString("ddd", culture) : "", t.ToString("ddd d MMM, HH:00", culture) + "–" + t.AddHours(1).ToString("HH:00", culture), 0, 0),
                // (labels on round times: every 3 hours, or every 10 minutes)
                TenMinutes => new Bucket(t.Minute == 0 && t.Hour % 3 == 0 ? t.ToString("HH:mm", culture) : "", t.ToString("ddd HH:mm", culture) + "–" + (t + size).ToString("HH:mm", culture), 0, 0),
                _ => new Bucket(t.Minute % 10 == 0 ? t.ToString("HH:mm", culture) : "", t.ToString("ddd HH:mm", culture) + "–" + (t + size).ToString("HH:mm", culture), 0, 0),
            };
        }).ToList();
        foreach (var e in events)
        {
            if (e.Time < start) continue;
            int i = (int)((e.Time - start).Ticks / size.Ticks);
            if (i < count) Tally(buckets, i, e);
        }
        return buckets;
    }

    private static void Tally(List<Bucket> list, int i, NetEvent e) =>
        list[i] = e.Bad ? list[i] with { Bad = list[i].Bad + 1 } : list[i] with { Other = list[i].Other + 1 };

    // ---------------------------------------------------------------- drawing

    private const double Left = 34, Bottom = 22, Top = 26, Right = 6;

    private Rect Plot => new(Left, Top, Math.Max(ActualWidth - Left - Right, 1), Math.Max(ActualHeight - Top - Bottom, 1));

    private int? BucketAt(Point p)
    {
        var plot = Plot;
        if (buckets.Count == 0 || p.X < plot.Left || p.X >= plot.Right || p.Y < 0 || p.Y > plot.Bottom) return null;
        return Math.Min((int)((p.X - plot.Left) / (plot.Width / buckets.Count)), buckets.Count - 1);
    }

    private void SetHover(int? i)
    {
        if (hover == i) return;
        hover = i;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // (so the pointer is seen everywhere)
        if (ActualWidth < 60 || ActualHeight < 60) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = Find("TextBrush");
        var secondary = Find("TextSecondaryBrush");
        var bad = Find("ErrorTextBrush");
        var other = secondary.Clone();
        other.Opacity = 0.55;
        var grid = new Pen(Find("CardBorderBrush"), 1);
        var plot = Plot;

        int max = buckets.Count == 0 ? 0 : buckets.Max(b => b.Bad + b.Other);
        if (max == 0)
        {
            var none = Text(T("No events in this time"), secondary, dpi);
            dc.DrawText(none, new Point(plot.Left + (plot.Width - none.Width) / 2, plot.Top + (plot.Height - none.Height) / 2));
        }
        int top = Math.Max(max, 1), step = 1;
        // (a round step: 1, 2, 5, 10, 20… for about four lines)
        for (int m = 1; step * 4 < top; m *= 10) step = new[] { m, 2 * m, 5 * m }.FirstOrDefault(c => c * 4 >= top, 10 * m);
        top = (int)Math.Ceiling((double)top / step) * step;
        for (int v = 0; v <= top; v += step)
        {
            double y = Math.Round(plot.Bottom - plot.Height * v / top) + 0.5;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(v.ToString(CultureInfo.CurrentCulture), secondary, dpi);
            dc.DrawText(label, new Point(plot.Left - 6 - label.Width, y - label.Height / 2));
        }

        double w = plot.Width / Math.Max(buckets.Count, 1), gap = w >= 6 ? 2 : w >= 3 ? 1 : 0;
        double lastLabel = double.NegativeInfinity;
        int every = by is Hour or TenMinutes or Minute ? 1 : Math.Max(1, (int)Math.Ceiling(56 / w));
        for (int i = 0; i < buckets.Count; i++)
        {
            var b = buckets[i];
            double x = plot.Left + i * w;
            if (hover == i) dc.DrawRectangle(Find("RowHoverBrush"), null, new Rect(x, plot.Top, w, plot.Height));
            double hBad = plot.Height * b.Bad / top, hOther = plot.Height * b.Other / top;
            if (b.Bad > 0) dc.DrawRectangle(bad, null, new Rect(x + gap / 2, plot.Bottom - hBad, Math.Max(w - gap, 1), hBad));
            if (b.Other > 0)
            {
                double split = b.Bad > 0 && hOther > 3 ? Math.Min(gap, 2) : 0; // (a gap between the two, where there's room)
                dc.DrawRectangle(other, null, new Rect(x + gap / 2, plot.Bottom - hBad - hOther, Math.Max(w - gap, 1), Math.Max(hOther - split, 1)));
            }
            // labels under the bars, as many as fit (By hour: each day's start)
            if (b.Label.Length > 0 && i % every == 0)
            {
                var label = Text(b.Label, secondary, dpi);
                double lx = Math.Clamp(x + w / 2 - label.Width / 2, plot.Left - 4, ActualWidth - label.Width);
                if (lx > lastLabel + 6) { dc.DrawText(label, new Point(lx, plot.Bottom + 4)); lastLabel = lx + label.Width; }
            }
        }
        dc.DrawLine(new Pen(secondary, 1), new Point(plot.Left, Math.Round(plot.Bottom) + 0.5), new Point(plot.Right, Math.Round(plot.Bottom) + 0.5));

        // the bar under the pointer, in words, above the plot
        if (hover is int h && h < buckets.Count)
        {
            var b = buckets[h];
            string what = b.Bad + b.Other == 0 ? T("no events")
                : string.Join(", ", new[] { b.Bad > 0 ? P(b.Bad, "{0} problem", "{0} problems") : "", b.Other > 0 ? P(b.Other, "{0} other", "{0} others") : "" }.Where(s => s.Length > 0));
            var readout = Text($"{b.Title}: {what}", text, dpi);
            dc.DrawText(readout, new Point(plot.Left, 2));
        }
    }

    private Brush Find(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private FormattedText Text(string s, Brush brush, double dpi) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, dpi);
}
