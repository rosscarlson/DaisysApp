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
    /// <summary>Alerts first (hosts with a problem or unreachable, worst first), then a box per host group.</summary>
    private readonly StackPanel sections = new();
    private readonly Dictionary<string, Section> sectionByKey = new();
    private const string AlertsKey = "\u0001alerts";
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
        filters.Children.Add(Ui.IconButton("", T("Refresh"), T("Read everything again now"), (_, _) => _ = service.RefreshAsync()));
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
        var body = new StackPanel { Margin = new Thickness(0, 0, 6, 0), Children = { empty, sections, widgetsCard } };
        var scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        gridPage.Children.Add(scroller);

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

        var keys = wantedSections.Select(w => w.Key).ToHashSet();
        foreach (var gone in sectionByKey.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            sections.Children.Remove(sectionByKey[gone].Frame);
            sectionByKey.Remove(gone);
        }
        int place = 0;
        foreach (var (key, list) in wantedSections)
        {
            if (!sectionByKey.TryGetValue(key, out var sec)) sectionByKey[key] = sec = new Section(key, key == AlertsKey ? T("Alerts") : key.Length == 0 ? T("No host group") : key);
            int now = sections.Children.IndexOf(sec.Frame);
            if (now != place)
            {
                if (now >= 0) sections.Children.RemoveAt(now);
                sections.Children.Insert(place, sec.Frame);
            }
            place++;
            Fill(sec, list);
        }
        Filter();
    }

    /// <summary>A section's boxes: one per host, in the order given.</summary>
    private void Fill(Section sec, List<ZHost> hosts)
    {
        var ids = hosts.Select(h => h.HostId).ToHashSet();
        foreach (var gone in sec.Boxes.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            sec.Grid.Children.Remove(sec.Boxes[gone]);
            sec.Boxes.Remove(gone);
        }
        int n = 0;
        foreach (var h in hosts)
        {
            if (!sec.Boxes.TryGetValue(h.HostId, out var box)) sec.Boxes[h.HostId] = box = MakeBox(h.HostId);
            int at = sec.Grid.Children.IndexOf(box);
            if (at != n)
            {
                if (at >= 0) sec.Grid.Children.RemoveAt(at);
                sec.Grid.Children.Insert(n, box);
            }
            box.Update();
            n++;
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
        foreach (var sec in sectionByKey.Values)
        {
            var ranks = new List<int>();
            foreach (var box in sec.Boxes.Values)
            {
                var h = service.Host(box.HostId);
                bool show = h != null
                    && (q.Length == 0 || h.Display.Contains(q, StringComparison.CurrentCultureIgnoreCase) || h.Host.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                        || h.Address.Contains(q, StringComparison.OrdinalIgnoreCase) || h.Groups.Any(g => g.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                    && (group == null || (sec.Key == AlertsKey ? h.Groups.Contains(group) : sec.Key == group))
                    && (problemsOnly.IsChecked != true || service.WorstSeverity(h.HostId) >= 0 || (h.Monitored && h.Availability == Availability.Unavailable));
                box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) continue;
                ranks.Add(Rank(h!));
                shownHosts.Add(h!.HostId);
            }
            sec.Frame.Visibility = ranks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            sec.ShowCounts(ranks.GroupBy(r => r).OrderByDescending(g => g.Key).Select(g => (Status(g.Key), g.Count())));
        }
        int shown = shownHosts.Count;
        // a UniformGrid gives collapsed children no cell, so the shown ones close up
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
        var menu = new ContextMenu();
        void Add(string text, Action a) { var m = new MenuItem { Header = text }; m.Click += (_, _) => a(); menu.Items.Add(m); }
        Add(T("Open"), () => OpenHost(hostId));
        Add(T("Make a widget"), () => widgets.CreateHost(hostId, service.Host(hostId)?.Display ?? "", Window.GetWindow(this)));
        Add(T("Hide this host"), () =>
        {
            service.Settings.HiddenHosts.Add(hostId);
            service.Settings.Save();
            Update();
        });
        box.ContextMenu = menu;
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
            var delete = new Button { Content = T("Delete"), Margin = new Thickness(8, 0, 0, 0) };
            delete.Click += (_, _) => widgets.Delete(def);
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

    /// <summary>One group's outer box: its name, a coloured count per status (worst first), and its hosts' boxes.</summary>
    private sealed class Section
    {
        public string Key { get; }
        public Border Frame { get; }
        public UniformGrid Grid { get; } = new() { Margin = new Thickness(-6, 0, -6, -12) };
        public Dictionary<string, HostBox> Boxes { get; } = new();
        private readonly StackPanel counts = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };

        public Section(string key, string title)
        {
            Key = key;
            var name = Ui.Header(title);
            name.Margin = new Thickness(0);
            name.VerticalAlignment = VerticalAlignment.Center;
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 10), Children = { name, counts } };
            Grid.SizeChanged += (_, _) => Grid.Columns = Math.Max(1, (int)((Grid.ActualWidth + 2) / 250));
            Frame = new Border
            {
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12),
                Child = new StackPanel { Children = { head, Grid } },
            };
            Frame.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
            Frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        }

        /// <summary>A circle per status, its count in it (3 in green: three hosts fine).</summary>
        public void ShowCounts(IEnumerable<((string Color, string Name) Status, int Count)> list)
        {
            counts.Children.Clear();
            foreach (var ((color, what), n) in list)
            {
                var dot = new Border
                {
                    MinWidth = 22, Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = $"{n} {what}",
                    Child = new TextBlock { Text = n.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                Ui.SetBrush(dot, Border.BackgroundProperty, color);
                counts.Children.Add(dot);
            }
        }
    }
}
