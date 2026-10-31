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
    private readonly ScrollViewer scroller;
    private readonly GroupGrid grid;
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
        filters.Children.Add(Ui.Spaced(Ui.IconButton("", T("Refresh"), T("Connect again and read everything"), (_, _) => service.Reconnect())));
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
        grid = new GroupGrid(layout, scroller, DragFormat, () => S.GridColumns, () => S.GridRows, () => ordered.Select(s => s.Key), keys =>
        {
            S.GroupOrder[S.GroupBy] = keys;
            S.Save();
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
        var placed = new List<(GroupSection Sec, List<UIElement> Boxes)>();
        int shown = 0;
        foreach (var sec in ordered)
        {
            var list = new List<UIElement>();
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
        grid.Lay(sectionByKey.Values, placed);
        empty.Text = service.Updated == null ? ""
            : service.Things.Count == 0 ? T("Home Assistant has no devices this user can see.")
            : shown == 0 ? (onOnly.IsChecked == true && q.Length == 0 && area == null && type == null ? T("Nothing is on.") : T("Nothing matches."))
            : "";
        empty.Visibility = empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>One group: its devices.</summary>
    private sealed class Section(string key) : GroupSection(key)
    {
        public List<string> Order { get; } = new();
        /// <summary>The ones the filter lets through.</summary>
        public List<string> Shown { get; } = new();
    }
}
