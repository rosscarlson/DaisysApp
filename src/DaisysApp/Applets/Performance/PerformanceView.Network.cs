using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Applets.Performance;

/// <summary>One host on the Network tests card.</summary>
public sealed class PingRow(NetHost host, Brush swatch) : INotifyPropertyChanged
{
    private string value = "—", detail = "", tip = "";
    private bool isError;

    public string Address { get; } = host.Address.Trim();
    public string Name { get; } = host.Display;
    public Brush Swatch { get; } = swatch;
    public string Value { get => value; set => Set(ref this.value, value); }
    public bool IsError { get => isError; set => Set(ref isError, value); }
    public string Detail { get => detail; set => Set(ref detail, value); }
    public string Tip { get => tip; set => Set(ref tip, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T v, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, v)) return;
        field = v;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public partial class PerformanceView
{
    private const int PingGraphSeconds = 300;
    private static readonly string[] HostColors = ["AccentBrush", "#7FD07A", "#F7A541", "#B48EF0", "#FF7B72", "#F2CC60", "#E58AD8", "#5FD3C6"];
    private static readonly MetricInfo PingMetric = new(default, "ping", "Response time", "ms", "0", null);
    private static readonly MetricInfo SpeedMetric = new(default, "speed", "Speed", "Mbit/s", "0", null);

    private PingMonitor? pings;
    private SpeedTester? speed;
    private List<PingRow> pingRows = new();
    private string hostsKey = "";

    private void InitNetwork(PingMonitor pingMonitor, SpeedTester speedTester)
    {
        pings = pingMonitor;
        speed = speedTester;
        pings.Updated += OnPings;
        speed.Changed += OnSpeed;
        IsVisibleChanged += (_, _) => { if (IsVisible) { UpdatePings(); UpdateSpeed(); } };
        slowTimer.Tick += (_, _) => UpdateSpeed(); // the "next test" time
        SidePanel.SizeChanged += (_, _) => MatchProcessHeight();
        UpdatePings();
        UpdateSpeed();
    }

    /// <summary>
    /// The process list grows to the height of the cards beside it (Storage, Network tests, Speed test, System), so
    /// the two columns end together instead of leaving a gap in the process card.
    /// </summary>
    private void MatchProcessHeight()
    {
        double other = ProcessCard.DesiredSize.Height - ProcessGrid.Height;
        if (other <= 0 || double.IsNaN(ProcessGrid.Height)) return;
        double want = Math.Max(430, Math.Floor(SidePanel.ActualHeight - other));
        if (Math.Abs(want - ProcessGrid.Height) >= 1) ProcessGrid.Height = want;
    }

    private void ShutdownNetwork()
    {
        if (pings != null) pings.Updated -= OnPings;
        if (speed != null) speed.Changed -= OnSpeed;
    }

    private Brush BrushOf(string key) => key.StartsWith('#')
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(key))
        : (Brush)FindResource(key);

    // ---------------------------------------------------------------- pings

    private void OnPings() => Dispatcher.BeginInvoke(() => { if (IsVisible) UpdatePings(); });

    private void NetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (new NetSettingsWindow(settings) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
        {
            UpdatePings();
            UpdateSpeed();
        }
    }

    // ---------------------------------------------------------------- on / off

    // Checked / Unchecked (not Click), so the keyboard and UI Automation work too; ShowOn setting the box to the saved
    // value changes nothing here
    private void NetOn_Click(object sender, RoutedEventArgs e)
    {
        bool on = NetOnBox.IsChecked == true;
        if (on == settings.NetTestsOn) return;
        settings.NetTestsOn = on;
        settings.Save();
        UpdatePings();
    }

    private void SpeedOn_Click(object sender, RoutedEventArgs e)
    {
        bool on = SpeedOnBox.IsChecked == true;
        if (on == settings.SpeedTestOn) return;
        settings.SpeedTestOn = on;
        settings.Save();
        if (!settings.SpeedTestOn) speed?.Cancel(); // stops one that's running
        UpdateSpeed();
    }

    /// <summary>A card that's off: its contents dimmed and inert, and a line saying so.</summary>
    private static void ShowOn(bool on, CheckBox box, FrameworkElement body, FrameworkElement offText)
    {
        box.IsChecked = on;
        body.Opacity = on ? 1 : 0.3;
        body.IsEnabled = on;
        offText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdatePings()
    {
        if (pings == null) return;
        ShowOn(settings.NetTestsOn, NetOnBox, NetBody, NetOffText);
        var hosts = settings.NetHosts.Where(h => h.Address.Trim().Length > 0).ToList();
        string key = string.Join("|", hosts.Select(h => h.Display + "=" + h.Address.Trim()));
        if (key != hostsKey)
        {
            hostsKey = key;
            pingRows = hosts.Select((h, i) => new PingRow(h, BrushOf(HostColors[i % HostColors.Length]))).ToList();
            PingList.ItemsSource = pingRows;
            NoHosts.Visibility = pingRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        var now = DateTime.Now;
        if (!settings.NetTestsOn)
        {
            foreach (var row in pingRows) { row.Value = "—"; row.IsError = false; row.Detail = row.Address; row.Tip = ""; }
            PingGraph.Show(new List<GraphSeries>(), now.AddSeconds(-PingGraphSeconds), now, 10, TimeSpan.FromSeconds(3));
            PingCaption.Text = "";
            return;
        }
        var series = new List<GraphSeries>();
        double top = 0;
        for (int i = 0; i < pingRows.Count; i++)
        {
            var row = pingRows[i];
            var points = pings.Points(row.Address, now.AddSeconds(-PingGraphSeconds));
            series.Add(new GraphSeries { Name = row.Name, Metric = PingMetric, Points = points, Color = HostColors[i % HostColors.Length], Fill = false });

            var minute = points.Where(p => p.T >= now.AddSeconds(-60)).ToList();
            var answered = minute.Where(p => !double.IsNaN(p.V)).Select(p => p.V).ToList();
            double last = points.Count > 0 ? points[^1].V : double.NaN;
            string? error = pings.Error(row.Address);
            if (points.Count == 0) { row.Value = "…"; row.IsError = false; }
            else if (double.IsNaN(last)) { row.Value = error ?? "no answer"; row.IsError = true; }
            else { row.Value = Ms(last); row.IsError = false; }
            double loss = minute.Count > 0 ? 100.0 * (minute.Count - answered.Count) / minute.Count : 0;
            row.Detail = answered.Count == 0
                ? row.Address
                : $"{row.Address} · avg {answered.Average():0} · max {answered.Max():0}" + (loss >= 0.5 ? $" · {loss:0}% lost" : "");
            row.Tip = $"{row.Name} ({row.Address}). Over the last minute: " + (answered.Count == 0
                ? "no answers."
                : $"average {Ms(answered.Average())}, best {Ms(answered.Min())}, worst {Ms(answered.Max())}, {loss:0}% unanswered.");
            if (points.Count > 0) top = Math.Max(top, points.Where(p => !double.IsNaN(p.V)).Select(p => p.V).DefaultIfEmpty(0).Max());
        }
        double max = NiceCeiling(Math.Max(top * 1.15, 10));
        PingGraph.Show(series, now.AddSeconds(-PingGraphSeconds), now, max, TimeSpan.FromSeconds(3));
        PingCaption.Text = pingRows.Count == 0 ? "" : $"Last 5 minutes · top of the graph {max:0} ms · a gap is no answer";
    }

    private static string Ms(double v) => v < 10 ? $"{v:0.#} ms" : $"{v:0} ms";

    /// <summary>1, 2, 2.5 or 5 × a power of ten, at or above <paramref name="v"/>.</summary>
    private static double NiceCeiling(double v)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1, 2, 2.5, 5, 10 }) if (m * p >= v) return m * p;
        return 10 * p;
    }

    // ---------------------------------------------------------------- speed test

    private void OnSpeed() => Dispatcher.BeginInvoke(() => { if (IsVisible) UpdateSpeed(); });

    private async void SpeedRun_Click(object sender, RoutedEventArgs e)
    {
        if (speed == null) return;
        if (speed.Running) speed.Cancel();
        else await speed.RunNowAsync();
    }

    private void SpeedSettings_Click(object sender, RoutedEventArgs e)
    {
        var lastGood = speed?.Results.LastOrDefault(r => !r.Failed);
        if (new SpeedSettingsWindow(settings, lastGood) { Owner = Window.GetWindow(this) }.ShowDialog() == true) UpdateSpeed();
    }

    private static string Mbps(double v) => double.IsNaN(v) ? "—" : v >= 100 ? $"{v:0} Mbit/s" : $"{v:0.0} Mbit/s";

    /// <summary>0 = fine, 1 = orange, 2 = red, for a speed below (or a latency above) its levels; a level of 0 is off.</summary>
    private static int Level(double v, double warn, double bad, bool higherIsWorse)
    {
        if (double.IsNaN(v)) return 0;
        bool Over(double level) => level > 0 && (higherIsWorse ? v >= level : v < level);
        return Over(bad) ? 2 : Over(warn) ? 1 : 0;
    }

    private Brush? SeverityBrush(int severity) => severity switch
    {
        2 => (Brush)FindResource("DangerBrush"),
        1 => new SolidColorBrush(Color.FromRgb(0xF7, 0xA5, 0x41)),
        _ => null,
    };

    /// <summary>The tiles' look: an orange or red border on the card.</summary>
    private void ShowCardSeverity(Border card, int severity)
    {
        if (SeverityBrush(severity) is { } brush)
        {
            card.BorderBrush = brush;
            card.BorderThickness = new Thickness(2);
        }
        else
        {
            card.ClearValue(Border.BorderBrushProperty);
            card.ClearValue(Border.BorderThicknessProperty);
        }
    }

    private static void Paint(TextBlock text, Brush? brush)
    {
        if (brush == null) text.ClearValue(TextBlock.ForegroundProperty);
        else text.Foreground = brush;
    }

    private void UpdateSpeed()
    {
        if (speed == null) return;
        var results = speed.Results;
        var last = results.LastOrDefault();
        SpeedRunButton.Content = speed.Running ? "Stop" : "Run now";
        ShowOn(settings.SpeedTestOn, SpeedOnBox, SpeedBody, SpeedOffText);
        SpeedRunButton.IsEnabled = settings.SpeedTestOn;
        string next = speed.NextDue is DateTime due
            ? (due <= DateTime.Now.AddSeconds(20) ? "next test shortly" : $"next at {due:t}")
            : "no schedule (gear)";

        if (speed.Running)
        {
            string live = speed.LiveMbps > 0 ? Mbps(speed.LiveMbps) : "…";
            DownText.Text = speed.Phase == "Download" ? live : speed.Phase == "Upload" ? DownText.Text : "…";
            UpText.Text = speed.Phase == "Upload" ? live : "…";
            Paint(DownText, null);
            Paint(UpText, null);
            SpeedStatus.ClearValue(ForegroundProperty);
            SpeedStatus.Text = speed.Phase switch
            {
                "Latency" => "Testing: latency…",
                "Download" => speed.LiveMbps > 0 ? "Testing: download…" : "Testing: download, waiting for data…",
                _ => speed.LiveMbps > 0 ? "Testing: upload…" : "Testing: upload, waiting for data…",
            };
            ShowCardSeverity(SpeedCard, 0);
        }
        else
        {
            int down = 0, up = 0, latency = 0, severity = 0;
            if (last is { Failed: false })
            {
                down = Level(last.DownMbps, settings.SpeedWarnDown, settings.SpeedBadDown, higherIsWorse: false);
                up = Level(last.UpMbps, settings.SpeedWarnUp, settings.SpeedBadUp, higherIsWorse: false);
                latency = Level(last.PingMs, settings.SpeedWarnLatency, settings.SpeedBadLatency, higherIsWorse: true);
                severity = Math.Max(down, Math.Max(up, latency));
            }
            else if (last is { Failed: true }) severity = 2;

            DownText.Text = last != null ? Mbps(last.DownMbps) : "—";
            UpText.Text = last != null ? Mbps(last.UpMbps) : "—";
            Paint(DownText, SeverityBrush(down));
            Paint(UpText, SeverityBrush(up));

            if (speed.LastError == "Cancelled.")
            {
                SpeedStatus.ClearValue(ForegroundProperty);
                SpeedStatus.Text = $"Cancelled · {next}";
            }
            else if (last is { Failed: true })
            {
                SpeedStatus.SetResourceReference(ForegroundProperty, "ErrorTextBrush");
                SpeedStatus.Text = $"Failed at {last.Time:t}: {last.Error} · {next}";
            }
            else if (last == null)
            {
                SpeedStatus.ClearValue(ForegroundProperty);
                SpeedStatus.Text = $"No tests yet · {next}";
            }
            else
            {
                if (SeverityBrush(severity) is { } b) SpeedStatus.Foreground = b; else SpeedStatus.ClearValue(ForegroundProperty);
                var slow = new List<string>();
                if (down > 0) slow.Add("download");
                if (up > 0) slow.Add("upload");
                if (latency > 0) slow.Add("latency");
                string flag = slow.Count == 0 ? "" : (latency > 0 && slow.Count == 1 ? "high latency · " : $"slow {string.Join(" and ", slow)} · ");
                SpeedStatus.Text = $"{flag}{last.Time:t} · latency {last.PingMs:0} ms · used {DataText(last.MegaBytes)} · {next}";
            }
            ShowCardSeverity(SpeedCard, settings.SpeedTestOn ? severity : 0); // no warning on a card that's off
        }

        var now = DateTime.Now;
        var day = results.Where(r => r.Time >= now.AddHours(-24)).ToList();
        var series = new List<GraphSeries>
        {
            new() { Name = "Download", Metric = SpeedMetric, Points = day.Select(r => (r.Time, r.DownMbps)).ToList(), Color = "AccentBrush", Fill = true },
            new() { Name = "Upload", Metric = SpeedMetric, Points = day.Select(r => (r.Time, r.UpMbps)).ToList(), Color = "#F7A541", Fill = false },
        };
        double top = day.SelectMany(r => new[] { r.DownMbps, r.UpMbps }).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
        double max = NiceCeiling(Math.Max(top * 1.1, 10));
        SpeedGraph.Show(series, now.AddHours(-24), now, max, TimeSpan.FromMinutes(Math.Max(settings.SpeedTestMinutes, 10) * 2.5));
        double usedToday = results.Where(r => r.Time.Date == now.Date).Sum(r => double.IsNaN(r.MegaBytes) ? 0 : r.MegaBytes);
        int failed = day.Count(r => r.Failed);
        SpeedCaption.Text = day.Count == 0 ? "Last 24 hours" : $"Last 24 hours · top of the graph {max:0} Mbit/s · {day.Count} test{(day.Count == 1 ? "" : "s")}"
            + (failed > 0 ? $" ({failed} failed, gaps in the lines)" : "") + $" · {DataText(usedToday)} used today";
    }

    private static string DataText(double mb) => mb >= 1000 ? $"{mb / 1000:0.0} GB" : $"{mb:0} MB";
}
