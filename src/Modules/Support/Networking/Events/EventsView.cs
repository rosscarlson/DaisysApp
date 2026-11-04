using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Events: what happened to the connection (drops, adapters, addresses, Wi-Fi, speed tests), newest
/// first (to the second), a chip per kind to show or hide it, a chart of them over time beside it (<see cref="EventsChart"/>), Report (a page to print, <see cref="EventReport"/>), Export (every event as text) and the last 24 hours in
/// figures (drops and the longest, Wi-Fi changes, adapter disconnects), with the last week and month beside it once
/// the events go back further than a day and a week (since 3.37.13).
/// </summary>
internal sealed class EventsView : UserControl, IDisposable
{
    private sealed record EventRow(DateTime Time, string When, string Kind, string Text, string Mark);

    private readonly NetworkingSettings settings;
    private readonly EventMonitor monitor;
    private readonly DataGrid table = Ui.Table(520);
    private readonly NetUi.Fields summary = new(), weekSummary = new(), monthSummary = new();
    private readonly Border weekCard, monthCard;
    private readonly EventsChart chart = new() { Height = 520 };
    private readonly CheckBox onBox;

    public EventsView(NetworkingSettings settings, EventMonitor monitor)
    {
        this.settings = settings;
        this.monitor = monitor;
        onBox = NetUi.OnBox(T("Events"), settings.EventsOn, T("Untick to stop watching"));
        Ui.OnToggle(onBox, on => { settings.EventsOn = on; settings.Save(); });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var kind in EventMonitor.Kinds)
        {
            var chip = Ui.Chip(KindName(kind), !settings.EventsHidden.Contains(kind));
            Ui.OnToggle(chip, on =>
            {
                settings.EventsHidden.Remove(kind);
                if (!on) settings.EventsHidden.Add(kind);
                settings.Save();
                Draw();
            });
            chips.Children.Add(chip);
        }
        var export = new Button { Content = T("Export…"), ToolTip = T("Save every event as text") };
        export.Click += (_, _) => Export();
        // (since 3.33.0) a page to read or print: the outages and problems of a period
        var report = new Button { Content = T("Report") + " ▾", ToolTip = T("A report to read or print") };
        System.Windows.Automation.AutomationProperties.SetName(report, T("Report"));
        report.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = report, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (name, since) in new (string, DateTime?)[]
                { (T("Last 24 hours"), DateTime.Now.AddHours(-24)), (T("Last 7 days"), DateTime.Now.AddDays(-7)), (T("Last 30 days"), null) })
            {
                var item = new MenuItem { Header = name };
                item.Click += (_, _) =>
                {
                    try { EventReport.Open(monitor.Events, settings, since, name); }
                    catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, T("Report"), MessageBoxButton.OK, MessageBoxImage.Warning); }
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        var clear = ConfirmButton.Make(T("Clear"), monitor.Clear);
        clear.ToolTip = T("Delete every event");
        var header = Ui.HeaderRow(onBox, chips, report, export, clear);

        table.Columns.Add(Ui.Column("", nameof(EventRow.Mark), 30));
        table.Columns.Add(Ui.Column(T("Time"), nameof(EventRow.When), 175, nameof(EventRow.Time)));
        table.Columns.Add(Ui.Column(T("Kind"), nameof(EventRow.Kind), 90));
        table.Columns.Add(NetUi.CopyColumn(T("What"), nameof(EventRow.Text), -1));
        NetUi.ItemMenu(table, o => o is EventRow r ? [r.Text] : []);

        var day = Ui.Card(new StackPanel { Children = { Ui.Header(T("Last 24 Hours")), summary } }, new Thickness(0));
        weekCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Last Week")), weekSummary } }, new Thickness(12, 0, 0, 0));
        monthCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Last Month")), monthSummary } }, new Thickness(12, 0, 0, 0));
        // (a collapsed card leaves no room behind: UniformGrid counts only the ones showing)
        var periods = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Children = { day, weekCard, monthCard } };
        var list = Ui.Card(new StackPanel { Children = { header, table } }, new Thickness(0, 12, 0, 0));

        // (since 3.34.0) the events over time, beside the list: the same kinds as the chips show
        var by = Ui.Choice(EventsChart.Choices, EventsChart.Choices.Any(c => c.Value == settings.EventsChartBy) ? settings.EventsChartBy : EventsChart.Hour,
            v => { settings.EventsChartBy = v; settings.Save(); Draw(); }, 150);
        System.Windows.Automation.AutomationProperties.SetName(by, T("Chart by"));
        var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var (name, brush, opacity) in new[] { (T("Problems"), "ErrorTextBrush", 1.0), (T("Other"), "TextSecondaryBrush", 0.55) })
        {
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Opacity = opacity, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, brush);
            legend.Children.Add(swatch);
            legend.Children.Add(Ui.Secondary(name));
        }
        var chartCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(Ui.Header(T("Over Time")), legend, by), chart } }, new Thickness(12, 12, 0, 0));
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) } } };
        Grid.SetColumn(chartCard, 1);
        row.Children.Add(list);
        row.Children.Add(chartCard);
        Content = Ui.Page(new StackPanel { Children = { periods, row } });

        monitor.Changed += OnChanged;
        // (the chart's newest bar moves on with the clock)
        Ui.Every(this, 30000, Draw);
        IsVisibleChanged += (_, _) => { if (IsVisible) Draw(); };
    }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = T("Text") + " (*.txt)|*.txt", Title = T("Export Events"),
            FileName = string.Concat($"{Environment.MachineName} Network Events {DateTime.Now:yyyy-MM-dd HH-mm}".Split(System.IO.Path.GetInvalidFileNameChars())) + ".txt",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, monitor.Report());
            Logging.Log.Here.Info($"Network events exported to {dialog.FileName}");
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, T("Export Events"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public static string KindName(string kind) => kind switch
    {
        EventMonitor.Cause => T("Diagnosis"),
        EventMonitor.Drop => T("Drops"),
        EventMonitor.Adapter => T("Adapters"),
        EventMonitor.Address => T("Addresses"),
        EventMonitor.WiFi => T("Wi-Fi"),
        EventMonitor.Speed => T("Speed Test"),
        _ => kind,
    };

    private void OnChanged() => Dispatcher.BeginInvoke(() => { if (IsVisible) Draw(); });

    private void Draw()
    {
        onBox.IsChecked = settings.EventsOn;
        var all = monitor.Events;
        var shown = all.Where(e => !settings.EventsHidden.Contains(e.Kind)).ToList();
        chart.Show(shown, settings.EventsChartBy);
        table.ItemsSource = Enumerable.Reverse(shown)
            .Select(e => new EventRow(e.Time, e.Time.ToString("G"), KindName(e.Kind), e.Text, e.Bad ? "⚠" : "")).ToList();

        // the week and the month only once the events go back further than the period before (else they'd say the same)
        var now = DateTime.Now;
        var oldest = all.Count > 0 ? all.Min(e => e.Time) : now;
        Summarize(summary, all.Where(e => e.Time >= now.AddHours(-24)).ToList());
        Summarize(weekSummary, all.Where(e => e.Time >= now.AddDays(-7)).ToList());
        Summarize(monthSummary, all.Where(e => e.Time >= now.AddDays(-30)).ToList());
        weekCard.Visibility = oldest < now.AddHours(-24) ? Visibility.Visible : Visibility.Collapsed;
        monthCard.Visibility = oldest < now.AddDays(-7) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Summarize(NetUi.Fields fields, List<NetEvent> span)
    {
        var drops = span.Where(e => e.Kind == EventMonitor.Drop && e.Bad).ToList();
        var backs = span.Where(e => e.Kind == EventMonitor.Drop && !e.Bad && !double.IsNaN(e.Seconds)).ToList();
        fields.Clear();
        var danger = (System.Windows.Media.Brush)FindResource("ErrorTextBrush");
        fields.Add(T("Drops"), drops.Count == 0 ? T("none") : drops.Count + (backs.Count > 0 ? " · " + F("longest {0}", NetUi.Duration(TimeSpan.FromSeconds(backs.Max(b => b.Seconds)))) : ""),
            null, drops.Count > 0 ? danger : null);
        int wifi = span.Count(e => e.Kind == EventMonitor.WiFi && e.Bad);
        fields.Add(T("Wi-Fi"), wifi == 0 ? T("no problems") : P(wifi, "{0} problem", "{0} problems"), null, wifi > 0 ? danger : null);
        int adapters = span.Count(e => e.Kind == EventMonitor.Adapter && e.Bad);
        fields.Add(T("Adapters"), adapters == 0 ? T("no disconnects") : P(adapters, "{0} disconnect", "{0} disconnects"), null, adapters > 0 ? danger : null);
        int addresses = span.Count(e => e.Kind == EventMonitor.Address);
        fields.Add(T("Addresses"), addresses == 0 ? T("no changes") : P(addresses, "{0} change", "{0} changes"));
        int speed = span.Count(e => e.Kind == EventMonitor.Speed);
        if (speed > 0) fields.Add(T("Speed Test"), P(speed, "{0} failed", "{0} failed"), null, danger);
    }

    public void Dispose() => monitor.Changed -= OnChanged;
}
