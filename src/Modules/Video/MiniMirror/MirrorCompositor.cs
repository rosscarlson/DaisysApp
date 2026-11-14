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

    /// <summary>The mirror's name, for the log.</summary>
    public string Name { get; set; } = "";

    /// <summary>What the window did with the pictures since it was last asked (for the log; it starts counting again).</summary>
    public Func<string>? DrawStats { get; set; }

    /// <summary>1 = exactly the selected region; above 1 crops in around its centre, below 1 takes in more around it.</summary>
    public double Zoom
    {
        get => zoom;
        set => zoom = double.IsNaN(value) ? 1.0 : Math.Clamp(value, MinZoom, MaxZoom);
    }

    /// <summary>
    /// Raised on the render thread with a frame (buffer, width, height, stride). The buffer is reused for the next
    /// frame as soon as this returns, so handlers copy what they need before returning, without waiting on the UI
    /// thread (since 4.16.3 the window keeps a copy and draws the newest when the UI thread gets to it).
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
        // (Thread.Sleep is 15.6 ms at a time without it: a 144 Hz mirror couldn't go past about 64 a second)
        using var timer = DaisysApp.Shared.Win32.HighResTimer.Hold();
        try { RenderFrames(); }
        catch (Exception ex) { DaisysApp.Logging.ErrorLog.Write("Mini Mirror compositor (this mirror stopped updating)", ex); }
    }

    private void RenderFrames()
    {
        var clock = Stopwatch.StartNew();
        long[] lastVersions = Array.Empty<long>();
        PixelRect lastRect = default;
        int firstComposed = 0;
        // once, 15 s in: pictures drawn and pictures the captures brought, so a Normal log tells a frozen mirror's side
        long firstReport = 15000, firstVersions = -1;
        // (since 4.16.3) each minute after that: a slow one (drawn well under what came in) at Info, with where the time went
        var period = new Period();
        int slowReports = 0;

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
                long composeStart = Stopwatch.GetTimestamp();
                Compose(snapshot, effective);
                period.Composed(Stopwatch.GetTimestamp() - composeStart);
                lastVersions = versions;
                lastRect = effective;
                firstComposed++;
            }
            if (firstVersions < 0)
            {
                firstVersions = versions.Sum();
                period.Start(snapshot, clock.ElapsedMilliseconds);
            }
            else if (firstReport > 0 && clock.ElapsedMilliseconds >= firstReport)
            {
                firstReport = 0;
                log.Info($"Mirror {Name} {rect.Width}x{rect.Height} at {rect.X},{rect.Y} on {string.Join(", ", snapshot.Select(s => s.Monitor.DeviceName))}: {firstComposed} pictures drawn in its first 15 s, {versions.Sum() - firstVersions} captured (at most {TargetFps}/s); picture brightness {Pixels.Brightness(composeBuffer, effective.Width, effective.Height, effective.Width * 4)} of 255{string.Concat(snapshot.Select(x => $", {x.Monitor.DeviceName} {x.Buffer.Brightness()}"))}; {period.Report(snapshot, clock.ElapsedMilliseconds, TargetFps, DrawStats, out _)}");
                period.Start(snapshot, clock.ElapsedMilliseconds);
            }
            else if (firstReport == 0 && clock.ElapsedMilliseconds - period.Since >= 60000)
            {
                string text = period.Report(snapshot, clock.ElapsedMilliseconds, TargetFps, DrawStats, out bool slow);
                if (slow && (++slowReports <= 10 || slowReports % 30 == 0))
                    log.Info($"Mirror {Name} {rect.Width}x{rect.Height} is slow (time {slowReports}) {text}");
                else log.Debug($"Mirror {Name} {rect.Width}x{rect.Height} {text}");
                period.Start(snapshot, clock.ElapsedMilliseconds);
            }

            long elapsed = clock.ElapsedMilliseconds - frameStart;
            int sleep = (int)Math.Max(1, budgetMs - elapsed);
            long sleepStart = Stopwatch.GetTimestamp();
            Thread.Sleep(sleep);
            period.Slept(sleep, Stopwatch.GetTimestamp() - sleepStart);
        }
    }

    /// <summary>
    /// One stretch of a mirror's work, for the log: the pictures each capture brought and what each cost it, the
    /// pictures composed and how long that took, how long the waits between frames really were (Windows' timer), and
    /// what the window did with them (<see cref="DrawStats"/>).
    /// </summary>
    private sealed class Period
    {
        public long Since;
        private long[] versions = [], costs = [];
        private int composed, sleeps;
        private long composeTicks, composeMax, sleptTicks, askedMs, sleepMax;

        public void Start((MonitorInfo Monitor, MonitorFrameBuffer Buffer)[] snapshot, long now)
        {
            Since = now;
            versions = snapshot.Select(s => s.Buffer.Version).ToArray();
            costs = snapshot.Select(s => s.Buffer.CostTicks).ToArray();
            composed = sleeps = 0;
            composeTicks = composeMax = sleptTicks = askedMs = sleepMax = 0;
        }

        public void Composed(long ticks)
        {
            composed++;
            composeTicks += ticks;
            composeMax = Math.Max(composeMax, ticks);
        }

        public void Slept(int asked, long ticks)
        {
            sleeps++;
            askedMs += asked;
            sleptTicks += ticks;
            sleepMax = Math.Max(sleepMax, ticks);
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        /// <summary>The stretch as text; slow when it composed well under what came in, or the window says so.</summary>
        public string Report((MonitorInfo Monitor, MonitorFrameBuffer Buffer)[] snapshot, long now, int target, Func<string>? drawStats, out bool slow)
        {
            double seconds = Math.Max(0.001, (now - Since) / 1000.0);
            var parts = new List<string>();
            double captured = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                var b = snapshot[i].Buffer;
                long frames = i < versions.Length ? b.Version - versions[i] : 0;
                long cost = i < costs.Length ? b.CostTicks - costs[i] : 0;
                captured = Math.Max(captured, frames / seconds);
                parts.Add($"{snapshot[i].Monitor.DeviceName} ({b.Path}) {frames / seconds:0.#}/s{(frames > 0 ? $" at {Ms(cost) / frames:0.#} ms each" : "")}");
            }
            string draw = drawStats?.Invoke() ?? "";
            bool drawSlow = draw.Contains("UI thread busy", StringComparison.Ordinal) || draw.Contains("WPF behind", StringComparison.Ordinal);
            double perSecond = composed / seconds;
            // (a still screen brings nothing, and then nothing needs drawing)
            slow = drawSlow || captured >= 5 && perSecond < 0.6 * Math.Min(target, captured);
            return $"in {seconds:0} s: captured {string.Join(", ", parts)}; composed {perSecond:0.#}/s (at most {target}/s)" +
                   (composed > 0 ? $" at {Ms(composeTicks) / composed:0.##} ms each, longest {Ms(composeMax):0.#}" : "") +
                   (sleeps > 0 ? $"; waits between frames {Ms(sleptTicks) / sleeps:0.#} ms for {askedMs / (double)sleeps:0.#} asked, longest {Ms(sleepMax):0}" : "") +
                   (draw.Length > 0 ? "; " + draw : "");
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
