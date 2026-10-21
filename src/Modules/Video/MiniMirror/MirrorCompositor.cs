using System.Diagnostics;

namespace DaisysApp.Applets.Video.MiniMirror;

/// <summary>
/// Builds one mirror's picture at a capped frame rate from whichever monitors its region overlaps (a region can span
/// monitors), cropped by <see cref="Zoom"/>. Hands each finished frame to <see cref="FrameReady"/>; drawing it is the
/// window's job.
/// </summary>
internal sealed class MirrorCompositor : IDisposable
{
    public const double MinZoom = 0.2;
    public const double MaxZoom = 8.0;

    private readonly CaptureManager captureManager;
    private readonly object sourcesGate = new();
    private List<(MonitorInfo Monitor, MonitorFrameBuffer Buffer)> sources = new();
    private PixelRect baseRect;
    private byte[] composeBuffer = Array.Empty<byte>();
    private double zoom = 1.0;
    private Thread? thread;
    private volatile bool running;
    private static readonly DaisysApp.Logging.Logger log = DaisysApp.Logging.Log.Here;

    public MirrorCompositor(CaptureManager captureManager, PixelRect sourceRect, int targetFps)
    {
        this.captureManager = captureManager;
        TargetFps = Math.Max(1, targetFps);
        SetSourceRect(sourceRect);
    }

    public int TargetFps { get; set; }

    /// <summary>1 = exactly the selected region; above 1 crops in around its centre, below 1 takes in more around it.</summary>
    public double Zoom
    {
        get => zoom;
        set => zoom = double.IsNaN(value) ? 1.0 : Math.Clamp(value, MinZoom, MaxZoom);
    }

    /// <summary>
    /// Raised on the render thread with a frame (buffer, width, height, stride). The buffer is reused for the next
    /// frame as soon as this returns, so handlers must finish with it before returning (Dispatcher.Invoke, not
    /// BeginInvoke).
    /// </summary>
    public event Action<byte[], int, int, int>? FrameReady;

    /// <summary>
    /// Works out which monitors the region overlaps and subscribes to their captures. Subscribes for the largest area
    /// the zoom slider could ever need, so dragging the slider never starts or stops a monitor capture.
    /// </summary>
    public void SetSourceRect(PixelRect sourceRect)
    {
        lock (sourcesGate)
        {
            ReleaseSources();
            baseRect = sourceRect;
            var widest = EffectiveRect(sourceRect, MinZoom);
            sources = MonitorService.GetMonitorsIntersecting(widest)
                .Select(m => (m, captureManager.Acquire(m.DeviceName)))
                .ToList();
        }
    }

    public void Start()
    {
        if (thread != null) return;
        running = true;
        thread = new Thread(RenderLoop) { IsBackground = true, Name = "MiniMirror-Compose" };
        thread.Start();
    }

    private void RenderLoop()
    {
        try { RenderFrames(); }
        catch (Exception ex) { DaisysApp.Logging.ErrorLog.Write("Mini Mirror compositor (this mirror stopped updating)", ex); }
    }

    private void RenderFrames()
    {
        var clock = Stopwatch.StartNew();
        long[] lastVersions = Array.Empty<long>();
        PixelRect lastRect = default;
        long nextStats = clock.ElapsedMilliseconds + 60000;
        int composed = 0, firstComposed = 0;
        // once, 15 s in: pictures drawn and pictures the captures brought, so a Normal log tells a frozen mirror's side
        long firstReport = 15000, firstVersions = -1;

        while (running)
        {
            long budgetMs = 1000L / Math.Max(1, TargetFps);
            long frameStart = clock.ElapsedMilliseconds;

            (MonitorInfo Monitor, MonitorFrameBuffer Buffer)[] snapshot;
            PixelRect rect;
            lock (sourcesGate)
            {
                snapshot = sources.ToArray();
                rect = baseRect;
            }
            if (snapshot.Length == 0 || rect.IsEmpty)
            {
                Thread.Sleep(200);
                continue;
            }

            var effective = EffectiveRect(rect, Zoom);
            var versions = snapshot.Select(s => s.Buffer.Version).ToArray();
            if (!effective.Equals(lastRect) || !versions.SequenceEqual(lastVersions))
            {
                Compose(snapshot, effective);
                lastVersions = versions;
                lastRect = effective;
                composed++;
                firstComposed++;
            }
            if (firstVersions < 0) firstVersions = versions.Sum();
            else if (firstReport > 0 && clock.ElapsedMilliseconds >= firstReport)
            {
                firstReport = 0;
                log.Info($"Mirror {rect.Width}x{rect.Height} at {rect.X},{rect.Y} on {string.Join(", ", snapshot.Select(s => s.Monitor.DeviceName))}: {firstComposed} pictures drawn in its first 15 s, {versions.Sum() - firstVersions} captured (at most {TargetFps}/s); picture brightness {Pixels.Brightness(composeBuffer, effective.Width, effective.Height, effective.Width * 4)} of 255{string.Concat(snapshot.Select(x => $", {x.Monitor.DeviceName} {x.Buffer.Brightness()}"))}");
            }
            if (clock.ElapsedMilliseconds >= nextStats)
            {
                log.Debug($"Mirror {rect.Width}x{rect.Height} at {rect.X},{rect.Y} on {string.Join(", ", snapshot.Select(s => s.Monitor.DeviceName))}: {composed} frames drawn in the last minute (at most {TargetFps}/s)");
                composed = 0;
                nextStats = clock.ElapsedMilliseconds + 60000;
            }

            long elapsed = clock.ElapsedMilliseconds - frameStart;
            Thread.Sleep((int)Math.Max(1, budgetMs - elapsed));
        }
    }

    private static PixelRect EffectiveRect(PixelRect rect, double zoom)
    {
        if (Math.Abs(zoom - 1.0) < 0.0001) return rect;
        int w = Math.Max(1, (int)Math.Round(rect.Width / zoom));
        int h = Math.Max(1, (int)Math.Round(rect.Height / zoom));
        return new PixelRect(rect.X + (rect.Width - w) / 2, rect.Y + (rect.Height - h) / 2, w, h);
    }

    private void Compose((MonitorInfo Monitor, MonitorFrameBuffer Buffer)[] snapshot, PixelRect rect)
    {
        int stride = rect.Width * 4;
        if (rect.Width <= 0 || rect.Height <= 0) return;
        int needed = stride * rect.Height;
        if (composeBuffer.Length != needed) composeBuffer = new byte[needed];
        else Array.Clear(composeBuffer);

        foreach (var (monitor, frame) in snapshot)
        {
            var part = rect.Intersect(monitor.Bounds);
            if (part.IsEmpty) continue;
            var local = new PixelRect(part.X - monitor.Bounds.X, part.Y - monitor.Bounds.Y, part.Width, part.Height);
            frame.CopyLocalRegion(local, composeBuffer, stride, part.X - rect.X, part.Y - rect.Y);
        }

        FrameReady?.Invoke(composeBuffer, rect.Width, rect.Height, stride);
    }

    private void ReleaseSources()
    {
        foreach (var (monitor, _) in sources) captureManager.Release(monitor.DeviceName);
        sources.Clear();
    }

    public void Dispose()
    {
        // No Join: the render thread may be waiting on the UI thread (FrameReady) that is disposing us. It stops by
        // itself after the current frame, and anything it hands over late is ignored by the closed window.
        running = false;
        thread = null;
        lock (sourcesGate) ReleaseSources();
    }
}
