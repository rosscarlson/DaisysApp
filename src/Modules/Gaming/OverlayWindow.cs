using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming;

/// <summary>Everything the overlay shows, gathered twice a second.</summary>
internal sealed class OverlayData
{
    public string Game = "";
    /// <summary>The game's upscaling and frame generation libraries (Advanced), e.g. "DLSS 3.8.10 · DLSS Frame Gen".</summary>
    public string Tech = "";
    public FrameStats Now = FrameStats.None;      // the last second
    public FrameStats Recent = FrameStats.None;   // the last 30 seconds, for the lows
    public double[] Frametimes = Array.Empty<double>(); // the last few seconds, one per frame
    public double[] FpsHistory = Array.Empty<double>(); // a value a second, the last minute
    public double[] VramHistory = Array.Empty<double>();
    public HardwareSample Hardware = HardwareSample.Empty;
    public TimeSpan Session;
    public TimeSpan? Recording;
    /// <summary>A benchmark running: how long so far, and its set length (null when it runs until stopped).</summary>
    public TimeSpan? Benchmark, BenchmarkLength;
    public string? Message;
    /// <summary>False for just a note (and the recording light) with the overlay itself hidden.</summary>
    public bool Full = true;
}

/// <summary>
/// The FPS overlay: a borderless, always-on-top, see-through window that never takes the focus from the game. Unlocked,
/// drag it to move it and drag a corner to scale it; locked, clicks go straight through to the game.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly GamingSettings settings;
    private readonly Border root;
    private readonly OverlayPanel panel;
    private readonly ScaleTransform scale = new();
    private readonly Grid grips;
    private bool dragging;
    private int dragCorner = -1; // -1 move, 0 TL, 1 TR, 2 BL, 3 BR
    private (int X, int Y) dragStart;
    private PixelRect startBounds;
    private double startScale;

    /// <summary>Raised when the user has finished moving or scaling it.</summary>
    public event Action? Moved;

    public OverlayWindow(GamingSettings settings)
    {
        this.settings = settings;
        Title = "Daisy's App – FPS";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; // placed in physical pixels once it has a handle

        panel = new OverlayPanel { LayoutTransform = scale };
        root = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 8, 6), Child = panel };
        grips = new Grid { IsHitTestVisible = true };
        var host = new Grid();
        host.Children.Add(root);
        host.Children.Add(grips);
        for (int corner = 0; corner < 4; corner++) AddGrip(corner);
        Content = host;

        root.MouseLeftButtonDown += (_, e) => BeginDrag(-1, root, e);
        SourceInitialized += (_, _) =>
        {
            Native.MakeOverlay(this);
            ApplySettings();
        };
    }

    private void AddGrip(int corner)
    {
        var grip = new Border
        {
            Width = 12, Height = 12, Background = Brushes.Transparent,
            HorizontalAlignment = corner is 0 or 2 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = corner is 0 or 1 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Cursor = corner is 0 or 3 ? Cursors.SizeNWSE : Cursors.SizeNESW,
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            BorderThickness = new Thickness(corner is 0 or 2 ? 2 : 0, corner is 0 or 1 ? 2 : 0, corner is 1 or 3 ? 2 : 0, corner is 2 or 3 ? 2 : 0),
        };
        grip.MouseLeftButtonDown += (_, e) => BeginDrag(corner, grip, e);
        grips.Children.Add(grip);
    }

    /// <summary>Colours, scale, lock and capture exclusion from the settings.</summary>
    public void ApplySettings()
    {
        var bg = ParseColor(settings.BackgroundColor, Colors.Black);
        bg.A = (byte)Math.Round(Math.Clamp(settings.BackgroundOpacity, 0, 1) * 255);
        root.Background = new SolidColorBrush(bg);
        panel.TextColor = ParseColor(settings.TextColor, Colors.White);
        scale.ScaleX = scale.ScaleY = Math.Clamp(settings.Scale, 0.5, 4);
        grips.Visibility = settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        if (IsHandleCreated)
        {
            Native.SetClickThrough(this, settings.Locked);
            Native.ExcludeFromCapture(this, settings.HideFromCapture);
        }
        panel.InvalidateVisual();
        Fit();
    }

    private bool IsHandleCreated => new System.Windows.Interop.WindowInteropHelper(this).Handle != IntPtr.Zero;

    public void Update(OverlayData data)
    {
        panel.Data = data;
        panel.Settings = settings;
        panel.InvalidateMeasure();
        panel.InvalidateVisual();
        if (!dragging) Fit();
    }

    /// <summary>Back on top, in case a game pushed it under.</summary>
    public void KeepOnTop()
    {
        if (IsHandleCreated && !dragging) Native.BringToTop(this);
    }

    /// <summary>The window's size in physical pixels for its content as it is now.</summary>
    private (int W, int H) Measure()
    {
        var host = (FrameworkElement)Content;
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var d = host.DesiredSize;
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = d.Width;
        Height = d.Height;
        return (Math.Max(1, (int)Math.Ceiling(d.Width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(d.Height * dpi.DpiScaleY)));
    }

    /// <summary>
    /// Sizes it to its content and puts it at its saved anchor: top-left, or top-right when right-justified (so it
    /// grows to the left). Sized here rather than by WPF, which would resize it after the fact, keeping the top-left.
    /// </summary>
    private void Fit()
    {
        if (!IsHandleCreated) return;
        var (w, h) = Measure();
        if (!settings.PositionSet)
        {
            settings.X = settings.Align == OverlayAlign.Right ? 40 + w : 40;
            settings.Y = 40;
            settings.PositionSet = true;
        }
        int x = settings.Align == OverlayAlign.Right ? settings.X - w : settings.X;
        var target = new PixelRect(x, settings.Y, w, h);
        if (Native.GetBounds(this) != target) Native.SetBounds(this, target);
    }
    public void ResetPosition()
    {
        settings.PositionSet = false;
        Fit();
    }

    // ---------------------------------------------------------------- moving and scaling

    private void BeginDrag(int corner, UIElement element, MouseButtonEventArgs e)
    {
        if (settings.Locked) return;
        dragging = true;
        dragCorner = corner;
        dragStart = Native.Cursor();
        startBounds = Native.GetBounds(this);
        startScale = settings.Scale;
        element.MouseMove += OnDragMove;
        element.MouseLeftButtonUp += OnDragEnd;
        element.LostMouseCapture += OnDragEnd;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var c = Native.Cursor();
        int dx = c.X - dragStart.X, dy = c.Y - dragStart.Y;
        if (dragCorner < 0)
        {
            Native.SetBounds(this, startBounds with { X = startBounds.X + dx, Y = startBounds.Y + dy });
            return;
        }
        // dragging a corner away from the opposite one makes it bigger, by whichever way moved further
        bool left = dragCorner is 0 or 2, top = dragCorner is 0 or 1;
        double gx = (left ? -dx : dx) / (double)Math.Max(1, startBounds.Width);
        double gy = (top ? -dy : dy) / (double)Math.Max(1, startBounds.Height);
        double g = Math.Abs(gx) > Math.Abs(gy) ? gx : gy;
        settings.Scale = Math.Clamp(startScale * (1 + g), 0.5, 4);
        scale.ScaleX = scale.ScaleY = settings.Scale;
        // keep the opposite corner where it was
        var (w, h) = Measure();
        int x = left ? startBounds.Right - w : startBounds.X;
        int y = top ? startBounds.Bottom - h : startBounds.Y;
        Native.SetBounds(this, new PixelRect(x, y, w, h));
    }

    private void OnDragEnd(object sender, EventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        var element = (UIElement)sender;
        element.MouseMove -= OnDragMove;
        element.MouseLeftButtonUp -= OnDragEnd;
        element.LostMouseCapture -= OnDragEnd;
        element.ReleaseMouseCapture();
        var b = Native.GetBounds(this);
        settings.X = settings.Align == OverlayAlign.Right ? b.Right : b.X;
        settings.Y = b.Y;
        settings.PositionSet = true;
        Moved?.Invoke();
    }

    public static Color ParseColor(string? text, Color fallback)
    {
        try { return text != null && ColorConverter.ConvertFromString(text) is Color c ? c : fallback; }
        catch { return fallback; }
    }
}

/// <summary>Draws the overlay's numbers and graphs (no controls: one drawing, redrawn twice a second).</summary>
internal sealed class OverlayPanel : FrameworkElement
{
    public OverlayData Data = new();
    public GamingSettings? Settings;
    public Color TextColor = Colors.White;

    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Mono = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Brush FpsBrush = Frozen(Color.FromRgb(0x4C, 0xD9, 0x64));
    private static readonly Brush FrametimeBrush = Frozen(Color.FromRgb(0xFF, 0xB0, 0x20));
    private static readonly Brush VramBrush = Frozen(Color.FromRgb(0x40, 0xC8, 0xFF));
    private static readonly Brush RecBrush = Frozen(Color.FromRgb(0xFF, 0x3B, 0x30));
    private static readonly Brush BenchBrush = Frozen(Color.FromRgb(0xFF, 0xB0, 0x20));
    private static readonly Brush Shadow = Frozen(Color.FromArgb(170, 0, 0, 0));
    private static readonly Brush GraphBack = Frozen(Color.FromArgb(40, 255, 255, 255));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly List<Action<DrawingContext, double, double>> items = new(); // draw(dc, rowTop, width)
    private readonly List<double> heights = new();
    private double width;

    private sealed record Line(string Text, double Size, Brush Brush, bool Bold = false);

    protected override Size MeasureOverride(Size available)
    {
        Build();
        return new Size(width, heights.Sum());
    }

    protected override void OnRender(DrawingContext dc)
    {
        double y = 0;
        for (int i = 0; i < items.Count; i++)
        {
            items[i](dc, y, width);
            y += heights[i];
        }
    }

    private Brush text = Brushes.White;
    private double pixelsPerDip = 1;

    /// <summary>A piece of text and its soft shadow, which keeps it readable on any background, even fully transparent.</summary>
    private sealed record Txt(FormattedText Main, FormattedText Shade)
    {
        public double Width => Main.WidthIncludingTrailingWhitespace;
        public double Height => Main.Height;
        public double Baseline => Main.Baseline;
    }

    private Txt Format(string s, double size, Brush brush, bool bold = false) =>
        new(new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Mono : Face, size, brush, pixelsPerDip),
            new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Mono : Face, size, Shadow, pixelsPerDip));

    /// <summary>Lays the rows out for the current mode and data.</summary>
    private void Build()
    {
        items.Clear();
        heights.Clear();
        width = 0;
        var s = Settings;
        if (s == null) return;
        pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tb = new SolidColorBrush(TextColor);
        tb.Freeze();
        text = tb;
        var dim = new SolidColorBrush(Color.FromArgb(170, TextColor.R, TextColor.G, TextColor.B));
        dim.Freeze();
        var d = Data;
        bool right = s.Align == OverlayAlign.Right;

        if (d.Message != null) AddText(new[] { new Line(d.Message, 13, text) }, right);
        if (d.Recording is TimeSpan rec)
            AddText(new[] { new Line("●", 13, RecBrush), new Line(" REC " + Clock(rec), 12, text, true) }, right);
        if (d.Benchmark is TimeSpan bench)
            AddText(new[] { new Line("◆", 13, BenchBrush), new Line(" BENCH " + Clock(bench) + (d.BenchmarkLength is TimeSpan len ? " / " + Clock(len) : ""), 12, text, true) }, right);
        if (!d.Full) return;

        string fps = double.IsFinite(d.Now.Fps) ? d.Now.Fps.ToString("0") : "–";
        if (s.Mode == OverlayMode.Simple)
        {
            AddText(new[] { new Line(fps, 22, text, true), new Line(" FPS", 11, dim) }, right);
            return;
        }

        bool advanced = s.Mode == OverlayMode.Advanced;
        double graphW = advanced ? 200 : 90, graphH = advanced ? 34 : 20;
        if (advanced && d.Game.Length > 0)
            AddText(new[] { new Line(d.Game, 12, text), new Line("  " + Clock(d.Session) + "  " + DateTime.Now.ToString("t"), 11, dim) }, right);
        if (advanced && d.Tech.Length > 0)
            AddText(new[] { new Line(d.Tech, 11, dim) }, right);

        if (s.GraphFps)
        {
            var parts = new List<Line> { new(fps, advanced ? 24 : 18, FpsBrush, true), new(" FPS", 11, dim) };
            if (advanced)
                parts.Add(new Line("   " + F("1% {0} · 0.1% {1}", N(d.Recent.Low1Fps), N(d.Recent.Low01Fps)), 11, dim));
            AddRow(parts, right, graphW, graphH, (dc, r) => DrawSeries(dc, r, d.FpsHistory, 0, Math.Max(30, Max(d.FpsHistory) * 1.15), FpsBrush), advanced);
        }
        if (s.GraphFrametime)
        {
            string ft = double.IsFinite(d.Now.FrametimeMs) ? d.Now.FrametimeMs.ToString("0.0") : "–";
            var parts = new List<Line> { new(ft, advanced ? 16 : 14, FrametimeBrush, true), new(" ms", 11, dim) };
            if (advanced) parts.Add(new Line("   " + F("max {0} ms", double.IsFinite(d.Now.MaxFrametimeMs) ? d.Now.MaxFrametimeMs.ToString("0.0") : "–"), 11, dim));
            double top = Math.Max(20, Math.Min(Max(d.Frametimes) * 1.15, Average(d.Frametimes) * 4));
            AddRow(parts, right, graphW, graphH, (dc, r) => DrawSeries(dc, r, d.Frametimes, 0, top, FrametimeBrush), advanced);
        }
        var hw = d.Hardware;
        if (s.GraphVram)
        {
            var parts = new List<Line> { new(Gb(hw.VramUsedMB), advanced ? 16 : 14, VramBrush, true), new(" / " + Gb(hw.VramTotalMB) + " GB", 11, dim) };
            if (advanced) parts.Add(new Line("   " + T("video memory"), 11, dim));
            double total = double.IsFinite(hw.VramTotalMB) ? hw.VramTotalMB : Math.Max(1, Max(d.VramHistory) * 1.2);
            AddRow(parts, right, graphW, graphH, (dc, r) => DrawSeries(dc, r, d.VramHistory, 0, total, VramBrush), advanced);
        }
        if (!advanced) return;

        AddText(new[] { new Line("GPU ", 11, dim), new Line(Pct(hw.GpuPercent), 13, text, true),
            new Line("  " + Unit(hw.GpuTempC, "°C") + "  " + Unit(hw.GpuClockMHz, " MHz") + "  " + Unit(hw.GpuPowerW, " W"), 11, dim) }, right);
        AddText(new[] { new Line("CPU ", 11, dim), new Line(Pct(hw.CpuPercent), 13, text, true),
            new Line("  " + F("game {0}", Pct(hw.GameCpuPercent)), 11, dim) }, right);
        AddText(new[] { new Line("RAM ", 11, dim), new Line(Gb(hw.RamUsedMB) + " / " + Gb(hw.RamTotalMB) + " GB", 13, text, true),
            new Line("  " + F("game {0} GB", Gb(hw.GameRamMB)), 11, dim) }, right);
        if (double.IsFinite(d.Recent.Fps))
            AddText(new[] { new Line(F("30 s average {0} FPS · {1} ms", d.Recent.Fps.ToString("0"), d.Recent.FrametimeMs.ToString("0.0")), 11, dim) }, right);
    }

    private static string N(double v) => double.IsFinite(v) ? v.ToString("0") : "–";
    private static string Pct(double v) => double.IsFinite(v) ? v.ToString("0") + "%" : "–";
    private static string Unit(double v, string unit) => double.IsFinite(v) ? v.ToString("0") + unit : "";
    private static string Gb(double mb) => double.IsFinite(mb) ? (mb / 1024).ToString("0.0") : "–";
    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static double Max(double[] v)
    {
        double m = 0;
        foreach (double x in v) if (double.IsFinite(x) && x > m) m = x;
        return m;
    }

    private static double Average(double[] v)
    {
        double sum = 0;
        int n = 0;
        foreach (double x in v) if (double.IsFinite(x)) { sum += x; n++; }
        return n > 0 ? sum / n : 0;
    }

    /// <summary>A line of text pieces, left- or right-justified.</summary>
    private void AddText(IList<Line> parts, bool right)
    {
        var texts = parts.Select(p => Format(p.Text, p.Size, p.Brush, p.Bold)).ToList();
        double w = texts.Sum(t => t.Width), h = texts.Max(t => t.Height);
        double baseline = texts.Max(t => t.Baseline);
        width = Math.Max(width, w);
        heights.Add(h);
        items.Add((dc, top, total) =>
        {
            double x = right ? total - w : 0;
            foreach (var t in texts)
            {
                DrawText(dc, t, x, top + baseline - t.Baseline);
                x += t.Width;
            }
        });
    }

    /// <summary>
    /// Numbers with a graph beside them (medium; on the left when right-justified) or under them (advanced).
    /// </summary>
    private void AddRow(IList<Line> parts, bool right, double graphW, double graphH, Action<DrawingContext, Rect> graph, bool graphBelow)
    {
        var texts = parts.Select(p => Format(p.Text, p.Size, p.Brush, p.Bold)).ToList();
        double tw = texts.Sum(t => t.Width), th = texts.Max(t => t.Height);
        double baseline = texts.Max(t => t.Baseline);
        const double gap = 8;
        double numberW = Math.Max(tw, 70);
        double w = graphBelow ? Math.Max(tw, graphW) : numberW + gap + graphW;
        double h = graphBelow ? th + graphH + 4 : Math.Max(th, graphH) + 2;
        width = Math.Max(width, w);
        heights.Add(h);
        items.Add((dc, top, total) =>
        {
            double x0 = right ? total - w : 0;
            double tx = graphBelow ? (right ? total - tw : 0) : right ? total - tw : x0;
            foreach (var t in texts)
            {
                DrawText(dc, t, tx, top + baseline - t.Baseline + (graphBelow ? 0 : (h - th) / 2));
                tx += t.Width;
            }
            var area = graphBelow
                ? new Rect(right ? total - graphW : 0, top + th + 2, graphW, graphH)
                : new Rect(right ? total - numberW - gap - graphW : numberW + gap, top + (h - graphH) / 2, graphW, graphH);
            dc.DrawRectangle(GraphBack, null, area);
            graph(dc, area);
        });
    }

    private static void DrawText(DrawingContext dc, Txt t, double x, double y)
    {
        dc.DrawText(t.Shade, new Point(x + 1, y + 1));
        dc.DrawText(t.Main, new Point(x, y));
    }

    private static void DrawSeries(DrawingContext dc, Rect r, double[] values, double min, double max, Brush brush)
    {
        if (values.Length < 2 || max <= min) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            bool started = false;
            for (int i = 0; i < values.Length; i++)
            {
                double v = values[i];
                if (!double.IsFinite(v)) continue;
                double x = r.Left + r.Width * i / (values.Length - 1);
                double y = r.Bottom - r.Height * Math.Clamp((v - min) / (max - min), 0, 1);
                if (!started) { g.BeginFigure(new Point(x, y), false, false); started = true; }
                else g.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        var pen = new Pen(brush, 1.2);
        pen.Freeze();
        dc.PushClip(new RectangleGeometry(r));
        dc.DrawGeometry(null, pen, geo);
        dc.Pop();
    }
}
