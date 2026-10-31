using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>
/// One device (or an entity on its own) at a glance, as Zabbix's host box: a dot (lit in the light's colour, red when
/// it can't be reached), its name and a quick control for its main entity, what that's doing, a row for each of its
/// other controls (up to three), and its sensors' values. Click it for its window.
/// </summary>
internal sealed class ThingBox : Border
{
    private const int MoreRows = 3, SensorValues = 4;
    public const string OnColor = "#F7C948";

    private readonly HaService service;
    public string Key { get; }
    private readonly Ellipse dot = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock name = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly QuickControl quick;
    private readonly TextBlock summary = Ui.Secondary();
    private readonly StackPanel rows = new();
    private readonly TextBlock sensors = Ui.Secondary("", wrap: true);
    private readonly List<(string Id, TextBlock Name, QuickControl Control)> rowViews = new();

    public ThingBox(HaService service, string key)
    {
        this.service = service;
        Key = key;
        SetResourceReference(StyleProperty, "Card");
        Margin = new Thickness(6, 0, 6, 12);
        quick = new QuickControl(service) { Margin = new Thickness(8, 0, 0, 0) };

        var top = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(quick, Dock.Right);
        top.Children.Add(dot);
        top.Children.Add(quick);
        top.Children.Add(name);
        summary.Margin = new Thickness(18, 2, 0, 0);
        rows.Margin = new Thickness(0, 6, 0, 0);
        sensors.Margin = new Thickness(0, 6, 0, 0);
        Child = new StackPanel { Children = { top, summary, rows, sensors } };
        Update();
    }

    /// <summary>Whether it shows any of these entities (so only those boxes are drawn again).</summary>
    public bool Shows(IReadOnlyCollection<string> ids) => service.Thing(Key) is { } t && t.Shown.Any(ids.Contains);

    public void Update()
    {
        if (service.Thing(Key) is not { } t) return;
        name.Text = t.Name;
        name.ToolTip = t.About.Length > 0 ? $"{t.Name}\n{t.About}" : t.Name;
        var main = t.Main != null ? service.Entity(t.Main) : null;
        quick.Show(main);

        // the other things it can do, a row each
        var others = t.Shown.Where(id => id != t.Main && Domains.Controllable(id[..id.IndexOf('.')])).Take(MoreRows).ToList();
        if (!rowViews.Select(r => r.Id).SequenceEqual(others))
        {
            rows.Children.Clear();
            rowViews.Clear();
            foreach (var id in others)
            {
                var n = Ui.Secondary();
                n.VerticalAlignment = VerticalAlignment.Center;
                var c = new QuickControl(service, valueWhenNone: true) { Margin = new Thickness(8, 0, 0, 0) };
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                DockPanel.SetDock(c, Dock.Right);
                row.Children.Add(c);
                row.Children.Add(n);
                rows.Children.Add(row);
                rowViews.Add((id, n, c));
            }
        }
        foreach (var (id, n, c) in rowViews)
        {
            var e = service.Entity(id);
            n.Text = e != null ? service.ShortName(t, e) : id;
            c.Show(e);
        }
        rows.Visibility = rowViews.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // what it reads
        var read = t.Shown.Where(id => !Domains.Controllable(id[..id.IndexOf('.')])).Select(service.Entity).OfType<HaEntity>().ToList();
        var first = main ?? read.FirstOrDefault();
        summary.Text = first == null ? "" : main != null ? Domains.Summary(main)
            : t.DeviceId == null ? first.ValueText() : service.ShortName(t, first) + ": " + first.ValueText();
        summary.Visibility = summary.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var values = (main != null ? read : read.Skip(1)).Take(SensorValues).ToList();
        sensors.Text = string.Join("  ·  ", values.Select(e => e.ValueText()));
        sensors.ToolTip = values.Count > 0 ? string.Join("\n", read.Select(e => $"{service.ShortName(t, e)}: {e.ValueText()}")) : null;
        sensors.Visibility = values.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // lit, off, or out of reach
        bool gone = t.Shown.Select(service.Entity).All(e => e == null || e.Unavailable);
        bool on = main != null && !main.Unavailable && Domains.IsOn(main);
        string color = gone ? "DangerBrush" : on ? LitColor(main!) : main == null ? "#7FD07A" : "TextSecondaryBrush";
        Ui.SetBrush(dot, Shape.FillProperty, color);
        if (on) { BorderBrush = Ui.Brush(this, LitColor(main!)); BorderThickness = new Thickness(2); }
        else { ClearValue(BorderBrushProperty); ClearValue(BorderThicknessProperty); }
        Opacity = gone ? 0.6 : 1;
    }

    /// <summary>A light's own colour while it's on (if it has one), else warm yellow.</summary>
    public static string LitColor(HaEntity e)
    {
        if (e.Domain == "light" && e.Numbers("rgb_color") is { Length: 3 } c)
        {
            // very pale colours (white light) look like nothing on a card: those are yellow too
            if (Math.Max(c[0], Math.Max(c[1], c[2])) - Math.Min(c[0], Math.Min(c[1], c[2])) > 40)
                return $"#{(int)c[0]:X2}{(int)c[1]:X2}{(int)c[2]:X2}";
        }
        return OnColor;
    }
}

/// <summary>
/// The control a box gives an entity: ON / OFF for one that switches, Activate / Run / Press for a scene, script or
/// button, a drop-down's option or a number's value as text otherwise (or nothing).
/// </summary>
internal sealed class QuickControl : ContentControl
{
    private readonly HaService service;
    private readonly bool valueWhenNone;
    private readonly ToggleButton chip = HaUi.Chip(T("On / off"));
    private readonly Button press;
    private readonly TextBlock value = Ui.Secondary();
    private string? id;

    public QuickControl(HaService service, bool valueWhenNone = false)
    {
        this.service = service;
        this.valueWhenNone = valueWhenNone;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        chip.Click += (_, _) => { if (id != null) service.Toggle(id); };
        press = HaUi.Small("", () => { if (id != null) service.Press(id); });
        value.VerticalAlignment = VerticalAlignment.Center;
    }

    public void Show(HaEntity? e)
    {
        id = e?.EntityId;
        if (e == null) { Content = null; return; }
        if (Domains.CanToggle(e))
        {
            var (onText, offText) = e.Domain switch
            {
                "lock" => (T("Unlocked"), T("Locked")),
                "cover" or "valve" => (T("Open"), T("Closed")),
                _ => ((string?)null, (string?)null),
            };
            HaUi.ShowChip(chip, Domains.IsOn(e), onText, offText);
            chip.IsEnabled = !e.Unavailable;
            Content = chip;
        }
        else if (Domains.IsPress(e.Domain))
        {
            press.Content = Domains.PressWord(e.Domain);
            press.IsEnabled = !e.Unavailable;
            Content = press;
        }
        else if (valueWhenNone)
        {
            value.Text = Domains.Summary(e);
            Content = value;
        }
        else Content = null;
    }
}
