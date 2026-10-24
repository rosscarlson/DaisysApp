using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>
/// Arduinos: every board with its status light (left: Add Arduino, Remove) and the one picked (right): its name, COM
/// port, on / off and what it's doing, then its features (Add Feature from every kind there is, each with the settings
/// its board's sketch uses, Test and Remove). Changes apply as they're made (the board is set up again).
/// </summary>
internal sealed class ArduinoWindow : Window
{
    private static ArduinoWindow? open;

    private readonly ArduinoHub hub;
    private readonly ArduinoSettings settings;
    private readonly ListBox list = new() { MinWidth = 200, BorderThickness = new Thickness(0) };
    private readonly StackPanel detail = new() { Margin = new Thickness(16, 0, 0, 0) };
    private readonly DispatcherTimer applyTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(TextBlock Text, ArduinoBoard Board, ArduinoFeature? Feature)> statuses = new();
    private readonly List<(Ellipse Dot, ArduinoBoard Board)> dots = new();
    private readonly List<(Button Test, ArduinoBoard Board)> tests = new();
    private readonly Button remove;
    private ArduinoBoard? picked;
    private string shownSketch = "";   // the sketch whose fields are showing

    /// <summary>Opens it (or brings the open one forward), on <paramref name="board"/> if given.</summary>
    public static void Show(ArduinoHub hub, Window? owner, ArduinoBoard? board = null)
    {
        if (open != null)
        {
            open.Activate();
            if (board != null) open.Pick(board);
            return;
        }
        open = new ArduinoWindow(hub) { Owner = owner };
        open.Closed += (_, _) => open = null;
        open.Show();
        open.Pick(board ?? hub.Settings.Boards.FirstOrDefault());
    }

    private ArduinoWindow(ArduinoHub hub)
    {
        this.hub = hub;
        settings = hub.Settings;
        Title = T("Arduinos");
        Width = 860;
        Height = 640;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        list.SetResourceReference(BackgroundProperty, "WindowBrush");
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: ArduinoBoard b } && b != picked) Pick(b); };
        var add = Ui.Button(T("Add Arduino"), AddBoard, accent: true);
        add.Margin = new Thickness(0, 0, 4, 0);
        remove = Ui.Confirm(T("Remove"), () =>
        {
            if (picked == null) return;
            settings.Boards.Remove(picked);
            hub.Apply();
            Pick(settings.Boards.FirstOrDefault());
        });
        remove.Margin = new Thickness(4, 0, 0, 0);
        remove.IsEnabled = false;
        var buttons = new UniformGrid { Columns = 2, Rows = 1, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        var leftPanel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        leftPanel.Children.Add(buttons);
        leftPanel.Children.Add(list);

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(leftPanel);
        var scroll = Ui.Scroll(detail);
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;

        applyTimer.Tick += (_, _) => { applyTimer.Stop(); hub.Apply(); };
        statusTimer.Tick += (_, _) => ShowStatuses();
        statusTimer.Start();
        Closed += (_, _) =>
        {
            statusTimer.Stop();
            if (applyTimer.IsEnabled) { applyTimer.Stop(); hub.Apply(); }
        };
        List();
    }

    /// <summary>A change: applied (and saved) once the typing stops.</summary>
    private void Changed()
    {
        applyTimer.Stop();
        applyTimer.Start();
    }

    private void List()
    {
        list.Items.Clear();
        dots.RemoveAll(d => d.Dot.Tag is "list");
        foreach (var b in settings.Boards)
        {
            var item = new ListBoxItem { Tag = b, Padding = new Thickness(8, 6, 8, 6) };
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = b.Name, FontWeight = FontWeights.SemiBold });
            p.Children.Add(Ui.Secondary(b.Port.Length > 0 ? b.Port : T("No COM port")));
            var dot = Ui.Dot();
            dot.Tag = "list";
            Ui.Light(dot, hub.State(b));
            dots.Add((dot, b));
            var row = new DockPanel();
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(p);
            item.Content = row;
            AutomationProperties.SetName(item, b.Name);
            list.Items.Add(item);
            if (b == picked) list.SelectedItem = item;
        }
    }

    private void AddBoard()
    {
        var b = new ArduinoBoard { Name = F("Arduino {0}", settings.Boards.Count + 1) };
        // the first port no board has yet
        var used = settings.Boards.Select(x => x.Port).ToHashSet(StringComparer.OrdinalIgnoreCase);
        b.Port = SerialPorts.List().FirstOrDefault(p => !used.Contains(p.Port))?.Port ?? "";
        settings.Boards.Add(b);
        hub.Apply();
        Pick(b);
    }

    private void Pick(ArduinoBoard? b)
    {
        picked = b;
        remove.IsEnabled = b != null;
        List();
        Render();
    }

    private void Render()
    {
        detail.Children.Clear();
        statuses.Clear();
        dots.RemoveAll(d => d.Dot.Tag is not "list");
        tests.Clear();
        var b = picked;
        shownSketch = b?.Sketch ?? "";
        if (b == null || !settings.Boards.Contains(b))
        {
            detail.Children.Add(Ui.Secondary(T("Add an Arduino")));
            return;
        }

        // ---- the board ----
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header(T("Arduino")));
        var name = new TextBox { Text = b.Name };
        name.TextChanged += (_, _) => { b.Name = name.Text; Changed(); RenameInList(b); };
        panel.Children.Add(Ui.Field(T("Name"), name));
        panel.Children.Add(Ui.Field(T("COM Port"), PortPicker(b)));
        var on = new CheckBox { Content = T("Use it"), IsChecked = b.Enabled, Margin = new Thickness(0, 8, 0, 0) };
        on.Click += (_, _) => { b.Enabled = on.IsChecked == true; hub.Apply(); };
        panel.Children.Add(on);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        statuses.Add((status, b, null));
        var light = Ui.Dot();
        dots.Add((light, b));
        var statusRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(light, Dock.Left);
        statusRow.Children.Add(light);
        statusRow.Children.Add(status);
        panel.Children.Add(statusRow);
        detail.Children.Add(Ui.Card(panel));

        // ---- its features ----
        var features = new StackPanel();
        var addFeature = Ui.Button(T("Add Feature"), () => { }, accent: true);
        addFeature.Margin = new Thickness(0);
        addFeature.IsEnabled = b.Features.Count < ArduinoProtocol.MaxSlots;
        addFeature.ToolTip = addFeature.IsEnabled ? null : F("{0} at most", ArduinoProtocol.MaxSlots);
        var menu = new ContextMenu();
        foreach (var kind in ArduinoFeatures.All)
        {
            var item = new MenuItem { Header = kind.Title };
            item.Click += (_, _) =>
            {
                b.Features.Add(new ArduinoFeature { Kind = kind.Id, Name = UniqueName(b, kind.Title) });
                hub.Apply();
                Render();
            };
            menu.Items.Add(item);
        }
        addFeature.Click += (_, _) => { menu.PlacementTarget = addFeature; menu.IsOpen = true; };
        features.Children.Add(Ui.HeaderLine(T("Features"), addFeature));
        if (b.Features.Count == 0) features.Children.Add(Ui.Secondary(T("None yet")));
        foreach (var f in b.Features) features.Children.Add(FeatureRow(b, f));
        detail.Children.Add(Ui.Card(features));
        ShowStatuses();
    }

    private static string UniqueName(ArduinoBoard b, string title)
    {
        string bare = title.Split(" (")[0];
        if (b.Features.All(f => f.Name != bare)) return bare;
        for (int i = 2; ; i++)
            if (b.Features.All(f => f.Name != $"{bare} {i}")) return $"{bare} {i}";
    }

    private void RenameInList(ArduinoBoard b)
    {
        foreach (ListBoxItem item in list.Items)
            if (item.Tag == b && item.Content is DockPanel { Children: [_, StackPanel { Children: [TextBlock t, ..] }] }) t.Text = b.Name;
    }

    private FrameworkElement PortPicker(ArduinoBoard b)
    {
        var box = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        void Fill()
        {
            box.Items.Clear();
            var ports = SerialPorts.List();
            if (b.Port.Length > 0 && ports.All(p => !p.Port.Equals(b.Port, StringComparison.OrdinalIgnoreCase)))
                ports.Insert(0, new SerialPorts.Info(b.Port, T("not plugged in")));
            foreach (var p in ports)
            {
                var item = new ComboBoxItem { Content = p.Display, Tag = p.Port };
                box.Items.Add(item);
                if (p.Port.Equals(b.Port, StringComparison.OrdinalIgnoreCase)) box.SelectedItem = item;
            }
        }
        Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string port } && port != b.Port)
            {
                b.Port = port;
                hub.Apply();
                RenameInList(b);
                List();
            }
        };
        var refresh = new Button { Content = "", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Look for ports again") };
        refresh.SetResourceReference(Control.FontFamilyProperty, "IconFont");
        AutomationProperties.SetName(refresh, T("Look for ports again"));
        refresh.Click += (_, _) => Fill();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(refresh);
        return row;
    }

    private FrameworkElement FeatureRow(ArduinoBoard b, ArduinoFeature f)
    {
        var info = f.Info;
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var test = Ui.Button(T("Test"), () => hub.Test(b, f), tip: T("A test pattern for a few seconds"));
        ToolTipService.SetShowOnDisabled(test, true);
        tests.Add((test, b));
        buttons.Children.Add(test);
        var remove = Ui.Confirm(T("Remove"), () => { b.Features.Remove(f); hub.Apply(); Render(); });
        remove.Margin = new Thickness(0);
        buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        var name = new TextBox { Text = f.Name, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        name.TextChanged += (_, _) => { f.Name = name.Text; Changed(); };
        head.Children.Add(name);
        panel.Children.Add(head);
        panel.Children.Add(Ui.Secondary(info?.Title ?? F("{0} (not in this version)", f.Kind)));
        if (info != null)
            foreach (var field in info.Fields.Where(x => x.Only == null || b.Sketch.Length == 0 || x.Only == b.Sketch))
                panel.Children.Add(FieldControl(f, field));
        var problem = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        problem.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        panel.Children.Add(problem);
        statuses.Add((problem, b, f));

        var row = new Border { Child = panel, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1) };
        row.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        row.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return row;
    }

    private FrameworkElement FieldControl(ArduinoFeature f, FeatureField field)
    {
        int value = f.Get(field);
        void Set(int v)
        {
            if (v == field.Default) f.Values.Remove(field.Key); else f.Values[field.Key] = v;
            Changed();
        }
        FrameworkElement control;
        if (field.Toggle)
        {
            var c = new CheckBox { Content = field.Title, IsChecked = value != 0, ToolTip = field.Tip, Margin = new Thickness(0, 6, 0, 0) };
            c.Click += (_, _) => Set(c.IsChecked == true ? 1 : 0);
            return c;
        }
        if (field.Menu)
        {
            var c = new ComboBox { MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Left };
            for (int v = field.Min; v <= field.Max; v++) c.Items.Add(new ComboBoxItem { Content = "D" + v, Tag = v });
            c.SelectedIndex = Math.Clamp(value, field.Min, field.Max) - field.Min;
            c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: int v }) Set(v); };
            control = c;
        }
        else if (field.Choices is { } choices)
        {
            var c = new ComboBox { ItemsSource = choices, SelectedIndex = Math.Clamp(value, 0, choices.Length - 1), MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
            c.SelectionChanged += (_, _) => Set(c.SelectedIndex);
            control = c;
        }
        else if (field.Unit.Length > 0)
        {
            var row = Ui.SliderRow("", field.Min, field.Max, 1, value, v => $"{v:0}{field.Unit}", v => Set((int)v));
            row.ColumnDefinitions[0].Width = new GridLength(0);
            control = row;
        }
        else control = Ui.Number(value, field.Min, field.Max, Set, 60);
        control.ToolTip = field.Tip;
        AutomationProperties.SetName(control, field.Title);
        return Ui.Field(field.Title, control);
    }

    private void ShowStatuses()
    {
        if (picked != null && picked.Sketch != shownSketch && settings.Boards.Contains(picked))
        {
            // its sketch was found: that sketch's settings
            Render();
            return;
        }
        foreach (var (dot, b) in dots) Ui.Light(dot, hub.State(b));
        foreach (var (test, b) in tests)
        {
            bool on = hub.Connected(b);
            test.IsEnabled = on;
            test.ToolTip = on ? T("A test pattern for a few seconds") : T("Not connected");
        }
        foreach (var (text, b, f) in statuses)
        {
            if (f == null)
            {
                text.Text = hub.Status(b);
                text.SetResourceReference(TextBlock.ForegroundProperty, hub.Connected(b) ? "SuccessBrush" : "TextSecondaryBrush");
            }
            else
            {
                string? p = hub.Problem(b, f);
                text.Text = p ?? "";
                text.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
