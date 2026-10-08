using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaisysApp.Applets.MiniMirror;

/// <summary>
/// Borderless, always-on-top window showing the live picture of a mirror's region, stretched to the window's size.
/// Drag it to move, drag an edge or corner to resize. Built in code since one is created per mirror at run time.
/// </summary>
internal sealed class MirrorWindow : Window
{
    private readonly Grid root;
    private readonly Image image;
    private readonly ChromeInteraction chrome;
    private readonly MirrorCompositor compositor;
    private WriteableBitmap? bitmap;
    private bool closed;
    private bool hideFromCapture;

    public MirrorWindow(MirrorDefinition definition, CaptureManager captureManager, Func<IEnumerable<PixelRect>> snapTargets, bool hideFromCapture)
    {
        Definition = definition;
        this.hideFromCapture = hideFromCapture;
        Title = "Mini Mirror – " + definition.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Math.Max(1, definition.WindowBounds.Width);
        Height = Math.Max(1, definition.WindowBounds.Height);

        root = new Grid { Background = Brushes.Black };
        image = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        root.Children.Add(image);
        Content = root;

        chrome = new ChromeInteraction(this, root)
        {
            AspectLock = definition.AspectLock,
            Locked = definition.PositionLocked,
            SnapTargets = snapTargets,
        };
        chrome.InteractionEnded += OnInteractionEnded;

        compositor = new MirrorCompositor(captureManager, definition.SourceRect, definition.TargetFps) { Zoom = definition.Zoom };
        compositor.FrameReady += OnFrameReady;

        SourceInitialized += (_, _) =>
        {
            WindowPlacement.SetPhysicalBounds(this, Definition.WindowBounds);
            WindowPlacement.SetClickThrough(this, Definition.ClickThrough);
            WindowPlacement.MakeToolWindow(this);
            if (this.hideFromCapture) WindowPlacement.ExcludeFromCapture(this);
        };
        Loaded += (_, _) =>
        {
            ApplyDefinitionChanged();
            compositor.Start();
        };
        SizeChanged += (_, _) => UpdateClip();
        Closed += (_, _) =>
        {
            closed = true;
            compositor.Dispose();
        };
    }

    public MirrorDefinition Definition { get; }

    /// <summary>Raised on the UI thread after the user moved or resized the window.</summary>
    public event Action<MirrorDefinition>? BoundsChanged;

    private void UpdateClip() =>
        root.Clip = Definition.Shape == MirrorShape.Circle ? new EllipseGeometry(new Rect(0, 0, ActualWidth, ActualHeight)) : null;

    private void OnFrameReady(byte[] buffer, int width, int height, int stride)
    {
        if (closed || width <= 0 || height <= 0) return;
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (closed) return;
                if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
                {
                    bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    image.Source = bitmap;
                }
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
            });
        }
        catch (OperationCanceledException) { /* app shutting down */ }
    }

    private void OnInteractionEnded(PixelRect bounds)
    {
        Definition.WindowBounds = bounds;
        // keep the Window size slider in step with a manual resize
        if (Definition.SourceRect.Width > 0)
            Definition.SizeScale = Math.Max(0.05, (double)bounds.Width / Definition.SourceRect.Width);
        BoundsChanged?.Invoke(Definition);
    }

    /// <summary>Re-applies shape, frame rate, aspect lock, click-through, opacity, visibility, zoom and lock.</summary>
    public void ApplyDefinitionChanged()
    {
        chrome.AspectLock = Definition.AspectLock;
        chrome.Locked = Definition.PositionLocked;
        compositor.TargetFps = Definition.TargetFps;
        compositor.Zoom = Definition.Zoom;
        WindowPlacement.SetClickThrough(this, Definition.ClickThrough);
        Opacity = Definition.Opacity;
        Visibility = Definition.Visible ? Visibility.Visible : Visibility.Hidden;
        Title = "Mini Mirror – " + Definition.Name;
        UpdateClip();
    }

    public void SetHideFromCapture(bool hide)
    {
        hideFromCapture = hide;
        Native.SetWindowDisplayAffinity(WindowPlacement.EnsureHandle(this), hide ? Native.WDA_EXCLUDEFROMCAPTURE : 0);
    }

    /// <summary>Sizes the window to the region's size × <see cref="MirrorDefinition.SizeScale"/>, keeping its centre where it is.</summary>
    public void ApplyScale()
    {
        var source = Definition.SourceRect;
        if (source.IsEmpty) return;
        int w = Math.Max(1, (int)Math.Round(source.Width * Definition.SizeScale));
        int h = Math.Max(1, (int)Math.Round(source.Height * Definition.SizeScale));
        var current = WindowPlacement.GetPhysicalBounds(this);
        var bounds = new PixelRect(current.X + current.Width / 2 - w / 2, current.Y + current.Height / 2 - h / 2, w, h);
        WindowPlacement.SetPhysicalBounds(this, bounds);
        Definition.WindowBounds = bounds;
        BoundsChanged?.Invoke(Definition);
    }

    /// <summary>After the region was re-selected, or monitors changed.</summary>
    public void UpdateSourceRegion(PixelRect rect)
    {
        Definition.SourceRect = rect;
        compositor.SetSourceRect(rect);
    }

    /// <summary>Moves the window back onto a monitor if its own monitor has gone.</summary>
    public void EnsureOnScreen()
    {
        var bounds = WindowPlacement.GetPhysicalBounds(this);
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0 || monitors.Any(m => m.Bounds.Intersect(bounds) is { IsEmpty: false } part && part.Width >= 48 && part.Height >= 48))
            return;
        var main = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        int w = Math.Min(bounds.Width, main.Bounds.Width), h = Math.Min(bounds.Height, main.Bounds.Height);
        var moved = new PixelRect(main.Bounds.X + (main.Bounds.Width - w) / 2, main.Bounds.Y + (main.Bounds.Height - h) / 2, w, h);
        WindowPlacement.SetPhysicalBounds(this, moved);
        OnInteractionEnded(moved);
    }
}
