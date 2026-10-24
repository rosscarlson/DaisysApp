using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>The meters' look: lit segments green, then amber, then red at the top; unlit ones faint.</summary>
internal static class MeterColours
{
    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly Brush Green = Frozen("#3FB950"), Amber = Frozen("#D29922"), Red = Frozen("#F85149");
    public static readonly Brush[] Dim = { Frozen("#263FB950"), Frozen("#26D29922"), Frozen("#26F85149") };
    // the Level Wizard's effect: lighter colours, and unlit segments less faint
    public static readonly Brush BrightGreen = Frozen("#7EE787"), BrightAmber = Frozen("#F2CC60"), BrightRed = Frozen("#FF7B72");
    public static readonly Brush[] BrightDim = { Frozen("#663FB950"), Frozen("#66D29922"), Frozen("#66F85149") };

    public static Brush Lit(int band, bool bright) => bright ? band switch { 2 => BrightRed, 1 => BrightAmber, _ => BrightGreen } : Lit(band);
    public static Brush Unlit(int band, bool bright) => bright ? BrightDim[band] : Dim[band];

    /// <summary>Segment <paramref name="i"/> of <paramref name="n"/>: 0 green, 1 amber, 2 red.</summary>
    public static int Band(int i, int n) => i >= n - 2 ? 2 : i >= n * 0.65 ? 1 : 0;

    public static Brush Lit(int band) => band switch { 2 => Red, 1 => Amber, _ => Green };
}

/// <summary>
/// The spectrum-analyser-style meter above the effects: a column of segments per effect, in the effects' order, lit
/// from the bottom as it shakes, with the highest point held at the top for a moment and falling back.
/// </summary>
internal sealed class SpectrumMeter : FrameworkElement
{
    private const int Segments = 16;
    private const double LabelHeight = 18;
    private (string Label, float Level, bool Muted, bool Bright)[] items = Array.Empty<(string, float, bool, bool)>();
    private float[] peaks = Array.Empty<float>();
    private double[] peakHeld = Array.Empty<double>();

    public SpectrumMeter() => Height = 150;

    /// <summary>The levels now (0..1), one per effect; called by the view's timer.</summary>
    public void Update((string Label, float Level, bool Muted, bool Bright)[] now, double dt)
    {
        if (peaks.Length != now.Length) { peaks = new float[now.Length]; peakHeld = new double[now.Length]; }
        for (int i = 0; i < now.Length; i++)
        {
            if (now[i].Level >= peaks[i]) { peaks[i] = now[i].Level; peakHeld[i] = 0.8; }
            else if ((peakHeld[i] -= dt) <= 0) peaks[i] = Math.Max(0, peaks[i] - (float)(dt * 0.6));
        }
        items = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight - LabelHeight;
        int n = items.Length;
        if (n == 0 || w <= 0 || h <= 0) return;
        double col = w / n, bar = Math.Min(col * 0.72, 56), segH = h / Segments, gap = Math.Max(1, segH * 0.22);
        var text = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int c = 0; c < n; c++)
        {
            var (label, level, muted, bright) = items[c];
            double x = c * col + (col - bar) / 2;
            int lit = (int)Math.Round(Math.Clamp(level, 0, 1) * Segments);
            int peak = (int)Math.Ceiling(Math.Clamp(peaks[c], 0, 1) * Segments) - 1;
            for (int s = 0; s < Segments; s++)
            {
                int band = MeterColours.Band(s, Segments);
                bool on = s < lit || (s == peak && peak > 0);
                var rect = new Rect(x, h - (s + 1) * segH + gap / 2, bar, segH - gap);
                dc.DrawRectangle(on && !muted ? MeterColours.Lit(band, bright) : MeterColours.Unlit(band, bright), null, rect);
            }
            var face = bright ? new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal) : new Typeface("Segoe UI");
            var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 11, bright ? TryFindResource("TextBrush") as Brush ?? text : text, dpi)
            {
                MaxTextWidth = Math.Max(1, col - 2), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
            };
            if (muted) dc.PushOpacity(0.5);
            dc.DrawText(ft, new Point(c * col + 1, h + 3));
            if (muted) dc.Pop();
        }
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
