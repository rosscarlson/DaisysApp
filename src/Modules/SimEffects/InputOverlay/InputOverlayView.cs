using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Controllers;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Applets.SimEffects.Analysis;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// The Overlays tab (the Input Overlay's before 3.33.0). Top: on / off, its shortcut and what it's reading. Under it
/// (since 4.17.9; a list and the one picked's settings before) a box per overlay, four across and two down
/// (<see cref="OverlayKinds.Boxes"/>): its tick at the top left switches it on (and its settings with it), then its
/// shortcut (show / hide), show / hide itself and Edit (place it); a picture of it (<see cref="OverlayPreviews"/>); and
/// all its settings. Inputs: the overlay (history, background, traces), the speeds, and where the inputs come from (the
/// game, or an axis of a controller each), the SimEffects profile in use's; the Dash: its dials and look; a block: its
/// rows, title and background.
/// </summary>
internal sealed class InputOverlayView : UserControl
{
    private readonly InputOverlayApplet applet;
    private readonly OverlaySettings settings;
    private readonly System.Windows.Controls.Primitives.ToggleButton onOff;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid boxes = new();
    private readonly StackPanel source = new();
    private readonly Dictionary<string, Box> shown = new();
    private bool filling;
    private readonly Dictionary<string, (TextBlock Input, TextBlock Value)> rows = new();
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? capturing;

    /// <summary>An overlay's box: its tick, its show / hide and Edit, its picture, its settings.</summary>
    private sealed record Box(CheckBox On, Button? Eye, TextBlock? EyeGlyph, Button? Edit, Border Preview, Func<FrameworkElement>? Picture, FrameworkElement? Options);

    public InputOverlayView(InputOverlayApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        onOff = SimUi.OnOff(T("Overlays on"), settings.Enabled, applet.SetEnabled);
        // the on / off shortcut: a key combination or a controller button (Core's Hotkeys)
        var onOffKey = HotkeyWindow.Button(T("On / off shortcut"), () => applet.Hotkey);
        onOffKey.Margin = new Thickness(6, 0, 0, 0);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(onOffKey, Dock.Left);
        top.Children.Add(onOffKey);
        top.Children.Add(status);

        for (int c = 0; c < 4; c++) boxes.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 240 });
        boxes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        boxes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(new ScrollViewer
        {
            Content = boxes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = page;

        timer = Ui.Every(this, 100, Tick, now: true, shown: on => { applet.Reading(); if (!on) capturing?.Cancel(); });
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

    /// <summary>Everything drawn again from the settings (another profile, or the source changed).</summary>
    public void Render()
    {
        ShowOnOff();
        boxes.Children.Clear();
        shown.Clear();
        for (int i = 0; i < OverlayKinds.Boxes.Length; i++)
        {
            string id = OverlayKinds.Boxes[i];
            var card = MakeBox(id);
            card.Margin = new Thickness(0, 0, i % 4 < 3 ? 12 : 0, 12);
            Grid.SetColumn(card, i % 4);
            Grid.SetRow(card, i / 4);
            boxes.Children.Add(card);
        }
        ShowStates();
        Tick();
    }

    // ---------------------------------------------------------------- the boxes

    private Border MakeBox(string id)
    {
        string name = OverlayKinds.Name(id);
        var title = Ui.Header(name);
        title.Margin = new Thickness(0);
        var on = new CheckBox { Content = title, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("Switch it on") };
        AutomationProperties.SetName(on, F("{0} on", name));
        Ui.OnToggle(on, v => { if (!filling) applet.Enable(id, v); });
        var body = new StackPanel();
        var analysis = applet.Analysis;
        bool block = id is not (OverlayKinds.Inputs or OverlayKinds.Dash);
        if (block && analysis == null)
        {
            on.IsEnabled = false;
            body.Children.Add(Ui.HeaderRow(on));
            var note = OverlayPreviews.Note(T("Needs Analysis (Settings → General → Modules)"));
            body.Children.Add(note);
            shown[id] = new Box(on, null, null, null, note, null, null);
            return Ui.Card(body);
        }

        var key = applet.KeyOf(id) is { } k ? HotkeyWindow.Button(F("{0} shortcut", name), () => k) : null;
        var eyeGlyph = Ui.Icon("", 14);
        var eye = new Button { Content = eyeGlyph, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        AutomationProperties.SetName(eye, F("Show or hide {0}", name));
        eye.Click += (_, _) => applet.SetTicked(id, !applet.IsTicked(id));
        var edit = new Button { Content = T("Edit"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = T("Move and size it") };
        AutomationProperties.SetName(edit, F("Edit {0}", name));
        edit.Click += (_, _) => applet.Edit(id);
        body.Children.Add(Ui.HeaderRow(on, key, eye, edit));

        Func<FrameworkElement> picture = id switch
        {
            OverlayKinds.Dash => () => OverlayPreviews.Dash(settings),
            OverlayKinds.Inputs => () => OverlayPreviews.Inputs(settings),
            _ => () => OverlayPreviews.Block(Blocks.ById(id)!, analysis!.Settings),
        };
        var preview = OverlayPreviews.Frame(picture());
        body.Children.Add(preview);
        FrameworkElement options = id switch
        {
            OverlayKinds.Dash => DashOptions(),
            OverlayKinds.Inputs => InputsOptions(),
            _ => BlockOptions(Blocks.ById(id)!, analysis!),
        };
        body.Children.Add(options);
        shown[id] = new Box(on, eye, eyeGlyph, edit, preview, picture, options);
        return Ui.Card(body);
    }

    /// <summary>Each box's tick, show / hide and Edit as they are; its settings usable only while it's on.</summary>
    public void ShowStates()
    {
        filling = true;
        foreach (var (id, box) in shown)
        {
            bool on = applet.IsOn(id);
            if (box.On.IsEnabled) box.On.IsChecked = on;
            box.Preview.Opacity = on ? 1 : 0.4;
            if (box.Options != null) box.Options.IsEnabled = on;
            if (box.Eye != null && box.EyeGlyph != null)
            {
                bool visible = applet.IsTicked(id);
                box.Eye.IsEnabled = on;
                box.EyeGlyph.Text = visible ? "" : "";
                box.Eye.ToolTip = visible ? T("Shown: hide it") : T("Hidden: show it");
            }
            if (box.Edit != null)
            {
                box.Edit.IsEnabled = on;
                box.Edit.Content = applet.IsEditing(id) ? T("Done") : T("Edit");
            }
        }
        filling = false;
    }

    /// <summary>Its look changed here: the overlay and its picture drawn again.</summary>
    private void Changed(string id)
    {
        applet.Redraw(id);
        if (shown.TryGetValue(id, out var box) && box.Picture != null && box.Preview.Child is Viewbox v) v.Child = box.Picture();
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

    private static TextBlock Section(string text)
    {
        var t = Ui.Header(text);
        t.Margin = new Thickness(0, 12, 0, 4);
        return t;
    }

    private static CheckBox Tick(string text, bool value, Action<bool> set, string? tip = null)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 12, 0), ToolTip = tip };
        Ui.OnToggle(box, set);
        return box;
    }

    // ---------------------------------------------------------------- Inputs

    private FrameworkElement InputsOptions()
    {
        var s = new StackPanel();
        var reset = new Button { Content = T("Reset position"), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        reset.Click += (_, _) => applet.ResetPlace();
        s.Children.Add(reset);
        var history = Ui.Choice(new[] { 3, 5, 10, 20, 30 }.Select(sec => (F("{0} s", sec), sec)), settings.Seconds,
            sec => { settings.Seconds = sec; settings.Save(); Changed(OverlayKinds.Inputs); }, 90);
        history.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(SimUi.Field(T("History"), history, 100));
        s.Children.Add(SimUi.SliderRow(T("Background"), 0, 1, 0.05, settings.Background, v => $"{v * 100:0}%",
            v => { settings.Background = v; settings.Save(); Changed(OverlayKinds.Inputs); }, T("How dark the overlay's background is")));

        var traces = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        void Trace(string name, bool value, Action<bool> set) =>
            traces.Children.Add(Tick(name, value, v => { set(v); settings.Save(); Changed(OverlayKinds.Inputs); }));
        Trace(T("Steering"), settings.Steering, v => settings.Steering = v);
        Trace(T("Throttle"), settings.Throttle, v => settings.Throttle = v);
        Trace(T("Brake"), settings.Brake, v => settings.Brake = v);
        Trace(T("Clutch"), settings.Clutch, v => settings.Clutch = v);
        Trace(T("Handbrake"), settings.Handbrake, v => settings.Handbrake = v);
        s.Children.Add(traces);

        // the speeds
        var speed = Tick(T("Show"), settings.Speed, v => { settings.Speed = v; settings.Save(); Changed(OverlayKinds.Inputs); },
            T("Now in the middle; the window's highest above, its lowest below"));
        speed.Margin = new Thickness(0);
        var speedRow = Ui.HeaderRow(Ui.Header(T("Speed")), speed);
        speedRow.Margin = new Thickness(0, 12, 0, 4);
        s.Children.Add(speedRow);
        s.Children.Add(SimUi.SliderRow(T("Window Size"), 1, 30, 1, settings.WindowSeconds, v => F("{0:0} s", v),
            v => { settings.WindowSeconds = (int)v; settings.Save(); }, T("How long the highest and lowest speeds hold")));
        var unit = Ui.Choice(new[] { (T("mph"), SpeedUnit.Mph), (T("km/h"), SpeedUnit.Kmh) }, settings.Unit,
            u => { settings.Unit = u; settings.Save(); Changed(OverlayKinds.Inputs); Changed(OverlayKinds.Dash); }, 90);
        unit.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(SimUi.Field(T("Units"), unit, 100));

        // where the inputs come from
        s.Children.Add(source);
        RenderSource();
        return s;
    }

    // ---------------------------------------------------------------- the Dash and the blocks

    private FrameworkElement DashOptions()
    {
        var d = settings.Dash;
        var s = new StackPanel();
        void Redo() { settings.Save(); Changed(OverlayKinds.Dash); }
        var look = Ui.Choice(new[] { (T("Analog"), DashLook.Analog), (T("Digital"), DashLook.Digital) }, d.Look, v => { d.Look = v; Redo(); }, 120);
        look.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(SimUi.Field(T("Look"), look, 100));
        // (one dial at least: the last one on can't be unticked)
        var dials = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        CheckBox? tach = null, speedo = null;
        void Lock()
        {
            tach!.IsEnabled = !(d.Tach && !d.Speedo);
            speedo!.IsEnabled = !(d.Speedo && !d.Tach);
        }
        tach = Tick(T("Tach"), d.Tach, v => { d.Tach = v; Lock(); Redo(); });
        speedo = Tick(T("Speedometer"), d.Speedo, v => { d.Speedo = v; Lock(); Redo(); });
        Lock();
        dials.Children.Add(tach);
        dials.Children.Add(speedo);
        s.Children.Add(SimUi.Field(T("Dials"), dials, 100));
        s.Children.Add(SimUi.SliderRow(T("Background"), 0, 1, 0.05, d.Opacity, v => $"{v * 100:0}%",
            v => { d.Opacity = v; settings.Save(); applet.Redraw(OverlayKinds.Dash); }, T("How see-through the dial is")));
        var unit = Ui.Choice(new (string, SpeedUnit?)[] { (T("As Inputs'"), null), (T("mph"), SpeedUnit.Mph), (T("km/h"), SpeedUnit.Kmh) }, d.Unit,
            v => { d.Unit = v; Redo(); }, 120);
        unit.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(SimUi.Field(T("Units"), unit, 100));
        s.Children.Add(Tick(T("TCS and ABS lights"), d.Lights, v => { d.Lights = v; Redo(); },
            T("TCS: the wheels spinning under power; ABS: a wheel locking under braking")));
        s.Children.Add(Tick(T("Flash the gear at the shift point"), d.ShiftFlash, v => { d.ShiftFlash = v; Redo(); }, T("Red and flashing")));
        return s;
    }

    private FrameworkElement BlockOptions(Block block, AnalysisApplet a)
    {
        var place = a.Settings.Widget(block.Id);
        var s = new StackPanel();
        void Redo() { a.Settings.Save(); Changed(block.Id); }
        s.Children.Add(SimUi.SliderRow(T("Background"), 0, 1, 0.05, place.Background, v => $"{v * 100:0}%",
            v => { place.Background = v; Redo(); }, T("How see-through its background is")));
        s.Children.Add(Tick(T("Title"), place.Title ?? block.Kind is not (BlockKind.Timing or BlockKind.Big), v => { place.Title = v; Redo(); }, T("Show the title")));
        s.Children.Add(Section(T("Shows")));
        var rowsPanel = new WrapPanel();
        foreach (var row in block.Rows)
        {
            var key = row.Key;
            rowsPanel.Children.Add(Tick(row.Label, !place.Hidden.Contains(key), on =>
            {
                if (on) place.Hidden.Remove(key);
                else place.Hidden.Add(key);
                Redo();
            }));
        }
        s.Children.Add(rowsPanel);
        return s;
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
        var header = Ui.HeaderRow(T("Read From"), refresh);
        header.Margin = new Thickness(0, 12, 0, 4);
        source.Children.Add(header);
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
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
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
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
        Grid.SetColumn(value, 2);
        g.Children.Add(value);
        var invert = new CheckBox { Content = T("Invert"), IsChecked = axis.Invert, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            ToolTip = T("For a pedal that reads full when released") };
        AutomationProperties.SetName(invert, F("Invert {0}", Title(role)));
        invert.Click += (_, _) => { axis.Invert = invert.IsChecked == true; applet.Changed(); };
        Grid.SetColumn(invert, 3);
        g.Children.Add(invert);
        var set = new Button { Content = T("Set"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = T("Then move it") };
        AutomationProperties.SetName(set, F("Set {0}", Title(role)));
        set.Click += (_, _) => CaptureAsync(role, input, ShowInput);
        Grid.SetColumn(set, 4);
        g.Children.Add(set);
        var clear = VolumeControl.IconButton("");
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
