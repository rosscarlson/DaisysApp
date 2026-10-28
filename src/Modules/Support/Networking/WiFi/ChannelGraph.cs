using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// The Wi-Fi tool's graph, as Android's Wi-Fi analysers draw it: one band's channels across, signal (dBm) up, each
/// access point a hump as wide as its channel (20 to 160 MHz) and as tall as its signal, named at its top. The network
/// this PC is on is drawn thicker; one picked in the table is highlighted.
/// </summary>
internal sealed class ChannelGraph : FrameworkElement
{
    private const double Bottom = -100, Top = -20, AxisWidth = 40, AxisHeight = 22;
    private IReadOnlyList<(WlanBss Bss, string Color)> networks = Array.Empty<(WlanBss, string)>();
    private string band = "2.4";
    private string? connected, highlight;

    public void Show(IReadOnlyList<(WlanBss Bss, string Color)> list, string band, string? connectedBssid, string? highlightBssid)
    {
        networks = list;
        this.band = band;
        connected = connectedBssid;
        highlight = highlightBssid;
        InvalidateVisual();
    }

    /// <summary>
    /// The stretches of channels drawn. 2.4 GHz is drawn whole; 5 and 6 GHz span 150 and 240 channels, so only the
    /// stretches access points use are drawn (side by side, with a break between), each wide enough to tell them apart.
    /// </summary>
    private List<(double From, double To)> Segments()
    {
        if (band is not ("5" or "6")) return [(-1, 16)];
        if (networks.Count == 0) return band == "5" ? [(30, 179)] : [(-3, 237)];
        var spans = networks.Select(n =>
        {
            double half = n.Bss.WidthMHz / 10.0, from = n.Bss.CenterChannel - half - 2, to = n.Bss.CenterChannel + half + 2;
            if (to - from < 14) { double mid = (from + to) / 2; from = mid - 7; to = mid + 7; }
            return (From: from, To: to);
        }).OrderBy(s => s.From).ToList();
        var merged = new List<(double From, double To)>();
        foreach (var s in spans)
            if (merged.Count > 0 && s.From <= merged[^1].To + 6) merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, s.To));
            else merged.Add(s);
        return merged;
    }

    /// <summary>The first 20 MHz channel of the run a channel is in: channel numbers are labelled from it.</summary>
    private int LabelBase(int ch) => band == "5" && ch > 144 ? 149 : band == "5" ? 36 : 1;

    private Brush Brush(string key, double opacity = 1)
    {
        Brush b = key.StartsWith('#') ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(key)) : TryFindResource(key) as Brush ?? Brushes.DodgerBlue;
        if (opacity < 1) { b = b.CloneCurrentValue(); b.Opacity = opacity; }
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < AxisWidth + 20 || h < AxisHeight + 20) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = Brush("TextSecondaryBrush");
        var grid = new Pen(Brush("TextSecondaryBrush", 0.18), 1);
        var face = new Typeface("Segoe UI");
        var segments = Segments();
        const double Gap = 14;
        double plotW = w - AxisWidth - 6, plotH = h - AxisHeight - 4;
        double scale = (plotW - Gap * (segments.Count - 1)) / segments.Sum(s => s.To - s.From);
        var starts = new double[segments.Count];
        starts[0] = AxisWidth;
        for (int i = 1; i < segments.Count; i++) starts[i] = starts[i - 1] + (segments[i - 1].To - segments[i - 1].From) * scale + Gap;
        double X(double ch)
        {
            int i = segments.FindIndex(s => ch <= s.To);
            if (i < 0) i = segments.Count - 1;
            return starts[i] + (Math.Clamp(ch, segments[i].From, segments[i].To) - segments[i].From) * scale;
        }
        double Y(double dbm) => 4 + (Top - Math.Clamp(dbm, Bottom, Top)) / (Top - Bottom) * plotH;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        for (int dbm = -90; dbm <= -30; dbm += 10)
        {
            dc.DrawLine(grid, new Point(AxisWidth, Y(dbm)), new Point(w - 6, Y(dbm)));
            var t = new FormattedText(dbm.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10.5, text, dpi);
            dc.DrawText(t, new Point(AxisWidth - 6 - t.Width, Y(dbm) - t.Height / 2));
        }
        // channel numbers: every 20 MHz channel on 5 and 6 GHz (every channel on 2.4), fewer where they'd crowd
        int unit = band is "5" or "6" ? 4 : 1, every = 1;
        while (unit * every * scale < 24) every++;
        var axis = new Pen(Brush("TextSecondaryBrush", 0.5), 1);
        for (int i = 0; i < segments.Count; i++)
        {
            var (from, to) = segments[i];
            for (int ch = (int)Math.Ceiling(from); ch <= to; ch++)
            {
                int b = LabelBase(ch);
                if (ch < 1 || (band == "2.4" && ch > 14) || (ch - b) % (unit * every) != 0 || (band == "5" && ch > 144 && ch < 149)) continue;
                var t = new FormattedText(ch.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10.5, text, dpi);
                dc.DrawText(t, new Point(Math.Min(X(ch) - t.Width / 2, w - t.Width), h - AxisHeight + 4));
                dc.DrawLine(grid, new Point(X(ch), h - AxisHeight), new Point(X(ch), h - AxisHeight + 3));
            }
            double right = starts[i] + (to - from) * scale;
            dc.DrawLine(axis, new Point(starts[i], h - AxisHeight), new Point(right, h - AxisHeight));
            if (i > 0)
            {
                // a break in the axis: the channels between aren't drawn
                double gx = starts[i] - Gap / 2, gy = h - AxisHeight;
                dc.DrawLine(axis, new Point(gx - 4, gy + 4), new Point(gx - 1, gy - 4));
                dc.DrawLine(axis, new Point(gx + 1, gy + 4), new Point(gx + 4, gy - 4));
            }
        }

        // weakest first, so the strong ones are drawn on top; the highlighted one last of all
        var labels = new List<(FormattedText Text, Rect At, int Rank, int Rssi)>();
        foreach (var (bss, color) in networks.OrderBy(n => n.Bss.Bssid == highlight ? 1 : 0).ThenBy(n => n.Bss.Rssi))
        {
            double half = bss.WidthMHz / 10.0; // in channels (5 MHz each)
            double x0 = X(bss.CenterChannel - half), x1 = X(bss.CenterChannel + half), peak = Y(bss.Rssi), floor = Y(Bottom);
            bool mine = bss.Bssid == connected, picked = bss.Bssid == highlight;
            double dim = highlight != null && !picked ? 0.35 : 1;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                // a hump: steep sides, flat-ish top, as a channel's spectrum mask looks
                double rise = Math.Min((x1 - x0) * 0.18, 14);
                g.BeginFigure(new Point(x0, floor), true, false);
                g.BezierTo(new Point(x0 + rise * 0.4, floor), new Point(x0 + rise * 0.2, peak), new Point(x0 + rise, peak), true, true);
                g.LineTo(new Point(x1 - rise, peak), true, true);
                g.BezierTo(new Point(x1 - rise * 0.2, peak), new Point(x1 - rise * 0.4, floor), new Point(x1, floor), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(Brush(color, 0.12 * dim), new Pen(Brush(color, dim), mine || picked ? 2.6 : 1.3), geo);
            var label = new FormattedText(bss.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(face.FontFamily, FontStyles.Normal, mine ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), 11, Brush(color, dim), dpi);
            label.MaxTextWidth = Math.Max(x1 - x0 + 40, 60);
            label.MaxLineCount = 1;
            label.Trimming = TextTrimming.CharacterEllipsis;
            double lw = Math.Min(label.Width, label.MaxTextWidth);
            labels.Add((label, new Rect(Math.Clamp((x0 + x1) / 2 - lw / 2, AxisWidth, Math.Max(w - lw, AxisWidth)), Math.Max(peak - label.Height - 1, 0), lw, label.Height), picked ? 0 : mine ? 1 : 2, bss.Rssi));
        }

        // names last, over every hump, each moved down (or up) out of the way of the ones placed before it, so access
        // points on one channel with much the same signal can still be read: the picked one first, then this PC's,
        // then the strongest
        var placed = new List<Rect>();
        var backing = Brush("CardBrush", 0.75);
        foreach (var (label, want, _, _) in labels.OrderBy(l => l.Rank).ThenByDescending(l => l.Rssi))
        {
            var at = Place(want, placed, h - AxisHeight);
            placed.Add(at);
            if (at != want) dc.DrawRectangle(backing, null, at);
            dc.DrawText(label, at.TopLeft);
        }
        dc.Pop();
    }

    /// <summary>Where a name goes: where it wants to be, else the nearest place below (or above) clear of the others.</summary>
    private static Rect Place(Rect want, List<Rect> placed, double floor)
    {
        static Rect? Hit(Rect r, List<Rect> placed) => placed.Cast<Rect?>().FirstOrDefault(p => p!.Value.IntersectsWith(r));
        var down = want;
        for (var hit = Hit(down, placed); hit != null && down.Bottom <= floor; hit = Hit(down, placed)) down.Y = hit.Value.Bottom + 1;
        if (down.Bottom <= floor) return down;
        var up = want;
        for (var hit = Hit(up, placed); hit != null && up.Top >= 0; hit = Hit(up, placed)) up.Y = hit.Value.Top - up.Height - 1;
        return up.Top >= 0 ? up : want;
    }
}
