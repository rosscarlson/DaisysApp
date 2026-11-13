using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DaisysApp.Applets.Video.MiniMirror;

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
    // The newest picture, waiting for the UI thread (since 4.16.3; before, each frame waited for the UI thread to draw
    // it, at a priority above WPF's own drawing and input, so a busy UI thread slowed the mirrors and the app's window)
    private readonly object frameGate = new();
    private byte[] pending = [];
    private int pendingWidth, pendingHeight, pendingStride;
    private bool posted;
    private long postedAt;
    // what happened to the pictures since the log last asked (DrawStats)
    private int statDrawn, statSkipped;
    private long statWaitTicks, statWaitMax, statWriteTicks;
    private string drawing = "";
    private static readonly DaisysApp.Logging.Logger log = DaisysApp.Logging.Log.Here;

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

        compositor = new MirrorCompositor(captureManager, definition.SourceRect, definition.TargetFps) { Zoom = definition.Zoom, Name = definition.Name, DrawStats = DrawStats };
        compositor.FrameReady += OnFrameReady;

        SourceInitialized += (_, _) =>
        {
            WindowTools.SetBounds(this, Definition.WindowBounds, silent: true);
            WindowTools.SetClickThrough(this, Definition.ClickThrough);
            WindowTools.MakeToolWindow(this);
            if (this.hideFromCapture) WindowTools.ExcludeFromCapture(this);
            HwndSource.FromHwnd(WindowTools.Handle(this))?.AddHook(HoldLocked);
        };
        Loaded += (_, _) =>
        {
            ApplyDefinitionChanged();
            drawing = Drawing();
            compositor.Start();
            log.Info($"Mirror {Definition.Name} shown: window {Definition.WindowBounds.Width}x{Definition.WindowBounds.Height}, region {Definition.SourceRect.Width}x{Definition.SourceRect.Height}, zoom {Definition.Zoom:0.##}, {Definition.TargetFps}/s, {drawing}");
        };
        SizeChanged += (_, _) => UpdateClip();
        Closed += (_, _) =>
        {
            lock (frameGate) closed = true;
            compositor.Dispose();
        };
    }

    public MirrorDefinition Definition { get; }

    /// <summary>Raised on the UI thread after the user moved or resized the window.</summary>
    public event Action<MirrorDefinition>? BoundsChanged;

    private void UpdateClip() =>
        root.Clip = Definition.Shape == MirrorShape.Circle ? new EllipseGeometry(new Rect(0, 0, ActualWidth, ActualHeight)) : null;

    /// <summary>
    /// On the compositor's thread: keeps a copy of the picture and, unless one is already on its way, asks the UI
    /// thread to draw it. A picture that comes before the UI thread got to the last one replaces it (skipped).
    /// </summary>
    private void OnFrameReady(byte[] buffer, int width, int height, int stride)
    {
        if (closed || width <= 0 || height <= 0) return;
        lock (frameGate)
        {
            int size = stride * height;
            if (pending.Length != size) pending = new byte[size];
            Buffer.BlockCopy(buffer, 0, pending, 0, size);
            pendingWidth = width;
            pendingHeight = height;
            pendingStride = stride;
            if (posted) { statSkipped++; return; }
            posted = true;
            postedAt = Stopwatch.GetTimestamp();
        }
        try { Dispatcher.BeginInvoke(DispatcherPriority.Render, DrawPending); }
        catch (OperationCanceledException) { /* app shutting down */ }
    }

    private void DrawPending()
    {
        lock (frameGate)
        {
            posted = false;
            if (closed) return;
            long start = Stopwatch.GetTimestamp(), wait = start - postedAt;
            int width = pendingWidth, height = pendingHeight;
            if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            {
                bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                image.Source = bitmap;
            }
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pending, pendingStride, 0);
            statDrawn++;
            statWaitTicks += wait;
            statWaitMax = Math.Max(statWaitMax, wait);
            statWriteTicks += Stopwatch.GetTimestamp() - start;
        }
    }

    /// <summary>For the log (the compositor's thread): what the window did with the pictures since the last time.</summary>
    private string DrawStats()
    {
        int drawn, skipped;
        long wait, waitMax, write;
        lock (frameGate)
        {
            (drawn, skipped, wait, waitMax, write) = (statDrawn, statSkipped, statWaitTicks, statWaitMax, statWriteTicks);
            statDrawn = statSkipped = 0;
            statWaitTicks = statWaitMax = statWriteTicks = 0;
        }
        double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        if (drawn == 0) return $"window drew none{(skipped > 0 ? $", {skipped} skipped: UI thread busy" : "")}";
        double avgWait = Ms(wait) / drawn;
        // the UI thread taking longer than a frame to get to a picture is what slows a mirror on this side
        string busy = avgWait > 1000.0 / Math.Max(1, compositor.TargetFps) && skipped > drawn / 4 ? " (UI thread busy)" : "";
        return $"window drew {drawn}, {skipped} skipped for a newer one{busy}; UI thread got to each after {avgWait:0.#} ms, longest {Ms(waitMax):0}; writing each took {Ms(write) / drawn:0.##} ms; {drawing}";
    }

    /// <summary>How WPF draws the app (on the UI thread): on the graphics card or in software (Settings → General, or after a blank window).</summary>
    private static string Drawing()
    {
        try
        {
            bool software = RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly;
            return $"WPF {(software ? "drawing in software" : "on the graphics card")} (tier {RenderCapability.Tier >> 16})";
        }
        catch { return "WPF ?"; }
    }

    private void OnInteractionEnded(PixelRect bounds)
    {
        Definition.WindowBounds = bounds;
        // keep the Window size slider in step with a manual resize
        if (Definition.SourceRect.Width > 0)
            Definition.SizeScale = Math.Max(0.05, (double)bounds.Width / Definition.SourceRect.Width);
        BoundsChanged?.Invoke(Definition);
    }

    /// <summary>
    /// A locked mirror stays exactly where it was put. When a monitor goes or the resolution changes (a 5120-wide
    /// screen switched to 2560 while the game isn't running), Windows moves windows that end up off screen, and a DPI
    /// change resizes them: for a locked mirror those moves are turned back into its saved place and size, even off
    /// screen, so it's there when the wide screen is back. (The app's own moves don't come through here.)
    /// </summary>
    private IntPtr HoldLocked(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;
        if (msg != WM_WINDOWPOSCHANGING || !Definition.PositionLocked || Definition.WindowBounds.IsEmpty || lParam == IntPtr.Zero) return IntPtr.Zero;
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        var saved = Definition.WindowBounds;
        bool changed = false;
        if ((pos.flags & SWP_NOMOVE) == 0 && (pos.x != saved.X || pos.y != saved.Y)) { pos.x = saved.X; pos.y = saved.Y; changed = true; }
        if ((pos.flags & SWP_NOSIZE) == 0 && (pos.cx != saved.Width || pos.cy != saved.Height)) { pos.cx = saved.Width; pos.cy = saved.Height; changed = true; }
        if (changed) Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    /// <summary>A locked mirror back in its saved place and size, if anything moved it.</summary>
    public void RestoreLocked()
    {
        if (!Definition.PositionLocked || Definition.WindowBounds.IsEmpty) return;
        if (!WindowTools.GetBounds(this).Equals(Definition.WindowBounds)) WindowTools.SetBounds(this, Definition.WindowBounds, silent: true);
    }

    /// <summary>Re-applies shape, frame rate, aspect lock, click-through, opacity, visibility, zoom and lock.</summary>
    public void ApplyDefinitionChanged()
    {
        chrome.AspectLock = Definition.AspectLock;
        chrome.Locked = Definition.PositionLocked;
        compositor.TargetFps = Definition.TargetFps;
        compositor.Zoom = Definition.Zoom;
        compositor.Name = Definition.Name;
        WindowTools.SetClickThrough(this, Definition.ClickThrough);
        Opacity = Definition.Opacity;
        Visibility = Definition.Visible ? Visibility.Visible : Visibility.Hidden;
        Title = "Mini Mirror – " + Definition.Name;
        UpdateClip();
        RestoreLocked();
    }

    public void SetHideFromCapture(bool hide)
    {
        hideFromCapture = hide;
        WindowTools.ExcludeFromCapture(this, hide);
    }

    /// <summary>Sizes the window to the region's size × <see cref="MirrorDefinition.SizeScale"/>, keeping its centre where it is.</summary>
    public void ApplyScale()
    {
        var source = Definition.SourceRect;
        if (source.IsEmpty) return;
        int w = Math.Max(1, (int)Math.Round(source.Width * Definition.SizeScale));
        int h = Math.Max(1, (int)Math.Round(source.Height * Definition.SizeScale));
        // (a locked one from its saved place, not wherever Windows may have put it)
        var current = Definition.PositionLocked && !Definition.WindowBounds.IsEmpty ? Definition.WindowBounds : WindowTools.GetBounds(this);
        var bounds = new PixelRect(current.X + current.Width / 2 - w / 2, current.Y + current.Height / 2 - h / 2, w, h);
        WindowTools.SetBounds(this, bounds, silent: true);
        Definition.WindowBounds = bounds;
        BoundsChanged?.Invoke(Definition);
    }

    /// <summary>After the region was re-selected, or monitors changed.</summary>
    public void UpdateSourceRegion(PixelRect rect)
    {
        Definition.SourceRect = rect;
        compositor.SetSourceRect(rect);
    }

    /// <summary>Moves the window back onto a monitor if its own monitor has gone; a locked one isn't moved (it goes
    /// back to its saved place instead, even off screen).</summary>
    public void EnsureOnScreen()
    {
        if (Definition.PositionLocked) { RestoreLocked(); return; }
        var bounds = WindowTools.GetBounds(this);
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0 || monitors.Any(m => m.Bounds.Intersect(bounds) is { IsEmpty: false } part && part.Width >= 48 && part.Height >= 48))
            return;
        var main = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        int w = Math.Min(bounds.Width, main.Bounds.Width), h = Math.Min(bounds.Height, main.Bounds.Height);
        var moved = new PixelRect(main.Bounds.X + (main.Bounds.Width - w) / 2, main.Bounds.Y + (main.Bounds.Height - h) / 2, w, h);
        WindowTools.SetBounds(this, moved, silent: true);
        OnInteractionEnded(moved);
    }
}
