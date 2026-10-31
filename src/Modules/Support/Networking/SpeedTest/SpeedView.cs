using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Speed Test: Core's speed test (the one Hardware → Performance's card runs too) with its own
/// schedule and results file: the last result in large figures, orange / red below the levels set, the results over
/// the span picked, and every result in a table. Make Widget on a right-click.
/// </summary>
internal sealed class SpeedView : UserControl, IDisposable
{
    private sealed record ResultRow(DateTime Time, string When, string Down, double DownN, string Up, double UpN, string Latency, double LatencyN, string Data, string Server, string Error);

    private readonly NetworkingSettings settings;
    private readonly SpeedTester speed;
    private readonly CheckBox onBox;
    private readonly Button run;
    private readonly TextBlock logText = Ui.Secondary();
    private readonly TextBlock offText = Ui.Secondary(T("Off: no tests run."), wrap: true);
    private readonly FrameworkElement body;
    private readonly Run down = Big(), up = Big(), latency = Big();
    private readonly TextBlock status = Ui.Secondary(wrap: true);
    private readonly LineGraph graph = new() { Height = 240, Detailed = true, TimeSpans = 4, Margin = new Thickness(0, 12, 0, 0) };
    private readonly DataGrid table = Ui.Table(320);
    private readonly Border card;

    public SpeedView(NetworkingSettings settings, SpeedTester speed, NetWidgets widgets)
    {
        this.settings = settings;
        this.speed = speed;
        onBox = NetUi.OnBox(T("Speed Test"), settings.SpeedOn, T("Untick to stop speed tests completely"));
        onBox.Checked += (_, _) => SetOn(true);
        onBox.Unchecked += (_, _) => SetOn(false);
        run = Ui.Button(T("Run Now"), async () => { if (speed.Running) speed.Cancel(); else await speed.RunNowAsync(); }, accent: true);
        var span = Ui.Choice(new[] { (T("24 h"), 24), (T("7 days"), 168), (T("30 days"), 720) }, settings.SpeedSpanHours,
            h => { settings.SpeedSpanHours = h; settings.Save(); Draw(); }, 90);
        span.ToolTip = T("Time shown");
        logText.VerticalAlignment = VerticalAlignment.Center;
        var header = Ui.HeaderRow(onBox, logText, span, run, NetUi.Gear(T("Schedule and test length"), EditSchedule));

        var figures = new Grid();
        for (int i = 0; i < 3; i++) figures.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        figures.Children.Add(Figure(T("Download"), "AccentBrush", down, " Mbit/s", 0));
        figures.Children.Add(Figure(T("Upload"), NetUi.Orange, up, " Mbit/s", 1));
        figures.Children.Add(Figure(T("Latency"), null, latency, " ms", 2));
        status.Margin = new Thickness(0, 8, 0, 0);
        body = new StackPanel { Children = { figures, status, graph } };
        offText.Margin = new Thickness(0, 0, 0, 8);
        card = Ui.Card(new StackPanel { Children = { header, offText, body } }, new Thickness(0));
        Widget.Offer(card, () => widgets.Create(NetWidgetPlace.SpeedKind, Window.GetWindow(this)));

        table.Columns.Add(Ui.Column(T("Time"), nameof(ResultRow.When), 150, nameof(ResultRow.Time)));
        table.Columns.Add(Ui.Column(T("Download"), nameof(ResultRow.Down), 110, nameof(ResultRow.DownN), right: true));
        table.Columns.Add(Ui.Column(T("Upload"), nameof(ResultRow.Up), 110, nameof(ResultRow.UpN), right: true));
        table.Columns.Add(Ui.Column(T("Latency"), nameof(ResultRow.Latency), 80, nameof(ResultRow.LatencyN), right: true));
        table.Columns.Add(Ui.Column(T("Data"), nameof(ResultRow.Data), 80, right: true));
        table.Columns.Add(NetUi.CopyColumn(T("Server"), nameof(ResultRow.Server), -1));
        NetUi.ItemMenu(table, o => o is ResultRow r ? [r.Server] : []);
        table.Columns.Add(Ui.Column(T("Error"), nameof(ResultRow.Error), -1.2));
        var results = Ui.Card(new StackPanel { Children = { Ui.Header(T("Results")), table } }, new Thickness(0, 12, 0, 0));

        var widgetList = Widget.ListCard();
        widgetList.Margin = new Thickness(0, 12, 0, 0);
        Content = Ui.Page(new StackPanel { Children = { card, results, widgetList } });

        speed.Changed += OnChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) Draw(); };
        Draw();
    }

    /// <summary>The gear: Settings' Speed Test card in a window (how often, for how long, the test's length, the levels).</summary>
    private void EditSchedule()
    {
        var w = Ui.Dialog(T("Speed Test"), this, 452);
        w.ResizeMode = ResizeMode.NoResize;
        var close = Ui.Button(T("Close"), w.Close, accent: true);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.MinWidth = 90;
        close.IsDefault = true;
        w.Content = new StackPanel { Margin = new Thickness(16, 16, 4, 16), Children = { new NetworkingSettingsView(settings, null, speed, null, speedOnly: true), close } };
        w.Closed += (_, _) => Draw();
        w.ShowDialog();
    }

    private static Run Big() => new("—") { FontSize = 26, FontWeight = FontWeights.SemiBold };

    private static FrameworkElement Figure(string name, string? color, Run value, string unit, int column)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        if (color != null)
        {
            var swatch = new Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            if (color.StartsWith('#')) swatch.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            else swatch.SetResourceReference(Shape.FillProperty, color);
            label.Children.Add(swatch);
        }
        label.Children.Add(Ui.Secondary(name));
        var unitRun = new Run(unit) { FontSize = 12 };
        unitRun.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
        var text = new TextBlock();
        text.Inlines.Add(value);
        text.Inlines.Add(unitRun);
        var panel = new StackPanel { Children = { label, text } };
        Grid.SetColumn(panel, column);
        return panel;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(() => { if (IsVisible) Draw(); });

    private void SetOn(bool on)
    {
        if (on == settings.SpeedOn) return;
        settings.SpeedOn = on;
        settings.Save();
        if (!on) speed.Cancel();
        Draw();
    }

    /// <summary>0 fine, 1 orange, 2 red (a level of 0 is off).</summary>
    private static int Level(double v, double warn, double bad, bool higherIsWorse)
    {
        if (double.IsNaN(v)) return 0;
        bool Over(double level) => level > 0 && (higherIsWorse ? v >= level : v < level);
        return Over(bad) ? 2 : Over(warn) ? 1 : 0;
    }

    private void Paint(Run run, int level)
    {
        if (level == 2) run.SetResourceReference(TextElement.ForegroundProperty, "DangerBrush");
        else if (level == 1) run.Foreground = Ui.Brush(this, NetUi.Orange);
        else run.ClearValue(TextElement.ForegroundProperty);
    }

    private static string Number(double v) => double.IsNaN(v) ? "—" : v >= 100 ? $"{v:0}" : $"{v:0.0}";

    private void Draw()
    {
        onBox.IsChecked = settings.SpeedOn;
        body.Opacity = settings.SpeedOn ? 1 : 0.3;
        offText.Visibility = settings.SpeedOn ? Visibility.Collapsed : Visibility.Visible;
        run.IsEnabled = settings.SpeedOn;
        run.Content = speed.Running ? T("Stop") : T("Run Now");
        bool scheduled = ((ISpeedTestSettings)settings).SpeedTestEnabled;
        logText.Text = !scheduled ? ""
            : settings.SpeedLogUntil is DateTime until ? F("Every {0} until {1}", Every(settings.SpeedMinutes), NetUi.When(until))
            : F("Every {0}", Every(settings.SpeedMinutes));

        var results = speed.Results;
        var last = results.LastOrDefault();
        int severity = 0;
        if (speed.Running)
        {
            string live = speed.LiveMbps > 0 ? Number(speed.LiveMbps) : "…";
            if (speed.Phase == "Download") down.Text = live;
            else if (speed.Phase == "Latency") { down.Text = "…"; latency.Text = "…"; }
            up.Text = speed.Phase == "Upload" ? live : "…";
            foreach (var r in new[] { down, up, latency }) Paint(r, 0);
            status.Text = speed.Phase switch
            {
                "Latency" => T("Testing: latency…"),
                "Download" => T("Testing: download…"),
                _ => T("Testing: upload…"),
            };
            status.ClearValue(TextBlock.ForegroundProperty);
        }
        else
        {
            int d = 0, u = 0, l = 0;
            if (last is { Failed: false })
            {
                d = Level(last.DownMbps, settings.SpeedWarnDown, settings.SpeedBadDown, false);
                u = Level(last.UpMbps, settings.SpeedWarnUp, settings.SpeedBadUp, false);
                l = Level(last.PingMs, settings.SpeedWarnLatency, settings.SpeedBadLatency, true);
                severity = Math.Max(d, Math.Max(u, l));
            }
            else if (last is { Failed: true }) severity = 2;
            down.Text = last != null ? Number(last.DownMbps) : "—";
            up.Text = last != null ? Number(last.UpMbps) : "—";
            latency.Text = last is { Failed: false } && !double.IsNaN(last.PingMs) ? last.PingMs.ToString("0") : "—";
            Paint(down, d);
            Paint(up, u);
            Paint(latency, l);
            string next = speed.NextDue is DateTime due ? F("next {0}", NetUi.When(due)) : "";
            status.Text = last == null ? T("No tests yet")
                : last.Failed ? F("Failed {0}: {1}", NetUi.When(last.Time), last.Error)
                : F("Last {0} · {1}", NetUi.When(last.Time), last.Server ?? "");
            if (speed.LastError == T("Cancelled.")) status.Text = T("Cancelled");
            if (next.Length > 0) status.Text += " · " + next;
            if (last is { Failed: true }) status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            else status.ClearValue(TextBlock.ForegroundProperty);
        }
        if (settings.SpeedOn && severity > 0)
        {
            card.BorderBrush = severity == 2 ? (Brush)FindResource("DangerBrush") : Ui.Brush(this, NetUi.Orange);
            card.BorderThickness = new Thickness(2);
        }
        else
        {
            card.ClearValue(Border.BorderBrushProperty);
            card.ClearValue(Border.BorderThicknessProperty);
        }

        var now = DateTime.Now;
        var from = now.AddHours(-settings.SpeedSpanHours);
        var shown = results.Where(r => r.Time >= from).ToList();
        var series = new List<GraphSeries>
        {
            new() { Name = T("Download"), Metric = MbpsUnit.Instance, Points = shown.Select(r => (r.Time, r.DownMbps)).ToList(), Color = "AccentBrush", Fill = true },
            new() { Name = T("Upload"), Metric = MbpsUnit.Instance, Points = shown.Select(r => (r.Time, r.UpMbps)).ToList(), Color = NetUi.Orange, Fill = false },
        };
        double top = shown.SelectMany(r => new[] { r.DownMbps, r.UpMbps }).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
        graph.Show(series, from, now, NetUi.NiceCeiling(Math.Max(top * 1.1, 10)), TimeSpan.FromMinutes(Math.Max(settings.SpeedMinutes, 10) * 2.5));

        if (!speed.Running || table.Items.Count == 0)
            table.ItemsSource = results.AsEnumerable().Reverse().Select(r => new ResultRow(r.Time, r.Time.ToString("g"),
                NetUi.Mbps(r.DownMbps), N(r.DownMbps), NetUi.Mbps(r.UpMbps), N(r.UpMbps), NetUi.Ms(r.PingMs), N(r.PingMs),
                $"{r.MegaBytes:0} MB", r.Server ?? "", r.Error ?? "")).ToList();

        static double N(double v) => double.IsNaN(v) ? -1 : v;
    }

    public static string Every(int minutes) => minutes < 60 ? F("{0} min", minutes) : minutes % 60 == 0 ? F("{0} h", minutes / 60) : F("{0} min", minutes);

    public void Dispose() => speed.Changed -= OnChanged;
}
