using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Your plan's usage at the top of the Claude Code tab: a line with each window (how much is used, and when it resets,
/// counting down), and, opened, the details: each window's bar (with how far through it you are in time), the pace and
/// when it would reach the limit, the last day's history as a chart, what's been using it, and the warnings' settings.
/// </summary>
internal sealed class UsagePanel : StackPanel
{
    private readonly PlanUsage usage;
    private readonly AiSettings settings;
    private readonly WrapPanel strip = new();
    private readonly Button toggle = new() { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 6, 0) };
    private readonly DockPanel row = new();

    /// <summary>Usage details / Hide usage: put in the Claude Code box (AiView), not on the line.</summary>
    public Button Toggle => toggle;
    private readonly Border details = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel windows = new();
    private readonly TextBlock headline = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
    private readonly TextBlock breakdown = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Margin = new Thickness(0, 4, 0, 0) };
    private readonly UsageChart chart;

    /// <summary>The problem, when there are no meters to show (for the Claude Code box).</summary>
    public string? Problem => usage.Windows.Count == 0 ? usage.Problem : null;

    public UsagePanel(PlanUsage usage, AiSettings settings)
    {
        this.usage = usage;
        this.settings = settings;
        chart = new UsageChart(usage, settings) { Height = 190, Margin = new Thickness(0, 6, 0, 0) };
        Margin = new Thickness(0, 0, 0, 10);

        row.Children.Add(strip);
        Children.Add(row);
        toggle.Click += (_, _) =>
        {
            settings.Usage.Open = details.Visibility != Visibility.Visible;
            settings.Save();
            Show();
        };

        details.SetResourceReference(StyleProperty, "Card");
        var d = new StackPanel();
        var head = new DockPanel();
        var check = new Button { Content = T("Check now"), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Top, ToolTip = T("Ask Claude Code now (this uses none of your plan)") };
        check.Click += async (_, _) => { check.IsEnabled = false; await usage.CheckAsync(); check.IsEnabled = true; };
        DockPanel.SetDock(check, Dock.Right);
        head.Children.Add(check);
        var title = new TextBlock { Text = T("Your plan's usage") };
        title.SetResourceReference(StyleProperty, "CardHeader");
        var titles = new StackPanel();
        titles.Children.Add(title);
        headline.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        titles.Children.Add(headline);
        head.Children.Add(titles);
        d.Children.Add(head);
        d.Children.Add(windows);
        d.Children.Add(Section(T("The last day")));
        d.Children.Add(chart);
        d.Children.Add(Section(T("What's been using it")));
        breakdown.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        d.Children.Add(breakdown);
        d.Children.Add(Section(T("Warnings")));
        d.Children.Add(Warnings());
        // (closing it at the bottom too, where you end up after reading it)
        var hide = new Button { Content = T("Hide details"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        hide.Click += (_, _) =>
        {
            settings.Usage.Open = false;
            settings.Save();
            Show();
        };
        d.Children.Add(hide);
        details.Child = d;
        Children.Add(details);

        usage.Changed += Show;
        Show();
    }

    private void Show()
    {
        bool open = settings.Usage.Open;
        details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        toggle.Content = open ? T("Hide usage") : T("Usage details");

        // the line
        strip.Children.Clear();
        if (usage.Windows.Count == 0)
            strip.Children.Add(Small(usage.Problem ?? T("Checking your plan's usage…"), secondary: true));
        foreach (var w in usage.Windows.Take(3))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(Small(w.Label, secondary: true));
            item.Children.Add(Bar(w, 110, 7, new Thickness(8, 0, 8, 0)));
            var pct = Small(F("{0:0}%", w.Used * 100));
            pct.FontWeight = FontWeights.SemiBold;
            pct.Foreground = Palette.Brush(Colour(w.Used));
            item.Children.Add(pct);
            if (w.Resets is { } r) item.Children.Add(Small("  " + F("resets {0}", PlanUsage.Until(r)), secondary: true));
            item.ToolTip = w.Resets is { } rr ? F("{0}: {1:0}% used, resets {2}", w.Label, w.Used * 100, PlanUsage.When(rr)) : w.Label;
            strip.Children.Add(item);
        }
        if (usage.Blocked)
        {
            var out_ = Small(usage.BlockedUntil is { } b ? F("Limit reached: back {0}", PlanUsage.When(b)) : T("Limit reached"));
            out_.Foreground = Palette.Brush(Palette.Of(SessionState.Error));
            out_.FontWeight = FontWeights.SemiBold;
            strip.Children.Insert(0, out_);
        }
        // the line only with something to show (a problem alone is in the Claude Code box)
        row.Visibility = usage.Windows.Count > 0 || usage.Blocked ? Visibility.Visible : Visibility.Collapsed;
        Visibility = row.Visibility == Visibility.Visible || open ? Visibility.Visible : Visibility.Collapsed;
        if (!open) return;

        // the details
        headline.Text = string.Join("  ·  ", new[]
        {
            usage.Plan,
            usage.Checked is { } c ? F("checked {0}", Ago(c)) : null,
            usage.Problem,
        }.Where(s => !string.IsNullOrEmpty(s)));
        windows.Children.Clear();
        foreach (var w in usage.Windows) windows.Children.Add(WindowBlock(w));
        breakdown.Text = usage.Breakdown is { Length: > 0 } t ? t : T("Claude Code says what's been using it when it's asked (Check now).");
        chart.InvalidateVisual();
    }

    private FrameworkElement WindowBlock(PlanUsage.Window w)
    {
        var s = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var head = new DockPanel();
        var pct = new TextBlock { Text = F("{0:0}% used", w.Used * 100), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Palette.Brush(Colour(w.Used)) };
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        head.Children.Add(new TextBlock { Text = w.Label, FontSize = 15, FontWeight = FontWeights.SemiBold });
        s.Children.Add(head);
        s.Children.Add(Bar(w, double.NaN, 12, new Thickness(0, 6, 0, 6), timeMark: true));
        var lines = new List<string>();
        if (w.Resets is { } r)
        {
            lines.Add(F("Resets {0} ({1}).", PlanUsage.When(r), PlanUsage.Until(r)));
            double through = 1 - Math.Clamp((r - DateTime.Now).TotalSeconds / w.Length.TotalSeconds, 0, 1);
            lines.Add(w.Used > through + 0.05
                ? F("You're {0:0}% of the way through it in time and have used {1:0}%: faster than it lasts.", through * 100, w.Used * 100)
                : F("You're {0:0}% of the way through it in time and have used {1:0}%: it should last.", through * 100, w.Used * 100));
        }
        var (perHour, full, atReset) = usage.Pace(w);
        if (w.Used >= 1) lines.Add(T("It's used up: Claude Code refuses requests until it resets."));
        else if (full is { } f)
            lines.Add(F("At this pace (+{0:0.#}% an hour) it reaches 100% around {1:t}, {2} before it resets.", perHour * 100, f,
                w.Resets is { } r2 ? Span(r2 - f) : "?"));
        else if (perHour > 0.0005 && w.Resets != null)
            lines.Add(F("At this pace (+{0:0.#}% an hour) it'll be at about {1:0}% when it resets.", perHour * 100, atReset * 100));
        else lines.Add(T("It hasn't gone up lately."));
        foreach (var l in lines)
        {
            var t = new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 1, 0, 1) };
            if (l == lines[^1] && full != null) t.Foreground = Palette.Brush(Palette.Of(SessionState.Waiting));
            else t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            s.Children.Add(t);
        }
        return s;
    }

    private UIElement Warnings()
    {
        var u = settings.Usage;
        var s = new StackPanel();
        CheckBox Box(string text, bool on, Action<bool> set, string? tip = null)
        {
            var b = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Margin = new Thickness(0, 6, 0, 0), ToolTip = tip };
            b.Click += (_, _) => { set(b.IsChecked == true); settings.Save(); };
            return b;
        }
        ComboBox Combo(IEnumerable<int> values, int current, string format, Action<int> set)
        {
            var c = new ComboBox { Width = 80, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (int v in values) c.Items.Add(new ComboBoxItem { Content = string.Format(format, v), Tag = v });
            c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == current) ?? c.Items[0];
            c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: int v }) { set(v); settings.Save(); } };
            return c;
        }
        s.Children.Add(Box(T("Notify me (Windows notifications)"), u.Notify, v => u.Notify = v));
        var levels = new WrapPanel { Margin = new Thickness(26, 4, 0, 0) };
        levels.Children.Add(Small(T("when a window passes"), secondary: true));
        levels.Children.Add(Combo(new[] { 50, 60, 70, 75, 80, 85, 90 }, u.WarnAt, "{0}%", v => u.WarnAt = v));
        levels.Children.Add(Small(T("and again at"), secondary: true));
        levels.Children.Add(Combo(new[] { 85, 90, 95, 98 }, u.UrgentAt, "{0}%", v => u.UrgentAt = v));
        s.Children.Add(levels);
        var sub = new StackPanel { Margin = new Thickness(26, 0, 0, 0) };
        sub.Children.Add(Box(T("when the pace will reach the limit within the hour"), u.NotifyForecast, v => u.NotifyForecast = v));
        sub.Children.Add(Box(T("when a window you were close on resets (and when the limit's reached)"), u.NotifyReset, v => u.NotifyReset = v));
        s.Children.Add(sub);
        s.Children.Add(Box(T("Show a bubble in the title bar when close (amber) or out (red), while Claude is working"), u.TitleBadge, v => u.TitleBadge = v));
        var every = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        every.Children.Add(Small(T("Check every"), secondary: true));
        every.Children.Add(Combo(new[] { 5, 10, 15, 30, 60 }, u.CheckMinutes, "{0} min", v => u.CheckMinutes = v));
        every.Children.Add(Small(T("(every 3 minutes when you're close; it uses none of your plan)"), secondary: true));
        s.Children.Add(every);
        return s;
    }

    // ---------------------------------------------------------------- helpers

    public static Color Colour(double used) => used < 0.6 ? Palette.Of(SessionState.Idle) : used < 0.85 ? Palette.Of(SessionState.Waiting) : Palette.Of(SessionState.Error);

    /// <summary>A bar filled to the window's use; with <paramref name="timeMark"/>, a line where the time through it is.</summary>
    private static FrameworkElement Bar(PlanUsage.Window w, double width, double height, Thickness margin, bool timeMark = false)
    {
        var grid = new Grid { Height = height, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        if (!double.IsNaN(width)) grid.Width = width;
        var back = new Border { CornerRadius = new CornerRadius(height / 2) };
        back.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        grid.Children.Add(back);
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = Palette.Brush(Colour(w.Used)), HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(fill);
        Canvas? marks = null;
        if (timeMark && w.Resets is { } r)
        {
            marks = new Canvas();
            grid.Children.Add(marks);
        }
        grid.SizeChanged += (_, _) =>
        {
            fill.Width = Math.Max(height, grid.ActualWidth * Math.Clamp(w.Used, 0, 1));
            if (marks != null && w.Resets is { } reset)
            {
                marks.Children.Clear();
                double through = 1 - Math.Clamp((reset - DateTime.Now).TotalSeconds / w.Length.TotalSeconds, 0, 1);
                var line = new Border { Width = 2, Height = height + 6, ToolTip = T("How far through the window you are in time") };
                line.SetResourceReference(Border.BackgroundProperty, "TextBrush");
                Canvas.SetLeft(line, grid.ActualWidth * through - 1);
                Canvas.SetTop(line, -3);
                marks.Children.Add(line);
            }
        };
        return grid;
    }

    private static TextBlock Small(string text, bool secondary = false)
    {
        var t = new TextBlock { Text = text, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
        if (secondary) t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return t;
    }

    private static TextBlock Section(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) };
        return t;
    }

    private static string Ago(DateTime t)
    {
        var a = DateTime.Now - t;
        return a.TotalMinutes < 1 ? T("just now") : a.TotalHours < 1 ? F("{0} min ago", (int)a.TotalMinutes) : F("at {0:t}", t);
    }

    private static string Span(TimeSpan s) => s.TotalHours >= 1 ? F("{0} h {1} min", (int)s.TotalHours, s.Minutes) : F("{0} min", Math.Max(1, (int)s.TotalMinutes));
}

/// <summary>
/// The last day of this 5 hours' (blue) and this week's (violet) usage, with the warning level (amber dashed), now,
/// where the 5-hour window resets, and the pace carried on from now to then (dashed).
/// </summary>
internal sealed class UsageChart : FrameworkElement
{
    private readonly PlanUsage usage;
    private readonly AiSettings settings;
    private static readonly Color SessionColour = Color.FromRgb(0x4C, 0xC2, 0xFF), WeekColour = Color.FromRgb(0xA3, 0x71, 0xF7);

    public UsageChart(PlanUsage usage, AiSettings settings)
    {
        this.usage = usage;
        this.settings = settings;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var plot = new Rect(38, 8, Math.Max(10, w - 46), Math.Max(10, h - 30));
        var dim = Palette.Theme(this, "TextSecondaryBrush");
        var room = Palette.Theme(this, "RoomBrush");
        dc.DrawRectangle(room, null, plot);
        var now = DateTime.Now;
        var from = now.AddHours(-24);
        var to = usage.Session?.Resets is { } r && r > now && r < now.AddHours(6) ? r.AddMinutes(20) : now.AddHours(1);
        double X(DateTime t) => plot.Left + (t - from).TotalSeconds / (to - from).TotalSeconds * plot.Width;
        double Y(double v) => plot.Bottom - Math.Clamp(v, 0, 1) * plot.Height;
        var grid = new Pen(dim, 1) { DashStyle = null };
        grid.Brush = dim.CloneCurrentValue();
        grid.Brush.Opacity = 0.2;
        for (int q = 0; q <= 4; q++)
        {
            double y = plot.Top + plot.Height * q / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawText(Palette.Text(this, (100 - q * 25) + "%", 11, dim), new Point(2, y - 8));
        }
        for (var t = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0).AddHours(1); t < to; t = t.AddHours(1))
        {
            if (t.Hour % 4 != 0) continue;
            double x = X(t);
            dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Palette.Text(this, t.ToString("t"), 11, dim);
            dc.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 3));
        }
        // the warning level
        var warn = new Pen(Palette.Brush(Palette.Of(SessionState.Waiting), 0xA0), 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(warn, new Point(plot.Left, Y(settings.Usage.WarnAt / 100.0)), new Point(plot.Right, Y(settings.Usage.WarnAt / 100.0)));

        dc.PushClip(new RectangleGeometry(plot));
        var samples = settings.Usage.History.Where(s => s.At >= from.AddMinutes(-30)).ToList();
        void Line(Func<UsageSample, double> of, Color colour)
        {
            if (samples.Count < 1) return;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(X(samples[0].At), Y(of(samples[0]))), false, false);
                for (int i = 1; i < samples.Count; i++)
                {
                    // a step: the reading holds until the next one (a reset drops straight down)
                    ctx.LineTo(new Point(X(samples[i].At), Y(of(samples[i - 1]))), true, false);
                    ctx.LineTo(new Point(X(samples[i].At), Y(of(samples[i]))), true, false);
                }
                ctx.LineTo(new Point(X(now), Y(of(samples[^1]))), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(Palette.Brush(colour), 2) { LineJoin = PenLineJoin.Round }, g);
        }
        Line(s => s.Week, WeekColour);
        Line(s => s.Session, SessionColour);
        // the pace, carried on to the reset (or to 100%)
        if (usage.Session is { } session && session.Resets is { } reset && reset > now)
        {
            var (perHour, full, atReset) = usage.Pace(session);
            if (perHour > 0.0005)
            {
                var end = full ?? reset;
                double endValue = full != null ? 1 : atReset;
                var pace = new Pen(Palette.Brush(SessionColour, 0xC0), 2) { DashStyle = DashStyles.Dash };
                dc.DrawLine(pace, new Point(X(now), Y(session.Used)), new Point(X(end), Y(endValue)));
            }
            double rx = X(reset);
            var resetPen = new Pen(Palette.Brush(Palette.Of(SessionState.Idle)), 1.5) { DashStyle = DashStyles.Dot };
            dc.DrawLine(resetPen, new Point(rx, plot.Top), new Point(rx, plot.Bottom));
            var label = Palette.Text(this, F("resets {0:t}", reset), 11, Palette.Brush(Palette.Of(SessionState.Idle)));
            dc.DrawText(label, new Point(Math.Min(rx + 3, plot.Right - label.Width - 2), plot.Top + 2));
        }
        // now
        dc.DrawLine(new Pen(Palette.Theme(this, "TextBrush"), 1), new Point(X(now), plot.Top), new Point(X(now), plot.Bottom));
        dc.Pop();

        // the legend
        double lx = plot.Left + 8;
        foreach (var (name, colour) in new[] { (T("This 5 hours"), SessionColour), (T("This week"), WeekColour) })
        {
            dc.DrawRectangle(Palette.Brush(colour), null, new Rect(lx, plot.Top + 8, 10, 3));
            var t = Palette.Text(this, name, 11, dim);
            dc.DrawText(t, new Point(lx + 14, plot.Top + 1));
            lx += 14 + t.Width + 14;
        }
        if (samples.Count == 0)
        {
            var t = Palette.Text(this, T("The history builds up as the app checks."), 12, dim);
            dc.DrawText(t, new Point(plot.Left + (plot.Width - t.Width) / 2, plot.Top + plot.Height / 2 - 8));
        }
    }
}
