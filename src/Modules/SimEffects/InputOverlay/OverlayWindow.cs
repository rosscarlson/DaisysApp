using System.Windows;
using System.Windows.Media;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// The input overlay: a racing-telemetry trace of the pedals and wheel over the last few seconds (throttle green, brake
/// red, clutch blue, handbrake orange, steering white about the centre), with live bars, the speeds (now, between the
/// highest and lowest of the last few seconds) and a turning wheel at the right, on a see-through background. Core's
/// widget, as every other: right-click (or Move / resize on the tab) shows its edit strip (opacity, on top, Done, and
/// Delete, which switches it off) and makes it draggable and stretchable until Done. Sampled every frame from the
/// <see cref="OverlayReader"/> (moved here from ForzaWheel in 3.17.0).
/// </summary>
internal sealed class OverlayWindow : Widget
{
    private readonly OverlayReader reader;
    private readonly OverlaySettings settings;
    private readonly Trace trace;
    private readonly Queue<(DateTime At, double Speed)> speeds = new();

    // (Core's widget: drawn by the trace itself, its edit strip on right-click)
    public OverlayWindow(OverlaySettings settings, OverlayReader reader, Action save, Action delete)
        : base(settings, save, delete, card: false, firstSize: new Size(480, 140))
    {
        this.reader = reader;
        this.settings = settings;
        Title = T("Input overlay");
        MinWidth = 180;
        MinHeight = 60;
        trace = new Trace(settings);
        Body = trace;
        EditingChanged += () => { trace.Editing = Editing; trace.InvalidateVisual(); };
        // sampled each frame it's shown
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) CompositionTarget.Rendering += OnFrame;
            else CompositionTarget.Rendering -= OnFrame;
        };
    }

    // ---- the time scale: the left fifth runs twice as fast as the rest, easing from one to the other ----

    private const double SlowFrom = 0.75, FastFrom = 0.85;

    /// <summary>Seconds a slice of the width at <paramref name="u"/> holds (0 = now at the right, 1 = the left edge), relative to the rest.</summary>
    private static double Rate(double u)
    {
        double t = Math.Clamp((u - SlowFrom) / (FastFrom - SlowFrom), 0, 1);
        return 1 + t * t * (3 - 2 * t);
    }

    /// <summary>How far back (in widths at the normal speed) each of <see cref="Steps"/> + 1 points across holds.</summary>
    private static readonly double[] AgeAt = Ages();
    private const int Steps = 400;

    private static double[] Ages()
    {
        var a = new double[Steps + 1];
        for (int i = 1; i <= Steps; i++) a[i] = a[i - 1] + Rate((i - 0.5) / Steps) / Steps;
        return a;
    }

    /// <summary>The seconds the whole width shows, for a History of <paramref name="seconds"/>.</summary>
    internal static double Shown(int seconds) => Math.Clamp(seconds, 1, 60) * AgeAt[Steps];

    /// <summary>Where (0 = now, 1 = the left edge) something <paramref name="widths"/> old is (in widths at the normal speed), or more than 1 if it's off.</summary>
    private static double Across(double widths)
    {
        if (widths <= 0) return 0;
        if (widths > AgeAt[Steps]) return 2;
        int lo = 0, hi = Steps;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (AgeAt[mid] <= widths) lo = mid; else hi = mid; }
        return (lo + (widths - AgeAt[lo]) / (AgeAt[hi] - AgeAt[lo])) / Steps;
    }

    /// <summary>The history or what's drawn changed.</summary>
    public void Changed() => trace.InvalidateVisual();

    private void OnFrame(object? sender, EventArgs e)
    {
        var v = reader.Now;
        var at = DateTime.UtcNow;
        trace.Add(at, v.Steer, v.Throttle, v.Brake, v.Clutch, v.Handbrake);
        // the speed now, and the highest and lowest of the window: each holds until it's older than the window, then
        // catches up with what came after
        if (double.IsNaN(v.SpeedMs)) speeds.Clear();
        else
        {
            double shown = v.SpeedMs * (settings.Unit == SpeedUnit.Kmh ? 3.6 : 2.23694);
            speeds.Enqueue((at, shown));
            var cut = at - TimeSpan.FromSeconds(Math.Clamp(settings.WindowSeconds, 1, 60));
            while (speeds.Count > 1 && speeds.Peek().At < cut) speeds.Dequeue();
        }
        trace.Speeds = speeds.Count == 0 ? null : (speeds.Last().Speed, speeds.Max(x => x.Speed), speeds.Min(x => x.Speed));
        trace.InvalidateVisual();
    }

    // ---------------------------------------------------------------- the drawing

    /// <summary>The traces, the bars and the wheel, drawn straight (every frame).</summary>
    private sealed class Trace : FrameworkElement
    {
        private readonly OverlaySettings s;
        private readonly record struct Sample(DateTime At, double Steer, double Throttle, double Brake, double Clutch, double Handbrake);
        private readonly List<Sample> history = new();
        public bool Editing;
        /// <summary>The speed now, and the highest and lowest of the window (null: no telemetry).</summary>
        public (double Now, double Top, double Bottom)? Speeds;

        private static readonly Brush ThrottleBrush = Frozen(Color.FromRgb(0x3C, 0xD0, 0x70));
        private static readonly Brush BrakeBrush = Frozen(Color.FromRgb(0xFF, 0x4D, 0x4D));
        private static readonly Brush ClutchBrush = Frozen(Color.FromRgb(0x4D, 0xA3, 0xFF));
        private static readonly Brush HandbrakeBrush = Frozen(Color.FromRgb(0xFF, 0xB1, 0x3B));
        private static readonly Brush SteerBrush = Frozen(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        private static readonly Brush WheelBrush = Frozen(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly Brush SpeedBrush = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush SpeedTopBrush = Frozen(Color.FromRgb(0x3C, 0xD0, 0x70));
        private static readonly Brush SpeedBottomBrush = Frozen(Color.FromRgb(0xFF, 0x8C, 0x1A));
        private static readonly Typeface SpeedFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Pen GridPen = FrozenPen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Pen CentrePen = FrozenPen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Typeface NumberFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Pen EditPen = FrozenPen(Color.FromRgb(0x4D, 0xA3, 0xFF), 2, dashed: true);

        public Trace(OverlaySettings settings) => s = settings;

        public void Add(DateTime at, double steer, double throttle, double brake, double clutch, double handbrake)
        {
            history.Add(new Sample(at, steer, throttle, brake, clutch, handbrake));
            var cut = at - TimeSpan.FromSeconds(Shown(s.Seconds)) - TimeSpan.FromSeconds(1);
            int old = history.FindIndex(h => h.At >= cut);
            if (old > 0) history.RemoveRange(0, old);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            var back = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(s.Background, 0, 1) * 255), 0, 0, 0));
            dc.DrawRoundedRectangle(back, Editing ? EditPen : null, new Rect(0, 0, w, h), 8, 8);

            // right: a bar per pedal shown, and the wheel
            const double pad = 8;
            var pedals = new List<(Brush Brush, Func<Sample, double> Of)>();
            if (s.Clutch) pedals.Add((ClutchBrush, x => x.Clutch));
            if (s.Brake) pedals.Add((BrakeBrush, x => x.Brake));
            if (s.Throttle) pedals.Add((ThrottleBrush, x => x.Throttle));
            if (s.Handbrake) pedals.Add((HandbrakeBrush, x => x.Handbrake));
            double barW = Math.Max(6, Math.Min(16, h / 9)), wheelD = s.Steering ? Math.Min(h - 2 * pad, 64) : 0;
            // each bar's number above it, in its colour: the slot is as wide as "100"
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip, numSize = Math.Max(9, Math.Min(14, h / 9));
            FormattedText Number(string text, Brush brush) => new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                NumberFace, numSize, brush, dip);
            var widest = Number("100", Brushes.White);
            double slot = Math.Max(barW, widest.Width), numH = pedals.Count > 0 ? widest.Height : 0;
            // between the bars and the wheel: the speeds, the one now in the middle, the window's highest above and lowest below
            double big = Math.Max(9, Math.Min(30, (h - 2 * pad) / 4));
            FormattedText Speed(double? v, Brush brush) => new(v is { } x ? Math.Round(x).ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "–",
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SpeedFace, big, brush, dip);
            double speedW = s.Speed ? Speed(888, SpeedBrush).Width + 8 : 0;
            double side = pedals.Count * (slot + 4) + speedW + (wheelD > 0 ? wheelD + 8 : 0);
            var plot = new Rect(pad, pad + numH, Math.Max(20, w - 2 * pad - side - 8), Math.Max(4, h - 2 * pad - numH));

            // the grid: quarters, and the centre (steering straight ahead)
            for (int q = 1; q < 4; q++)
            {
                double y = plot.Top + plot.Height * q / 4;
                dc.DrawLine(q == 2 && s.Steering ? CentrePen : GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }

            if (history.Count > 1)
            {
                var now = history[^1].At;
                double span = Math.Clamp(s.Seconds, 1, 60);
                double X(DateTime t) => plot.Right - Across((now - t).TotalSeconds / span) * plot.Width;
                void Line(Brush brush, double thick, Func<Sample, double> of)
                {
                    var g = new StreamGeometry();
                    bool any = false;
                    using (var ctx = g.Open())
                    {
                        bool open = false;
                        foreach (var p in history)
                        {
                            double v = of(p);
                            double x = X(p.At);
                            if (double.IsNaN(v) || x < plot.Left) { open = false; continue; }
                            double y = plot.Bottom - Math.Clamp(v, 0, 1) * plot.Height;
                            if (!open) { ctx.BeginFigure(new Point(x, y), false, false); open = true; }
                            else ctx.LineTo(new Point(x, y), true, true);
                            any = true;
                        }
                    }
                    g.Freeze();
                    if (any) dc.DrawGeometry(null, new Pen(brush, thick) { LineJoin = PenLineJoin.Round }, g);
                }
                double thick = Math.Max(1.5, Math.Min(3, h / 60));
                if (s.Handbrake) Line(HandbrakeBrush, thick, p => p.Handbrake);
                if (s.Clutch) Line(ClutchBrush, thick, p => p.Clutch);
                if (s.Brake) Line(BrakeBrush, thick, p => p.Brake);
                if (s.Throttle) Line(ThrottleBrush, thick, p => p.Throttle);
                if (s.Steering) Line(SteerBrush, thick, p => p.Steer);
            }

            Sample? last = history.Count > 0 ? history[^1] : null;
            double bx = plot.Right + 8, barTop = plot.Top, barH = plot.Height;
            foreach (var (brush, of) in pedals)
            {
                double x = bx + (slot - barW) / 2;
                dc.DrawRectangle(null, GridPen, new Rect(x, barTop, barW, barH));
                double v = last is { } l ? of(l) : double.NaN;
                if (!double.IsNaN(v))
                {
                    double fill = Math.Clamp(v, 0, 1) * barH;
                    dc.DrawRectangle(brush, null, new Rect(x, barTop + barH - fill, barW, fill));
                    var number = Number(Math.Round(Math.Clamp(v, 0, 1) * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture), brush);
                    dc.DrawText(number, new Point(bx + (slot - number.Width) / 2, plot.Top - numH));
                }
                bx += slot + 4;
            }
            if (s.Speed)
            {
                var sp = Speeds;
                var top = Speed(sp?.Top, SpeedTopBrush);
                var mid = Speed(sp?.Now, SpeedBrush);
                var bottom = Speed(sp?.Bottom, SpeedBottomBrush);
                double cx = bx + 4 + (speedW - 8) / 2;
                dc.DrawText(top, new Point(cx - top.Width / 2, pad));
                dc.DrawText(mid, new Point(cx - mid.Width / 2, h / 2 - mid.Height / 2));
                dc.DrawText(bottom, new Point(cx - bottom.Width / 2, h - pad - bottom.Height));
            }
            if (wheelD > 0)
            {
                // the wheel: a ring with a top mark, turned over 450° (Forza's wheel)
                var c = new Point(w - pad - wheelD / 2, h / 2);
                double r = wheelD / 2 - 2;
                double steer = last is { } l && !double.IsNaN(l.Steer) ? l.Steer : 0.5;
                dc.PushTransform(new RotateTransform((steer - 0.5) * 450, c.X, c.Y));
                dc.DrawEllipse(null, new Pen(WheelBrush, Math.Max(3, r / 6)), c, r, r);
                dc.DrawLine(new Pen(WheelBrush, Math.Max(2, r / 10)), new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
                dc.DrawLine(new Pen(WheelBrush, Math.Max(2, r / 10)), c, new Point(c.X, c.Y + r));
                dc.DrawLine(new Pen(BrakeBrush, Math.Max(3, r / 6)), new Point(c.X, c.Y - r - 3), new Point(c.X, c.Y - r + 5));
                dc.Pop();
            }

            if (Editing)
            {
                var text = new FormattedText(T("Drag to move, drag an edge to resize."), System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(10, Math.Min(14, h / 10)), Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(pad + 4, pad + 2));
            }
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double thick, bool dashed = false)
        {
            var p = new Pen(Frozen(c), thick);
            if (dashed) p.DashStyle = DashStyles.Dash;
            p.Freeze();
            return p;
        }
    }
}
