using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>The input overlay's settings (in ForzaWheel.json): shown or not, where, how much history, what's drawn.</summary>
public sealed class OverlaySettings
{
    public bool Show { get; set; }
    /// <summary>Its place and size on screen, in physical pixels (0 width: not placed yet).</summary>
    public int X { get; set; } = 80;
    public int Y { get; set; } = 80;
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>How many seconds of history the traces show.</summary>
    public int Seconds { get; set; } = 5;
    /// <summary>How dark the background is behind the traces, 0 (none) to 1.</summary>
    public double Background { get; set; } = 0.35;
    public bool Steering { get; set; } = true;
    public bool Throttle { get; set; } = true;
    public bool Brake { get; set; } = true;
    public bool Clutch { get; set; } = true;
    public bool Handbrake { get; set; } = true;
}

/// <summary>
/// The input overlay: a racing-telemetry trace of the pedals and wheel over the last few seconds (throttle green, brake
/// red, clutch blue, handbrake orange, steering white about the centre), with live bars and a turning wheel at the right,
/// on a see-through background. A borderless window that stays on top of the game and lets clicks through; Move /
/// resize (in the ForzaWheel tab) makes it draggable and stretchable until Done. Sampled every frame from the emulator's
/// values (it keeps the emulator reading while it shows).
/// </summary>
internal sealed class InputOverlay : Window
{
    private readonly OverlaySettings s;
    private readonly WheelEmulator emulator;
    private readonly Action save;
    private readonly Trace trace;
    private bool editing;

    public InputOverlay(OverlaySettings settings, WheelEmulator emulator, Action save)
    {
        s = settings;
        this.emulator = emulator;
        this.save = save;
        Title = T("ForzaWheel input overlay");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        MinWidth = 180;
        MinHeight = 60;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        trace = new Trace(settings);
        Content = trace;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (s.Width <= 0)
            {
                // first time: 480 × 140 at the top left of the main screen
                double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
                s.Width = (int)(480 * scale);
                s.Height = (int)(140 * scale);
            }
            SetWindowPos(hwnd, IntPtr.Zero, s.X, s.Y, s.Width, s.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            ApplyStyle();
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        // sampled each frame it's shown
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { emulator.OverlayReading = true; CompositionTarget.Rendering += OnFrame; }
            else { CompositionTarget.Rendering -= OnFrame; emulator.OverlayReading = false; }
        };
    }

    public bool Editing => editing;

    /// <summary>Movable and stretchable (and catches the mouse) until <see cref="EndEdit"/>.</summary>
    public void BeginEdit()
    {
        editing = true;
        trace.Editing = true;
        ApplyStyle();
        trace.InvalidateVisual();
    }

    /// <summary>Fixed again, clicks passing through to the game; its place is saved.</summary>
    public void EndEdit()
    {
        editing = false;
        trace.Editing = false;
        ApplyStyle();
        SaveBounds();
        trace.InvalidateVisual();
    }

    /// <summary>The history or what's drawn changed.</summary>
    public void Changed() => trace.InvalidateVisual();

    /// <summary>A tool window that never takes the focus; while not being moved, clicks go through it to the game.</summary>
    private void ApplyStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        ex = editing ? ex & ~WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var v = emulator.Values;
        double Get(string role) => v.TryGetValue(role, out var x) ? x : double.NaN;
        trace.Add(DateTime.UtcNow, Get("Steering"), Get("Throttle"), Get("Brake"), Get("Clutch"), Get("Handbrake"));
        trace.InvalidateVisual();
    }

    private void SaveBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
        {
            s.X = r.Left;
            s.Y = r.Top;
            s.Width = r.Right - r.Left;
            s.Height = r.Bottom - r.Top;
        }
        save();
    }

    /// <summary>While being moved, Windows moves (anywhere on it) and resizes (its edges and corners) it.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && editing)
        {
            int x = unchecked((short)(lParam.ToInt64() & 0xFFFF)), y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
            GetWindowRect(hwnd, out var r);
            const int edge = 10;
            bool left = x < r.Left + edge, right = x >= r.Right - edge, top = y < r.Top + edge, bottom = y >= r.Bottom - edge;
            int hit = top && left ? HTTOPLEFT : top && right ? HTTOPRIGHT : bottom && left ? HTBOTTOMLEFT : bottom && right ? HTBOTTOMRIGHT
                : left ? HTLEFT : right ? HTRIGHT : top ? HTTOP : bottom ? HTBOTTOM : HTCAPTION;
            handled = true;
            return new IntPtr(hit);
        }
        if (msg == WM_EXITSIZEMOVE) SaveBounds();
        if (msg == WM_NCRBUTTONUP && editing) { handled = true; return IntPtr.Zero; }
        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- the drawing

    /// <summary>The traces, the bars and the wheel, drawn straight (every frame).</summary>
    private sealed class Trace : FrameworkElement
    {
        private readonly OverlaySettings s;
        private readonly record struct Sample(DateTime At, double Steer, double Throttle, double Brake, double Clutch, double Handbrake);
        private readonly List<Sample> history = new();
        public bool Editing;

        private static readonly Brush ThrottleBrush = Frozen(Color.FromRgb(0x3C, 0xD0, 0x70));
        private static readonly Brush BrakeBrush = Frozen(Color.FromRgb(0xFF, 0x4D, 0x4D));
        private static readonly Brush ClutchBrush = Frozen(Color.FromRgb(0x4D, 0xA3, 0xFF));
        private static readonly Brush HandbrakeBrush = Frozen(Color.FromRgb(0xFF, 0xB1, 0x3B));
        private static readonly Brush SteerBrush = Frozen(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        private static readonly Pen GridPen = FrozenPen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Pen CentrePen = FrozenPen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Pen EditPen = FrozenPen(Color.FromRgb(0x4D, 0xA3, 0xFF), 2, dashed: true);

        public Trace(OverlaySettings settings) => s = settings;

        public void Add(DateTime at, double steer, double throttle, double brake, double clutch, double handbrake)
        {
            history.Add(new Sample(at, steer, throttle, brake, clutch, handbrake));
            var cut = at - TimeSpan.FromSeconds(Math.Clamp(s.Seconds, 1, 60)) - TimeSpan.FromSeconds(1);
            int old = history.FindIndex(h => h.At >= cut);
            if (old > 0) history.RemoveRange(0, old);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            var back = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(s.Background, 0, 1) * 255), 0, 0, 0));
            dc.DrawRoundedRectangle(back, Editing ? EditPen : null, new Rect(0, 0, w, h), 8, 8);

            // right: a bar per pedal shown, and the wheel
            const double pad = 8;
            var pedals = new List<(Brush Brush, Func<Sample, double> Of)>();
            if (s.Clutch) pedals.Add((ClutchBrush, x => x.Clutch));
            if (s.Brake) pedals.Add((BrakeBrush, x => x.Brake));
            if (s.Throttle) pedals.Add((ThrottleBrush, x => x.Throttle));
            if (s.Handbrake) pedals.Add((HandbrakeBrush, x => x.Handbrake));
            double barW = Math.Max(6, Math.Min(16, h / 9)), wheelD = s.Steering ? Math.Min(h - 2 * pad, 64) : 0;
            double side = pedals.Count * (barW + 4) + (wheelD > 0 ? wheelD + 8 : 0);
            var plot = new Rect(pad, pad, Math.Max(20, w - 2 * pad - side - 8), h - 2 * pad);

            // the grid: quarters, and the centre (steering straight ahead)
            for (int q = 1; q < 4; q++)
            {
                double y = plot.Top + plot.Height * q / 4;
                dc.DrawLine(q == 2 && s.Steering ? CentrePen : GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }

            if (history.Count > 1)
            {
                var now = history[^1].At;
                double span = Math.Clamp(s.Seconds, 1, 60);
                double X(DateTime t) => plot.Right - (now - t).TotalSeconds / span * plot.Width;
                void Line(Brush brush, double thick, Func<Sample, double> of)
                {
                    var g = new StreamGeometry();
                    bool any = false;
                    using (var ctx = g.Open())
                    {
                        bool open = false;
                        foreach (var p in history)
                        {
                            double v = of(p);
                            double x = X(p.At);
                            if (double.IsNaN(v) || x < plot.Left) { open = false; continue; }
                            double y = plot.Bottom - Math.Clamp(v, 0, 1) * plot.Height;
                            if (!open) { ctx.BeginFigure(new Point(x, y), false, false); open = true; }
                            else ctx.LineTo(new Point(x, y), true, true);
                            any = true;
                        }
                    }
                    g.Freeze();
                    if (any) dc.DrawGeometry(null, new Pen(brush, thick) { LineJoin = PenLineJoin.Round }, g);
                }
                double thick = Math.Max(1.5, Math.Min(3, h / 60));
                if (s.Handbrake) Line(HandbrakeBrush, thick, p => p.Handbrake);
                if (s.Clutch) Line(ClutchBrush, thick, p => p.Clutch);
                if (s.Brake) Line(BrakeBrush, thick, p => p.Brake);
                if (s.Throttle) Line(ThrottleBrush, thick, p => p.Throttle);
                if (s.Steering) Line(SteerBrush, thick, p => p.Steer);
            }

            Sample? last = history.Count > 0 ? history[^1] : null;
            double bx = plot.Right + 8;
            foreach (var (brush, of) in pedals)
            {
                var frame = new Rect(bx, plot.Top, barW, plot.Height);
                dc.DrawRectangle(null, GridPen, frame);
                double v = last is { } l ? of(l) : double.NaN;
                if (!double.IsNaN(v))
                {
                    double fill = Math.Clamp(v, 0, 1) * plot.Height;
                    dc.DrawRectangle(brush, null, new Rect(bx, plot.Bottom - fill, barW, fill));
                }
                bx += barW + 4;
            }
            if (wheelD > 0)
            {
                // the wheel: a ring with a top mark, turned as far as Forza's 450° wheel is
                var c = new Point(w - pad - wheelD / 2, h / 2);
                double r = wheelD / 2 - 2;
                double steer = last is { } l && !double.IsNaN(l.Steer) ? l.Steer : 0.5;
                dc.PushTransform(new RotateTransform((steer - 0.5) * 450, c.X, c.Y));
                dc.DrawEllipse(null, new Pen(SteerBrush, Math.Max(3, r / 6)), c, r, r);
                dc.DrawLine(new Pen(SteerBrush, Math.Max(2, r / 10)), new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
                dc.DrawLine(new Pen(SteerBrush, Math.Max(2, r / 10)), c, new Point(c.X, c.Y + r));
                dc.DrawLine(new Pen(BrakeBrush, Math.Max(3, r / 6)), new Point(c.X, c.Y - r - 3), new Point(c.X, c.Y - r + 5));
                dc.Pop();
            }

            if (Editing)
            {
                var text = new FormattedText(T("Drag to move, drag an edge to resize. Done in ForzaWheel."), System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(10, Math.Min(14, h / 10)), Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(pad + 4, pad + 2));
            }
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double thick, bool dashed = false)
        {
            var p = new Pen(Frozen(c), thick);
            if (dashed) p.DashStyle = DashStyles.Dash;
            p.Freeze();
            return p;
        }
    }

    private const int WM_NCHITTEST = 0x84, WM_EXITSIZEMOVE = 0x232, WM_NCRBUTTONUP = 0xA5;
    private const int HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
