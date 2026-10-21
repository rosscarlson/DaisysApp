using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// The template wizard: first a template (a game's buttons), then its groups and buttons to tick, each group opening
/// and closing; then the ButtScreens are made (<see cref="TemplateBuilder"/>). For a new BigButt, also its name and
/// whether it gets the game's programs. <see cref="Made"/> is the ButtScreens, for the caller to add.
/// </summary>
internal sealed class TemplateWindow : Window
{
    private readonly ButtScreen shape;
    private readonly bool newBigButt;
    private readonly Func<string, string> freeBigButt;
    private readonly ContentControl page = new();
    private readonly Button back = new() { Content = T("Back"), MinWidth = 90 };
    private readonly Button next = new() { Content = T("Next"), MinWidth = 90, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock summary = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBox name = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox games = new() { Margin = new Thickness(0, 8, 0, 0), IsChecked = true };
    private readonly List<(Action Retick, Action<bool> Open)> groupRows = new();
    private ButtTemplate? picked;
    private bool onButtons;

    public List<ButtScreen> Made { get; private set; } = new();
    public ButtTemplate? Template => picked;
    public string BigButtName => name.Text.Trim();
    public bool AddGames => games.IsChecked == true;

    public TemplateWindow(ButtScreen shape, bool newBigButt, Func<string, string> freeBigButt, string? preselect = null)
    {
        this.shape = shape;
        this.newBigButt = newBigButt;
        this.freeBigButt = freeBigButt;
        Title = newBigButt ? T("New BigButt from a template") : T("ButtScreens from a template");
        Width = 620;
        Height = 680;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var dock = new DockPanel { Margin = new Thickness(16) };
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        next.SetResourceReference(StyleProperty, "AccentButton");
        back.Click += (_, _) => ShowTemplates();
        next.Click += (_, _) => { if (onButtons) Create(); else ShowButtons(); };
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

        foreach (var t in TemplateStore.LoadAll())
        {
            var item = new ListBoxItem { Tag = t, Padding = new Thickness(8, 6, 8, 6), Content = TemplateRow(t) };
            item.SetResourceReference(ForegroundProperty, "TextBrush");
            AutomationProperties.SetName(item, t.Name);
            item.MouseDoubleClick += (_, _) => ShowButtons();
            list.Items.Add(item);
            if (preselect != null && t.Name.Equals(preselect, StringComparison.CurrentCultureIgnoreCase)) list.SelectedItem = item;
        }
        if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) => next.IsEnabled = list.SelectedItem != null;
        ShowTemplates();
    }

    private static FrameworkElement TemplateRow(ButtTemplate t)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        if (t.Description.Length > 0) s.Children.Add(Secondary(t.Description));
        s.Children.Add(Secondary(F("{0} · {1} · {2}", P(t.ButtonCount, "{0} button", "{0} buttons"), P(t.Groups.Count, "{0} group", "{0} groups"),
            t.BuiltIn ? T("built in") : F("yours ({0})", System.IO.Path.GetFileName(t.File!)))));
        return s;
    }

    // ---- 1: the template ----

    private void ShowTemplates()
    {
        onButtons = false;
        back.Visibility = Visibility.Collapsed;
        next.Content = T("Next");
        next.IsEnabled = list.SelectedItem != null;
        summary.Text = T("Add your own in Settings → Gaming → Button2Key Remote.");
        var d = new DockPanel();
        var head = Head(T("Pick a template"));
        DockPanel.SetDock(head, Dock.Top);
        d.Children.Add(head);
        (list.Parent as Panel)?.Children.Remove(list);
        if (list.Items.Count == 0) d.Children.Add(Secondary(T("There are no templates: import one in Settings → Gaming → Button2Key Remote.")));
        else d.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        page.Content = d;
        list.Focus();
    }

    // ---- 2: its groups and buttons ----

    private void ShowButtons()
    {
        if (list.SelectedItem is not ListBoxItem { Tag: ButtTemplate t }) return;
        if (picked == null || picked.Name != t.Name || picked.File != t.File)
        {
            picked = t.Clone();
            // (a group off to start with has its buttons off too)
            foreach (var g in picked.Groups.Where(g => !g.Selected))
                foreach (var b in g.Buttons) b.Selected = false;
            name.Text = freeBigButt(picked.Name);
        }
        onButtons = true;
        back.Visibility = Visibility.Visible;
        next.Content = newBigButt ? T("Make the BigButt") : T("Make the ButtScreens");

        var d = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Head(F("{0}: pick its buttons", picked.Name)));
        if (newBigButt)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
            var l = new TextBlock { Text = T("BigButt's name"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(l, Dock.Left);
            line.Children.Add(l);
            (name.Parent as Panel)?.Children.Remove(name);
            AutomationProperties.SetName(name, T("BigButt's name"));
            line.Children.Add(name);
            top.Children.Add(line);
            (games.Parent as Panel)?.Children.Remove(games);
            games.Content = F("Its games: {0}", string.Join(", ", picked.Programs));
            games.ToolTip = T("While one runs, this BigButt is the one in use (with Auto Launch)");
            games.Visibility = picked.Programs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            top.Children.Add(games);
        }
        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        tools.Children.Add(Small(T("Tick all"), () => SetAll(true)));
        tools.Children.Add(Small(T("Untick all"), () => SetAll(false)));
        tools.Children.Add(Small(T("Open all"), () => OpenAll(true)));
        tools.Children.Add(Small(T("Close all"), () => OpenAll(false)));
        top.Children.Add(tools);
        DockPanel.SetDock(top, Dock.Top);
        d.Children.Add(top);

        var tree = new StackPanel();
        groupRows.Clear();
        foreach (var g in picked.Groups) tree.Children.Add(Group(g));
        d.Children.Add(new ScrollViewer { Content = tree, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        page.Content = d;
        Summarise();
    }

    /// <summary>A group: open / close, its tick (all its buttons, or some), then a line per button.</summary>
    private FrameworkElement Group(TemplateGroup g)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var head = new DockPanel();
        bool isOpen = true;
        var glyph = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var open = new Border { Width = 28, Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = glyph, ToolTip = T("Open or close") };
        DockPanel.SetDock(open, Dock.Left);
        head.Children.Add(open);
        var tick = new CheckBox { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetName(tick, g.Name);
        head.Children.Add(tick);
        s.Children.Add(head);

        var rows = new StackPanel { Margin = new Thickness(36, 2, 0, 0) };
        var boxes = new List<CheckBox>();
        void ShowTick()
        {
            int on = g.Buttons.Count(b => b.Selected);
            // (ticked while any of its buttons is: clicking it then unticks them all)
            tick.IsChecked = on > 0;
            tick.Content = F("{0} ({1} of {2})", g.Name, on, g.Buttons.Count);
        }
        foreach (var b in g.Buttons)
        {
            var button = b;
            var box = new CheckBox { IsChecked = b.Selected, Margin = new Thickness(0, 3, 0, 3), Content = ButtonLine(b) };
            AutomationProperties.SetName(box, b.Label);
            box.Click += (_, _) => { button.Selected = box.IsChecked == true; ShowTick(); Summarise(); };
            boxes.Add(box);
            rows.Children.Add(box);
        }
        tick.Click += (_, _) =>
        {
            bool all = !g.Buttons.Any(b => b.Selected);
            foreach (var b in g.Buttons) b.Selected = all;
            for (int i = 0; i < boxes.Count; i++) boxes[i].IsChecked = all;
            ShowTick();
            Summarise();
        };
        void ShowOpen(bool now)
        {
            isOpen = now;
            glyph.Text = now ? "" : "";
            rows.Visibility = now ? Visibility.Visible : Visibility.Collapsed;
        }
        open.MouseLeftButtonUp += (_, _) => ShowOpen(!isOpen);
        ShowOpen(true);
        ShowTick();
        s.Children.Add(rows);
        // (re-ticked from outside: Tick all / Untick all)
        groupRows.Add((() => { for (int i = 0; i < boxes.Count; i++) boxes[i].IsChecked = g.Buttons[i].Selected; ShowTick(); }, ShowOpen));
        return s;
    }

    /// <summary>A button's line: its icon, label, and the keys it presses (and how).</summary>
    private static FrameworkElement ButtonLine(TemplateButton b)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Path { Width = 18, Height = 18, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Data = IconLibrary.GeometryOf(b.Icon) };
        icon.SetResourceReference(Shape.FillProperty, "TextBrush");
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = b.Label, VerticalAlignment = VerticalAlignment.Center });
        string keys = KeyCatalog.Describe(b.Keys) + (b.Mode == PressMode.Tap ? "" : " · " + ModeName(b.Mode))
            + (b.Wide > 1 || b.High > 1 ? " · " + F("{0} × {1} cells", b.Wide, b.High) : "");
        var k = new TextBlock { Text = keys, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        k.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        line.Children.Add(k);
        return line;
    }

    private static string ModeName(PressMode m) => m switch
    {
        PressMode.Hold => T("held"),
        PressMode.Repeat => T("repeats while held"),
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

    private void Summarise()
    {
        if (picked == null) return;
        int buttons = picked.Groups.Sum(g => g.Buttons.Count(b => b.Selected));
        Made = buttons == 0 ? new() : TemplateBuilder.Build(picked, shape);
        summary.Text = buttons == 0 ? T("Tick the buttons you want.")
            : F("{0} on {1}, {2} × {3}", P(buttons, "{0} button", "{0} buttons"), P(Made.Count, "{0} ButtScreen", "{0} ButtScreens"), Made[0].Columns, Made[0].Rows);
        next.IsEnabled = buttons > 0;
    }

    private void Create()
    {
        Summarise();
        if (Made.Count == 0 || newBigButt && BigButtName.Length == 0) return;
        DialogResult = true;
    }

    // ---- small parts ----

    private static TextBlock Head(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        return t;
    }

    private static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    private static Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
        b.Click += (_, _) => click();
        return b;
    }
}
