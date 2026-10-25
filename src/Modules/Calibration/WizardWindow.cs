using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Calibration;

/// <summary>
/// A controller's axes one at a time (those not ignored): move it end to end and the lowest and highest readings are
/// its min and max, with the centre halfway (a straight line). Reset starts the axis again, Skip leaves it as it is
/// this time, Ignore leaves it out from now on (its window's Ignore in Wizard brings it back). Then the summary:
/// Save gives Windows every axis found.
/// </summary>
internal sealed class WizardWindow : Window
{
    private enum State { Waiting, Done, Skipped, Ignored }

    private sealed class Step
    {
        public required Axis Axis { get; init; }
        public required RangeFinder Finder { get; init; }
        public required string Key { get; init; }
        public State State { get; set; }
    }

    private static readonly Brush Amber = Frozen(Color.FromRgb(0xD2, 0x99, 0x22));

    private readonly Live live;
    private readonly CalibrationSettings settings;
    private readonly List<Step> steps;
    private readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
    private readonly ContentControl body = new();
    private int at;

    // the step showing: its live pieces
    private AxisBar? bar;
    private CalGraph? graph;
    private TextBlock? numbers, lightText;
    private Ellipse? light;
    private Button? next;

    public WizardWindow(Live live, CalibrationSettings settings)
    {
        this.live = live;
        this.settings = settings;
        var info = live.Info!;
        steps = live.Device!.Axes
            .Select(a => new Step { Axis = a, Finder = new RangeFinder(a), Key = CalibrationSettings.KeyOf(info, a) })
            .Where(s => !settings.Ignored.Contains(s.Key))
            .ToList();

        Title = F("Wizard · {0}", info.Name);
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var page = new StackPanel { Margin = new Thickness(16) };
        page.Children.Add(dots);
        page.Children.Add(body);
        Content = page;

        live.Ticked += Tick;
        Closed += (_, _) => live.Ticked -= Tick;
        Go(0);
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void Go(int index)
    {
        at = Math.Clamp(index, 0, steps.Count);
        ShowDots();
        bar = null;
        graph = null;
        if (steps.Count == 0) ShowNone();
        else if (at == steps.Count) ShowSummary();
        else ShowStep(steps[at]);
        Tick();
    }

    /// <summary>A numbered dot per axis (green found, struck through ignored, hollow skipped, ringed the one showing), then the summary's.</summary>
    private void ShowDots()
    {
        dots.Children.Clear();
        for (int i = 0; i <= steps.Count; i++)
        {
            bool summary = i == steps.Count;
            var state = summary ? State.Waiting : steps[i].State;
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(2) };
            var text = new TextBlock { Text = summary ? "✓" : (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeights.SemiBold };
            dot.Child = text;
            switch (state)
            {
                case State.Done:
                    dot.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
                    dot.SetResourceReference(Border.BorderBrushProperty, "SuccessBrush");
                    text.Foreground = Brushes.White;
                    break;
                case State.Ignored:
                    dot.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                    text.TextDecorations = TextDecorations.Strikethrough;
                    break;
                case State.Skipped:
                    dot.SetResourceReference(Border.BorderBrushProperty, "TextSecondaryBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                    break;
                default:
                    dot.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
                    dot.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
                    break;
            }
            if (i == at) dot.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            dot.ToolTip = summary ? T("Save") : steps[i].Axis.Name + state switch
            {
                State.Done => " · " + T("Found"),
                State.Skipped => " · " + T("Skipped"),
                State.Ignored => " · " + T("Ignored"),
                _ => "",
            };
            dots.Children.Add(dot);
        }
    }

    private void ShowNone()
    {
        var s = new StackPanel();
        s.Children.Add(CalibrationView.Secondary(T("Every axis is ignored")));
        s.Children.Add(Buttons(left: Array.Empty<Button>(), right: new[] { Btn(T("Close"), Close) }));
        body.Content = s;
    }

    private void ShowStep(Step step)
    {
        var s = new StackPanel();
        var head = CalibrationView.Header(step.Axis.Name);
        head.Margin = new Thickness(0);
        s.Children.Add(head);
        s.Children.Add(CalibrationView.Secondary(F("Axis {0} of {1}", at + 1, steps.Count)));
        s.Children.Add(new TextBlock { Text = T("Move it all the way, both ways"), Margin = new Thickness(0, 10, 0, 6), FontSize = 15 });

        bar = new AxisBar { Fill = false, Height = 18 };
        s.Children.Add(bar);
        var line = new DockPanel { Margin = new Thickness(0, 6, 0, 8) };
        var lit = new StackPanel { Orientation = Orientation.Horizontal };
        light = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        lightText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        lit.Children.Add(light);
        lit.Children.Add(lightText);
        DockPanel.SetDock(lit, Dock.Right);
        line.Children.Add(lit);
        numbers = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(numbers);
        s.Children.Add(line);

        graph = new CalGraph { Axis = step.Axis, Height = 200 };
        s.Children.Add(graph);

        next = Btn(T("Next"), () => { step.State = State.Done; Go(at + 1); }, accent: true);
        var reset = Btn(T("Reset"), () => { step.Finder.Reset(); Tick(); }, tip: T("Start this axis again"));
        var ignore = Btn(T("Ignore"), () =>
        {
            step.State = State.Ignored;
            settings.Ignored.Add(step.Key);
            settings.Save();
            Go(at + 1);
        }, tip: T("Leave it out of the wizard from now on"));
        var back = Btn(T("Back"), () => Go(at - 1));
        back.IsEnabled = at > 0;
        var skip = Btn(T("Skip"), () => { if (step.State != State.Ignored) step.State = State.Skipped; Go(at + 1); }, tip: T("Leave it as it is"));
        s.Children.Add(Buttons(new[] { reset, ignore }, new[] { back, skip, next }));
        body.Content = s;
    }

    private void ShowSummary()
    {
        var s = new StackPanel();
        var head = CalibrationView.Header(T("Summary"));
        head.Margin = new Thickness(0, 0, 0, 8);
        s.Children.Add(head);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var dev = live.Device;
        for (int i = 0; i < steps.Count; i++)
        {
            var st = steps[i];
            g.RowDefinitions.Add(new RowDefinition());
            var name = new TextBlock { Text = st.Axis.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 3) };
            string what = st.State switch
            {
                State.Done => $"{(dev != null && live.AxisAt(st.Axis.Offset) is { } a ? dev.Get(a).ToString() : "?")}  →  {st.Finder.Result}",
                State.Ignored => T("Ignored"),
                _ => T("Skipped"),
            };
            var value = new TextBlock { Text = what, Margin = new Thickness(0, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
            if (st.State != State.Done) value.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            g.Children.Add(name);
            g.Children.Add(value);
        }
        s.Children.Add(g);
        var problems = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        problems.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        s.Children.Add(problems);
        var save = Btn(T("Save"), () =>
        {
            var wrong = SaveAll();
            if (wrong.Count == 0) { Close(); return; }
            problems.Text = string.Join("\n", wrong);
            problems.Visibility = Visibility.Visible;
        }, accent: true);
        save.IsEnabled = dev != null && steps.Any(x => x.State == State.Done);
        s.Children.Add(Buttons(new[] { Btn(T("Back"), () => Go(at - 1)) }, new[] { save, Btn(T("Close"), Close) }));
        body.Content = s;
    }

    /// <summary>Gives Windows each axis found; the problems (none when it took them all).</summary>
    private List<string> SaveAll()
    {
        var wrong = new List<string>();
        if (live.Device is not { } dev) { wrong.Add(T("Not connected")); return wrong; }
        foreach (var st in steps.Where(x => x.State == State.Done))
        {
            if (live.AxisAt(st.Axis.Offset) is not { } a) continue;
            var before = dev.Get(a);
            if (!settings.Original.ContainsKey(st.Key)) settings.Original[st.Key] = before;
            string? problem = dev.Set(a, st.Finder.Result);
            if (problem == null) Logging.Log.Here.Info($"Calibrated {live.Info?.Name} {a.Name} (wizard): {before} → {st.Finder.Result}");
            else wrong.Add($"{a.Name}: {problem}");
        }
        settings.Save();
        return wrong;
    }

    private void Tick()
    {
        if (bar == null || graph == null || at >= steps.Count) return;
        var step = steps[at];
        var a = live.AxisAt(step.Axis.Offset);
        bool connected = a != null && a.Index < live.Raw.Length;
        int? raw = connected ? live.Raw[a!.Index] : null;
        if (raw is int r) step.Finder.See(r);
        var f = step.Finder;
        double range = Math.Max(1, step.Axis.High - step.Axis.Low);
        double Pos(int v) => (v - step.Axis.Low) / range;
        bar.Value = raw is int rv ? Pos(rv) : double.NaN;
        if (f.Low is int lo && f.High is int hi)
        {
            bar.Span = (Pos(lo), Pos(hi));
            graph.Seen = (lo, hi);
            var c = f.Result;
            bar.Marks = c.Valid ? new[] { Pos(c.Min), Pos(c.Center), Pos(c.Max) } : Array.Empty<double>();
            numbers!.Text = c.Valid ? F("Min {0} · Centre {1} · Max {2}", c.Min, c.Center, c.Max) : "";
            graph.Cal = c.Valid ? c : step.Axis.Default;
        }
        else
        {
            bar.Span = null;
            bar.Marks = Array.Empty<double>();
            graph.Seen = null;
            numbers!.Text = "";
            graph.Cal = step.Axis.Default;
        }
        graph.Live = raw;
        if (!connected) Light("ErrorTextBrush", T("Not connected"));
        else if (f.Found) Light("SuccessBrush", T("Range found"));
        else Light(null, T("Move it"));
        next!.IsEnabled = connected && f.Found;
    }

    private void Light(string? brush, string text)
    {
        if (brush == null) light!.Fill = Amber; else light!.SetResourceReference(Shape.FillProperty, brush);
        if (lightText!.Text != text) lightText.Text = text;
    }

    private static Button Btn(string text, Action click, bool accent = false, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip, MinWidth = 70 };
        if (accent) b.SetResourceReference(StyleProperty, "AccentButton");
        b.Click += (_, _) => click();
        AutomationProperties.SetName(b, text);
        return b;
    }

    private static DockPanel Buttons(Button[] left, Button[] right)
    {
        var d = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var r = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in right) r.Children.Add(b);
        if (r.Children.Count > 0) ((Button)r.Children[^1]).Margin = new Thickness(0);
        DockPanel.SetDock(r, Dock.Right);
        d.Children.Add(r);
        var l = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in left) l.Children.Add(b);
        d.Children.Add(l);
        return d;
    }
}
