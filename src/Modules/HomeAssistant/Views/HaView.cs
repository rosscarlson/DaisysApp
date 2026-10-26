using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>
/// The HA tab, laid out as Zabbix's: a box per device (click one for its window), in a box per area, floor or type
/// (dragged into the order wanted), packed into one grid; filtered by a search, an area, a type, or what's on. Before a
/// server is set up, the connection form instead. Only draws while it's showing.
/// </summary>
internal sealed class HaView : ContentControl
{
    private readonly HaService service;
    private readonly HaHotkeys hotkeys;
    private HaSettings S => service.Settings;
    private readonly DockPanel gridPage = new();
    private readonly FrameworkElement setupPage;
    private readonly Grid layout = new() { Margin = new Thickness(-6, 0, -6, 0) };
    private readonly Dictionary<string, Section> sectionByKey = new();
    private readonly List<Section> ordered = new();
    private readonly Dictionary<string, ThingBox> boxes = new();
    private const string DragFormat = "DaisysApp.HaGroup";
    private const double BoxWidth = 250;
    private readonly ScrollViewer scroller;
    private int laidColumns;
    private bool dirty = true;
    private readonly TextBlock status = Ui.Secondary("", wrap: true);
    private readonly TextBlock empty = Ui.Secondary("", wrap: true);
    private readonly TextBox search = new() { Width = 180 };
    private readonly ComboBox groupBy = new() { Width = 130, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ComboBox areaBox = new() { Width = 160, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ComboBox typeBox = new() { Width = 140, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ToggleButton onOnly = new() { Content = T("On only"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
    private bool filling;

    private static readonly (string Key, string Name)[] Groupings = { ("Area", T("By area")), ("Floor", T("By floor")), ("Type", T("By type")) };

    public HaView(HaService service, HaHotkeys hotkeys)
    {
        this.service = service;
        this.hotkeys = hotkeys;
        Focusable = false;

        // the bar: what's happening, and the filters
        var filters = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glass = Ui.Icon("", 13);
        glass.Margin = new Thickness(0, 0, 8, 0);
        filters.Children.Add(glass);
        filters.Children.Add(search);
        filters.Children.Add(groupBy);
        filters.Children.Add(areaBox);
        filters.Children.Add(typeBox);
        onOnly.SetResourceReference(StyleProperty, "ChipToggle");
        filters.Children.Add(onOnly);
        filters.Children.Add(Ui.IconButton("", T("Refresh"), T("Connect again and read everything"), (_, _) => service.Reconnect()));
        search.ToolTip = T("Name, area or entity");
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
        System.Windows.Automation.AutomationProperties.SetName(groupBy, T("Group by"));
        System.Windows.Automation.AutomationProperties.SetName(areaBox, T("Area"));
        System.Windows.Automation.AutomationProperties.SetName(typeBox, T("Type"));
        foreach (var (_, n) in Groupings) groupBy.Items.Add(n);
        groupBy.SelectedIndex = Math.Max(0, Array.FindIndex(Groupings, g => g.Key == S.GroupBy));
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(filters, Dock.Right);
        bar.Children.Add(filters);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(0, 0, 12, 0);
        bar.Children.Add(status);
        DockPanel.SetDock(bar, Dock.Top);
        gridPage.Children.Add(bar);

        empty.Margin = new Thickness(0, 0, 0, 12);
        var body = new StackPanel { Margin = new Thickness(0, 0, 6, 0), Children = { empty, layout } };
        scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        gridPage.Children.Add(scroller);
        scroller.SizeChanged += (_, e) =>
        {
            if (Columns() != laidColumns) Filter();
            else if (e.HeightChanged) FitRows();
        };

        // before there's a server: the form, right here
        var form = new ConnectionForm(service);
        var setup = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        setup.Children.Add(Ui.Header(T("Connect to Home Assistant")));
        setup.Children.Add(form);
        setupPage = new ScrollViewer { Content = Ui.Card(setup, new Thickness(0)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        search.TextChanged += (_, _) => Filter();
        areaBox.SelectionChanged += (_, _) => Filter();
        typeBox.SelectionChanged += (_, _) => Filter();
        onOnly.Click += (_, _) => Filter();
        groupBy.SelectionChanged += (_, _) =>
        {
            if (groupBy.SelectedIndex < 0) return;
            S.GroupBy = Groupings[groupBy.SelectedIndex].Key;
            S.Save();
            Update();
        };
        service.Changed += OnChanged;
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Show(); };
        Show();
    }

    private bool Ready => S.HasServer && (S.Configured || service.Things.Count > 0);

    /// <summary>The setup form until a server has answered, then the devices.</summary>
    private void Show()
    {
        if (!Ready) { Content = setupPage; return; }
        Content = gridPage;
        if (dirty) Update();
        else ShowStatus();
    }

    private void OnChanged(IReadOnlyCollection<string>? ids)
    {
        if (!IsVisible || (Content == setupPage && Ready)) { dirty = true; if (IsVisible) Show(); return; }
        if (Content != gridPage) return;
        if (ids == null || dirty) { Update(); return; }
        // only some states changed: their boxes, the counts, and the filter when it's about what's on
        foreach (var b in boxes.Values) if (b.Shows(ids)) b.Update();
        if (onOnly.IsChecked == true) Filter();
        else foreach (var s in ordered) s.ShowCounts(Counts(s));
        ShowStatus();
    }

    private void ShowStatus()
    {
        var shown = Visible().ToList();
        int on = shown.Count(t => t.Main != null && service.Entity(t.Main) is { } e && !e.Unavailable && Domains.IsOn(e));
        string text = service.Error is { } err ? err
            : service.Connecting && service.Updated == null ? T("Connecting…")
            : string.Join(" · ", new[]
            {
                F("Home Assistant {0}", service.Version),
                P(shown.Count, "{0} device", "{0} devices"),
                P(on, "{0} on", "{0} on"),
                service.Updated is { } u ? F("updated {0:T}", u) : "",
            }.Where(s => s.Length > 0));
        bool bad = service.Error != null;
        if (service.Problem is { } p && DateTime.Now - p.At < TimeSpan.FromSeconds(15)) { text = p.Text + "  ·  " + text; bad = true; }
        status.Text = text;
        if (bad) status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else status.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>The devices the tab has: not hidden, with something to switch unless sensors-only ones are wanted, and
    /// media players only when they're in an area (Plex makes one for every person a library is shared with).</summary>
    private IEnumerable<HaThing> Visible() => service.Things.Where(t => !S.IsHidden(t.Key) && (S.ShowSensorsOnly || t.Main != null)
        && (t.Type != "media_player" || service.Area(t.AreaId) != null || S.ShowMediaWithoutArea));

    /// <summary>Which group a device is in, and the group's name, for the way they're grouped.</summary>
    private (string Key, string Name) GroupOf(HaThing t)
    {
        switch (S.GroupBy)
        {
            case "Type":
                string type = Domains.TypeName(t.Type);
                return (type, type);
            case "Floor":
                var floor = service.Floor(service.Area(t.AreaId)?.FloorId);
                return floor != null ? (floor.Id, floor.Name) : ("", T("No floor"));
            default:
                var area = service.Area(t.AreaId);
                return area != null ? (area.Id, area.Name) : ("", T("No area"));
        }
    }

    /// <summary>The order groups come in before any has been dragged: floors by level, types as the app ranks them, areas by name; the ones without last.</summary>
    private IComparable DefaultPlace(string key, string name, List<HaThing> things) => S.GroupBy switch
    {
        "Floor" => (key.Length == 0 ? 1 : 0, service.Floor(key)?.Level ?? 0, name),
        "Type" => (0, things.Min(t => Domains.Rank(t.Type)), name),
        _ => (key.Length == 0 ? 1 : 0, 0, name),
    };

    private void Update()
    {
        dirty = false;
        if (Content != gridPage) return;
        ShowStatus();
        var things = Visible().ToList();

        // the area and type pickers
        filling = true;
        Refill(areaBox, T("All areas"), things.Select(t => service.Area(t.AreaId)?.Name ?? T("No area")));
        Refill(typeBox, T("All types"), things.Select(t => Domains.TypeName(t.Type)));
        filling = false;

        // the groups and their devices
        var groups = things.GroupBy(GroupOf).Select(g => (g.Key.Key, g.Key.Name, Things: g.ToList())).ToList();
        if (!S.GroupOrder.TryGetValue(S.GroupBy, out var order)) order = new List<string>();
        int Place(string key) { int i = order.IndexOf(key); return i < 0 ? int.MaxValue : i; }
        groups = groups.OrderBy(g => Place(g.Key)).ThenBy(g => DefaultPlace(g.Key, g.Name, g.Things)).ToList();
        var keys = groups.Select(g => g.Key).ToHashSet();
        foreach (var gone in sectionByKey.Keys.Where(k => !keys.Contains(k)).ToList()) sectionByKey.Remove(gone);
        var thingKeys = things.Select(t => t.Key).ToHashSet();
        foreach (var gone in boxes.Keys.Where(k => !thingKeys.Contains(k)).ToList()) boxes.Remove(gone);
        ordered.Clear();
        foreach (var (key, name, list) in groups)
        {
            if (!sectionByKey.TryGetValue(key, out var sec)) sectionByKey[key] = sec = new Section(key);
            sec.Title = name;
            sec.Order.Clear();
            foreach (var t in list)
            {
                if (!boxes.TryGetValue(t.Key, out var box)) boxes[t.Key] = box = MakeBox(t.Key);
                else box.Update();
                sec.Order.Add(t.Key);
            }
            ordered.Add(sec);
        }
        Filter();
    }

    private static void Refill(ComboBox box, string all, IEnumerable<string> names)
    {
        var current = box.SelectedItem as string;
        var wanted = new List<string> { all };
        wanted.AddRange(names.Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
        if (box.Items.Cast<string>().SequenceEqual(wanted)) return;
        box.ItemsSource = wanted;
        box.SelectedItem = current != null && wanted.Contains(current) ? current : wanted[0];
    }

    private int Columns() =>
        S.GridColumns > 0 ? S.GridColumns : Math.Max(1, (int)((scroller.ActualWidth - 10) / BoxWidth));

    private List<((string Color, string Name) Status, int Count)> Counts(Section sec)
    {
        int on = 0, off = 0, gone = 0;
        foreach (var key in sec.Shown)
        {
            if (service.Thing(key) is not { } t) continue;
            var es = t.Shown.Select(service.Entity).OfType<HaEntity>().ToList();
            if (es.Count > 0 && es.All(e => e.Unavailable)) gone++;
            else if (t.Main != null && service.Entity(t.Main) is { } m && Domains.CanToggle(m)) { if (!m.Unavailable && Domains.IsOn(m)) on++; else off++; }
        }
        var list = new List<((string, string), int)>();
        if (on > 0) list.Add(((ThingBox.OnColor, T("on")), on));
        if (off > 0) list.Add((("TextSecondaryBrush", T("off")), off));
        if (gone > 0) list.Add((("DangerBrush", T("unavailable")), gone));
        return list;
    }

    private void Filter()
    {
        if (filling || Content != gridPage) return;
        string q = search.Text.Trim();
        string? area = areaBox.SelectedIndex > 0 ? areaBox.SelectedItem as string : null;
        string? type = typeBox.SelectedIndex > 0 ? typeBox.SelectedItem as string : null;
        var placed = new List<(Section Sec, List<ThingBox> Boxes)>();
        int shown = 0;
        foreach (var sec in ordered)
        {
            var list = new List<ThingBox>();
            sec.Shown.Clear();
            foreach (var key in sec.Order)
            {
                if (service.Thing(key) is not { } t) continue;
                string areaName = service.Area(t.AreaId)?.Name ?? T("No area");
                var main = t.Main != null ? service.Entity(t.Main) : null;
                bool show = (q.Length == 0 || t.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || areaName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                        || t.About.Contains(q, StringComparison.CurrentCultureIgnoreCase) || t.Shown.Any(id => id.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    && (area == null || areaName == area)
                    && (type == null || Domains.TypeName(t.Type) == type)
                    && (onOnly.IsChecked != true || (main != null && !main.Unavailable && Domains.IsOn(main)));
                if (!show) continue;
                list.Add(boxes[key]);
                sec.Shown.Add(key);
            }
            shown += list.Count;
            sec.Counts = Counts(sec);
            if (list.Count > 0) placed.Add((sec, list));
        }
        Lay(placed);
        empty.Text = service.Updated == null ? ""
            : service.Things.Count == 0 ? T("Home Assistant has no devices this user can see.")
            : shown == 0 ? (onOnly.IsChecked == true && q.Length == 0 && area == null && type == null ? T("Nothing is on.") : T("Nothing matches."))
            : "";
        empty.Visibility = empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Packs the groups into the grid in order, as Zabbix does: each takes as many cells as it has devices
    /// (the rest of the row if it has more, going on at the start of the next).</summary>
    private void Lay(List<(Section Sec, List<ThingBox> Boxes)> placed)
    {
        foreach (var sec in sectionByKey.Values) sec.Detach();
        layout.Children.Clear();
        layout.RowDefinitions.Clear();
        layout.ColumnDefinitions.Clear();
        int columns = laidColumns = Columns();
        for (int c = 0; c < columns; c++) layout.ColumnDefinitions.Add(new ColumnDefinition());
        int row = 0, col = 0;
        foreach (var (sec, list) in placed)
        {
            for (int at = 0; at < list.Count;)
            {
                int take = Math.Min(columns - col, list.Count - at);
                if (layout.RowDefinitions.Count <= row) layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (piece, head) = sec.Piece(list.GetRange(at, take), first: at == 0, more: at + take < list.Count);
                piece.AllowDrop = true;
                piece.DragOver += (_, e) => { e.Effects = CanDrop(e, sec) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                piece.Drop += (_, e) => { if (CanDrop(e, sec)) MoveGroup((string)e.Data.GetData(DragFormat), sec.Key); e.Handled = true; };
                DragFrom(head, sec.Key);
                Grid.SetRow(piece, row);
                Grid.SetColumn(piece, col);
                Grid.SetColumnSpan(piece, take);
                layout.Children.Add(piece);
                at += take;
                col += take;
                if (col == columns) { col = 0; row++; }
            }
        }
        FitRows();
    }

    private void FitRows()
    {
        int rows = S.GridRows;
        double each = rows > 0 && scroller.ViewportHeight > 0 ? Math.Floor(scroller.ViewportHeight / rows) : 0;
        foreach (var r in layout.RowDefinitions) r.MinHeight = each;
    }

    private static bool CanDrop(DragEventArgs e, Section target) =>
        e.Data.GetDataPresent(DragFormat) && (string)e.Data.GetData(DragFormat) != target.Key;

    /// <summary>A group's title starts a drag; dropped on another group, it takes that one's place.</summary>
    private static void DragFrom(FrameworkElement head, string key)
    {
        Point? down = null;
        head.Cursor = Cursors.SizeAll;
        head.ToolTip = T("Drag to move");
        head.MouseLeftButtonDown += (_, e) => down = e.GetPosition(head);
        head.MouseLeftButtonUp += (_, _) => down = null;
        head.MouseMove += (_, e) =>
        {
            if (down is not { } d || e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
            var v = e.GetPosition(head) - d;
            if (Math.Abs(v.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(v.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            DragDrop.DoDragDrop(head, new DataObject(DragFormat, key), DragDropEffects.Move);
        };
    }

    /// <summary>Puts a group where another is (the others move along) and keeps the order, for this way of grouping.</summary>
    private void MoveGroup(string key, string onto)
    {
        var keys = ordered.Select(s => s.Key).ToList();
        int from = keys.IndexOf(key), to = keys.IndexOf(onto);
        if (from < 0 || to < 0) return;
        keys.RemoveAt(from);
        keys.Insert(to, key);
        S.GroupOrder[S.GroupBy] = keys;
        S.Save();
        Dispatcher.BeginInvoke(Update); // after the drag has finished with the piece it started from
    }

    private ThingBox MakeBox(string key)
    {
        var box = new ThingBox(service, key) { Cursor = Cursors.Hand, ToolTip = null };
        box.MouseLeftButtonUp += (_, _) => Open(key);
        var menu = new ContextMenu();
        void Add(string text, Action a) { var m = new MenuItem { Header = text }; m.Click += (_, _) => a(); menu.Items.Add(m); }
        Add(T("Open"), () => Open(key));
        Add(T("Hide"), () =>
        {
            if (service.Thing(key) is not { } t) return;
            if (!S.IsHidden(key)) S.Hidden.Add(new HiddenThing { Key = key, Name = t.Name });
            S.Save();
            service.RaiseChanged();
        });
        box.ContextMenu = menu;
        return box;
    }

    private void Open(string key) => ThingWindow.Open(service, hotkeys, key, Window.GetWindow(this));

    /// <summary>One group: its devices' boxes and its coloured counts, drawn as a piece per grid row it's on.</summary>
    private sealed class Section
    {
        public string Key { get; }
        public string Title { get; set; } = "";
        public List<string> Order { get; } = new();
        /// <summary>The ones the filter lets through.</summary>
        public List<string> Shown { get; } = new();
        public List<((string Color, string Name) Status, int Count)> Counts { get; set; } = new();
        private readonly List<UniformGrid> grids = new();
        private StackPanel? countsPanel;

        public Section(string key) => Key = key;

        public void Detach()
        {
            foreach (var g in grids) g.Children.Clear();
            grids.Clear();
            countsPanel = null;
        }

        public (Border Piece, FrameworkElement Head) Piece(List<ThingBox> boxes, bool first, bool more)
        {
            var name = Ui.Header(Title);
            name.Margin = new Thickness(0);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 10), Background = Brushes.Transparent, Children = { name } };
            if (first)
            {
                countsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                ShowCounts(Counts);
                head.Children.Add(countsPanel);
            }
            else name.Opacity = 0.55;
            var grid = new UniformGrid { Rows = 1, Columns = boxes.Count, Margin = new Thickness(-6, 0, -6, -12) };
            foreach (var b in boxes) grid.Children.Add(b);
            grids.Add(grid);
            DockPanel.SetDock(head, Dock.Top);
            double l = first ? 10 : 2, r = more ? 2 : 10;
            var piece = new Border
            {
                CornerRadius = new CornerRadius(l, r, r, l), BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(6, 0, 6, 12),
                Child = new DockPanel { Children = { head, grid } },
            };
            piece.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
            piece.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return (piece, head);
        }

        public void ShowCounts(List<((string Color, string Name) Status, int Count)> counts)
        {
            Counts = counts;
            if (countsPanel == null) return;
            countsPanel.Children.Clear();
            foreach (var ((color, what), n) in counts) countsPanel.Children.Add(Dot(color, $"{n} {what}", n));
        }

        /// <summary>A circle with a count in it (3 in yellow: three on).</summary>
        private static Border Dot(string color, string tip, int n)
        {
            var dot = new Border
            {
                MinWidth = 22, Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 5, 0),
                ToolTip = tip,
                Child = new TextBlock { Text = n.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Ui.SetBrush(dot, Border.BackgroundProperty, color);
            return dot;
        }
    }
}
