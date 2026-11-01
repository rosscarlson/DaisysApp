using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Controllers;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;
using DaisysApp.Applets.SimEffects.Analysis;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// The Overlays tab (the Input Overlay's before 3.33.0). Top: on / off, its shortcut and what it's reading. Left: the
/// overlays made (each ticked to show), with New, Edit and Delete; right: the one picked's settings. Inputs: the overlay
/// (history, background, traces, session log) and the speeds, and where the inputs come from (the game, or an axis of a
/// controller each), the SimEffects profile in use's; the Dash: its look; a block: its rows, title and background.
/// </summary>
internal sealed class InputOverlayView : UserControl
{
    private readonly InputOverlayApplet applet;
    private readonly OverlaySettings settings;
    private readonly ToggleButton onOff;
    private readonly Button onOffKey = VolumeControl.IconButton("");
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel left = new(), source = new();
    private readonly ListBox list = new() { MinHeight = 120, BorderThickness = new Thickness(0) };
    private readonly ContentControl detail = new();
    private readonly UIElement inputsPane;
    private readonly Button newButton = new() { Padding = new Thickness(10, 3, 10, 3) };
    private readonly Button editButton = new() { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
    private readonly StackPanel listButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private string selected = OverlayKinds.Inputs;
    private bool filling;
    private readonly Dictionary<string, (TextBlock Input, TextBlock Value)> rows = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? capturing;

    public InputOverlayView(InputOverlayApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        onOff = SimUi.OnOff(T("Overlays on"), settings.Enabled, applet.SetEnabled);
        // the on / off shortcut: a key combination or a controller button (Core's Hotkeys)
        onOffKey.Margin = new Thickness(6, 0, 0, 0);
        AutomationProperties.SetName(onOffKey, T("On / off shortcut"));
        ShowKey();
        onOffKey.Click += (_, _) => { HotkeyWindow.Open(T("On / off shortcut"), Window.GetWindow(this), applet.Hotkey); ShowKey(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(onOffKey, Dock.Left);
        top.Children.Add(onOffKey);
        top.Children.Add(status);

        var right = new StackPanel();
        right.Children.Add(SimUi.Card(source));
        inputsPane = SimUi.Columns(left, right, 360);

        // the overlays made: ticked ones show while it's on; New, Edit, Delete under them
        newButton.Content = T("New") + " ▾";
        newButton.ToolTip = T("Make an overlay");
        AutomationProperties.SetName(newButton, T("New overlay"));
        newButton.Click += (_, _) => NewMenu();
        editButton.Content = T("Edit");
        editButton.ToolTip = T("Move, size and set it up");
        editButton.Click += (_, _) => applet.Edit(selected);
        AutomationProperties.SetName(list, T("Overlays"));
        list.Background = Brushes.Transparent;
        list.SetResourceReference(ForegroundProperty, "TextBrush");
        list.SelectionChanged += (_, _) =>
        {
            if (filling || list.SelectedItem is not ListBoxItem { Tag: string id }) return;
            selected = id;
            ShowDetail();
            ShowListButtons();
        };
        var listPanel = new DockPanel();
        var header = Ui.HeaderRow(T("Overlays"), newButton);
        DockPanel.SetDock(header, Dock.Top);
        listPanel.Children.Add(header);
        DockPanel.SetDock(listButtons, Dock.Bottom);
        listPanel.Children.Add(listButtons);
        listPanel.Children.Add(list);
        var listCard = SimUi.Card(listPanel);
        listCard.Width = 250;
        listCard.Margin = new Thickness(0, 0, 16, 12);
        listCard.VerticalAlignment = VerticalAlignment.Top;

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        DockPanel.SetDock(listCard, Dock.Left);
        page.Children.Add(listCard);
        page.Children.Add(detail);
        Content = page;

        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            applet.Reading();
            if (IsVisible) { timer.Start(); Tick(); }
            else { timer.Stop(); capturing?.Cancel(); }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && capturing != null) { capturing.Cancel(); e.Handled = true; } };
    }

    public void Stop()
    {
        timer.Stop();
        capturing?.Cancel();
    }

    /// <summary>On or off changed (here, by its shortcut or a game).</summary>
    public void ShowOnOff()
    {
        SimUi.ShowOnOff(onOff, settings.Enabled);
    }

    private void ShowKey()
    {
        ((TextBlock)onOffKey.Content).SetResourceReference(TextBlock.ForegroundProperty, applet.Hotkey.Value != null ? "AccentBrush" : "TextBrush");
        onOffKey.ToolTip = applet.Hotkey.Value is { } k ? F("On / off shortcut: {0}", k) : T("On / off shortcut");
    }

    /// <summary>Everything drawn again from the settings (another profile, or the source changed).</summary>
    public void Render()
    {
        ShowOnOff();
        left.Children.Clear();
        left.Children.Add(SimUi.Card(OverlayPanel()));
        left.Children.Add(SimUi.Card(SpeedPanel()));
        RenderSource();
        ShowList();
        ShowDetail();
    }

    // ---------------------------------------------------------------- the list

    /// <summary>The overlays made, each with its tick (shows while it's on), the one picked selected.</summary>
    public void ShowList()
    {
        filling = true;
        list.Items.Clear();
        var shown = applet.Made.Where(id => id is OverlayKinds.Inputs or OverlayKinds.Dash || applet.Analysis != null).ToList();
        if (!shown.Contains(selected)) selected = shown.FirstOrDefault() ?? "";
        foreach (var id in shown)
        {
            string name = OverlayKinds.Name(id);
            var tick = new CheckBox { IsChecked = applet.IsTicked(id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                ToolTip = T("Show it while Overlays is on") };
            AutomationProperties.SetName(tick, F("Show {0}", name));
            string kind = id;
            tick.Checked += (_, _) => { if (!filling) applet.SetTicked(kind, true); };
            tick.Unchecked += (_, _) => { if (!filling) applet.SetTicked(kind, false); };
            var text = new TextBlock { Text = applet.IsEditing(id) ? F("{0} (editing)", name) : name, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { tick, text } };
            var item = new ListBoxItem { Content = row, Tag = id, Padding = new Thickness(4) };
            AutomationProperties.SetName(item, name);
            list.Items.Add(item);
            if (id == selected) list.SelectedItem = item;
        }
        filling = false;
        newButton.IsEnabled = applet.CanMake.Any();
        ShowListButtons();
    }

    private void ShowListButtons()
    {
        listButtons.Children.Clear();
        if (selected == "") return;
        editButton.Content = applet.IsEditing(selected) ? T("Done") : T("Edit");
        listButtons.Children.Add(editButton);
        string id = selected;
        var delete = Widget.DeleteButton(() => applet.Delete(id), OverlayKinds.Name(id));
        delete.ToolTip = T("Delete this overlay");
        listButtons.Children.Add(delete);
    }

    private void NewMenu()
    {
        var menu = new ContextMenu { PlacementTarget = newButton, Placement = PlacementMode.Bottom };
        foreach (var id in applet.CanMake)
        {
            var item = new MenuItem { Header = OverlayKinds.Name(id) };
            string kind = id;
            item.Click += (_, _) => { selected = kind; applet.Create(kind); ShowDetail(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>The one picked's settings, at the right.</summary>
    private void ShowDetail()
    {
        detail.Content = selected switch
        {
            "" => null,
            OverlayKinds.Inputs => inputsPane,
            OverlayKinds.Dash => DashPane(),
            _ => applet.Analysis != null && Blocks.ById(selected) is { } b ? BlockPane(b, applet.Analysis) : null,
        };
    }

    private void Tick()
    {
        status.Text = applet.Reader.Status;
        status.SetResourceReference(TextBlock.ForegroundProperty, applet.Reader.Receiving ? "SuccessBrush" : "TextSecondaryBrush");
        var v = applet.Reader.Now;
        double[] values = { v.Steer, v.Throttle, v.Brake, v.Clutch, v.Handbrake };
        for (int i = 0; i < OverlaySettings.Roles.Length; i++)
            if (rows.TryGetValue(OverlaySettings.Roles[i], out var row))
                row.Value.Text = double.IsNaN(values[i]) ? "" : $"{values[i] * 100:0}%";
    }

    // ---------------------------------------------------------------- the overlay

    private UIElement OverlayPanel()
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Header(T("Inputs")));
        var reset = new Button { Content = T("Reset position"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
        reset.Click += (_, _) => applet.ResetPlace();
        var buttons = new WrapPanel();
        buttons.Children.Add(reset);
        s.Children.Add(buttons);

        var history = new ComboBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (int sec in new[] { 3, 5, 10, 20, 30 }) history.Items.Add(new ComboBoxItem { Content = F("{0} s", sec), Tag = sec });
        history.SelectedItem = history.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.Seconds) ?? history.Items[1];
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is ComboBoxItem { Tag: int sec }) { settings.Seconds = sec; settings.Save(); applet.Redraw(OverlayKinds.Inputs); } };
        s.Children.Add(SimUi.Field(T("History"), history));
        s.Children.Add(SimUi.SliderRow(T("Background"), 0, 1, 0.05, settings.Background, v => $"{v * 100:0}%",
            v => { settings.Background = v; settings.Save(); applet.Redraw(OverlayKinds.Inputs); }, T("How dark the overlay's background is: 0% is none, only the traces show")));

        var traces = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Trace(string name, Func<bool> get, Action<bool> set)
        {
            var box = new CheckBox { Content = name, IsChecked = get(), Margin = new Thickness(0, 0, 12, 4) };
            box.Click += (_, _) => { set(box.IsChecked == true); settings.Save(); applet.Redraw(OverlayKinds.Inputs); };
            traces.Children.Add(box);
        }
        Trace(T("Steering"), () => settings.Steering, v => settings.Steering = v);
        Trace(T("Throttle"), () => settings.Throttle, v => settings.Throttle = v);
        Trace(T("Brake"), () => settings.Brake, v => settings.Brake = v);
        Trace(T("Clutch"), () => settings.Clutch, v => settings.Clutch = v);
        Trace(T("Handbrake"), () => settings.Handbrake, v => settings.Handbrake = v);
        s.Children.Add(traces);

        var record = new CheckBox { Content = T("Record a session log while it shows"), IsChecked = settings.Record, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = T("For Forza Analysis") };
        record.Click += (_, _) => { settings.Record = record.IsChecked == true; settings.Save(); };
        s.Children.Add(record);
        return s;
    }

    private UIElement SpeedPanel()
    {
        var s = new StackPanel();
        var show = new CheckBox { Content = T("Show"), IsChecked = settings.Speed, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = T("Now in the middle; the window's highest above, its lowest below") };
        show.Click += (_, _) => { settings.Speed = show.IsChecked == true; settings.Save(); applet.Redraw(OverlayKinds.Inputs); };
        s.Children.Add(Ui.HeaderRow(T("Speed"), show));
        s.Children.Add(SimUi.SliderRow(T("Window Size"), 1, 30, 1, settings.WindowSeconds, v => F("{0:0} s", v),
            v => { settings.WindowSeconds = (int)v; settings.Save(); }, T("How long the highest and lowest speeds hold")));
        var unit = new ComboBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { new KeyValuePair<SpeedUnit, string>(SpeedUnit.Mph, T("mph")), new KeyValuePair<SpeedUnit, string>(SpeedUnit.Kmh, T("km/h")) },
            DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = settings.Unit };
        unit.SelectionChanged += (_, _) => { if (unit.SelectedValue is SpeedUnit u) { settings.Unit = u; settings.Save(); } };
        s.Children.Add(SimUi.Field(T("Units"), unit));
        return s;
    }

    // ---------------------------------------------------------------- the Dash and the blocks

    /// <summary>An overlay's shortcut button (on / off), as the tool's.</summary>
    private Button KeyButton(Hotkey key, string name)
    {
        var b = VolumeControl.IconButton("");
        AutomationProperties.SetName(b, F("{0} shortcut", name));
        void Show()
        {
            ((TextBlock)b.Content).SetResourceReference(TextBlock.ForegroundProperty, key.Value != null ? "AccentBrush" : "TextBrush");
            b.ToolTip = key.Value is { } k ? F("On / off shortcut: {0}", k) : T("On / off shortcut");
        }
        Show();
        b.Click += (_, _) => { HotkeyWindow.Open(T("On / off shortcut"), Window.GetWindow(this), key); Show(); };
        return b;
    }

    private UIElement DashPane()
    {
        var d = settings.Dash;
        var s = new StackPanel();
        s.Children.Add(Ui.HeaderRow(T("Dash"), applet.KeyOf(OverlayKinds.Dash) is { } k ? KeyButton(k, T("Dash")) : null));
        s.Children.Add(SimUi.SliderRow(T("Background"), 0, 1, 0.05, d.Opacity, v => $"{v * 100:0}%",
            v => { d.Opacity = v; settings.Save(); applet.Redraw(OverlayKinds.Dash); }, T("How see-through the dial is")));
        var unit = new ComboBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (u, text) in new (SpeedUnit?, string)[] { (null, T("As Inputs'")), (SpeedUnit.Mph, T("mph")), (SpeedUnit.Kmh, T("km/h")) })
            unit.Items.Add(new ComboBoxItem { Content = text, Tag = u });
        unit.SelectedIndex = d.Unit == null ? 0 : d.Unit == SpeedUnit.Mph ? 1 : 2;
        unit.SelectionChanged += (_, _) => { d.Unit = (unit.SelectedItem as ComboBoxItem)?.Tag as SpeedUnit?; settings.Save(); applet.Redraw(OverlayKinds.Dash); };
        s.Children.Add(SimUi.Field(T("Units"), unit));
        void Box(string text, string tip, Func<bool> get, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = get(), Margin = new Thickness(0, 6, 0, 0), ToolTip = tip };
            Ui.OnToggle(box, on => { set(on); settings.Save(); applet.Redraw(OverlayKinds.Dash); });
            s.Children.Add(box);
        }
        Box(T("TCS and ABS lights"), T("TCS: the wheels spinning under power; ABS: a wheel locking under braking"), () => d.Lights, v => d.Lights = v);
        Box(T("Flash the gear at the shift point"), T("Red and flashing"), () => d.ShiftFlash, v => d.ShiftFlash = v);
        return new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Width = 420, Children = { SimUi.Card(s) } };
    }

    private UIElement BlockPane(Block block, AnalysisApplet a)
    {
        var place = a.Settings.Widget(block.Id);
        var s = new StackPanel();
        s.Children.Add(Ui.HeaderRow(block.Title, applet.KeyOf(block.Id) is { } k ? KeyButton(k, block.Title) : null));
        s.Children.Add(SimUi.SliderRow(T("Background"), 0, 1, 0.05, place.Background, v => $"{v * 100:0}%",
            v => { place.Background = v; a.Settings.Save(); applet.Redraw(block.Id); }, T("How see-through its background is")));
        var title = new CheckBox { Content = T("Title"), IsChecked = place.Title ?? block.Kind is not (BlockKind.Timing or BlockKind.Big),
            Margin = new Thickness(0, 6, 0, 0), ToolTip = T("Show the title") };
        Ui.OnToggle(title, on => { place.Title = on; a.Settings.Save(); applet.Redraw(block.Id); });
        s.Children.Add(title);
        var rows = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var row in block.Rows)
        {
            var key = row.Key;
            var box = new CheckBox { Content = row.Label, IsChecked = !place.Hidden.Contains(key), Margin = new Thickness(0, 0, 12, 4) };
            Ui.OnToggle(box, on =>
            {
                if (on) place.Hidden.Remove(key);
                else place.Hidden.Add(key);
                a.Settings.Save();
                applet.Redraw(block.Id);
            });
            rows.Children.Add(box);
        }
        var shows = Ui.Header(T("Shows"));
        shows.Margin = new Thickness(0, 10, 0, 4);
        s.Children.Add(shows);
        s.Children.Add(rows);
        return new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Width = 420, Children = { SimUi.Card(s) } };
    }

    // ---------------------------------------------------------------- where the inputs come from

    private void RenderSource()
    {
        source.Children.Clear();
        rows.Clear();
        // (the controllers are listed once; Refresh lists them again, for one plugged in since)
        Button? refresh = null;
        if (settings.Source == OverlaySource.Device)
        {
            refresh = GameControllers.RefreshButton(T("Look for controllers again"));
            refresh.Margin = new Thickness(0);
        }
        source.Children.Add(Ui.HeaderRow(T("Read From"), refresh));
        var game = new RadioButton { Content = T("Game"), IsChecked = settings.Source == OverlaySource.Game, Margin = new Thickness(0, 0, 16, 0),
            ToolTip = T("The profile's game's telemetry") };
        var device = new RadioButton { Content = T("Device"), IsChecked = settings.Source == OverlaySource.Device,
            ToolTip = T("A controller's axes") };
        game.Checked += (_, _) => SetSource(OverlaySource.Game);
        device.Checked += (_, _) => SetSource(OverlaySource.Device);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        line.Children.Add(game);
        line.Children.Add(device);
        source.Children.Add(line);
        if (settings.Source == OverlaySource.Device)
            foreach (var role in OverlaySettings.Roles) source.Children.Add(AxisRow(role));
        else
            foreach (var role in OverlaySettings.Roles) source.Children.Add(ValueRow(role));
        Tick();
    }

    private void SetSource(OverlaySource s)
    {
        if (settings.Source == s) return;
        settings.Source = s;
        Logging.Log.Here.Info($"Input Overlay: reading the {(s == OverlaySource.Game ? "game" : "controllers")}");
        applet.Changed();
    }

    private static string Title(string role) => role switch
    {
        "Steering" => T("Steering"), "Throttle" => T("Throttle"), "Brake" => T("Brake"), "Clutch" => T("Clutch"), _ => T("Handbrake"),
    };

    /// <summary>Game mode: each trace's live value.</summary>
    private Grid ValueRow(string role)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = Title(role), VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 1);
        g.Children.Add(value);
        rows[role] = (value, value);
        return g;
    }

    /// <summary>Device mode: the axis (Set, then move it), its live value, Invert, ✕.</summary>
    private Grid AxisRow(string role)
    {
        var axis = settings.Axis(role);
        var g = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = Title(role), VerticalAlignment = VerticalAlignment.Center });
        var input = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        void ShowInput()
        {
            input.Text = axis.Input?.Describe() ?? T("Not set");
            input.SetResourceReference(TextBlock.ForegroundProperty, axis.Input != null ? "TextBrush" : "TextSecondaryBrush");
            input.ToolTip = axis.Input?.Describe();
        }
        ShowInput();
        Grid.SetColumn(input, 1);
        g.Children.Add(input);
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(value, 2);
        g.Children.Add(value);
        var invert = new CheckBox { Content = T("Invert"), IsChecked = axis.Invert, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            ToolTip = T("For a pedal that reads full when released") };
        AutomationProperties.SetName(invert, F("Invert {0}", Title(role)));
        invert.Click += (_, _) => { axis.Invert = invert.IsChecked == true; applet.Changed(); };
        Grid.SetColumn(invert, 3);
        g.Children.Add(invert);
        var set = new Button { Content = T("Set"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = T("Then move it") };
        AutomationProperties.SetName(set, F("Set {0}", Title(role)));
        set.Click += (_, _) => CaptureAsync(role, input, ShowInput);
        Grid.SetColumn(set, 4);
        g.Children.Add(set);
        var clear = VolumeControl.IconButton("");
        clear.ToolTip = T("Clear");
        AutomationProperties.SetName(clear, F("Clear {0}", Title(role)));
        clear.Click += (_, _) => { axis.Input = null; applet.Changed(); };
        Grid.SetColumn(clear, 5);
        g.Children.Add(clear);
        rows[role] = (input, value);
        return g;
    }

    /// <summary>Set: the axis that moves next, on any controller (ForzaWheel's Capture, linked here).</summary>
    private async void CaptureAsync(string role, TextBlock input, Action restore)
    {
        capturing?.Cancel();
        var cts = capturing = new CancellationTokenSource();
        bool centred = role == "Steering";
        input.Text = centred ? T("Turn it left and right now… (Esc cancels)") : T("Press it all the way and let it go, now… (Esc cancels)");
        input.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        Focus();
        Capture.Result? result = null;
        var taken = OverlaySettings.Roles.Where(r => r != role).Select(r => settings.Axis(r).Input).OfType<InputRef>().ToList();
        try { result = await Capture.NextAsync(applet.Window, TimeSpan.FromSeconds(15), taken, true, centred ? 0.03 : 0.15, cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logging.ErrorLog.Write("Input Overlay capture", ex); }
        if (capturing == cts) capturing = null;
        if (result == null) { restore(); return; }
        var axis = settings.Axis(role);
        axis.Input = result.Input;
        axis.Input.Direction = 0;
        // a pedal that reads full when released
        if (!centred) axis.Invert = result.RestsHigh;
        Logging.Log.Here.Info($"Input Overlay: {role} is {axis.Input.Describe()}");
        applet.Changed();
    }
}
