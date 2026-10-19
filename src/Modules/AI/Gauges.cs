using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.AI;

/// <summary>What a session is doing, for the orb, the tab's dot and the status line.</summary>
internal enum SessionState { Idle, Starting, Thinking, Writing, Tool, Waiting, Error, Offline, Paused }

/// <summary>The colours: of a state, and of each kind of tool (reading, changing, running, the web, agents).</summary>
internal static class Palette
{
    public static Color Of(SessionState s) => s switch
    {
        SessionState.Starting => Color.FromRgb(0x58, 0xA6, 0xFF),
        SessionState.Thinking => Color.FromRgb(0xA3, 0x71, 0xF7),
        SessionState.Writing => Color.FromRgb(0x4C, 0xC2, 0xFF),
        SessionState.Tool => Color.FromRgb(0xF0, 0x88, 0x3E),
        SessionState.Waiting => Color.FromRgb(0xE3, 0xB3, 0x41),
        SessionState.Error => Color.FromRgb(0xF8, 0x51, 0x49),
        SessionState.Offline => Color.FromRgb(0x6E, 0x76, 0x81),
        SessionState.Paused => Color.FromRgb(0x8E, 0x9C, 0xC8),
        _ => Color.FromRgb(0x3F, 0xB9, 0x50),
    };

    public enum ToolKind { Look, Change, Run, Web, Agent, Plan, Other }

    public static ToolKind KindOf(string tool) => tool switch
    {
        "Read" or "Glob" or "Grep" or "LS" or "LSP" or "NotebookRead" => ToolKind.Look,
        "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => ToolKind.Change,
        "Bash" or "PowerShell" or "BashOutput" or "KillShell" or "Monitor" => ToolKind.Run,
        "WebFetch" or "WebSearch" => ToolKind.Web,
        "Task" or "Agent" or "SendMessage" => ToolKind.Agent,
        "TodoWrite" or "ExitPlanMode" or "EnterPlanMode" => ToolKind.Plan,
        _ => ToolKind.Other,
    };

    public static Color Of(ToolKind k) => k switch
    {
        ToolKind.Look => Color.FromRgb(0x39, 0xC5, 0xCF),
        ToolKind.Change => Color.FromRgb(0x3F, 0xB9, 0x50),
        ToolKind.Run => Color.FromRgb(0xF0, 0x88, 0x3E),
        ToolKind.Web => Color.FromRgb(0xBC, 0x8C, 0xFF),
        ToolKind.Agent => Color.FromRgb(0xF7, 0x78, 0xBA),
        ToolKind.Plan => Color.FromRgb(0xE3, 0xB3, 0x41),
        _ => Color.FromRgb(0x8B, 0x94, 0x9E),
    };

    /// <summary>A Segoe Fluent Icons glyph for a tool.</summary>
    public static string IconOf(string tool) => KindOf(tool) switch
    {
        ToolKind.Look => tool == "Read" ? "" : "",
        ToolKind.Change => "",
        ToolKind.Run => "",
        ToolKind.Web => "",
        ToolKind.Agent => "",
        ToolKind.Plan => "",
        _ => "",
    };

    public static SolidColorBrush Brush(Color c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public static Brush Theme(FrameworkElement e, string key) => e.TryFindResource(key) as Brush ?? Brushes.Gray;

    public static FormattedText Text(FrameworkElement e, string text, double size, Brush brush, bool bold = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(e).PixelsPerDip);

    /// <summary>12,345 → "12.3k"</summary>
    public static string Tokens(double n) => n >= 1_000_000 ? (n / 1_000_000).ToString("0.##", CultureInfo.CurrentCulture) + "M"
        : n >= 10_000 ? (n / 1000).ToString("0", CultureInfo.CurrentCulture) + "k"
        : n >= 1000 ? (n / 1000).ToString("0.0", CultureInfo.CurrentCulture) + "k"
        : n.ToString("0", CultureInfo.CurrentCulture);
}

/// <summary>
/// The status orb: a glowing ball in the state's colour. Idle it rests; thinking it breathes; writing it ripples
/// outwards; running a tool an arc spins round it; waiting for you it blinks.
/// </summary>
internal sealed class StatusOrb : FrameworkElement
{
    private SessionState state = SessionState.Offline;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime born = DateTime.UtcNow;

    public StatusOrb()
    {
        timer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => Animate();
    }

    public SessionState State
    {
        get => state;
        set { state = value; Animate(); InvalidateVisual(); }
    }

    private bool Busy => state is SessionState.Starting or SessionState.Thinking or SessionState.Writing or SessionState.Tool or SessionState.Waiting or SessionState.Paused;

    private void Animate()
    {
        if (Busy && IsVisible) timer.Start(); else timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, r = Math.Min(w, h) / 2 - 2;
        if (r < 3) return;
        var c = new Point(w / 2, h / 2);
        var colour = Palette.Of(state);
        double t = (DateTime.UtcNow - born).TotalSeconds;
        double core = r * 0.55;
        switch (state)
        {
            case SessionState.Thinking:
                core *= 0.9 + 0.12 * Math.Sin(t * 3);
                break;
            case SessionState.Paused:
                core *= 0.92 + 0.08 * Math.Sin(t * 1.2);
                break;
            case SessionState.Writing:
                for (int i = 0; i < 3; i++)
                {
                    double phase = (t * 0.9 + i / 3.0) % 1;
                    double rr = core + (r - core) * phase;
                    dc.DrawEllipse(null, new Pen(Palette.Brush(colour, (byte)(160 * (1 - phase))), 2), c, rr, rr);
                }
                break;
            case SessionState.Tool:
                {
                    double a0 = t * 4 % (2 * Math.PI);
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        double rr = r - 2;
                        ctx.BeginFigure(new Point(c.X + rr * Math.Cos(a0), c.Y + rr * Math.Sin(a0)), false, false);
                        double a1 = a0 + Math.PI * 1.2;
                        ctx.ArcTo(new Point(c.X + rr * Math.Cos(a1), c.Y + rr * Math.Sin(a1)), new Size(rr, rr), 0, true, SweepDirection.Clockwise, true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, new Pen(Palette.Brush(colour), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
                }
                break;
            case SessionState.Waiting:
                colour = Color.FromArgb((byte)(140 + 115 * (0.5 + 0.5 * Math.Sin(t * 6))), colour.R, colour.G, colour.B);
                break;
        }
        // the glow, then the ball
        var glow = new RadialGradientBrush(Color.FromArgb(0x70, colour.R, colour.G, colour.B), Color.FromArgb(0, colour.R, colour.G, colour.B));
        dc.DrawEllipse(glow, null, c, core * 1.7, core * 1.7);
        var ball = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.4, 0.4) };
        ball.GradientStops.Add(new GradientStop(Color.FromArgb(colour.A, (byte)Math.Min(255, colour.R + 70), (byte)Math.Min(255, colour.G + 70), (byte)Math.Min(255, colour.B + 70)), 0));
        ball.GradientStops.Add(new GradientStop(colour, 0.6));
        ball.GradientStops.Add(new GradientStop(Color.FromArgb(colour.A, (byte)(colour.R * 0.6), (byte)(colour.G * 0.6), (byte)(colour.B * 0.6)), 1));
        dc.DrawEllipse(ball, null, c, core, core);
    }
}

/// <summary>A ring filled to a fraction (the context window), with a value in the middle and a caption under it.</summary>
internal sealed class RingGauge : FrameworkElement
{
    private double fraction;
    private string value = "–", caption = "";

    public void Set(double fraction, string value, string caption)
    {
        this.fraction = Math.Clamp(fraction, 0, 1);
        this.value = value;
        this.caption = caption;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, r = Math.Min(w, h) / 2 - 6;
        if (r < 8) return;
        var c = new Point(w / 2, h / 2);
        dc.DrawEllipse(null, new Pen(Palette.Theme(this, "ControlBrush"), 8), c, r, r);
        var colour = fraction < 0.6 ? Palette.Of(SessionState.Idle) : fraction < 0.85 ? Palette.Of(SessionState.Waiting) : Palette.Of(SessionState.Error);
        if (fraction > 0.001)
        {
            double a0 = -Math.PI / 2, a1 = a0 + fraction * 2 * Math.PI - 0.0001;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false, false);
                ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(Palette.Brush(colour), 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        }
        var big = Palette.Text(this, value, Math.Max(11, r / 2.6), Palette.Theme(this, "TextBrush"), bold: true);
        dc.DrawText(big, new Point(c.X - big.Width / 2, c.Y - big.Height / 2 - 5));
        var small = Palette.Text(this, caption, 11.5, Palette.Theme(this, "TextSecondaryBrush"));
        dc.DrawText(small, new Point(c.X - small.Width / 2, c.Y + big.Height / 2 - 6));
    }
}

/// <summary>A labelled bar: a value as a fraction (plan usage), or a stack of parts (the tokens by kind).</summary>
internal sealed class Meter : StackPanel
{
    private readonly TextBlock label = new() { FontSize = 13 };
    private readonly TextBlock value = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid bar = new() { Height = 7, Margin = new Thickness(0, 3, 0, 0) };

    public Meter(string name)
    {
        label.Text = name;
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var head = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        head.Children.Add(value);
        head.Children.Add(label);
        Children.Add(head);
        var back = new Border { CornerRadius = new CornerRadius(3.5) };
        back.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        bar.Children.Add(back);
        Children.Add(bar);
        Margin = new Thickness(0, 0, 0, 10);
    }

    /// <summary>Parts (fraction of the bar, colour, what it is), and the text at the right.</summary>
    public void Set(IReadOnlyList<(double Fraction, Color Colour, string Tip)> parts, string text, string? tip = null)
    {
        value.Text = text;
        ToolTip = tip;
        while (bar.Children.Count > 1) bar.Children.RemoveAt(1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true };
        bar.Children.Add(row);
        bar.SizeChanged -= Resize;
        bar.SizeChanged += Resize;
        this.parts = parts;
        Resize(null, null);
    }

    private IReadOnlyList<(double Fraction, Color Colour, string Tip)> parts = Array.Empty<(double, Color, string)>();

    private void Resize(object? sender, SizeChangedEventArgs? e)
    {
        if (bar.Children.Count < 2 || bar.Children[1] is not StackPanel row) return;
        row.Children.Clear();
        double width = bar.ActualWidth;
        for (int i = 0; i < parts.Count; i++)
        {
            var (f, colour, tip) = parts[i];
            if (f <= 0) continue;
            row.Children.Add(new Border
            {
                Width = Math.Max(2, width * Math.Clamp(f, 0, 1)),
                Background = Palette.Brush(colour),
                ToolTip = tip,
                CornerRadius = new CornerRadius(i == 0 ? 3.5 : 0, 0, 0, i == 0 ? 3.5 : 0),
            });
        }
    }
}

/// <summary>
/// The activity ribbon: the turn's steps as coloured blocks, oldest first, each as wide as it took (thinking purple,
/// writing blue, then each tool in its kind's colour), the current one growing as it goes. Point at one for what it was.
/// </summary>
internal sealed class ActivityRibbon : WrapPanel
{
    private sealed class Step
    {
        public required Border Block;
        public required DateTime Start;
        public DateTime? End;
        public required string Text;
        public required string Group;
        public required Color Colour;
    }

    private readonly List<Step> steps = new();
    /// <summary>The turn's time so far by kind of step (thinking, writing, reading, editing, running…), in the order they came.</summary>
    private readonly List<(string Group, Color Colour, double Seconds)> totals = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public ActivityRibbon()
    {
        timer.Tick += (_, _) => Grow();
        Margin = new Thickness(0, 4, 0, 0);
    }

    public void Clear()
    {
        steps.Clear();
        totals.Clear();
        Children.Clear();
        timer.Stop();
    }

    /// <summary>A new step (the one before it ends here).</summary>
    public void Begin(Color colour, string text, string? group = null)
    {
        if (steps.Count > 0 && steps[^1].End == null && steps[^1].Text == text) return;
        End();
        var block = new Border { Height = 14, Width = 4, Margin = new Thickness(0, 0, 2, 3), CornerRadius = new CornerRadius(3), Background = Palette.Brush(colour), ToolTip = text };
        steps.Add(new Step { Block = block, Start = DateTime.UtcNow, Text = text, Group = group ?? text, Colour = colour });
        Children.Add(block);
        if (steps.Count > 150) { Children.RemoveAt(0); steps.RemoveAt(0); }
        timer.Start();
    }

    /// <summary>The current step is over (the turn ended, or another began).</summary>
    public void End()
    {
        if (steps.Count == 0 || steps[^1].End != null) return;
        var s = steps[^1];
        s.End = DateTime.UtcNow;
        Add(s.Group, s.Colour, (s.End.Value - s.Start).TotalSeconds);
        Size(s);
        s.Block.ToolTip = F("{0} ({1:0.0} s)", s.Text, (s.End.Value - s.Start).TotalSeconds);
        timer.Stop();
    }

    private void Add(string group, Color colour, double seconds)
    {
        int i = totals.FindIndex(t => t.Group == group);
        if (i < 0) totals.Add((group, colour, seconds));
        else totals[i] = (group, colour, totals[i].Seconds + seconds);
    }

    /// <summary>How long the turn has spent on each kind of step (the one going on now too).</summary>
    public List<(string Group, Color Colour, double Seconds)> Summary()
    {
        var list = totals.ToList();
        if (steps.Count > 0 && steps[^1] is { End: null } now)
        {
            double seconds = (DateTime.UtcNow - now.Start).TotalSeconds;
            int i = list.FindIndex(t => t.Group == now.Group);
            if (i < 0) list.Add((now.Group, now.Colour, seconds)); else list[i] = (now.Group, now.Colour, list[i].Seconds + seconds);
        }
        return list;
    }

    private void Grow() { if (steps.Count > 0 && steps[^1].End == null) Size(steps[^1]); }

    // 4 px, and 6 a second more, up to a fifth of the width
    private void Size(Step s) => s.Block.Width = Math.Min(Math.Max(60, ActualWidth / 5), 4 + 6 * ((s.End ?? DateTime.UtcNow) - s.Start).TotalSeconds);
}
