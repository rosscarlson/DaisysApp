using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>One target on the Ping card (and its line in the Statistics table).</summary>
internal sealed class PingRow(NetTarget target, string color) : INotifyPropertyChanged
{
    private string value = "—", detail = "", sent = "", lost = "", avg = "", min = "", max = "", jitter = "", outages = "", longest = "";
    private double lostNumber, avgNumber;
    private bool isError;
    private double opacity = 1;

    public NetTarget Target { get; } = target;
    public string Key { get; } = target.Key;
    public string Name { get; } = target.Display;
    public string What { get; } = target.What;
    public string Color { get; } = color;
    public Brush? Swatch { get; set; }
    public string Value { get => value; set => Set(ref this.value, value); }
    public bool IsError { get => isError; set => Set(ref isError, value); }
    public string Detail { get => detail; set => Set(ref detail, value); }
    public double Opacity { get => opacity; set => Set(ref opacity, value); }
    public string Sent { get => sent; set => Set(ref sent, value); }
    public string Lost { get => lost; set => Set(ref lost, value); }
    public double LostNumber { get => lostNumber; set => Set(ref lostNumber, value); }
    public string Avg { get => avg; set => Set(ref avg, value); }
    public double AvgNumber { get => avgNumber; set => Set(ref avgNumber, value); }
    public string Min { get => min; set => Set(ref min, value); }
    public string Max { get => max; set => Set(ref max, value); }
    public string Jitter { get => jitter; set => Set(ref jitter, value); }
    public string Outages { get => outages; set => Set(ref outages, value); }
    public string Longest { get => longest; set => Set(ref longest, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<TV>(ref TV field, TV v, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<TV>.Default.Equals(field, v)) return;
        field = v;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// Networking Tools → Ping: every target's time, live, over the span picked (5 minutes to 24 hours), the Statistics for
/// that span (loss, average, jitter, outages), the card's on / off and logging, and Make Widget on a right-click.
/// </summary>
internal sealed class PingView : UserControl, IDisposable
{
    public static readonly int[] Spans = [5, 15, 60, 360, 1440];

    private readonly NetworkingSettings settings;
    private readonly PingMonitor pings;
    private readonly NetWidgets widgets;
    private readonly CheckBox onBox;
    private readonly TextBlock offText = Ui.Secondary(T("Off: nothing is sent."), wrap: true);
    private readonly TextBlock logText = Ui.Secondary();
    private readonly FrameworkElement body;
    private readonly ItemsControl list = new() { Focusable = false };
    private readonly TextBlock none = Ui.Secondary(T("Nothing to test. Add targets with the gear."), wrap: true);
    private readonly LineGraph graph = new() { Height = 260, Detailed = true, TimeSpans = 4 };
    private readonly DataGrid stats = Ui.Table();
    private List<PingRow> rows = new();
    private string targetsKey = "";
    private DateTime lastDraw;

    public PingView(NetworkingSettings settings, PingMonitor pings, NetWidgets widgets)
    {
        this.settings = settings;
        this.pings = pings;
        this.widgets = widgets;

        onBox = Ui.OnBox(T("Ping"), settings.PingOn, T("Untick to stop sending completely"));
        onBox.Checked += (_, _) => SetOn(true);
        onBox.Unchecked += (_, _) => SetOn(false);
        var span = Ui.Choice(Spans.Select(m => (m < 60 ? F("{0} min", m) : F("{0} h", m / 60), m)), settings.PingSpanMinutes,
            m => { settings.PingSpanMinutes = m; settings.Save(); Draw(true); }, 90);
        span.ToolTip = T("Time shown");
        logText.VerticalAlignment = VerticalAlignment.Center;
        var header = Ui.HeaderRow(onBox, logText, span, Ui.Gear(T("Targets"), EditTargets));

        list.ItemTemplate = RowTemplate();
        list.ItemsSource = rows;
        var left = new StackPanel { Children = { list, none } };
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.Children.Add(left);
        Grid.SetColumn(graph, 2);
        columns.Children.Add(graph);
        body = columns;
        offText.Margin = new Thickness(0, 0, 0, 8);

        var card = Ui.Card(new StackPanel { Children = { header, offText, body } }, new Thickness(0));
        Widget.Offer(card, () => widgets.Create(NetWidgetPlace.PingKind, Window.GetWindow(this)));

        stats.Columns.Add(Ui.Column(T("Name"), nameof(PingRow.Name), -1));
        stats.Columns.Add(Ui.Column(T("Target"), nameof(PingRow.What), -1.4));
        stats.Columns.Add(Ui.Column(T("Sent"), nameof(PingRow.Sent), 70, right: true));
        stats.Columns.Add(Ui.Column(T("Lost"), nameof(PingRow.Lost), 70, nameof(PingRow.LostNumber), right: true));
        stats.Columns.Add(Ui.Column(T("Average"), nameof(PingRow.Avg), 80, nameof(PingRow.AvgNumber), right: true));
        stats.Columns.Add(Ui.Column(T("Best"), nameof(PingRow.Min), 70, right: true));
        stats.Columns.Add(Ui.Column(T("Worst"), nameof(PingRow.Max), 70, right: true));
        stats.Columns.Add(Ui.Column(T("Jitter"), nameof(PingRow.Jitter), 70, right: true));
        stats.Columns.Add(Ui.Column(T("Outages"), nameof(PingRow.Outages), 76, right: true));
        stats.Columns.Add(Ui.Column(T("Longest"), nameof(PingRow.Longest), 80, right: true));
        var statsCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Statistics")), stats } }, new Thickness(0, 12, 0, 0));
        statsCard.ToolTip = F("An outage: {0} or more missed in a row", PingMonitor.DownAfter);

        var widgetList = Widget.ListCard();
        widgetList.Margin = new Thickness(0, 12, 0, 0);
        Content = Ui.Page(new StackPanel { Children = { card, statsCard, widgetList } });

        pings.Updated += OnUpdated;
        pings.LoggingChanged += OnUpdated;
        IsVisibleChanged += (_, _) => { if (IsVisible) Draw(true); };
        Draw(true);
    }

    private void OnUpdated() => Dispatcher.BeginInvoke(() => { if (IsVisible) Draw(false); });

    private void SetOn(bool on)
    {
        if (on == settings.PingOn) return;
        settings.PingOn = on;
        settings.Save();
        Draw(true);
    }

    private void EditTargets()
    {
        if (TargetsWindow.Edit(settings, Window.GetWindow(this))) Draw(true);
    }

    private DataTemplate RowTemplate()
    {
        // swatch, name and detail, value at the right (red when it failed or is over its level)
        var dock = new FrameworkElementFactory(typeof(DockPanel));
        dock.SetValue(MarginProperty, new Thickness(0, 0, 0, 8));
        dock.SetValue(BackgroundProperty, Brushes.Transparent);
        dock.SetValue(CursorProperty, System.Windows.Input.Cursors.Hand);
        dock.SetBinding(OpacityProperty, new System.Windows.Data.Binding(nameof(PingRow.Opacity)));
        dock.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(PingRow.What)));
        dock.AddHandler(MouseLeftButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler(Row_Click));

        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetValue(DockPanel.DockProperty, Dock.Right);
        value.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        value.SetValue(MarginProperty, new Thickness(8, 0, 0, 0));
        value.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PingRow.Value)));
        var style = new Style(typeof(TextBlock));
        var trigger = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(PingRow.IsError)), Value = true };
        trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("ErrorTextBrush")));
        style.Triggers.Add(trigger);
        value.SetValue(StyleProperty, style);
        dock.AppendChild(value);

        var swatch = new FrameworkElementFactory(typeof(Rectangle));
        swatch.SetValue(DockPanel.DockProperty, Dock.Left);
        swatch.SetValue(WidthProperty, 10.0);
        swatch.SetValue(HeightProperty, 10.0);
        swatch.SetValue(Rectangle.RadiusXProperty, 2.0);
        swatch.SetValue(Rectangle.RadiusYProperty, 2.0);
        swatch.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
        swatch.SetValue(MarginProperty, new Thickness(0, 4, 8, 0));
        swatch.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding(nameof(PingRow.Swatch)));
        dock.AppendChild(swatch);

        var texts = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PingRow.Name)));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        texts.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PingRow.Detail)));
        detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        detail.SetValue(TextBlock.FontSizeProperty, 11.5);
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        texts.AppendChild(detail);
        dock.AppendChild(texts);
        return new DataTemplate { VisualTree = dock };
    }

    /// <summary>Click a target: its line is highlighted and the others dimmed; again for all.</summary>
    private void Row_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PingRow row) return;
        graph.Highlight = graph.Highlight == row.Name ? null : row.Name;
        foreach (var r in rows) r.Opacity = graph.Highlight == null || graph.Highlight == r.Name ? 1 : 0.45;
    }

    private void Draw(bool now)
    {
        // a long span is a lot of points: redrawn every few seconds, not every round
        if (!now && settings.PingSpanMinutes > 15 && (DateTime.Now - lastDraw).TotalSeconds < 5) return;
        lastDraw = DateTime.Now;
        onBox.IsChecked = settings.PingOn;
        body.Opacity = settings.PingOn ? 1 : 0.3;
        body.IsEnabled = settings.PingOn;
        offText.Visibility = settings.PingOn ? Visibility.Collapsed : Visibility.Visible;
        logText.Text = !settings.PingLog ? ""
            : settings.PingLogUntil is DateTime until ? F("Logging until {0}", Ui.When(until))
            : F("Logging every {0} s", settings.PingSeconds);

        var targets = settings.Targets.Where(t => t.Address.Trim().Length > 0).GroupBy(t => t.Key).Select(g => g.First()).ToList();
        string key = string.Join("|", targets.Select(t => t.Display + "=" + t.Key + "/" + t.AlertMs));
        if (key != targetsKey)
        {
            targetsKey = key;
            graph.Highlight = null;
            rows = targets.Select((t, i) => new PingRow(t, Ui.Colors[i % Ui.Colors.Length])).ToList();
            foreach (var r in rows) r.Swatch = Ui.BrushOf(this, r.Color);
            list.ItemsSource = rows;
            stats.ItemsSource = rows;
            none.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        var end = DateTime.Now;
        var from = end.AddMinutes(-settings.PingSpanMinutes);
        var series = new List<GraphSeries>();
        double top = 0, level = 0;
        foreach (var row in rows)
        {
            var points = pings.Points(row.Key, from);
            var s = pings.Stats(row.Key, from);
            double last = points.Count > 0 ? points[^1].V : double.NaN;
            bool slow = !double.IsNaN(last) && last > row.Target.AlertMs;
            if (!settings.PingOn) { row.Value = "—"; row.IsError = false; }
            else if (points.Count == 0) { row.Value = "…"; row.IsError = false; }
            else if (double.IsNaN(last)) { row.Value = pings.Error(row.Key) ?? T("no answer"); row.IsError = true; }
            else { row.Value = Ui.Ms(last); row.IsError = slow; }
            row.Detail = s.Answered == 0 ? row.What
                : F("avg {0:0} · max {1:0}", s.Avg, s.Max) + (s.LossPercent >= 0.5 ? F(" · {0:0.#}% lost", s.LossPercent) : "") + (s.Outages > 0 ? " · " + P(s.Outages, "{0} outage", "{0} outages") : "");
            row.Sent = s.Sent.ToString("#,0");
            row.Lost = s.Sent == 0 ? "" : $"{s.LossPercent:0.#}%";
            row.LostNumber = s.LossPercent;
            row.Avg = Ui.Ms(s.Avg);
            row.AvgNumber = double.IsNaN(s.Avg) ? double.MaxValue : s.Avg;
            row.Min = Ui.Ms(s.Min);
            row.Max = Ui.Ms(s.Max);
            row.Jitter = Ui.Ms(s.Jitter);
            row.Outages = s.Outages.ToString();
            row.Longest = s.Outages > 0 ? Ui.Duration(s.LongestOutage) : "";
            var shown = Thin(points, from, end);
            series.Add(new GraphSeries { Name = row.Name, Metric = MsUnit.Instance, Points = shown, Color = slow ? "DangerBrush" : row.Color, Fill = false, Bold = slow });
            top = Math.Max(top, shown.Where(p => !double.IsNaN(p.V)).Select(p => p.V).DefaultIfEmpty(0).Max());
            level = Math.Max(level, row.Target.AlertMs);
        }
        double gap = Math.Max(settings.PingSeconds * 3, (end - from).TotalSeconds / 400 * 2.5);
        graph.Show(series, from, end, Ui.NiceCeiling(Math.Max(top * 1.15, 10)), TimeSpan.FromSeconds(gap));
    }

    /// <summary>At most about 400 points across the span: each a stretch's average answer (NaN where all were lost).</summary>
    public static List<(DateTime T, double V)> Thin(List<(DateTime T, double V)> points, DateTime from, DateTime to, int buckets = 400)
    {
        if (points.Count <= buckets * 2) return points;
        var result = new List<(DateTime, double)>(buckets);
        double width = (to - from).Ticks / (double)buckets;
        int i = 0;
        for (int b = 0; b < buckets && i < points.Count; b++)
        {
            var end = from.AddTicks((long)(width * (b + 1)));
            double sum = 0;
            int n = 0, all = 0;
            DateTime at = default;
            while (i < points.Count && points[i].T < end)
            {
                if (all++ == 0) at = points[i].T;
                if (!double.IsNaN(points[i].V)) { sum += points[i].V; n++; }
                i++;
            }
            if (all > 0) result.Add((at, n > 0 ? sum / n : double.NaN));
        }
        return result;
    }

    public void Dispose()
    {
        pings.Updated -= OnUpdated;
        pings.LoggingChanged -= OnUpdated;
    }
}
