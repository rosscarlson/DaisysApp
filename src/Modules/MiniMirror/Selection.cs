using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DaisysApp.Applets.MiniMirror;

internal sealed record SelectionResult(PixelRect Rect, MirrorShape Shape);

/// <summary>
/// The whole pick-a-region flow: dim every monitor and let the user drag a rectangle (it can cross monitors), then show
/// an outline they can still move and resize, with Shape / Cancel / Confirm buttons. Reports the final rect and shape.
/// None of its windows take the focus, so a game in front keeps it (and doesn't pause); Esc, Enter, R and C are read
/// as temporary system-wide keys while it runs.
/// </summary>
internal sealed class SelectionFlow
{
    private const int MinDragPx = 10;
    private const int ToolbarGapPx = 12;

    private readonly List<SelectionOverlayWindow> overlays = new();
    private readonly Action<SelectionResult> confirmed;
    private readonly Action finished;
    private SelectionKeys? keys;
    private SelectionAdornerWindow? adorner;
    private SelectionToolbarWindow? toolbar;
    private PixelPoint start;
    private PixelRect current;
    private MirrorShape shape;
    private bool dragging;

    /// <param name="confirmed">Called with the region when the user confirms.</param>
    /// <param name="finished">Called once the flow is over, confirmed or not.</param>
    public SelectionFlow(MirrorShape initialShape, Action<SelectionResult> confirmed, Action finished)
    {
        shape = initialShape;
        this.confirmed = confirmed;
        this.finished = finished;
    }

    public void Start()
    {
        keys = new SelectionKeys();
        keys.Pressed += OnKey;
        bool first = true;
        foreach (var monitor in MonitorService.GetMonitors())
        {
            var overlay = new SelectionOverlayWindow(monitor, showHint: monitor.IsPrimary || (first && !MonitorService.GetMonitors().Any(m => m.IsPrimary)));
            first = false;
            overlay.DragStarted += p => { start = p; dragging = true; };
            overlay.DragMoved += OnDragMoved;
            overlay.DragEnded += OnDragEnded;
            overlays.Add(overlay);
            overlay.Show();
        }
    }

    private void OnKey(SelectionKey key)
    {
        switch (key)
        {
            case SelectionKey.Cancel: Cancel(); break;
            case SelectionKey.Confirm: if (adorner != null) Confirm(); break;
            case SelectionKey.Shape:
                if (adorner != null && toolbar != null)
                {
                    adorner.SetShape(adorner.Shape == MirrorShape.Circle ? MirrorShape.Rectangle : MirrorShape.Circle);
                    toolbar.SetShape(adorner.Shape);
                }
                else ToggleShape();
                break;
        }
    }

    public void Cancel()
    {
        CloseAll();
        finished();
    }

    private void OnDragMoved(PixelPoint p)
    {
        if (!dragging) return;
        current = FromPoints(start, p);
        foreach (var o in overlays) o.ShowSelection(current, shape);
    }

    private void OnDragEnded(PixelPoint p)
    {
        if (!dragging) return;
        dragging = false;
        var rect = FromPoints(start, p);
        if (rect.Width < MinDragPx || rect.Height < MinDragPx)
        {
            // a stray click rather than a drag: keep selecting
            foreach (var o in overlays) o.ShowSelection(default, shape);
            return;
        }
        foreach (var o in overlays) o.Close();
        overlays.Clear();
        ShowAdjuster(rect);
    }

    private void ToggleShape()
    {
        shape = shape == MirrorShape.Rectangle ? MirrorShape.Circle : MirrorShape.Rectangle;
        if (dragging) foreach (var o in overlays) o.ShowSelection(current, shape);
    }

    private void ShowAdjuster(PixelRect rect)
    {
        adorner = new SelectionAdornerWindow(rect, shape);
        toolbar = new SelectionToolbarWindow();
        toolbar.SetShape(shape);

        adorner.LiveBoundsChanged += PlaceToolbar;
        toolbar.CancelRequested += Cancel;
        toolbar.ConfirmRequested += Confirm;
        toolbar.ToggleShapeRequested += () =>
        {
            adorner.SetShape(adorner.Shape == MirrorShape.Circle ? MirrorShape.Rectangle : MirrorShape.Circle);
            toolbar.SetShape(adorner.Shape);
        };
        toolbar.SizeChanged += (_, _) => PlaceToolbar();

        adorner.Show();
        toolbar.Show();
        PlaceToolbar();
    }

    private void Confirm()
    {
        if (adorner == null) return;
        var result = new SelectionResult(adorner.GetPhysicalBounds(), adorner.Shape);
        CloseAll();
        confirmed(result);
        finished();
    }

    /// <summary>Centres the toolbar under the outline, or above it if it would fall off the bottom of the monitor.</summary>
    private void PlaceToolbar()
    {
        if (adorner == null || toolbar == null || !toolbar.IsLoaded) return;
        var anchor = adorner.GetPhysicalBounds();
        var size = WindowPlacement.GetPhysicalBounds(toolbar);
        int x = anchor.X + (anchor.Width - size.Width) / 2;
        int y = anchor.Bottom + ToolbarGapPx;
        var monitor = MonitorService.GetMonitorsIntersecting(anchor).FirstOrDefault() ?? MonitorService.GetMonitors().FirstOrDefault();
        if (monitor != null)
        {
            if (y + size.Height > monitor.Bounds.Bottom) y = anchor.Y - size.Height - ToolbarGapPx;
            if (y < monitor.Bounds.Y) y = anchor.Bottom - size.Height - ToolbarGapPx; // tall selection: inside, at the bottom
            x = Math.Clamp(x, monitor.Bounds.X, Math.Max(monitor.Bounds.X, monitor.Bounds.Right - size.Width));
        }
        WindowPlacement.SetPhysicalBounds(toolbar, new PixelRect(x, y, size.Width, size.Height));
    }

    private void CloseAll()
    {
        keys?.Dispose();
        keys = null;
        foreach (var o in overlays) o.Close();
        overlays.Clear();
        adorner?.Close();
        adorner = null;
        toolbar?.Close();
        toolbar = null;
    }

    private static PixelRect FromPoints(PixelPoint a, PixelPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
}

/// <summary>
/// A dimmed, see-through layer over one monitor. The drag is tracked centrally in global physical pixels (by
/// <see cref="SelectionFlow"/>), so it can start on one monitor and end on another; each layer just draws its part.
/// </summary>
internal sealed class SelectionOverlayWindow : Window
{
    private static readonly Brush SelectionStroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
    private static readonly Brush SelectionFill = new SolidColorBrush(Color.FromArgb(50, 0x4C, 0xC2, 0xFF));

    private readonly MonitorInfo monitor;
    private readonly Path selection;

    public SelectionOverlayWindow(MonitorInfo monitor, bool showHint)
    {
        this.monitor = monitor;
        ShowsHint = showHint;
        Title = T("Mini Mirror – select a region");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var root = new Grid();
        selection = new Path { Stroke = SelectionStroke, StrokeThickness = 2, Fill = SelectionFill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        root.Children.Add(selection);
        if (showHint)
        {
            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0x20, 0x20, 0x20)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 28, 0, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = T("Drag around what you want to mirror   ·   R / C: rectangle or circle   ·   Esc: cancel"),
                    Foreground = Brushes.White,
                    FontSize = 14,
                },
            });
        }
        Content = root;

        ShowActivated = false;
        SourceInitialized += (_, _) =>
        {
            WindowPlacement.SetPhysicalBounds(this, monitor.Bounds);
            WindowPlacement.MakeToolWindow(this);
            WindowPlacement.MakeNoActivate(this);
        };
        MouseLeftButtonDown += OnMouseDown;
    }

    public bool ShowsHint { get; }

    public event Action<PixelPoint>? DragStarted;
    public event Action<PixelPoint>? DragMoved;
    public event Action<PixelPoint>? DragEnded;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        CaptureMouse();
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        DragStarted?.Invoke(ScreenPoint(e));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMoved?.Invoke(ScreenPoint(e));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        MouseMove -= OnMouseMove;
        MouseLeftButtonUp -= OnMouseUp;
        DragEnded?.Invoke(ScreenPoint(e));
    }

    /// <summary>Where the event happened, in physical screen pixels (not where the cursor is by the time it's handled).</summary>
    private PixelPoint ScreenPoint(MouseEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this));
        return new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    /// <summary>Draws the part of <paramref name="rect"/> (global physical pixels) that's on this monitor.</summary>
    public void ShowSelection(PixelRect rect, MirrorShape shape)
    {
        var local = new PixelRect(rect.X - monitor.Bounds.X, rect.Y - monitor.Bounds.Y, rect.Width, rect.Height);
        if (local.Intersect(new PixelRect(0, 0, monitor.Bounds.Width, monitor.Bounds.Height)).IsEmpty)
        {
            selection.Visibility = Visibility.Collapsed;
            return;
        }
        // Draw the whole shape (WPF clips it at the window edge) so a circle spanning monitors stays round.
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        var dip = new Rect(local.X / scale, local.Y / scale, local.Width / scale, local.Height / scale);
        selection.Data = shape == MirrorShape.Circle ? new EllipseGeometry(dip) : new RectangleGeometry(dip);
        selection.Visibility = Visibility.Visible;
    }
}

/// <summary>
/// The outline shown after the drag, at exactly the selected rect, which can still be moved and resized before
/// confirming. Uses the same drag and resize behaviour as the mirror windows.
/// </summary>
internal sealed class SelectionAdornerWindow : Window
{
    private readonly Path outline;

    public SelectionAdornerWindow(PixelRect bounds, MirrorShape shape)
    {
        Shape = shape;
        Title = T("Mini Mirror – adjust the region");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Math.Max(1, bounds.Width);
        Height = Math.Max(1, bounds.Height);

        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) }; // hit-testable everywhere
        outline = new Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 0x4C, 0xC2, 0xFF)),
            IsHitTestVisible = false,
        };
        root.Children.Add(outline);
        Content = root;

        var chrome = new ChromeInteraction(this, root);
        chrome.InteractionEnded += _ => { UpdateOutline(); LiveBoundsChanged?.Invoke(); };

        ShowActivated = false;
        SourceInitialized += (_, _) =>
        {
            WindowPlacement.SetPhysicalBounds(this, bounds);
            WindowPlacement.MakeToolWindow(this);
            WindowPlacement.MakeNoActivate(this);
        };
        Loaded += (_, _) => UpdateOutline();
        SizeChanged += (_, _) => { UpdateOutline(); LiveBoundsChanged?.Invoke(); };
        LocationChanged += (_, _) => LiveBoundsChanged?.Invoke();
    }

    public MirrorShape Shape { get; private set; }

    /// <summary>Raised whenever it moves or resizes, so the toolbar can follow.</summary>
    public event Action? LiveBoundsChanged;

    public void SetShape(MirrorShape shape)
    {
        Shape = shape;
        UpdateOutline();
    }

    private void UpdateOutline()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var rect = new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2));
        outline.Data = Shape == MirrorShape.Circle ? new EllipseGeometry(rect) : new RectangleGeometry(rect);
    }

    public PixelRect GetPhysicalBounds() => WindowPlacement.GetPhysicalBounds(this);
}

/// <summary>Small floating Shape / Cancel / Confirm bar that follows the selection outline.</summary>
internal sealed class SelectionToolbarWindow : Window
{
    private readonly TextBlock shapeGlyph;
    private readonly TextBlock shapeText;

    public SelectionToolbarWindow()
    {
        Title = T("Mini Mirror – confirm the region");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;

        var shapeButton = MakeButton("", T("Shape"), out shapeGlyph, out shapeText);
        shapeButton.ToolTip = T("Switch between rectangle and circle (R / C)");
        var cancelButton = MakeButton("", T("Cancel"), out _, out _);
        cancelButton.ToolTip = "Esc";
        var confirmButton = MakeButton("", T("Confirm"), out _, out _);
        confirmButton.ToolTip = "Enter";
        if (Application.Current?.TryFindResource("AccentButton") is Style accent) confirmButton.Style = accent;

        shapeButton.Click += (_, _) => ToggleShapeRequested?.Invoke();
        cancelButton.Click += (_, _) => CancelRequested?.Invoke();
        confirmButton.Click += (_, _) => ConfirmRequested?.Invoke();

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(shapeButton);
        panel.Children.Add(cancelButton);
        panel.Children.Add(confirmButton);

        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6), BorderThickness = new Thickness(1), Child = panel };
        border.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        Content = border;

        SourceInitialized += (_, _) =>
        {
            WindowPlacement.MakeToolWindow(this);
            WindowPlacement.MakeNoActivate(this);
        };
    }

    public event Action? ToggleShapeRequested;
    public event Action? ConfirmRequested;
    public event Action? CancelRequested;

    private static Button MakeButton(string glyph, string text, out TextBlock glyphBlock, out TextBlock textBlock)
    {
        glyphBlock = new TextBlock { Text = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        glyphBlock.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        textBlock = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(glyphBlock);
        content.Children.Add(textBlock);
        return new Button { Content = content, Margin = new Thickness(3, 0, 3, 0), Focusable = false };
    }

    public void SetShape(MirrorShape shape)
    {
        shapeGlyph.Text = shape == MirrorShape.Circle ? "" : "";
        shapeText.Text = shape == MirrorShape.Circle ? T("Circle") : T("Rectangle");
    }
}

internal enum SelectionKey { Cancel, Confirm, Shape }

/// <summary>
/// Esc, Enter, R, C and Tab as system-wide keys for as long as a selection is running, because the selection windows
/// never have the keyboard focus (so the game in front keeps it). Registered without a window (a hidden window here
/// would itself take the focus): the keys arrive in the UI thread's message queue.
/// </summary>
internal sealed class SelectionKeys : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int FirstId = 0xB100; // ids are per thread; well away from anything else registered on it
    private static readonly (uint Vk, SelectionKey Key)[] Keys =
        { (0x1B, SelectionKey.Cancel), (0x0D, SelectionKey.Confirm), (0x52, SelectionKey.Shape), (0x43, SelectionKey.Shape), (0x09, SelectionKey.Shape) };

    public SelectionKeys()
    {
        ComponentDispatcher.ThreadFilterMessage += OnMessage;
        for (int i = 0; i < Keys.Length; i++) RegisterHotKey(IntPtr.Zero, FirstId + i, MOD_NOREPEAT, Keys[i].Vk); // one taken elsewhere just won't work
    }

    public event Action<SelectionKey>? Pressed;

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WM_HOTKEY || msg.hwnd != IntPtr.Zero) return;
        long id = msg.wParam.ToInt64() - FirstId; // other messages' wParam can be any size: only read it for WM_HOTKEY
        if (id < 0 || id >= Keys.Length) return;
        handled = true;
        Pressed?.Invoke(Keys[id].Key);
    }

    public void Dispose()
    {
        for (int i = 0; i < Keys.Length; i++) UnregisterHotKey(IntPtr.Zero, FirstId + i);
        ComponentDispatcher.ThreadFilterMessage -= OnMessage;
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
