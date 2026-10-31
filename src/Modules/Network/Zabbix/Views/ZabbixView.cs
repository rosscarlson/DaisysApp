using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Applets.Network.Zabbix.Api;
using DaisysApp.Applets.Network.Zabbix.Widgets;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>
/// The Zabbix sub-tab (on the Network tab): a box per host (click one for its page), filtered by a search, a host group or problems only, and
/// the widgets on the screen. Before a server is set up, the connection form instead. Reads from the server only while
/// it's showing.
/// </summary>
internal sealed class ZabbixView : ContentControl
{
    private readonly ZabbixService service;
    private readonly ZabbixWidgets widgets;
    private readonly DockPanel gridPage = new();
    private readonly FrameworkElement setupPage;
    /// <summary>One grid of host boxes: Alerts first (hosts with a problem or unreachable, worst first), then each host
    /// group in the order dragged, as many columns wide as it has hosts; one that doesn't fit what's left of a row takes it
    /// and goes on in the next.</summary>
    private readonly Grid layout = new() { Margin = new Thickness(-6, 0, -6, 0) };
    private readonly Dictionary<string, Section> sectionByKey = new();
    /// <summary>The sections in their order (Alerts, then the groups).</summary>
    private readonly List<Section> ordered = new();
    private const string AlertsKey = "\u0001alerts";
    private const string DragFormat = "DaisysApp.ZabbixGroup";
    private ScrollViewer scroller = null!;
    private readonly GroupGrid grid;
    private readonly TextBlock status = Ui.Secondary("", wrap: true);
    private readonly TextBlock empty = Ui.Secondary("", wrap: true);
    private readonly TextBox search = new() { Width = 200 };
    private readonly ComboBox groupBox = new() { Width = 180, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ToggleButton problemsOnly = new() { Content = T("Problems only"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
    private readonly StackPanel widgetList = new();
    private readonly TextBlock noWidgets = Ui.Secondary(T("None yet. Right-click a host, or open one and click Widget (or right-click a tile or graph), to keep a small copy of it anywhere on the screen."), wrap: true);
    private HostPage? hostPage;
    private IDisposable? watch;

    public ZabbixView(ZabbixService service, ZabbixWidgets widgets)
    {
        this.service = service;
        this.widgets = widgets;
        Focusable = false;

        // the bar: what's happening, and the filters
        var filters = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glass = Ui.Icon("", 13);
        glass.Margin = new Thickness(0, 0, 8, 0);
        filters.Children.Add(glass);
        filters.Children.Add(search);
        filters.Children.Add(groupBox);
        problemsOnly.SetResourceReference(StyleProperty, "ChipToggle");
        filters.Children.Add(problemsOnly);
        filters.Children.Add(Ui.Spaced(Ui.IconButton("", T("Refresh"), T("Read everything again now"), (_, _) => _ = service.RefreshAsync())));
        search.ToolTip = T("Show only hosts whose name, address or group contains this");
        problemsOnly.ToolTip = T("Show only hosts that have a problem or can't be reached");
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(filters, Dock.Right);
        bar.Children.Add(filters);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(0, 0, 12, 0);
        bar.Children.Add(status);
        DockPanel.SetDock(bar, Dock.Top);
        gridPage.Children.Add(bar);

        var widgetsCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Widgets")), noWidgets, widgetList } }, new Thickness(0, 6, 0, 0));
        empty.Margin = new Thickness(0, 0, 0, 12);
        var body = new StackPanel { Margin = new Thickness(0, 0, 6, 0), Children = { empty, layout, widgetsCard } };
        scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        grid = new GroupGrid(layout, scroller, DragFormat, () => service.Settings.GridColumns, () => service.Settings.GridRows, () => ordered.Where(s => s.Movable).Select(s => s.Key), keys =>
        {
            service.Settings.GroupOrder = keys;
            service.Settings.Save();
            Dispatcher.BeginInvoke(Update); // after the drag has finished with the piece it started from
        });
        gridPage.Children.Add(scroller);
        scroller.SizeChanged += (_, e) =>
        {
            if (grid.ColumnsChanged) Filter();
            else if (e.HeightChanged) grid.FitRows();
        };

        // before there's a server: the form, right here
        var form = new ConnectionForm(service);
        var setup = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        setup.Children.Add(Ui.Header(T("Connect to your Zabbix server")));
        var intro = Ui.Secondary(T("Daisy's App shows your Zabbix hosts, their values and their problems: it only reads, and changes nothing on the server. Fill in where the server is and how to sign in (an API token is best: make one in Zabbix under User settings → API tokens), then Test connection. This is also in Settings → Network → Zabbix."), wrap: true);
        intro.Margin = new Thickness(0, 0, 0, 14);
        setup.Children.Add(intro);
        setup.Children.Add(form);
        setupPage = new ScrollViewer { Content = Ui.Card(setup, new Thickness(0)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        form.Connected += Show;

        search.TextChanged += (_, _) => Filter();
        groupBox.SelectionChanged += (_, _) => Filter();
        problemsOnly.Click += (_, _) => Filter();
        service.Changed += OnChanged;
        widgets.Changed += ShowWidgets;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) { Show(); }
            else { watch?.Dispose(); watch = null; }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && hostPage != null && Content == hostPage) { ShowGrid(); e.Handled = true; }
        };
        Show();
        ShowWidgets();
    }

    /// <summary>The setup form until a server has answered, then the hosts (or the host that was open).</summary>
    private void Show()
    {
        bool ready = service.Settings.HasServer && (service.Settings.Configured || service.Hosts.Count > 0);
        if (!ready) { Content = setupPage; watch?.Dispose(); watch = null; return; }
        if (hostPage != null) Content = hostPage;
        else Content = gridPage;
        if (IsVisible) watch ??= service.Watch(null);
        Update();
    }

    private void OnChanged()
    {
        if (!IsVisible) return;
        if (Content == setupPage && service.Hosts.Count > 0) Show();
        else Update();
    }

    private void Update()
    {
        if (Content != gridPage) return;
        var hosts = Visible().ToList();
        int unreachable = hosts.Count(h => h.Monitored && h.Availability == Availability.Unavailable);
        int problems = service.Problems.Count;
        status.Text = service.Error is { } e ? e
            : service.Updated == null ? T("Connecting…")
            : string.Join(" · ", new[]
            {
                F("Zabbix {0}", service.ServerVersion),
                P(hosts.Count, "{0} host", "{0} hosts"),
                unreachable > 0 ? P(unreachable, "{0} unreachable", "{0} unreachable") : "",
                problems > 0 ? P(problems, "{0} problem", "{0} problems") : T("no problems"),
                F("read {0:T}", service.Updated),
            }.Where(s => s.Length > 0));
        if (service.Error != null) status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else status.ClearValue(TextBlock.ForegroundProperty);

        // the host group picker: every group the hosts are in
        var groups = hosts.SelectMany(h => h.Groups).Distinct().OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToList();
        var current = groupBox.SelectedItem as string;
        var wanted = new List<string> { T("All host groups") };
        wanted.AddRange(groups);
        if (!groupBox.Items.Cast<string>().SequenceEqual(wanted))
        {
            updatingGroups = true;
            groupBox.ItemsSource = wanted;
            groupBox.SelectedItem = current != null && wanted.Contains(current) ? current : wanted[0];
            updatingGroups = false;
        }

        // the alerts, worst first, then each host group with its hosts in Zabbix's order (a host in several groups is in each)
        var wantedSections = new List<(string Key, List<ZHost> Hosts)>();
        var alerts = hosts.Select((h, i) => (h, i, rank: Rank(h))).Where(x => x.rank >= 0).OrderByDescending(x => x.rank).ThenBy(x => x.i).Select(x => x.h).ToList();
        if (alerts.Count > 0) wantedSections.Add((AlertsKey, alerts));
        foreach (var g in groups) wantedSections.Add((g, hosts.Where(h => h.Groups.Contains(g)).ToList()));
        var loose = hosts.Where(h => h.Groups.Count == 0).ToList();
        if (loose.Count > 0) wantedSections.Add(("", loose));

        var order = service.Settings.GroupOrder;
        int Place(string key) { int i = order.IndexOf(key); return i < 0 ? int.MaxValue : i; }
        wantedSections = wantedSections.OrderBy(w => w.Key == AlertsKey ? -1 : 0).ThenBy(w => w.Key == AlertsKey ? 0 : Place(w.Key)).ToList();
        var keys = wantedSections.Select(w => w.Key).ToHashSet();
        foreach (var gone in sectionByKey.Keys.Where(k => !keys.Contains(k)).ToList()) sectionByKey.Remove(gone);
        ordered.Clear();
        foreach (var (key, list) in wantedSections)
        {
            if (!sectionByKey.TryGetValue(key, out var sec)) sectionByKey[key] = sec = new Section(key, key == AlertsKey ? T("Alerts") : key.Length == 0 ? T("No host group") : key);
            ordered.Add(sec);
            Fill(sec, list);
        }
        Filter();
    }

    /// <summary>A section's hosts, in the order given, each with its box.</summary>
    private void Fill(Section sec, List<ZHost> hosts)
    {
        var ids = hosts.Select(h => h.HostId).ToHashSet();
        foreach (var gone in sec.Boxes.Keys.Where(id => !ids.Contains(id)).ToList()) sec.Boxes.Remove(gone);
        sec.Order.Clear();
        foreach (var h in hosts)
        {
            if (!sec.Boxes.TryGetValue(h.HostId, out var box)) sec.Boxes[h.HostId] = box = MakeBox(h.HostId);
            box.Update();
            sec.Order.Add(h.HostId);
        }
    }

    /// <summary>How bad a host is: 6 unreachable, 0–5 its worst problem's severity, -1 fine, -2 not monitored.</summary>
    private int Rank(ZHost h) =>
        !h.Monitored ? -2
        : h.Availability == Availability.Unavailable ? 6
        : service.WorstSeverity(h.HostId);

    private static (string Color, string Name) Status(int rank) => rank switch
    {
        6 => ("DangerBrush", T("Unreachable")),
        >= 0 => (ZProblem.SeverityColor(rank), ZProblem.SeverityText(rank)),
        -1 => ("#7FD07A", T("OK")),
        _ => ("TextSecondaryBrush", T("Not monitored")),
    };

    private bool updatingGroups;

    /// <summary>The hosts the grid has: monitored ones (and switched-off ones if asked for), not hidden ones.</summary>
    private IEnumerable<ZHost> Visible() => service.Hosts.Where(h => (h.Monitored || service.Settings.ShowDisabled) && !service.Settings.HiddenHosts.Contains(h.HostId));

    private void Filter()
    {
        if (updatingGroups) return;
        string q = search.Text.Trim();
        string? group = groupBox.SelectedIndex > 0 ? groupBox.SelectedItem as string : null;
        var shownHosts = new HashSet<string>();
        var placed = new List<(GroupSection Sec, List<UIElement> Boxes)>();
        foreach (var sec in ordered)
        {
            var ranks = new List<int>();
            var boxes = new List<UIElement>();
            foreach (var id in sec.Order)
            {
                var h = service.Host(id);
                bool show = h != null
                    && (q.Length == 0 || h.Display.Contains(q, StringComparison.CurrentCultureIgnoreCase) || h.Host.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                        || h.Address.Contains(q, StringComparison.OrdinalIgnoreCase) || h.Groups.Any(g => g.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                    && (group == null || (sec.Key == AlertsKey ? h.Groups.Contains(group) : sec.Key == group))
                    && (problemsOnly.IsChecked != true || service.WorstSeverity(h.HostId) >= 0 || (h.Monitored && h.Availability == Availability.Unavailable));
                if (!show) continue;
                boxes.Add(sec.Boxes[id]);
                ranks.Add(Rank(h!));
                shownHosts.Add(h!.HostId);
            }
            sec.Counts = ranks.GroupBy(r => r).OrderByDescending(g => g.Key).Select(g => (Status(g.Key), g.Count())).ToList();
            if (boxes.Count > 0) placed.Add((sec, boxes));
        }
        grid.Lay(sectionByKey.Values, placed);
        int shown = shownHosts.Count;
        empty.Text = service.Updated == null ? ""
            : sectionByKey.Count == 0 ? T("Zabbix has no hosts this user can see.")
            : shown == 0 ? (problemsOnly.IsChecked == true && q.Length == 0 && group == null ? T("No host has a problem.") : T("No host matches."))
            : "";
        empty.Visibility = empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private HostBox MakeBox(string hostId)
    {
        var box = new HostBox(service, hostId) { Cursor = Cursors.Hand, ToolTip = T("Click for everything about this host. Right-click to make it a widget or hide it.") };
        box.MouseLeftButtonUp += (_, _) => OpenHost(hostId);
        var menu = DaisysApp.Shared.Widgets.Widget.Menu(box, T("Open"), () => OpenHost(hostId),
            () => widgets.CreateHost(hostId, service.Host(hostId)?.Display ?? "", Window.GetWindow(this)));
        var hide = new MenuItem { Header = T("Hide this host") };
        hide.Click += (_, _) =>
        {
            service.Settings.HiddenHosts.Add(hostId);
            service.Settings.Save();
            Update();
        };
        menu.Items.Add(hide);
        return box;
    }

    public void OpenHost(string hostId)
    {
        hostPage?.Close();
        hostPage = new HostPage(service, widgets, hostId);
        hostPage.Back += ShowGrid;
        Content = hostPage;
    }

    private void ShowGrid()
    {
        hostPage?.Close();
        hostPage = null;
        Content = gridPage;
        Update();
    }

    /// <summary>The Widgets card: one row per widget, to edit (move, stretch, options) or delete it.</summary>
    private void ShowWidgets()
    {
        widgetList.Children.Clear();
        noWidgets.Visibility = widgets.All.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var def in widgets.All)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var delete = DaisysApp.Shared.Widgets.Widget.DeleteButton(() => widgets.Delete(def), widgets.TitleOf(def));
            delete.Margin = new Thickness(8, 0, 0, 0);
            var edit = new Button { Content = T("Edit"), ToolTip = T("Move or stretch it, and its options (or right-click the widget)") };
            edit.Click += (_, _) => widgets.Edit(def);
            DockPanel.SetDock(delete, Dock.Right);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(delete);
            row.Children.Add(edit);
            var name = new TextBlock { Text = widgets.TitleOf(def), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            string kind = def.Kind switch { ZabbixWidgetKind.Host => T("host"), ZabbixWidgetKind.Group => T("graph"), _ => T("graph") };
            var detail = Ui.Secondary(def.Kind == ZabbixWidgetKind.Host ? $"  {kind}" : $"  {kind} · " + (def.Minutes < 60 ? F("{0} min", def.Minutes) : F("{0} h", def.Minutes / 60)));
            detail.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { name, detail } });
            widgetList.Children.Add(row);
        }
    }

    /// <summary>One group: its hosts' boxes (Alerts stays first, so it doesn't move).</summary>
    private sealed class Section : GroupSection
    {
        public Dictionary<string, HostBox> Boxes { get; } = new();
        public List<string> Order { get; } = new();
        public override bool Movable => Key != AlertsKey;

        public Section(string key, string title) : base(key) => Title = title;
    }
}
