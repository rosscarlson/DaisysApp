using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>
/// A device's window (from clicking its box): everything it can do, an <see cref="EntityPanel"/> per entity, then what
/// it reads, then its configuration and diagnostic entities under More. Live while it's open; one per device.
/// <b>Hide</b> takes it off the tab (Settings → HA lists it to show again).
/// </summary>
internal sealed class ThingWindow : Window
{
    private static readonly Dictionary<string, ThingWindow> open = new();

    private readonly HaService service;
    private readonly HaHotkeys hotkeys;
    private readonly string key;
    private readonly StackPanel content = new();
    private readonly TextBlock title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock about = Ui.Secondary("", wrap: true);
    private readonly Dictionary<string, EntityPanel> panels = new();
    private readonly Dictionary<string, TextBlock> values = new();
    private List<string> built = new();

    public static void Open(HaService service, HaHotkeys hotkeys, string key, Window? owner)
    {
        if (open.TryGetValue(key, out var w)) { w.Activate(); return; }
        w = new ThingWindow(service, hotkeys, key) { Owner = owner };
        open[key] = w;
        w.Show();
    }

    public static void CloseAll()
    {
        foreach (var w in open.Values.ToList()) w.Close();
    }

    private ThingWindow(HaService service, HaHotkeys hotkeys, string key)
    {
        this.service = service;
        this.hotkeys = hotkeys;
        this.key = key;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var hide = new Button { Content = T("Hide"), MinWidth = 80, ToolTip = T("Take it off the HA tab (Settings → HA shows it again)") };
        hide.Click += (_, _) => HideThing();
        var close = new Button { Content = T("Close"), MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(hide);
        buttons.Children.Add(close);

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Children = { title, about } };
        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) });
        Content = root;

        service.Changed += OnChanged;
        Closed += (_, _) => { service.Changed -= OnChanged; open.Remove(key); };
        Build();
    }

    private void OnChanged(IReadOnlyCollection<string>? ids)
    {
        if (service.Thing(key) is not { } t) { if (ids == null && service.Connected) Close(); return; }
        if (ids == null && !t.All.SequenceEqual(built)) { Build(); return; }
        foreach (var id in ids ?? (IEnumerable<string>)t.All)
        {
            if (service.Entity(id) is not { } e) continue;
            if (panels.TryGetValue(id, out var p)) p.Update(e);
            else if (values.TryGetValue(id, out var v)) v.Text = e.ValueText();
        }
        ShowHead(t);
    }

    private void ShowHead(HaThing t)
    {
        Title = t.Name;
        title.Text = t.Name;
        string area = service.Area(t.AreaId)?.Name ?? "";
        about.Text = string.Join(" · ", new[] { area, t.About }.Where(s => s.Length > 0));
        about.Visibility = about.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Build()
    {
        content.Children.Clear();
        panels.Clear();
        values.Clear();
        if (service.Thing(key) is not { } t) { content.Children.Add(Ui.Secondary(T("Not found in Home Assistant."))); return; }
        built = t.All.ToList();
        ShowHead(t);
        var entities = t.All.Select(service.Entity).OfType<HaEntity>().ToList();
        var shown = t.Shown.ToHashSet();
        var controls = entities.Where(e => shown.Contains(e.EntityId) && Domains.Controllable(e.Domain)).OrderBy(e => t.Shown.IndexOf(e.EntityId)).ToList();
        var read = entities.Where(e => shown.Contains(e.EntityId) && !Domains.Controllable(e.Domain)).ToList();
        var more = entities.Where(e => !shown.Contains(e.EntityId)).ToList();

        foreach (var e in controls) Add(t, e);
        if (read.Count > 0)
        {
            if (controls.Count > 0) content.Children.Add(Heading(T("Readings")));
            foreach (var e in read) Reading(t, e);
        }
        if (more.Count > 0)
        {
            var list = new StackPanel();
            foreach (var e in more.OrderBy(e => Domains.Controllable(e.Domain) ? 0 : 1))
            {
                if (Domains.Controllable(e.Domain)) Add(t, e, list);
                else Reading(t, e, list);
            }
            content.Children.Add(new Expander { Header = T("More"), Content = list, Margin = new Thickness(0, 8, 0, 0), ToolTip = T("Its settings and diagnostics") });
        }
        if (content.Children.Count == 0) content.Children.Add(Ui.Secondary(T("Nothing to show.")));
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };

    private void Add(HaThing t, HaEntity e, Panel? into = null)
    {
        var p = new EntityPanel(service, hotkeys, t, e);
        panels[e.EntityId] = p;
        (into ?? content).Children.Add(p);
    }

    private void Reading(HaThing t, HaEntity e, Panel? into = null)
    {
        var value = new TextBlock { Text = e.ValueText(), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var label = Ui.Secondary(service.ShortName(t, e));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ToolTip = e.EntityId;
        var row = new DockPanel { Margin = new Thickness(2, 3, 2, 3) };
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);
        row.Children.Add(label);
        values[e.EntityId] = value;
        (into ?? content).Children.Add(row);
    }

    private void HideThing()
    {
        if (service.Thing(key) is not { } t) return;
        if (!service.Settings.IsHidden(key)) service.Settings.Hidden.Add(new HiddenThing { Key = key, Name = t.Name });
        service.Settings.Save();
        service.RaiseChanged();
        Close();
    }
}
