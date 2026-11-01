using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.Binder;

/// <summary>
/// A thin live meter under an axis binding (from FotG Controls): one that rests in the middle (steering, pitch) fills out from
/// the centre; a throttle or pedal from the left.
/// </summary>
internal sealed class AxisBar : FrameworkElement
{
    private double value;

    /// <summary>The axis, from -1 to 1.</summary>
    public double Value
    {
        get => value;
        set
        {
            value = Math.Clamp(double.IsNaN(value) ? 0 : value, -1, 1);
            if (Math.Abs(value - this.value) < 0.004) return;
            this.value = value;
            InvalidateVisual();
        }
    }

    public bool Centred { get; set; } = true;

    protected override Size MeasureOverride(Size available) => new(0, 4);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var track = TryFindResource("ControlBrush") as Brush ?? Brushes.Gray;
        var fill = TryFindResource("AccentBrush") as Brush ?? Brushes.DeepSkyBlue;
        double r = h / 2;
        dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), r, r);
        if (Centred)
        {
            double mid = w / 2, len = Math.Abs(value) * mid;
            if (len >= 0.5) dc.DrawRoundedRectangle(fill, null, value >= 0 ? new Rect(mid, 0, len, h) : new Rect(mid - len, 0, len, h), r, r);
            dc.DrawRectangle(fill, null, new Rect(mid - 0.5, 0, 1, h));
        }
        else
        {
            double len = (value + 1) / 2 * w;
            if (len >= 0.5) dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, len, h), r, r);
        }
    }
}
