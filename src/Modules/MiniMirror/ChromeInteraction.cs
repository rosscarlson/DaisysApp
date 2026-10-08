using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.MiniMirror;

[Flags]
internal enum ResizeEdge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

/// <summary>
/// Drag-the-body-to-move plus 8 resize grips, shared by the selection outline and the mirror windows. Works from the
/// global cursor position in physical pixels and places the window with SetWindowPos, so it behaves on any monitor
/// and DPI, including while being dragged across them.
/// </summary>
internal sealed class ChromeInteraction
{
    private const double GripThickness = 8;
    private const double CornerThickness = 14;
    private const int MinSizePx = 48;
    private const int SnapThresholdPx = 12;

    private readonly Window window;
    private bool interacting;
    private ResizeEdge edge;
    private PixelRect startBounds;
    private PixelPoint startCursor;
    private FrameworkElement? captureElement;

    public ChromeInteraction(Window window, Grid root)
    {
        this.window = window;
        root.MouseLeftButtonDown += (_, e) => Begin(ResizeEdge.None, root, e);

        AddGrip(root, ResizeEdge.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch, GripThickness, double.NaN, Cursors.SizeWE);
        AddGrip(root, ResizeEdge.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch, GripThickness, double.NaN, Cursors.SizeWE);
        AddGrip(root, ResizeEdge.Top, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, GripThickness, Cursors.SizeNS);
        AddGrip(root, ResizeEdge.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, GripThickness, Cursors.SizeNS);
        AddGrip(root, ResizeEdge.Left | ResizeEdge.Top, HorizontalAlignment.Left, VerticalAlignment.Top, CornerThickness, CornerThickness, Cursors.SizeNWSE);
        AddGrip(root, ResizeEdge.Right | ResizeEdge.Bottom, HorizontalAlignment.Right, VerticalAlignment.Bottom, CornerThickness, CornerThickness, Cursors.SizeNWSE);
        AddGrip(root, ResizeEdge.Right | ResizeEdge.Top, HorizontalAlignment.Right, VerticalAlignment.Top, CornerThickness, CornerThickness, Cursors.SizeNESW);
        AddGrip(root, ResizeEdge.Left | ResizeEdge.Bottom, HorizontalAlignment.Left, VerticalAlignment.Bottom, CornerThickness, CornerThickness, Cursors.SizeNESW);
    }

    /// <summary>Corner drags keep the window's proportions.</summary>
    public bool AspectLock { get; set; }

    /// <summary>Moving and resizing are switched off.</summary>
    public bool Locked { get; set; }

    /// <summary>Other windows' bounds to snap to while dragging with Alt held; null = no snapping.</summary>
    public Func<IEnumerable<PixelRect>>? SnapTargets { get; set; }

    /// <summary>Raised when a drag or resize finishes, with the final bounds.</summary>
    public event Action<PixelRect>? InteractionEnded;

    private void AddGrip(Grid host, ResizeEdge gripEdge, HorizontalAlignment h, VerticalAlignment v, double width, double height, Cursor cursor)
    {
        var grip = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Width = width,
            Height = height,
            Cursor = cursor,
        };
        grip.MouseLeftButtonDown += (_, e) => Begin(gripEdge, grip, e);
        host.Children.Add(grip);
    }

    private void Begin(ResizeEdge gripEdge, FrameworkElement element, MouseButtonEventArgs e)
    {
        if (interacting || Locked) return;
        interacting = true;
        edge = gripEdge;
        captureElement = element;
        startBounds = WindowPlacement.GetPhysicalBounds(window);
        startCursor = WindowPlacement.GetCursorPosition();
        element.MouseMove += OnMouseMove;
        element.MouseLeftButtonUp += OnMouseUp;
        element.LostMouseCapture += OnLostCapture;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!interacting || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = WindowPlacement.GetCursorPosition();
        int dx = cursor.X - startCursor.X, dy = cursor.Y - startCursor.Y;

        if (edge == ResizeEdge.None)
        {
            int x = startBounds.X + dx, y = startBounds.Y + dy;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) Snap(ref x, ref y, startBounds.Width, startBounds.Height);
            WindowPlacement.SetPhysicalBounds(window, new PixelRect(x, y, startBounds.Width, startBounds.Height));
            return;
        }

        int w = startBounds.Width, h = startBounds.Height;
        if (edge.HasFlag(ResizeEdge.Left)) w = startBounds.Width - dx;
        if (edge.HasFlag(ResizeEdge.Right)) w = startBounds.Width + dx;
        if (edge.HasFlag(ResizeEdge.Top)) h = startBounds.Height - dy;
        if (edge.HasFlag(ResizeEdge.Bottom)) h = startBounds.Height + dy;

        bool corner = (edge & (ResizeEdge.Left | ResizeEdge.Right)) != 0 && (edge & (ResizeEdge.Top | ResizeEdge.Bottom)) != 0;
        if (AspectLock && corner && startBounds.Width > 0 && startBounds.Height > 0)
        {
            double ratio = (double)startBounds.Width / startBounds.Height;
            if (Math.Abs(dx) >= Math.Abs(dy)) h = (int)Math.Round(w / ratio);
            else w = (int)Math.Round(h * ratio);
        }

        w = Math.Max(MinSizePx, w);
        h = Math.Max(MinSizePx, h);
        int nx = edge.HasFlag(ResizeEdge.Left) ? startBounds.Right - w : startBounds.X;
        int ny = edge.HasFlag(ResizeEdge.Top) ? startBounds.Bottom - h : startBounds.Y;
        WindowPlacement.SetPhysicalBounds(window, new PixelRect(nx, ny, w, h));
    }

    /// <summary>
    /// Nudges the position so the window's left/right edges line up with another window's left/right edges, and
    /// top/bottom with top/bottom, when they're within a few pixels. X and Y snap independently, to the closest edge.
    /// </summary>
    private void Snap(ref int x, ref int y, int width, int height)
    {
        var targets = SnapTargets?.Invoke();
        if (targets == null) return;
        int bestDx = 0, bestAbsDx = SnapThresholdPx + 1, bestDy = 0, bestAbsDy = SnapThresholdPx + 1;
        foreach (var t in targets)
        {
            foreach (int c in new[] { t.X - x, t.Right - x, t.X - (x + width), t.Right - (x + width) })
                if (Math.Abs(c) < bestAbsDx) { bestAbsDx = Math.Abs(c); bestDx = c; }
            foreach (int c in new[] { t.Y - y, t.Bottom - y, t.Y - (y + height), t.Bottom - (y + height) })
                if (Math.Abs(c) < bestAbsDy) { bestAbsDy = Math.Abs(c); bestDy = c; }
        }
        if (bestAbsDx <= SnapThresholdPx) x += bestDx;
        if (bestAbsDy <= SnapThresholdPx) y += bestDy;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => End();

    private void OnLostCapture(object sender, MouseEventArgs e) => End();

    private void End()
    {
        if (!interacting) return;
        interacting = false;
        if (captureElement != null)
        {
            captureElement.MouseMove -= OnMouseMove;
            captureElement.MouseLeftButtonUp -= OnMouseUp;
            captureElement.LostMouseCapture -= OnLostCapture;
            captureElement.ReleaseMouseCapture();
            captureElement = null;
        }
        InteractionEnded?.Invoke(WindowPlacement.GetPhysicalBounds(window));
    }
}
