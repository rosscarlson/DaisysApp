using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.AudioTools.Tests.Controls;

/// <summary>
/// Small rotary knob. Drag up/down to change (Shift = fine), mouse wheel steps 0.5 dB (Shift = 0.1),
/// double-click resets to <see cref="DefaultValue"/>. Draws with the inherited Foreground.
/// </summary>
public sealed class TrimKnob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TrimKnob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(TrimKnob), new FrameworkPropertyMetadata(-40.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TrimKnob), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(TrimKnob), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private bool dragging;
    private Point dragStart;
    private double dragStartValue;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double DefaultValue { get; set; }

    /// <summary>Raised when a drag ends, after a wheel step, or after a reset.</summary>
    public event RoutedEventHandler? Committed;

    public TrimKnob()
    {
        Cursor = Cursors.SizeNS;
        Focusable = false;
    }

    private void SetClamped(double v) => Value = Math.Round(Math.Clamp(v, Minimum, Maximum), 1);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            SetClamped(DefaultValue);
            Committed?.Invoke(this, new RoutedEventArgs());
            return;
        }
        dragging = CaptureMouse();
        dragStart = e.GetPosition(this);
        dragStartValue = Value;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) return;
        double dy = dragStart.Y - e.GetPosition(this).Y;
        double perPixel = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.02 : 0.1;
        SetClamped(dragStartValue + dy * perPixel);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        Committed?.Invoke(this, new RoutedEventArgs());
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (dragging)
        {
            dragging = false;
            Committed?.Invoke(this, new RoutedEventArgs());
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.5;
        SetClamped(Value + Math.Sign(e.Delta) * step);
        Committed?.Invoke(this, new RoutedEventArgs());
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 4) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - 2;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // whole area is hit-testable

        var fg = Foreground;
        var track = new Pen(fg, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var dim = new Pen(fg.CloneCurrentValue(), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dim.Brush.Opacity = 0.3;

        double range = Maximum - Minimum;
        double f = range > 0 ? Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
        const double start = 135, sweep = 270;
        double end = start + sweep * f;

        dc.DrawGeometry(null, dim, Arc(center, r, start, start + sweep));
        if (f > 0.002) dc.DrawGeometry(null, track, Arc(center, r, start, end));

        var tip = PointAt(center, r - 4, end);
        dc.DrawLine(new Pen(fg, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, PointAt(center, r * 0.15, end), tip);
    }

    private static Point PointAt(Point c, double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    private static Geometry Arc(Point c, double r, double fromDeg, double toDeg)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(PointAt(c, r, fromDeg), false, false);
            ctx.ArcTo(PointAt(c, r, toDeg), new Size(r, r), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
