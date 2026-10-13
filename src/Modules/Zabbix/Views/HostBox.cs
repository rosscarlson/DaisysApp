using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Applets.Zabbix.Api;

namespace DaisysApp.Applets.Zabbix.Views;

/// <summary>
/// One host at a glance: whether Zabbix can reach it, its CPU and memory now and the CPU over the last hour, its
/// problems (the box's edge takes the worst one's colour) and how long it's been up. On the main grid, and as a widget.
/// </summary>
internal sealed class HostBox : Border
{
    public static readonly TimeSpan GraphSpan = TimeSpan.FromHours(1);

    private readonly ZabbixService service;
    public string HostId { get; }
    private readonly Ellipse dot = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock name = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border badge = new() { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock badgeText = new() { FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Brushes.Black };
    private readonly TextBlock address = Ui.Secondary();
    private readonly TextBlock cpu = Value(), memory = Value();
    private readonly TextBlock cpuLabel = Ui.Secondary("CPU"), memoryLabel = Ui.Secondary(T("Memory"));
    private readonly LineGraph graph = new() { Height = 40, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock state = Ui.Secondary();
    private readonly bool compact;

    public HostBox(ZabbixService service, string hostId, bool compact = false)
    {
        this.service = service;
        this.compact = compact;
        HostId = hostId;
        // in a widget, the widget is the card
        if (!compact)
        {
            SetResourceReference(StyleProperty, "Card");
            Margin = new Thickness(6, 0, 6, 12);
        }
        badge.Child = badgeText;

        var top = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(badge, Dock.Right);
        top.Children.Add(dot);
        top.Children.Add(badge);
        top.Children.Add(name);

        var values = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        values.ColumnDefinitions.Add(new ColumnDefinition());
        values.ColumnDefinitions.Add(new ColumnDefinition());
        values.Children.Add(Labelled(cpuLabel, cpu, 0));
        values.Children.Add(Labelled(memoryLabel, memory, 1));

        var stack = new StackPanel();
        stack.Children.Add(top);
        address.Margin = new Thickness(18, 1, 0, 0);
        if (!compact) stack.Children.Add(address);
        stack.Children.Add(values);
        stack.Children.Add(graph);
        state.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(state);
        Child = stack;
        Update();
    }

    private static TextBlock Value() => new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Text = "—" };

    private static StackPanel Labelled(TextBlock label, TextBlock value, int column)
    {
        var s = new StackPanel();
        s.Children.Add(label);
        s.Children.Add(value);
        Grid.SetColumn(s, column);
        return s;
    }

    public void Update()
    {
        var host = service.Host(HostId);
        name.Text = host?.Display ?? T("(host not found)");
        address.Text = host == null ? "" : host.Address + (host.Groups.Count > 0 ? " · " + string.Join(", ", host.Groups) : "");
        var items = service.Summary.TryGetValue(HostId, out var list) ? list : new List<ZItem>();
        var cpuItem = HostMetrics.Cpu(items);
        var memItem = HostMetrics.Memory(items);
        // a host Zabbix only pings (a router, a printer): its response time and loss instead
        var pingItem = cpuItem == null ? items.FirstOrDefault(i => i.IsNumeric && i.Key.StartsWith("icmppingsec", StringComparison.Ordinal)) : null;
        var lossItem = memItem == null ? items.FirstOrDefault(i => i.IsNumeric && i.Key.StartsWith("icmppingloss", StringComparison.Ordinal)) : null;
        cpuLabel.Text = pingItem != null ? T("Ping") : "CPU";
        memoryLabel.Text = lossItem != null ? T("Loss") : T("Memory");
        cpu.Text = cpuItem is { } c && !double.IsNaN(c.Value) ? $"{c.Value:0}%" : pingItem != null ? pingItem.ValueText : "—";
        memory.Text = memItem is { } m && !double.IsNaN(m.Value) ? $"{m.Value:0}%" : lossItem != null ? lossItem.ValueText : "—";

        if ((cpuItem ?? pingItem) is { } drawn)
        {
            var from = DateTime.Now - GraphSpan;
            service.Want(new[] { drawn }, from);
            var points = service.HistoryOf(drawn.ItemId, from);
            graph.Show(new[] { new GraphSeries { Name = drawn.Name, Metric = drawn.Unit, Points = points.Select(p => (p.Time, p.Value)).ToList(), Color = "AccentBrush" } },
                from, DateTime.Now, drawn.Unit.IsPercent ? 100 : null, TimeSpan.FromMinutes(5));
            graph.Visibility = Visibility.Visible;
        }
        else graph.Visibility = compact ? Visibility.Collapsed : Visibility.Hidden;

        var problems = service.ProblemsOf(HostId).OrderByDescending(p => p.Severity).ToList();
        int worst = problems.Select(p => p.Severity).DefaultIfEmpty(-1).Max();
        badge.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        badgeText.Text = problems.Count.ToString();
        if (worst >= 0) badge.Background = Ui.Brush(this, ZProblem.SeverityColor(worst));
        badge.ToolTip = problems.Count > 0 ? string.Join("\n", problems.Take(8).Select(p => $"{p.SeverityName}: {p.Description}")) : null;

        string dotColor;
        if (host == null) { dotColor = "TextSecondaryBrush"; state.Text = ""; }
        else if (!host.Monitored) { dotColor = "TextSecondaryBrush"; state.Text = T("Not monitored (switched off in Zabbix)"); }
        else if (host.Availability == Availability.Unavailable) { dotColor = "DangerBrush"; state.Text = host.Error.Length > 0 ? F("Unreachable: {0}", host.Error) : T("Unreachable"); }
        else
        {
            dotColor = host.Availability == Availability.Available ? "#7FD07A" : "TextSecondaryBrush";
            var uptime = HostMetrics.Uptime(items);
            state.Text = problems.Count > 0 ? problems[0].Description
                : host.InMaintenance ? T("In maintenance")
                : uptime != null && !double.IsNaN(uptime.Value) ? F("Up {0}", ZUnit.Of("uptime").Text(uptime.Value))
                : host.Availability == Availability.Unknown ? T("No agent checks") : T("OK");
        }
        if (host?.InMaintenance == true && problems.Count > 0) state.Text = T("In maintenance") + " · " + state.Text;
        state.ToolTip = state.Text;
        Ui.SetBrush(dot, Shape.FillProperty, dotColor);

        // the edge: unreachable red, else the worst problem's colour (warning and up)
        Edge = host?.Availability == Availability.Unavailable && host.Monitored ? Ui.Brush(this, "DangerBrush")
            : worst >= 2 ? Ui.Brush(this, ZProblem.SeverityColor(worst)) : null;
        if (compact) return; // (a widget colours its own edge)
        if (Edge != null) { BorderBrush = Edge; BorderThickness = new Thickness(2); }
        else { ClearValue(BorderBrushProperty); ClearValue(BorderThicknessProperty); }
        Opacity = host?.Monitored == false ? 0.6 : 1;
    }

    /// <summary>The colour of the box's edge: red when unreachable, else the worst problem's (warning and up); null for none.</summary>
    public Brush? Edge { get; private set; }
}
