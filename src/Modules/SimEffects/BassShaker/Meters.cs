using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>The meters' look: lit segments green, then amber, then red at the top; unlit ones faint.</summary>
internal static class MeterColours
{

    public static readonly Brush Green = Ui.Brush("#3FB950"), Amber = Ui.Brush("#D29922"), Red = Ui.Brush("#F85149");
    public static readonly Brush[] Dim = { Ui.Brush("#263FB950"), Ui.Brush("#26D29922"), Ui.Brush("#26F85149") };
    // the Level Wizard's effect: lighter colours, and unlit segments less faint
    public static readonly Brush BrightGreen = Ui.Brush("#7EE787"), BrightAmber = Ui.Brush("#F2CC60"), BrightRed = Ui.Brush("#FF7B72");
    public static readonly Brush[] BrightDim = { Ui.Brush("#663FB950"), Ui.Brush("#66D29922"), Ui.Brush("#66F85149") };
    /// <summary>A muted column's line, top to bottom.</summary>
    public static readonly Brush MutedLine = Ui.Brush("#8B949E");

    public static Brush Lit(int band, bool bright) => bright ? band switch { 2 => BrightRed, 1 => BrightAmber, _ => BrightGreen } : Lit(band);
    public static Brush Unlit(int band, bool bright) => bright ? BrightDim[band] : Dim[band];

    /// <summary>Segment <paramref name="i"/> of <paramref name="n"/>: 0 green, 1 amber, 2 red.</summary>
    public static int Band(int i, int n) => i >= n - 2 ? 2 : i >= n * 0.65 ? 1 : 0;

    public static Brush Lit(int band) => band switch { 2 => Red, 1 => Amber, _ => Green };
}

/// <summary>
/// The spectrum-analyser-style meter above the effects: a column of segments per effect, in the effects' order, lit
/// from the bottom as it shakes, with the highest point held at the top for a moment and falling back. On its left,
/// apart, a column per device: what it's playing, its volume as a small marker beside it (red while it's muted).
/// A muted column has a thin grey line top to bottom; a highlighted effect's (EffectPicker) is outlined.
/// </summary>
internal sealed class SpectrumMeter : FrameworkElement
{
    private const int Segments = 16;
    private const double LabelHeight = 18;
    /// <summary>The gap between the devices and the effects, with a faint line in it.</summary>
    private const double Spacer = 14;
    private (string Label, float Level, double Volume, bool Muted)[] devices = Array.Empty<(string, float, double, bool)>();
    private (string Label, float Level, bool Muted, bool Bright, bool Highlighted)[] items = Array.Empty<(string, float, bool, bool, bool)>();
    private float[] peaks = Array.Empty<float>();
    private double[] peakHeld = Array.Empty<double>();

    public SpectrumMeter() => Height = 150;

    /// <summary>The levels now (0..1), one per device and one per effect; called by the view's timer.</summary>
    public void Update((string Label, float Level, double Volume, bool Muted)[] devicesNow, (string Label, float Level, bool Muted, bool Bright, bool Highlighted)[] now, double dt)
    {
        int n = devicesNow.Length + now.Length;
        if (peaks.Length != n) { peaks = new float[n]; peakHeld = new double[n]; }
        for (int i = 0; i < n; i++)
        {
            float level = i < devicesNow.Length ? devicesNow[i].Level : now[i - devicesNow.Length].Level;
            if (level >= peaks[i]) { peaks[i] = level; peakHeld[i] = 0.8; }
            else if ((peakHeld[i] -= dt) <= 0) peaks[i] = Math.Max(0, peaks[i] - (float)(dt * 0.6));
        }
        devices = devicesNow;
        items = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        int nd = devices.Length, n = nd + items.Length;
        double spacer = nd > 0 && items.Length > 0 ? Spacer : 0;
        double w = ActualWidth - spacer, h = ActualHeight - LabelHeight;
        if (n == 0 || w <= 0 || h <= 0 || peaks.Length != n) return;
        double col = w / n, bar = Math.Min(col * 0.72, 56), segH = h / Segments, gap = Math.Max(1, segH * 0.22);
        var text = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (spacer > 0)
        {
            dc.PushOpacity(0.35);
            dc.DrawRectangle(text, null, new Rect(nd * col + spacer / 2 - 0.5, 0, 1, ActualHeight));
            dc.Pop();
        }
        for (int c = 0; c < n; c++)
        {
            bool device = c < nd;
            var (label, level, muted, bright, highlighted) = device ? (devices[c].Label, devices[c].Level, devices[c].Muted, false, false) : items[c - nd];
            double left = c * col + (device ? 0 : spacer), x = left + (col - bar) / 2;
            int lit = (int)Math.Round(Math.Clamp(level, 0, 1) * Segments);
            int peak = (int)Math.Ceiling(Math.Clamp(peaks[c], 0, 1) * Segments) - 1;
            for (int s = 0; s < Segments; s++)
            {
                int band = MeterColours.Band(s, Segments);
                bool on = s < lit || (s == peak && peak > 0);
                var rect = new Rect(x, h - (s + 1) * segH + gap / 2, bar, segH - gap);
                // (muted: faint, picked or not)
                dc.DrawRectangle(on && !muted ? MeterColours.Lit(band, bright) : MeterColours.Unlit(band, bright && !muted), null, rect);
            }
            if (device) VolumeMarker(dc, x, h, devices[c].Volume, muted);
            if (muted) dc.DrawRectangle(MeterColours.MutedLine, null, new Rect(x + bar / 2 - 0.75, 0, 1.5, h));
            if (highlighted)
            {
                var accent = TryFindResource("AccentBrush") as Brush ?? MeterColours.BrightGreen;
                double pad = Math.Min(4, (col - bar) / 2 - 1);
                dc.DrawRoundedRectangle(null, new Pen(accent, 1.5), new Rect(x - pad, 0.75, bar + 2 * pad, h - 0.5), 2, 2);
            }
            var face = bright ? new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal) : new Typeface("Segoe UI");
            var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 11, bright ? TryFindResource("TextBrush") as Brush ?? text : text, dpi)
            {
                MaxTextWidth = Math.Max(1, col - 2), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
            };
            if (muted) dc.PushOpacity(0.5);
            dc.DrawText(ft, new Point(left + 1, h + 3));
            if (muted) dc.Pop();
        }
    }

    /// <summary>A device's volume: a small triangle left of its column pointing at the height it's set to.</summary>
    private void VolumeMarker(DrawingContext dc, double x, double h, double volume, bool muted)
    {
        double y = h - Math.Clamp(volume, 0, 1) * h, size = 4;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x - 1, y), true, true);
            ctx.LineTo(new Point(x - 1 - size * 1.4, y - size), false, false);
            ctx.LineTo(new Point(x - 1 - size * 1.4, y + size), false, false);
        }
        g.Freeze();
        dc.DrawGeometry(muted ? MeterColours.Red : TryFindResource("TextBrush") as Brush ?? Brushes.White, null, g);
    }
}

/// <summary>A small level meter in a line of segments (an effect's header, a device's row).</summary>
internal sealed class MiniMeter : FrameworkElement
{
    private const int Segments = 12;
    private float level;
    private bool bright;

    /// <summary>The Level Wizard's effect: lighter colours.</summary>
    public bool Bright
    {
        get => bright;
        set { bright = value; InvalidateVisual(); }
    }

    public MiniMeter()
    {
        Width = 84;
        Height = 10;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public float Level
    {
        get => level;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(value - level) < 0.01f) return;
            level = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, seg = w / Segments;
        int lit = (int)Math.Round(level * Segments);
        for (int s = 0; s < Segments; s++)
        {
            int band = MeterColours.Band(s, Segments);
            dc.DrawRectangle(s < lit ? MeterColours.Lit(band, bright) : MeterColours.Unlit(band, bright), null, new Rect(s * seg, 0, Math.Max(1, seg - 2), h));
        }
    }
}
