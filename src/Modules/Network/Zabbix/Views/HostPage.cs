using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Applets.Network.Zabbix.Api;
using DaisysApp.Applets.Network.Zabbix.Widgets;

namespace DaisysApp.Applets.Network.Zabbix.Views;

/// <summary>
/// One host: tiles like Performance's (CPU, memory, disks, network, temperatures, ping; click one for its history),
/// the graphs made with New graph, its problems, its interfaces and details, and every item Zabbix has for it
/// (double-click one for its history).
/// </summary>
internal sealed class HostPage : DockPanel
{
    private static readonly TimeSpan TileSpan = TimeSpan.FromHours(1);

    private readonly ZabbixService service;
    private readonly ZabbixWidgets widgets;
    public string HostId { get; }
    private readonly TextBlock title = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock subtitle = Ui.Secondary();
    private readonly TextBlock status = Ui.Secondary("", wrap: true);
    private readonly UniformGrid tiles = new() { Columns = 4, Margin = new Thickness(-6, 0, -6, 0) };
    private readonly UniformGrid graphs = new() { Columns = 2, Margin = new Thickness(-6, 0, -6, 0) };
    private readonly StackPanel problems = new();
    private readonly Grid details = new();
    private readonly ObservableCollection<ItemRow> itemRows = new();
    private readonly ICollectionView itemView;
    private readonly TextBox itemSearch = new() { Width = 220 };
    private readonly TextBlock itemCount = Ui.Secondary();
    private readonly Dictionary<string, GroupTile> tileByKey = new();
    private readonly Dictionary<Guid, GraphCard> graphById = new();
    private IDisposable? watch;

    public event Action? Back;

    /// <summary>Hide Device: as the grid's right-click Hide this host, then back to the grid.</summary>
    private void Hide()
    {
        if (!service.Settings.HiddenHosts.Contains(HostId)) service.Settings.HiddenHosts.Add(HostId);
        service.Settings.Save();
        Back?.Invoke();
    }

    public HostPage(ZabbixService service, ZabbixWidgets widgets, string hostId)
    {
        this.service = service;
        this.widgets = widgets;
        HostId = hostId;

        // the top: back, the host, and its buttons
        var back = Ui.IconButton("", T("All hosts"), T("Back to every host (Esc)"), (_, _) => Back?.Invoke());
        back.Margin = new Thickness(0, 0, 14, 0);
        back.VerticalAlignment = VerticalAlignment.Center;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Ui.IconButton("", T("New graph"), T("Draw several of this host's values together on one graph"), (_, _) => NewGraph(null)));
        buttons.Children.Add(Ui.IconButton("", T("Widget"), T("Put this host's box in its own small window you can place anywhere"), (_, _) => widgets.CreateHost(HostId, HostName, Window.GetWindow(this))));
        buttons.Children.Add(Ui.IconButton("", T("Open in Zabbix"), T("This host's latest data in Zabbix's web interface"), (_, _) => OpenInZabbix()));
        buttons.Children.Add(Ui.IconButton("", T("Refresh"), T("Read everything again now"), (_, _) => _ = service.RefreshAsync()));
        buttons.Children.Add(Ui.IconButton("", T("Hide Device"), T("Hide this host from the grid (Settings → Network → Zabbix shows it again)"), (_, _) => Hide()));
        var names = new StackPanel { Children = { title, subtitle } };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(back, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(back);
        top.Children.Add(buttons);
        top.Children.Add(names);
        DockPanel.SetDock(top, Dock.Top);
        Children.Add(top);

        // the body
        var body = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        status.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(status);
        body.Children.Add(tiles);
        body.Children.Add(graphs);

        var columns = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

        var left = new StackPanel();
        var problemsHeader = Ui.Header(T("Problems"));
        left.Children.Add(Ui.Card(new StackPanel { Children = { problemsHeader, problems } }, new Thickness(0, 0, 0, 12)));

        // every item: a search, and a table (double-click one for its history)
        var grid = new DataGrid { Height = 460, BorderThickness = new Thickness(0), IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, EnableRowVirtualization = true };
        grid.SetResourceReference(DataGrid.RowStyleProperty, "EventGridRow");
        grid.Columns.Add(new DataGridTextColumn { Header = T("Name"), Binding = new Binding(nameof(ItemRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 160 });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Last value"), Binding = new Binding(nameof(ItemRow.Value)), Width = 150, SortMemberPath = nameof(ItemRow.Number) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Last check"), Binding = new Binding(nameof(ItemRow.Checked)), Width = 120, SortMemberPath = nameof(ItemRow.CheckedAt) });
        grid.Columns.Add(new DataGridTextColumn { Header = T("Key"), Binding = new Binding(nameof(ItemRow.Key)), Width = 200 });
        itemView = CollectionViewSource.GetDefaultView(itemRows);
        itemView.Filter = o => o is ItemRow r && (itemSearch.Text.Trim() is not { Length: > 0 } q || r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase));
        itemView.SortDescriptions.Add(new SortDescription(nameof(ItemRow.Name), ListSortDirection.Ascending));
        grid.ItemsSource = itemView;
        grid.MouseDoubleClick += (_, e) =>
        {
            if (grid.SelectedItem is ItemRow r && e.OriginalSource is FrameworkElement { DataContext: ItemRow }) OpenItem(r.Item);
        };
        var menu = DaisysApp.Shared.Widgets.Widget.Menu(grid, T("History"), () => { if (grid.SelectedItem is ItemRow r) OpenItem(r.Item); },
            () => { if (grid.SelectedItem is ItemRow r && r.Item.IsNumeric) widgets.CreateItems(HostId, new[] { r.Item.Key }, r.Item.Name, Window.GetWindow(this)); });
        var copy = new MenuItem { Header = T("Copy the key") };
        copy.Click += (_, _) => { if (grid.SelectedItem is ItemRow r) try { Clipboard.SetText(r.Key); } catch { } };
        menu.Items.Add(copy);
        menu.Opened += (_, _) =>
        {
            bool numeric = grid.SelectedItem is ItemRow { Item.IsNumeric: true };
            ((MenuItem)menu.Items[0]).IsEnabled = ((MenuItem)menu.Items[1]).IsEnabled = numeric;
        };
        itemSearch.TextChanged += (_, _) => { itemView.Refresh(); ShowItemCount(); };
        itemSearch.ToolTip = T("Show only items whose name or key contains this");
        var itemsHead = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var glass = Ui.Icon("", 13);
        glass.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(itemSearch, Dock.Right);
        DockPanel.SetDock(glass, Dock.Right);
        itemsHead.Children.Add(itemSearch);
        itemsHead.Children.Add(glass);
        var itemsTitle = Ui.Header(T("Items"));
        itemsTitle.Margin = new Thickness(0);
        itemCount.Margin = new Thickness(10, 0, 0, 0);
        itemCount.VerticalAlignment = VerticalAlignment.Center;
        itemsHead.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { itemsTitle, itemCount } });
        var itemsNote = Ui.Secondary(T("Everything Zabbix collects for this host. Double-click a number for its history; right-click for a widget."), wrap: true);
        itemsNote.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(Ui.Card(new StackPanel { Children = { itemsHead, grid, itemsNote } }, new Thickness(0)));
        columns.Children.Add(left);

        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var right = Ui.Card(new StackPanel { Children = { Ui.Header(T("Details")), details } }, new Thickness(0));
        right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);
        body.Children.Add(columns);

        Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) { watch ??= service.Watch(HostId); Update(); }
            else { watch?.Dispose(); watch = null; }
        };
        service.Changed += OnChanged;
        ZabbixHistoryWindow.ScaleChanged += OnChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !itemSearch.IsKeyboardFocusWithin) { Back?.Invoke(); e.Handled = true; } };
    }

    /// <summary>Stops following the server (the page is closed for good).</summary>
    public void Close()
    {
        service.Changed -= OnChanged;
        ZabbixHistoryWindow.ScaleChanged -= OnChanged;
        watch?.Dispose();
        watch = null;
    }

    private void OnChanged()
    {
        if (IsVisible) Update();
    }

    private string HostName => service.Host(HostId)?.Display ?? HostId;

    private void Update()
    {
        var host = service.Host(HostId);
        var items = service.ItemsOf(HostId);
        title.Text = HostName;
        subtitle.Text = host == null ? "" : string.Join(" · ", new[] { host.Host != host.Display ? host.Host : "", host.Address, string.Join(", ", host.Groups) }.Where(s => s.Length > 0));
        status.Text = service.Error is { } e ? e
            : host == null && service.Updated != null ? T("Zabbix doesn't have this host any more.")
            : items.Count == 0 ? T("Reading the host's items…")
            : "";
        status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTiles(items);
        UpdateGraphs(items);
        UpdateProblems();
        UpdateDetails(host, items);
        UpdateItems(items);
    }

    // ---------------------------------------------------------------- tiles

    private void UpdateTiles(IReadOnlyList<ZItem> items)
    {
        var groups = HostMetrics.Build(items);
        foreach (var gone in tileByKey.Keys.Except(groups.Select(g => g.Key)).ToList())
        {
            tiles.Children.Remove(tileByKey[gone].Root);
            tileByKey.Remove(gone);
        }
        int n = 0;
        foreach (var g in groups)
        {
            if (!tileByKey.TryGetValue(g.Key, out var tile))
            {
                tile = new GroupTile(this, g.Key);
                tileByKey[g.Key] = tile;
                tiles.Children.Insert(Math.Min(n, tiles.Children.Count), tile.Root);
            }
            tile.Update(g);
            n++;
        }
        tiles.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        tiles.Columns = Math.Clamp(groups.Count, 1, 4);
    }

    /// <summary>A tile: the group's value, two lines and its first graph over the last hour. Click for its history.</summary>
    private sealed class GroupTile
    {
        private readonly HostPage page;
        private readonly string key;
        private readonly TextBlock title, value = new() { FontSize = 24, FontWeight = FontWeights.SemiBold, Text = "—" }, line1 = Ui.Secondary(), line2 = Ui.Secondary();
        private readonly LineGraph graph = new() { Height = 64, Margin = new Thickness(0, 10, 0, 0) };
        public Border Root { get; }
        private MetricGroup? group;

        public GroupTile(HostPage page, string key)
        {
            this.page = page;
            this.key = key;
            title = Ui.Header("");
            title.Margin = new Thickness(0);
            title.VerticalAlignment = VerticalAlignment.Center;
            line1.Margin = line2.Margin = new Thickness(0, 2, 0, 0);
            var header = new DockPanel();
            DockPanel.SetDock(value, Dock.Right);
            header.Children.Add(value);
            header.Children.Add(title);
            Root = Ui.Card(new StackPanel { Children = { header, line1, line2, graph } }, new Thickness(6, 0, 6, 12));
            Root.Cursor = Cursors.Hand;
            Root.MouseLeftButtonUp += (_, _) => { if (group != null) page.OpenGroup(group); };
            DaisysApp.Shared.Widgets.Widget.Menu(Root, T("History"), () => { if (group != null) page.OpenGroup(group); },
                () => { if (group != null) page.widgets.CreateGroup(page.HostId, key, group.Title, Window.GetWindow(page)); });
        }

        public void Update(MetricGroup g)
        {
            group = g;
            title.Text = g.Title;
            value.Text = g.Value;
            line1.Text = g.Line1;
            line2.Text = g.Line2;
            Root.ToolTip = F("Click for {0} history. Right-click to make it a widget.", g.Title);
            var from = DateTime.Now - TileSpan;
            var lines = g.Main.Lines;
            page.service.Want(lines.Select(l => l.Item), from);
            var unit = lines[0].Item.Unit;
            graph.Show(lines.Select((l, i) => new GraphSeries
            {
                Name = l.Item.Name, Metric = l.Item.Unit, Color = l.Color, Fill = i == 0,
                Points = page.service.HistoryOf(l.Item.ItemId, from).Select(p => (p.Time, p.Value)).ToList(),
            }).ToList(), from, DateTime.Now, page.TopOf(unit), page.Gap);
        }
    }

    private double? TopOf(ZUnit unit) => unit.IsPercent ? 100 : service.Settings.ScaleMax.TryGetValue(unit.Kind, out double v) ? v : null;

    private TimeSpan Gap => TimeSpan.FromSeconds(Math.Max(180, service.Settings.RefreshSeconds * 3));

    // ---------------------------------------------------------------- graphs made with New graph

    private IEnumerable<CustomGraph> MyGraphs => service.Settings.Graphs.Where(g => g.HostId == HostId);

    private static List<(ZItem Item, string Color)> LinesOf(CustomGraph g, IReadOnlyList<ZItem> items) =>
        g.ItemKeys.Select(k => items.FirstOrDefault(i => i.Key == k)).OfType<ZItem>()
            .Select((i, n) => (i, HostMetrics.Palette[n % HostMetrics.Palette.Length])).ToList();

    private void UpdateGraphs(IReadOnlyList<ZItem> items)
    {
        var mine = MyGraphs.ToList();
        foreach (var gone in graphById.Keys.Except(mine.Select(g => g.Id)).ToList())
        {
            graphs.Children.Remove(graphById[gone].Root);
            graphById.Remove(gone);
        }
        foreach (var g in mine)
        {
            if (!graphById.TryGetValue(g.Id, out var card))
            {
                card = new GraphCard(this, g);
                graphById[g.Id] = card;
                graphs.Children.Add(card.Root);
            }
            card.Update(LinesOf(g, items));
        }
        graphs.Visibility = mine.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        graphs.Columns = mine.Count == 1 ? 1 : 2;
    }

    /// <summary>A graph made with New graph: its lines over the last hour, with a legend. Click for its history.</summary>
    private sealed class GraphCard
    {
        private readonly HostPage page;
        private readonly CustomGraph def;
        private readonly TextBlock title = Ui.Header("");
        private readonly ContentControl legend = new();
        private readonly LineGraph graph = new() { Height = 150, Margin = new Thickness(0, 6, 0, 0), Detailed = true, TimeSpans = 3 };
        public Border Root { get; }
        private string? highlight;

        public GraphCard(HostPage page, CustomGraph def)
        {
            this.page = page;
            this.def = def;
            title.Margin = new Thickness(0, 0, 16, 0);
            var header = new DockPanel();
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);
            header.Children.Add(legend);
            Root = Ui.Card(new StackPanel { Children = { header, graph } }, new Thickness(6, 0, 6, 12));
            Root.ToolTip = T("Double-click for its history. Right-click to change it, make it a widget or delete it.");
            Root.MouseLeftButtonUp += (_, e) => { if (e.ClickCount == 2) page.OpenGraph(def); };
            var menu = DaisysApp.Shared.Widgets.Widget.Menu(Root, T("History"), () => page.OpenGraph(def),
                () => page.widgets.CreateItems(page.HostId, def.ItemKeys, def.Title, Window.GetWindow(page)));
            void Add(string text, Action a) { var m = new MenuItem { Header = text }; m.Click += (_, _) => a(); menu.Items.Add(m); }
            Add(T("Change…"), () => page.NewGraph(def));
            menu.Items.Add(new Separator());
            Add(T("Delete this graph"), () =>
            {
                page.service.Settings.Graphs.RemoveAll(g => g.Id == def.Id);
                page.service.Settings.Save();
                page.Update();
            });
        }

        public void Update(List<(ZItem Item, string Color)> lines)
        {
            title.Text = def.Title;
            var from = DateTime.Now - TileSpan;
            if (lines.Count == 0)
            {
                graph.Show(Array.Empty<GraphSeries>(), from, DateTime.Now, null, page.Gap);
                legend.Content = Ui.Secondary(page.service.ItemsOf(page.HostId).Count == 0 ? "" : T("The host doesn't have these items any more."));
                return;
            }
            page.service.Want(lines.Select(l => l.Item), from);
            var unit = lines[0].Item.Unit;
            graph.AxisFormat = unit.Axis;
            graph.Highlight = highlight;
            graph.Show(lines.Select(l => new GraphSeries
            {
                Name = l.Item.Name, Metric = l.Item.Unit, Color = l.Color, Fill = lines.Count == 1,
                Points = page.service.HistoryOf(l.Item.ItemId, from).Select(p => (p.Time, p.Value)).ToList(),
            }).ToList(), from, DateTime.Now, page.TopOf(unit), page.Gap);
            var l2 = Ui.Legend(graph, lines.Select(l => (l.Item.Name, l.Color)), h => highlight = h);
            l2.HorizontalAlignment = HorizontalAlignment.Right;
            legend.Content = l2;
        }
    }

    // ---------------------------------------------------------------- problems, details, items

    private void UpdateProblems()
    {
        problems.Children.Clear();
        var list = service.ProblemsOf(HostId).OrderByDescending(p => p.Severity).ThenByDescending(p => p.Since).ToList();
        if (list.Count == 0) { problems.Children.Add(Ui.Secondary(service.Updated == null ? "" : T("None: nothing is firing on this host."))); return; }
        foreach (var p in list)
        {
            var swatch = new Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 4, 10, 0), VerticalAlignment = VerticalAlignment.Top, Fill = Ui.Brush(this, ZProblem.SeverityColor(p.Severity)) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = p.Description, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(Ui.Secondary(F("{0} · for {1} (since {2:g})", p.SeverityName, Ui.Duration(p.Since), p.Since)));
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(swatch, Dock.Left);
            row.Children.Add(swatch);
            row.Children.Add(text);
            problems.Children.Add(row);
        }
    }

    private void UpdateDetails(ZHost? host, IReadOnlyList<ZItem> items)
    {
        details.Children.Clear();
        details.RowDefinitions.Clear();
        if (host == null) return;
        void Row(string label, string value, string? color = null)
        {
            if (value.Length == 0) return;
            int r = details.RowDefinitions.Count;
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Ui.Secondary(label);
            l.Margin = new Thickness(0, 0, 12, 6);
            l.VerticalAlignment = VerticalAlignment.Top;
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            if (color != null) Ui.SetBrush(v, TextBlock.ForegroundProperty, color);
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            details.Children.Add(l);
            details.Children.Add(v);
        }
        Row(T("Status"), !host.Monitored ? T("Not monitored") : host.InMaintenance ? T("In maintenance") : T("Monitored"));
        foreach (var i in host.Interfaces.OrderByDescending(i => i.Main))
        {
            string state = i.Available switch { Availability.Available => T("available"), Availability.Unavailable => T("unavailable"), _ => T("not checked") };
            Row(F("{0} interface", i.TypeName), $"{i.Address}:{i.Port} · {state}" + (i.Error.Length > 0 ? $"\n{i.Error}" : ""),
                i.Available == Availability.Unavailable ? "ErrorTextBrush" : null);
        }
        Row(T("Groups"), string.Join(", ", host.Groups));
        string Find(params string[] keys) => keys.Select(k => items.FirstOrDefault(i => i.Key == k)).OfType<ZItem>().Select(i => i.ValueText).FirstOrDefault(v => v != "—") ?? "";
        Row(T("System"), Find("system.sw.os", "system.sw.os[]", "system.uname"));
        Row(T("Up for"), HostMetrics.Uptime(items) is { } up ? up.ValueText : "");
        Row(T("Name"), Find("system.hostname", "system.hostname[]"));
        Row(T("Agent"), Find("agent.version"));
        Row(T("Description"), host.Description);
        Row(T("Items"), items.Count > 0 ? F("{0:#,0} ({1:#,0} not supported)", items.Count, items.Count(i => i.NotSupported)) : "");
    }

    private void UpdateItems(IReadOnlyList<ZItem> items)
    {
        var byId = itemRows.ToDictionary(r => r.Item.ItemId);
        var now = items.Select(i => i.ItemId).ToHashSet();
        foreach (var gone in itemRows.Where(r => !now.Contains(r.Item.ItemId)).ToList()) itemRows.Remove(gone);
        foreach (var i in items)
        {
            if (byId.TryGetValue(i.ItemId, out var row)) row.Set(i);
            else itemRows.Add(new ItemRow(i));
        }
        ShowItemCount();
    }

    private void ShowItemCount()
    {
        int shown = itemView.Cast<object>().Count();
        itemCount.Text = shown == itemRows.Count ? P(itemRows.Count, "{0} item", "{0} items") : F("{0} of {1}", shown, itemRows.Count);
    }

    /// <summary>One row of the items table, updated in place so the sort and selection stay.</summary>
    private sealed class ItemRow(ZItem item) : INotifyPropertyChanged
    {
        public ZItem Item { get; private set; } = item;
        public string Name => Item.Name;
        public string Key => Item.Key;
        public string Value => Item.NotSupported ? T("Not supported: ") + Item.Error : Item.ValueText.Replace('\n', ' ');
        public double Number => Item.Value;
        public DateTime CheckedAt => Item.LastClock ?? DateTime.MinValue;
        public string Checked => Item.LastClock is { } t ? Ui.Ago(t) : "—";

        public void Set(ZItem i)
        {
            bool changed = i.LastValue != Item.LastValue || i.LastClock != Item.LastClock || i.NotSupported != Item.NotSupported || i.Name != Item.Name;
            Item = i;
            if (changed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            else PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ---------------------------------------------------------------- windows

    private void OpenGroup(MetricGroup g) => Open(g.Title,
        () => HostMetrics.Build(service.ItemsOf(HostId)).FirstOrDefault(x => x.Key == g.Key)?.Graphs ?? (IReadOnlyList<GraphDef>)Array.Empty<GraphDef>(),
        () => widgets.CreateGroup(HostId, g.Key, g.Title, null));

    private void OpenGraph(CustomGraph g) => Open(g.Title,
        () => LinesOf(g, service.ItemsOf(HostId)) is { Count: > 0 } lines ? new[] { new GraphDef(g.Title, lines) } : Array.Empty<GraphDef>(),
        () => widgets.CreateItems(HostId, g.ItemKeys, g.Title, null));

    private void OpenItem(ZItem item)
    {
        if (!item.IsNumeric)
        {
            MessageBox.Show(Window.GetWindow(this), item.LastValue.Length > 0 ? item.LastValue : T("(empty)"), item.Name);
            return;
        }
        string key = item.Key;
        Open(item.Name,
            () => service.ItemsOf(HostId).FirstOrDefault(i => i.Key == key) is { } i ? new[] { new GraphDef(i.Name, new[] { (i, "AccentBrush") }) } : Array.Empty<GraphDef>(),
            () => widgets.CreateItems(HostId, new[] { key }, item.Name, null));
    }

    private void Open(string what, Func<IReadOnlyList<GraphDef>> graphs, Action widget)
    {
        new ZabbixHistoryWindow(service, HostId, $"{HostName} · {what}", service.Host(HostId)?.Address ?? "", graphs, widget)
        {
            Owner = Window.GetWindow(this),
        }.Show();
    }

    private void NewGraph(CustomGraph? editing)
    {
        var items = service.ItemsOf(HostId);
        if (items.Count == 0) { status.Text = T("The host's items haven't been read yet."); status.Visibility = Visibility.Visible; return; }
        var picker = new GraphPickerWindow(HostName, items, editing) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || picker.Keys.Count == 0) return;
        var g = editing ?? new CustomGraph { HostId = HostId };
        g.Title = picker.GraphTitle.Length > 0 ? picker.GraphTitle : T("Graph");
        g.ItemKeys = picker.Keys;
        if (editing == null) service.Settings.Graphs.Add(g);
        service.Settings.Save();
        if (graphById.Remove(g.Id, out var old)) graphs.Children.Remove(old.Root); // drawn again with its new lines
        Update();
    }

    private void OpenInZabbix()
    {
        string url = $"{service.WebUrl}zabbix.php?action=latest.view&hostids%5B%5D={HostId}&filter_hostids%5B%5D={HostId}&filter_set=1";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { status.Text = ex.Message; status.Visibility = Visibility.Visible; }
    }
}
