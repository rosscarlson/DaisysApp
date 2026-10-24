using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>
/// A widget (Core's <see cref="Widget"/>): one tile's graph. Its edit strip adds how much time it shows. Redrawn once
/// a second.
/// </summary>
internal sealed class WidgetWindow : Widget
{
    private readonly WidgetDefinition def;
    private readonly MetricGroup group;
    private readonly PerformanceSettings settings;
    private readonly LineGraph graph = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock value = new() { FontWeight = FontWeights.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly ComboBox minutes = new() { Width = 78, VerticalAlignment = VerticalAlignment.Center };

    public WidgetDefinition Definition => def;

    public WidgetWindow(WidgetDefinition def, MetricGroup group, PerformanceSettings settings, Action save, Action delete) : base(def, save, delete)
    {
        this.def = def;
        this.group = group;
        this.settings = settings;
        Title = F("{0} widget", group.Title);

        var header = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        header.Children.Add(value);
        title.Text = group.Title;
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        var body = new DockPanel();
        body.Children.Add(header);
        body.Children.Add(graph);
        Body = body;

        foreach (int m in new[] { 1, 2, 5, 10 }) minutes.Items.Add(new ComboBoxItem { Content = F("{0} min", m), Tag = m });
        minutes.SelectedItem = minutes.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == def.Minutes) ?? minutes.Items[1];
        minutes.SelectionChanged += (_, _) => { if (minutes.SelectedItem is ComboBoxItem { Tag: int m }) def.Minutes = m; };
        AddOption(minutes);
    }

    /// <summary>Draws the latest readings: the last <see cref="WidgetDefinition.Minutes"/> minutes.</summary>
    public void Update(IReadOnlyList<PerfSample> live)
    {
        if (live.Count == 0) return;
        var last = live[^1];
        var first = MetricInfo.Of(group.Graph[0]);
        value.Text = first.Text(last[group.Graph[0]]);
        var from = last.Time.AddMinutes(-def.Minutes);
        var points = live.Where(s => s.Time >= from).ToList();
        var series = group.Graph.Select((m, i) => new GraphSeries
        {
            Name = MetricInfo.Of(m).Name,
            Metric = MetricInfo.Of(m),
            Points = points.Select(x => (x.Time, x[m])).ToList(),
            Color = PerformanceView.ColorOf(m),
            Fill = i == 0,
        }).ToList();
        graph.Show(series, from, last.Time, settings.MaxFor(first), TimeSpan.FromSeconds(5));
    }
}
