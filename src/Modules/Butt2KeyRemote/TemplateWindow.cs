using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Controllers;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// The Butt Wizard (the template wizard), a step at a time: the device (so the layout suits it), the template (a game's buttons), its
/// buttons to tick (a tile each, in groups that open and close), then the theme, with a picture of the ButtScreens as
/// they'll be, and Create (with the game's programs offered as the BigButt's games). <see cref="Made"/> is the
/// ButtScreens, for the caller to add.
/// </summary>
internal sealed class TemplateWindow : Window
{
    private enum Step { Device, Template, Buttons, Theme }

    private readonly BigButt into;
    private readonly List<DeviceShape> devices;
    private readonly ContentControl page = new();
    private readonly Button back = new() { Content = T("Back"), MinWidth = 90 };
    private readonly Button next = new() { Content = T("Next"), MinWidth = 90, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock summary = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ListBox deviceList = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly ComboBox held = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly CheckBox games = new() { Margin = new Thickness(0, 8, 0, 0), IsChecked = true };
    private readonly List<(Action Retick, Action<bool> Open)> groupRows = new();
    private Step step;
    private ButtTemplate? picked;
    /// <summary>The theme picked on the last step (to start with, the template's own).</summary>
    private ButtTheme theme = ButtTheme.All[0];
    private bool themePicked;

    public List<ButtScreen> Made { get; private set; } = new();
    public ButtTemplate? Chosen => picked;
    /// <summary>Give the BigButt the template's programs as games (offered when it has none of them yet).</summary>
    public bool AddGames => games.IsChecked == true && games.Visibility == Visibility.Visible;

    /// <param name="shape">The size to start with (the device that connected last, or a phone).</param>
    /// <param name="into">The BigButt the ButtScreens go into (the one in use).</param>
    public TemplateWindow(IEnumerable<SeenDevice> seen, ButtScreen shape, BigButt into)
    {
        this.into = into;
        Title = T("ButtScreen Template");
        Width = 760;
        Height = 720;
        MinWidth = 560;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var dock = new DockPanel { Margin = new Thickness(16) };
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        next.SetResourceReference(StyleProperty, "AccentButton");
        back.Click += (_, _) => Show(step - 1);
        next.Click += (_, _) => { if (step == Step.Theme) Create(); else Show(step + 1); };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(back);
        right.Children.Add(next);
        right.Children.Add(cancel);
        DockPanel.SetDock(right, Dock.Right);
        bottom.Children.Add(right);
        summary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        bottom.Children.Add(summary);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        dock.Children.Add(page);
        Content = dock;

        // the devices: one that has connected, a phone or tablet by name, or a shape
        devices = DeviceShape.Choices(seen);
        int small = Math.Min(shape.Width, shape.Height), big = Math.Max(shape.Width, shape.Height);
        foreach (var d in devices) deviceList.Items.Add(Row(d.Name, F("{0} × {1}", d.Width, d.Height)));
        deviceList.SelectedIndex = Math.Max(0, devices.FindIndex(d => d.Width == small && d.Height == big));
        Prepare(deviceList);
        held.Items.Add(T("Portrait (tall)"));
        held.Items.Add(T("Landscape (wide)"));
        held.SelectedIndex = shape.Width > shape.Height ? 1 : 0;
        AutomationProperties.SetName(held, T("Held"));

        foreach (var t in TemplateStore.LoadAll())
        {
            var item = Row(t.Name, t.Description, F("{0} · {1} · {2}", P(t.ButtonCount, "{0} button", "{0} buttons"), P(t.Groups.Count, "{0} group", "{0} groups"),
                t.BuiltIn ? T("built in") : F("yours ({0})", System.IO.Path.GetFileName(t.File!))));
            item.Tag = t;
            list.Items.Add(item);
        }
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        Prepare(list);
        Show(Step.Device);
    }

    private ListBoxItem Row(string title, params string[] lines)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        foreach (var l in lines.Where(l => l.Length > 0)) s.Children.Add(Secondary(l));
        var item = new ListBoxItem { Content = s, Padding = new Thickness(8, 6, 8, 6) };
        item.SetResourceReference(ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(item, title);
        item.MouseDoubleClick += (_, _) => Show(step + 1);
        return item;
    }

    private void Prepare(ListBox l)
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(l, ScrollBarVisibility.Disabled);
        l.SelectionChanged += (_, _) => next.IsEnabled = l.SelectedItem != null;
    }

    /// <summary>The size the ButtScreens are made at: the device picked, held as picked.</summary>
    private ButtScreen Shape()
    {
        var d = devices[Math.Max(0, deviceList.SelectedIndex)];
        bool wide = held.SelectedIndex == 1;
        return new ButtScreen { Width = wide ? d.Height : d.Width, Height = wide ? d.Width : d.Height };
    }

    private void Show(Step to)
    {
        if (to == Step.Buttons && !PickTemplate()) return;
        step = to;
        back.Visibility = step == Step.Device ? Visibility.Collapsed : Visibility.Visible;
        next.Content = step == Step.Theme ? T("Create") : T("Next");
        next.IsEnabled = true;
        summary.Text = "";
        page.Content = step switch
        {
            Step.Device => DevicePage(),
            Step.Template => TemplatePage(),
            Step.Buttons => ButtonsPage(),
            _ => ThemePage(),
        };
        if (step >= Step.Buttons) Summarise();
    }

    // ---- 1: the device ----

    private UIElement DevicePage()
    {
        var d = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Head(T("Which device is it for?")));
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var l = new TextBlock { Text = T("Held"), VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        DockPanel.SetDock(l, Dock.Left);
        line.Children.Add(l);
        Detach(held);
        line.Children.Add(held);
        top.Children.Add(line);
        DockPanel.SetDock(top, Dock.Top);
        d.Children.Add(top);
        Detach(deviceList);
        d.Children.Add(new ScrollViewer { Content = deviceList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        next.IsEnabled = deviceList.SelectedItem != null;
        summary.Text = T("A device that has connected is listed with its own size.");
        deviceList.Focus();
        deviceList.Loaded += (_, _) => { if (deviceList.SelectedItem != null) deviceList.ScrollIntoView(deviceList.SelectedItem); };
        return d;
    }

    // ---- 2: the template ----

    private UIElement TemplatePage()
    {
        var d = new DockPanel();
        var head = Head(T("Pick a template"));
        DockPanel.SetDock(head, Dock.Top);
        d.Children.Add(head);
        Detach(list);
        if (list.Items.Count == 0) d.Children.Add(Secondary(T("There are no templates: import one in Settings → Gaming → Button2Key Remote.")));
        else d.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        next.IsEnabled = list.SelectedItem != null;
        summary.Text = T("Add your own in Settings → Gaming → Button2Key Remote.");
        list.Focus();
        return d;
    }

    /// <summary>A copy of the template picked, its ticks as it starts (kept while it's the same one).</summary>
    private bool PickTemplate()
    {
        if (list.SelectedItem is not ListBoxItem { Tag: ButtTemplate t }) return false;
        if (picked != null && picked.Name == t.Name && picked.File == t.File) return true;
        picked = t.Clone();
        // (a group off to start with has its buttons off too)
        foreach (var g in picked.Groups.Where(g => !g.Selected))
            foreach (var b in g.Buttons) b.Selected = false;
        themePicked = false;
        return true;
    }

    // ---- 3: its buttons, a tile each ----

    private UIElement ButtonsPage()
    {
        var d = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Head(F("{0}: pick its buttons", picked!.Name)));
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        tools.Children.Add(Small(T("Tick all"), () => SetAll(true)));
        tools.Children.Add(Small(T("Untick all"), () => SetAll(false)));
        tools.Children.Add(Small(T("Open all"), () => OpenAll(true)));
        tools.Children.Add(Small(T("Close all"), () => OpenAll(false)));
        top.Children.Add(tools);
        DockPanel.SetDock(top, Dock.Top);
        d.Children.Add(top);

        var groups = new StackPanel();
        groupRows.Clear();
        foreach (var g in picked.Groups) groups.Children.Add(Group(g));
        d.Children.Add(new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return d;
    }

    /// <summary>A group: open / close, its tick (all its buttons, or none), then its buttons as tiles.</summary>
    private FrameworkElement Group(TemplateGroup g)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var head = new DockPanel();
        bool isOpen = true;
        var glyph = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var open = new Border { Width = 28, Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = glyph, ToolTip = T("Open or close") };
        DockPanel.SetDock(open, Dock.Left);
        head.Children.Add(open);
        var tick = new CheckBox { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 14 };
        AutomationProperties.SetName(tick, g.Name);
        head.Children.Add(tick);
        s.Children.Add(head);

        var tiles = new WrapPanel { Margin = new Thickness(28, 6, 0, 0) };
        var redraws = new List<Action>();
        void ShowTick()
        {
            int on = g.Buttons.Count(b => b.Selected);
            // (ticked while any of its buttons is: clicking it then unticks them all)
            tick.IsChecked = on > 0;
            tick.Content = F("{0} ({1} of {2})", g.Name, on, g.Buttons.Count);
        }
        foreach (var b in g.Buttons)
        {
            var (tile, redraw) = Tile(b, () => { ShowTick(); Summarise(); });
            redraws.Add(redraw);
            tiles.Children.Add(tile);
        }
        tick.Click += (_, _) =>
        {
            bool all = !g.Buttons.Any(b => b.Selected);
            foreach (var b in g.Buttons) b.Selected = all;
            foreach (var r in redraws) r();
            ShowTick();
            Summarise();
        };
        void ShowOpen(bool now)
        {
            isOpen = now;
            glyph.Text = now ? "" : "";
            tiles.Visibility = now ? Visibility.Visible : Visibility.Collapsed;
        }
        open.MouseLeftButtonUp += (_, _) => ShowOpen(!isOpen);
        ShowOpen(true);
        ShowTick();
        s.Children.Add(tiles);
        // (re-ticked from outside: Tick all / Untick all)
        groupRows.Add((() => { foreach (var r in redraws) r(); ShowTick(); }, ShowOpen));
        return s;
    }

    /// <summary>A button's tile: its tick, icon, label, and the keys it presses (and how); clicking it ticks or unticks it.</summary>
    private static (FrameworkElement Tile, Action Redraw) Tile(TemplateButton b, Action changed)
    {
        var box = new CheckBox { IsChecked = b.Selected, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Focusable = false, IsHitTestVisible = false };
        var inside = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var icon = new Path { Width = 30, Height = 30, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Data = IconLibrary.GeometryOf(b.Icon) };
        icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextBrush");
        inside.Children.Add(icon);
        inside.Children.Add(new TextBlock { Text = b.Label, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        string keys = KeyCatalog.Describe(b.Keys) + (b.Mode == PressMode.Tap ? "" : " · " + ModeName(b.Mode));
        var k = new TextBlock { Text = keys, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        k.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        inside.Children.Add(k);
        var grid = new Grid();
        grid.Children.Add(inside);
        grid.Children.Add(box);
        var tile = new Border { Width = 140, MinHeight = 108, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Child = grid,
            ToolTip = b.Wide > 1 || b.High > 1 ? F("Covers {0} × {1} cells", b.Wide, b.High) : null };
        AutomationProperties.SetName(tile, b.Label);
        void Redraw()
        {
            box.IsChecked = b.Selected;
            tile.SetResourceReference(Border.BackgroundProperty, b.Selected ? "RowSelectedBrush" : "RoomBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, b.Selected ? "AccentBrush" : "CardBorderBrush");
            tile.Opacity = b.Selected ? 1 : 0.65;
        }
        tile.MouseLeftButtonUp += (_, _) => { b.Selected = !b.Selected; Redraw(); changed(); };
        Redraw();
        return (tile, Redraw);
    }

    private static string ModeName(PressMode m) => m switch
    {
        PressMode.Hold => T("held"),
        PressMode.Repeat => T("repeats"),
        PressMode.Toggle => T("toggles"),
        _ => T("tapped"),
    };

    private void SetAll(bool on)
    {
        if (picked == null) return;
        foreach (var b in picked.Groups.SelectMany(g => g.Buttons)) b.Selected = on;
        foreach (var (retick, _) in groupRows) retick();
        Summarise();
    }

    private void OpenAll(bool open)
    {
        foreach (var (_, show) in groupRows) show(open);
    }

    // ---- 4: the theme, and what it'll look like ----

    private UIElement ThemePage()
    {
        if (!themePicked) theme = ButtTheme.Named(picked!.Theme) ?? ButtTheme.All[0];
        var d = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Head(T("Pick a theme")));
        // the game's programs as the BigButt's games, if it hasn't them yet
        var missing = picked!.Programs.Where(p => !into.Programs.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
        Detach(games);
        games.Content = F("Add its games to {0}: {1}", into.Name, string.Join(", ", missing));
        games.ToolTip = T("While one runs, this BigButt is the one in use (with Game Switching)");
        games.Visibility = missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        top.Children.Add(games);
        var themes = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        var preview = new WrapPanel();
        var swatches = new List<(Border Tile, ButtTheme Theme)>();
        void Pick(ButtTheme t)
        {
            theme = t;
            themePicked = true;
            foreach (var (tile, x) in swatches) tile.SetResourceReference(Border.BorderBrushProperty, x == theme ? "AccentBrush" : "CardBorderBrush");
            Summarise();
            preview.Children.Clear();
            foreach (var s in Made)
            {
                var v = new Viewbox { Height = 300, Margin = new Thickness(0, 0, 12, 12), Stretch = Stretch.Uniform, Child = new ScreenVisual(s), ToolTip = s.Name };
                preview.Children.Add(v);
            }
        }
        foreach (var x in ButtTheme.All)
        {
            var colours = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var c in new[] { x.Background, x.Button, x.Outline, x.Text })
                colours.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Margin = new Thickness(1), Background = ColorText.Brush(c, Colors.Gray) });
            var inside = new StackPanel();
            inside.Children.Add(colours);
            inside.Children.Add(new TextBlock { Text = x.Name, FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            var tile = new Border { Width = 104, Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Child = inside };
            tile.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
            AutomationProperties.SetName(tile, x.Name);
            tile.MouseLeftButtonUp += (_, _) => Pick(x);
            swatches.Add((tile, x));
            themes.Children.Add(tile);
        }
        top.Children.Add(themes);
        top.Children.Add(new TextBlock { Text = T("What it will look like"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 8) });
        DockPanel.SetDock(top, Dock.Top);
        d.Children.Add(top);
        d.Children.Add(new ScrollViewer { Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Pick(theme);
        return d;
    }

    private void Summarise()
    {
        if (picked == null) return;
        int buttons = picked.Groups.Sum(g => g.Buttons.Count(b => b.Selected));
        var t = picked.Clone();
        if (themePicked) t.Theme = theme.Name;
        Made = buttons == 0 ? new() : TemplateBuilder.Build(t, Shape());
        summary.Text = buttons == 0 ? T("Tick the buttons you want.")
            : F("{0} on {1}, {2} × {3}", P(buttons, "{0} button", "{0} buttons"), P(Made.Count, "{0} ButtScreen", "{0} ButtScreens"), Made[0].Columns, Made[0].Rows);
        next.IsEnabled = buttons > 0;
    }

    private void Create()
    {
        Summarise();
        if (Made.Count == 0) return;
        DialogResult = true;
    }

    // ---- small parts ----

    /// <summary>Takes a control kept from page to page out of the page it was on.</summary>
    private static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case ContentControl c: c.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
    }

    private static TextBlock Head(string text) =>
        new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };

    private static TextBlock Secondary(string text) => Ui.Note(text, 3);

    private static Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
        b.Click += (_, _) => click();
        return b;
    }
}
