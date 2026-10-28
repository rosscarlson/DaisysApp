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

    /// <summary>The channels drawn, and the ones labelled.</summary>
    private (double From, double To, int[] Labels) Range() => band switch
    {
        "5" => (30, 179, [36, 44, 52, 60, 100, 108, 116, 124, 132, 140, 149, 157, 165, 173]),
        "6" => (-3, 237, [1, 33, 65, 97, 129, 161, 193, 225]),
        _ => (-1, 16, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14]),
    };

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
        var (from, to, labels) = Range();
        double plotW = w - AxisWidth - 6, plotH = h - AxisHeight - 4;
        double X(double ch) => AxisWidth + (ch - from) / (to - from) * plotW;
        double Y(double dbm) => 4 + (Top - Math.Clamp(dbm, Bottom, Top)) / (Top - Bottom) * plotH;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        for (int dbm = -90; dbm <= -30; dbm += 10)
        {
            dc.DrawLine(grid, new Point(AxisWidth, Y(dbm)), new Point(w - 6, Y(dbm)));
            var t = new FormattedText(dbm.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10.5, text, dpi);
            dc.DrawText(t, new Point(AxisWidth - 6 - t.Width, Y(dbm) - t.Height / 2));
        }
        foreach (int ch in labels)
        {
            var t = new FormattedText(ch.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10.5, text, dpi);
            dc.DrawText(t, new Point(X(ch) - t.Width / 2, h - AxisHeight + 4));
            dc.DrawLine(grid, new Point(X(ch), h - AxisHeight), new Point(X(ch), h - AxisHeight + 3));
        }
        dc.DrawLine(new Pen(Brush("TextSecondaryBrush", 0.5), 1), new Point(AxisWidth, h - AxisHeight), new Point(w - 6, h - AxisHeight));

        // weakest first, so the strong ones are drawn on top; the highlighted one last of all
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
            dc.DrawText(label, new Point((x0 + x1) / 2 - Math.Min(label.Width, label.MaxTextWidth) / 2, Math.Max(peak - label.Height - 1, 0)));
        }
        dc.Pop();
    }
}
