using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// One brake zone or throttle move, opened from Brake Analysis or Throttle Analysis: its moment of the trace (a second
/// either side; the zone's application, hold and release shaded), its numbers and its notes. One window, showing
/// whichever was clicked last.
/// </summary>
internal static class MomentWindow
{
    private static Window? open;

    public static void Show(FrameworkElement owner, PedalAnalysis a, object item)
    {
        string title;
        var numbers = new List<(string Name, string Value)>();
        List<string> notes;
        var chart = new MomentChart(a) { Height = 230 };
        if (item is BrakeZone z)
        {
            title = F("Brake zone {0}", z.Number);
            chart.Range(z.Start, z.End, z);
            numbers.Add((T("At"), z.Clock));
            numbers.Add((T("Peak"), $"{z.PeakPct:0}%"));
            numbers.Add((T("Application"), $"{z.ApplyScore:0}"));
            numbers.Add((T("Release"), $"{z.ReleaseScore:0}"));
            numbers.Add((T("Apply"), $"{z.ApplyMs:0} ms"));
            numbers.Add((T("Hold"), $"{z.HoldS:0.00} s"));
            numbers.Add((T("Release time"), $"{z.ReleaseS:0.00} s"));
            numbers.Add((T("Smooth"), $"{z.ReleaseSmoothPct:0}%"));
            if (z.Trail is { } trail) numbers.Add((T("Trail"), $"{trail:0}%"));
            if (z.Coast is { } coast) numbers.Add((T("Coast"), $"{coast:0.00} s"));
            if (!double.IsNaN(z.EntrySpeed)) numbers.Add((T("Entry"), Speeds.Text(z.EntrySpeed)));
            if (!double.IsNaN(z.MinSpeed)) numbers.Add((T("Slowest"), Speeds.Text(z.MinSpeed)));
            notes = z.Notes;
        }
        else if (item is ThrottleMove m)
        {
            title = m.Up ? F("Throttle on {0}", m.Number) : F("Throttle off {0}", m.Number);
            chart.Range(m.Start, m.End, null);
            numbers.Add((T("At"), m.Clock));
            numbers.Add((T("From"), $"{m.FromPct:0}%"));
            numbers.Add((T("To"), $"{m.ToPct:0}%"));
            numbers.Add((T("Time"), $"{m.Ms:0} ms"));
            numbers.Add((T("Smooth"), $"{m.SmoothPct:0}%"));
            numbers.Add((m.Up ? T("Application") : T("Release"), $"{m.Score:0}"));
            notes = m.Notes;
        }
        else return;

        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        body.Children.Add(Ui.Header(title));
        chart.Margin = new Thickness(0, 6, 0, 10);
        body.Children.Add(chart);
        var row = new WrapPanel();
        foreach (var (name, value) in numbers)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 22, 8) };
            var label = new TextBlock { Text = name, FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            s.Children.Add(label);
            s.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold });
            row.Children.Add(s);
        }
        body.Children.Add(row);
        if (notes.Count > 0)
        {
            body.Children.Add(Ui.Header(T("Notes"), new Thickness(0, 6, 0, 4)));
            foreach (var note in notes)
            {
                var line = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 8, 0), Foreground = PedalColours.Frozen(PedalColours.Warning) };
                DockPanel.SetDock(bullet, Dock.Left);
                line.Children.Add(bullet);
                line.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(line);
            }
        }

        // (since 4.6.2) Close, bottom right
        var close = new Button { Content = T("Close"), Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Window.GetWindow(close)?.Close();
        body.Children.Add(close);

        if (open is { IsLoaded: true })
        {
            open.Title = title;
            open.Content = body;
            open.Activate();
            return;
        }
        var w = Ui.Dialog(title, owner, 640);
        w.Content = body;
        w.Closed += (_, _) => open = null;
        open = w;
        w.Show();
    }

    /// <summary>A moment of the session: brake, throttle, steering (and speed), the zone's parts shaded.</summary>
    private sealed class MomentChart : FrameworkElement
    {
        private readonly PedalAnalysis a;
        private int from, to;
        private BrakeZone? zone;

        public MomentChart(PedalAnalysis a) => this.a = a;

        public void Range(int start, int end, BrakeZone? z)
        {
            from = Math.Max(0, start - SessionData.Hz);
            to = Math.Min(a.Data.Count - 1, end + SessionData.Hz);
            zone = z;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var p = new Rect(36, 4, Math.Max(10, ActualWidth - 40), Math.Max(10, ActualHeight - 22));
            var dim = PedalColours.Theme(this, "TextSecondaryBrush");
            dc.DrawRectangle(PedalColours.Theme(this, "RoomBrush"), null, p);
            if (to <= from) return;
            double X(double i) => p.Left + (i - from) / (double)(to - from) * p.Width;
            double Y(double v) => p.Bottom - Math.Clamp(v, 0, 1) * p.Height;
            for (int q = 0; q <= 4; q++)
            {
                double y = p.Top + p.Height * q / 4;
                dc.DrawLine(PedalColours.Pen(dim, 1, 0.15), new Point(p.Left, y), new Point(p.Right, y));
                dc.DrawText(PedalColours.Text(this, (100 - q * 25) + "%", 9, dim), new Point(2, y - 6));
            }
            for (double t = 0; t <= (to - from) * SessionData.Dt + 0.001; t += 0.5)
            {
                double x = X(from + t * SessionData.Hz);
                dc.DrawText(PedalColours.Text(this, $"{t:0.0} s", 9, dim), new Point(x - 10, p.Bottom + 3));
            }
            if (zone is { } z)
            {
                void Shade(int a0, int a1, byte alpha, string name)
                {
                    if (a1 <= a0) return;
                    var r = new Rect(new Point(X(a0), p.Top), new Point(X(a1), p.Bottom));
                    dc.DrawRectangle(PedalColours.Frozen(PedalColours.Brake, alpha), null, r);
                    if (r.Width > 40) dc.DrawText(PedalColours.Text(this, name, 9.5, dim), new Point(r.Left + 3, p.Top + 2));
                }
                int peakish = Math.Clamp(z.PeakAt, z.Start, z.ReleaseFrom);
                Shade(z.Start, peakish, 0x22, T("Application"));
                Shade(peakish, z.ReleaseFrom, 0x10, T("Hold"));
                Shade(z.ReleaseFrom, z.End, 0x22, T("Release"));
            }
            var d = a.Data;
            dc.PushClip(new RectangleGeometry(p));
            if (d.HasSpeed)
            {
                double top = 1;
                for (int i = from; i <= to; i++) if (d.Speed[i] > top) top = d.Speed[i];
                SessionChart.Envelope(dc, d.Speed, from, to, PedalColours.Pen(PedalColours.Frozen(PedalColours.Speed), 1.3, 0.8), X, v => Y(v / (top * 1.05)), p);
            }
            if (a.HasSteer) SessionChart.Envelope(dc, d.Steer, from, to, PedalColours.Pen(PedalColours.Theme(this, "TextBrush"), 1.2, 0.5), X, Y, p);
            SessionChart.Envelope(dc, d.Throttle, from, to, PedalColours.Pen(PedalColours.Frozen(PedalColours.Throttle), 2), X, Y, p);
            SessionChart.Envelope(dc, d.Brake, from, to, PedalColours.Pen(PedalColours.Frozen(PedalColours.Brake), 2), X, Y, p);
            dc.Pop();
        }
    }
}
