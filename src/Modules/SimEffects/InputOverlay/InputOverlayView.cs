using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Applets.Gaming.ForzaWheel;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// The Input Overlay tab. Top: on / off, its shortcut and what it's reading. Left: the overlay (place, history,
/// background, traces, session log) and the speeds; right: where the inputs come from (the game, or an axis of a
/// controller each). What it shows is the SimEffects profile in use's.
/// </summary>
internal sealed class InputOverlayView : UserControl
{
    private readonly InputOverlayApplet applet;
    private readonly OverlaySettings settings;
    private readonly ToggleButton onOff;
    private readonly Button onOffKey = VolumeControl.IconButton("");
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel left = new(), source = new();
    private readonly Button move = new() { Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
    private readonly Dictionary<string, (TextBlock Input, TextBlock Value)> rows = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? capturing;

    public InputOverlayView(InputOverlayApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        onOff = Ui.OnOff(T("Input Overlay on"), settings.Enabled, applet.SetEnabled);
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

        move.ToolTip = T("Shows the overlay and lets it be dragged and stretched from its edges; Done fixes it in place again");
        move.Click += (_, _) => applet.Move();
        var right = new StackPanel();
        right.Children.Add(Ui.Card(source));

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(Ui.Columns(left, right, 360));
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
        onOff.IsChecked = settings.Enabled;
        onOff.Content = settings.Enabled ? T("ON") : T("OFF");
    }

    private void ShowKey()
    {
        ((TextBlock)onOffKey.Content).SetResourceReference(TextBlock.ForegroundProperty, applet.Hotkey.Value != null ? "AccentBrush" : "TextBrush");
        onOffKey.ToolTip = applet.Hotkey.Value is { } k ? F("On / off shortcut: {0}", k) : T("On / off shortcut");
    }

    public void ShowMove() => move.Content = applet.Overlay is { Editing: true } ? T("Done") : T("Move / resize");

    /// <summary>Everything drawn again from the settings (another profile, or the source changed).</summary>
    public void Render()
    {
        ShowOnOff();
        left.Children.Clear();
        (move.Parent as Panel)?.Children.Remove(move);
        left.Children.Add(Ui.Card(OverlayPanel()));
        left.Children.Add(Ui.Card(SpeedPanel()));
        RenderSource();
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
        s.Children.Add(Ui.Header(T("Overlay")));
        ShowMove();
        var reset = new Button { Content = T("Reset position"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
        reset.Click += (_, _) => applet.ResetPlace();
        var buttons = new WrapPanel();
        buttons.Children.Add(move);
        buttons.Children.Add(reset);
        s.Children.Add(buttons);

        var history = new ComboBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (int sec in new[] { 3, 5, 10, 20, 30 }) history.Items.Add(new ComboBoxItem { Content = F("{0} s", sec), Tag = sec });
        history.SelectedItem = history.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.Seconds) ?? history.Items[1];
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is ComboBoxItem { Tag: int sec }) { settings.Seconds = sec; settings.Save(); applet.Overlay?.Changed(); } };
        s.Children.Add(Ui.Field(T("History"), history));
        s.Children.Add(Ui.SliderRow(T("Background"), 0, 1, 0.05, settings.Background, v => $"{v * 100:0}%",
            v => { settings.Background = v; settings.Save(); applet.Overlay?.Changed(); }, T("How dark the overlay's background is: 0% is none, only the traces show")));

        var traces = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Trace(string name, Func<bool> get, Action<bool> set)
        {
            var box = new CheckBox { Content = name, IsChecked = get(), Margin = new Thickness(0, 0, 12, 4) };
            box.Click += (_, _) => { set(box.IsChecked == true); settings.Save(); applet.Overlay?.Changed(); };
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
        show.Click += (_, _) => { settings.Speed = show.IsChecked == true; settings.Save(); applet.Overlay?.Changed(); };
        s.Children.Add(Ui.HeaderLine(T("Speed"), show));
        s.Children.Add(Ui.SliderRow(T("Window Size"), 1, 30, 1, settings.WindowSeconds, v => F("{0:0} s", v),
            v => { settings.WindowSeconds = (int)v; settings.Save(); }, T("How long the highest and lowest speeds hold")));
        var unit = new ComboBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { new KeyValuePair<SpeedUnit, string>(SpeedUnit.Mph, T("mph")), new KeyValuePair<SpeedUnit, string>(SpeedUnit.Kmh, T("km/h")) },
            DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = settings.Unit };
        unit.SelectionChanged += (_, _) => { if (unit.SelectedValue is SpeedUnit u) { settings.Unit = u; settings.Save(); } };
        s.Children.Add(Ui.Field(T("Units"), unit));
        return s;
    }

    // ---------------------------------------------------------------- where the inputs come from

    private void RenderSource()
    {
        source.Children.Clear();
        rows.Clear();
        source.Children.Add(Ui.Header(T("Read From")));
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
