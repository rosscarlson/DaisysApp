using System.Windows;
using System.Windows.Media;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.15.2) A chart with what moves as Compare plays (the cursor, a dot, the readout at the cursor) on a layer of
/// its own over the rest: moving it draws only that layer again (<see cref="InvalidateTop"/>). 4.10.3–4.15.1 drew every
/// chart again each frame of the screen as it played, which took the whole app's time.
/// </summary>
internal abstract class LayeredChart : FrameworkElement
{
    private readonly DrawingVisual top = new();

    protected LayeredChart() => AddVisualChild(top);

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => top;

    /// <summary>The top layer drawn again (the chart under it as it was).</summary>
    protected void InvalidateTop()
    {
        using var dc = top.RenderOpen();
        RenderTop(dc);
    }

    protected sealed override void OnRender(DrawingContext dc)
    {
        RenderChart(dc);
        InvalidateTop();
    }

    /// <summary>The chart, all but what moves.</summary>
    protected abstract void RenderChart(DrawingContext dc);

    /// <summary>What moves, over the chart.</summary>
    protected abstract void RenderTop(DrawingContext dc);
}
