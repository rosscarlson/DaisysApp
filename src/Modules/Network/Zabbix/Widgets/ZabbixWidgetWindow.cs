using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DaisysApp.Applets.Network.Zabbix.Api;
using DaisysApp.Applets.Network.Zabbix.Views;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.Network.Zabbix.Widgets;

/// <summary>
/// A widget (Core's <see cref="Widget"/>, as Performance's are): a host's box, one of a host's graphs, or chosen items.
/// Its edit strip adds how much time it shows (not for a host's box). Redrawn after each refresh.
/// </summary>
internal sealed class ZabbixWidgetWindow : Widget
{
    private readonly ZabbixWidgetDefinition def;
    private readonly ZabbixService service;
    private readonly HostBox? hostBox;
    private readonly LineGraph graph = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock value = new() { FontWeight = FontWeights.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly ComboBox minutes = new() { Width = 88, VerticalAlignment = VerticalAlignment.Center };
    private IDisposable? watch;

    public ZabbixWidgetDefinition Definition => def;

    public ZabbixWidgetWindow(ZabbixWidgetDefinition def, ZabbixService service, Action save, Action delete) : base(def, save, delete)
    {
        this.def = def;
        this.service = service;
        Title = F("{0} widget", def.Title);

        if (def.Kind == ZabbixWidgetKind.Host)
        {
            hostBox = new HostBox(service, def.HostId, compact: true);
            Body = hostBox;
        }
        else
        {
            var header = new DockPanel();
            DockPanel.SetDock(value, Dock.Right);
            header.Children.Add(value);
            header.Children.Add(title);
            DockPanel.SetDock(header, Dock.Top);
            var body = new DockPanel();
            body.Children.Add(header);
            body.Children.Add(graph);
            Body = body;
            foreach (int m in new[] { 30, 60, 360, 1440 }) minutes.Items.Add(new ComboBoxItem { Content = m < 60 ? F("{0} min", m) : F("{0} h", m / 60), Tag = m });
            minutes.SelectedItem = minutes.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == def.Minutes) ?? minutes.Items[1];
            minutes.ToolTip = T("How much time the graph shows");
            minutes.SelectionChanged += (_, _) => { if (minutes.SelectedItem is ComboBoxItem { Tag: int m }) { def.Minutes = m; Update(); } };
            AddOption(minutes);
        }
        EditingChanged += () => { if (!Editing) Update(); };

        // a host's box needs only what every box has; a graph needs the host's items
        watch = service.Watch(def.Kind == ZabbixWidgetKind.Host ? null : def.HostId);
        service.Changed += Update;
        ZabbixHistoryWindow.ScaleChanged += Update;
        Closed += (_, _) =>
        {
            service.Changed -= Update;
            ZabbixHistoryWindow.ScaleChanged -= Update;
            watch?.Dispose();
            watch = null;
        };
        Update();
    }

    /// <summary>The lines this widget draws, from the host's items as they are now.</summary>
    private (string Title, List<(ZItem Item, string Color)> Lines) Lines()
    {
        var items = service.ItemsOf(def.HostId);
        string host = service.Host(def.HostId)?.Display ?? "";
        if (def.Kind == ZabbixWidgetKind.Group)
        {
            var group = HostMetrics.Build(items).FirstOrDefault(g => g.Key == def.Group);
            return ($"{host} · {group?.Title ?? def.Title}", group?.Main.Lines.ToList() ?? new());
        }
        var lines = def.ItemKeys.Select(k => items.FirstOrDefault(i => i.Key == k)).OfType<ZItem>()
            .Select((i, n) => (i, HostMetrics.Palette[n % HostMetrics.Palette.Length])).ToList();
        return ($"{host} · {def.Title}", lines);
    }

    public void Update()
    {
        if (hostBox != null)
        {
            hostBox.Update();
            Edge = hostBox.Edge;
            return;
        }
        var (text, lines) = Lines();
        title.Text = text;
        title.ToolTip = text;
        if (lines.Count == 0)
        {
            value.Text = "";
            graph.Show(Array.Empty<GraphSeries>(), DateTime.Now.AddMinutes(-def.Minutes), DateTime.Now, null, TimeSpan.FromMinutes(5));
            return;
        }
        var first = lines[0].Item;
        value.Text = first.ValueText;
        var to = DateTime.Now;
        var from = to.AddMinutes(-def.Minutes);
        service.Want(lines.Select(l => l.Item), from);
        var unit = first.Unit;
        var series = lines.Select(l => new GraphSeries
        {
            Name = l.Item.Name,
            Metric = l.Item.Unit,
            Points = service.HistoryOf(l.Item.ItemId, from).Select(p => (p.Time, p.Value)).ToList(),
            Color = l.Color,
            Fill = lines.Count == 1,
        }).ToList();
        graph.AxisFormat = unit.Axis;
        graph.Show(series, from, to, unit.IsPercent ? 100 : service.Settings.ScaleMax.TryGetValue(unit.Kind, out double top) ? top : null,
            TimeSpan.FromSeconds(Math.Max(180, service.Settings.RefreshSeconds * 3)));
    }
}
