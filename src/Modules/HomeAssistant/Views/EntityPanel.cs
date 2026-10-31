using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.HomeAssistant.Views;

/// <summary>
/// Everything that can be done with one entity, in a device's window: its name and state, ON / OFF (or Run), its
/// shortcuts, its level as a slider, and what its kind has besides (a light's colour, warmth and effects, a cover's
/// Open / Stop / Close, a thermostat's modes, a player's buttons, volume and source, a drop-down's options…). Built
/// once, then <see cref="Update"/> shows each new state (a slider being dragged is left alone).
/// </summary>
internal sealed class EntityPanel : Border
{
    private readonly HaService service;
    private readonly HaHotkeys hotkeys;
    private readonly HaThing thing;
    public string EntityId { get; }
    private readonly TextBlock name = new() { FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock state = Ui.Secondary("", wrap: true);
    private readonly QuickControl quick;
    private readonly Button? keys;
    private readonly StackPanel body = new();
    private readonly List<Action<HaEntity>> updaters = new();
    private bool updating;

    public EntityPanel(HaService service, HaHotkeys hotkeys, HaThing thing, HaEntity e)
    {
        this.service = service;
        this.hotkeys = hotkeys;
        this.thing = thing;
        EntityId = e.EntityId;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(12, 10, 12, 10);
        Margin = new Thickness(0, 0, 0, 8);
        SetResourceReference(BackgroundProperty, "RoomBrush");

        quick = new QuickControl(service) { Margin = new Thickness(8, 0, 0, 0) };
        var top = new DockPanel();
        if (HaHotkeys.Offered(e))
        {
            keys = HaUi.Glyph("", T("Shortcuts"), OpenKeys);
            keys.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(keys, Dock.Right);
            top.Children.Add(keys);
        }
        DockPanel.SetDock(quick, Dock.Right);
        top.Children.Add(quick);
        top.Children.Add(name);
        state.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(top);
        body.Children.Add(state);
        Build(e);
        Child = body;
        Update(e);
    }

    public void Update(HaEntity e)
    {
        updating = true;
        try
        {
            name.Text = service.ShortName(thing, e);
            name.ToolTip = e.EntityId;
            state.Text = Domains.Summary(e);
            state.Visibility = state.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            quick.Show(e);
            if (keys != null)
            {
                ((TextBlock)keys.Content).SetResourceReference(TextBlock.ForegroundProperty, hotkeys.Has(EntityId) ? "AccentBrush" : "TextBrush");
                keys.ToolTip = hotkeys.Has(EntityId) ? F("Shortcuts: {0}", hotkeys.Summary(EntityId)) : T("Shortcuts");
            }
            foreach (var u in updaters) u(e);
            body.IsEnabled = !e.Unavailable;
        }
        finally { updating = false; }
    }

    private void OpenKeys()
    {
        if (service.Entity(EntityId) is not { } e) return;
        HotkeyWindow.Open(e.FriendlyName, Window.GetWindow(this), hotkeys.For(e, e.FriendlyName));
        hotkeys.Register(); // (drops the ones left unbound)
        Update(service.Entity(EntityId) ?? e);
    }

    // ---------------------------------------------------------------- by kind

    private void Build(HaEntity e)
    {
        if (Domains.LevelOf(e) is { } level) Slider(level.Name, x => Domains.LevelOf(x), (x, v) => service.SetLevel(x.EntityId, v));
        string id = e.EntityId;
        void Call(string service, Dictionary<string, object?>? data = null) => this.service.Call(id, service, data);
        switch (e.Domain)
        {
            case "light":
                var modes = e.List("supported_color_modes");
                if (modes.Contains("color_temp"))
                    Slider(T("Warmth"), x =>
                    {
                        double min = x.Number("min_color_temp_kelvin") ?? 2000, max = x.Number("max_color_temp_kelvin") ?? 6500;
                        return new Level(x.Number("color_temp_kelvin") ?? (min + max) / 2, min, max, 100, "K", T("Warmth"));
                    }, (_, v) => Call("turn_on", new() { ["color_temp_kelvin"] = (int)v }));
                if (modes.Any(m => m is "hs" or "xy" or "rgb" or "rgbw" or "rgbww")) Colours(h => Call("turn_on", new() { ["hs_color"] = h }));
                if (e.Has(4)) Combo(T("Effect"), x => x.List("effect_list"), x => x.Text("effect"), o => Call("turn_on", new() { ["effect"] = o }));
                break;
            case "cover":
            case "valve":
                bool valve = e.Domain == "valve";
                Buttons(
                    (T("Open"), () => Call(valve ? "open_valve" : "open_cover"), e.Has(1)),
                    (T("Stop"), () => Call(valve ? "stop_valve" : "stop_cover"), e.Has(8)),
                    (T("Close"), () => Call(valve ? "close_valve" : "close_cover"), e.Has(2)));
                break;
            case "climate":
                Combo(T("Mode"), x => x.List("hvac_modes"), x => x.State, o => Call("set_hvac_mode", new() { ["hvac_mode"] = o }), Domains.StateWords);
                Combo(T("Preset"), x => x.List("preset_modes"), x => x.Text("preset_mode"), o => Call("set_preset_mode", new() { ["preset_mode"] = o }), Domains.StateWords);
                Combo(T("Fan"), x => x.List("fan_modes"), x => x.Text("fan_mode"), o => Call("set_fan_mode", new() { ["fan_mode"] = o }), Domains.StateWords);
                break;
            case "water_heater":
                Combo(T("Mode"), x => x.List("operation_list"), x => x.Text("operation_mode") ?? x.State, o => Call("set_operation_mode", new() { ["operation_mode"] = o }), Domains.StateWords);
                break;
            case "media_player":
                Buttons(
                    ("⏮", () => Call("media_previous_track"), e.Has(16)),
                    ("⏯", () => Call("media_play_pause"), e.Has(1) || e.Has(16384)),
                    ("⏭", () => Call("media_next_track"), e.Has(32)));
                if (e.Has(8)) Toggle(T("Mute"), x => x.Bool("is_volume_muted") == true, (x, on) => Call("volume_mute", new() { ["is_volume_muted"] = on }));
                if (e.Has(2048)) Combo(T("Source"), x => x.List("source_list"), x => x.Text("source"), o => Call("select_source", new() { ["source"] = o }));
                if (e.Has(65536)) Combo(T("Sound"), x => x.List("sound_mode_list"), x => x.Text("sound_mode"), o => Call("select_sound_mode", new() { ["sound_mode"] = o }));
                break;
            case "fan":
                if (e.Has(2)) Toggle(T("Oscillate"), x => x.Bool("oscillating") == true, (x, on) => Call("oscillate", new() { ["oscillating"] = on }));
                if (e.Has(8)) Combo(T("Preset"), x => x.List("preset_modes"), x => x.Text("preset_mode"), o => Call("set_preset_mode", new() { ["preset_mode"] = o }), Domains.StateWords);
                break;
            case "humidifier":
                Combo(T("Mode"), x => x.List("available_modes"), x => x.Text("mode"), o => Call("set_mode", new() { ["mode"] = o }), Domains.StateWords);
                break;
            case "select":
            case "input_select":
                Combo(T("Option"), x => x.List("options"), x => x.State, o => Call("select_option", new() { ["option"] = o }));
                break;
            case "lock":
                Buttons((T("Lock"), () => Call("lock"), true), (T("Unlock"), () => Call("unlock"), true), (T("Open"), () => Call("open"), e.Has(1)));
                break;
            case "vacuum":
                Buttons((T("Start"), () => Call("start"), true), (T("Pause"), () => Call("pause"), e.Has(4)), (T("Stop"), () => Call("stop"), e.Has(8)),
                    (T("Return to base"), () => Call("return_to_base"), e.Has(16)));
                break;
            case "lawn_mower":
                Buttons((T("Start"), () => Call("start_mowing"), true), (T("Pause"), () => Call("pause"), e.Has(2)), (T("Dock"), () => Call("dock"), e.Has(4)));
                break;
            case "automation":
                Buttons((T("Run its actions"), () => Call("trigger"), true));
                break;
            case "script":
                Buttons((T("Stop"), () => Call("turn_off"), true));
                break;
        }
    }

    private Grid Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        body.Children.Add(g);
        return g;
    }

    /// <summary>A level as a slider and its value; what's dragged is sent a moment after it stops moving.</summary>
    private void Slider(string label, Func<HaEntity, Level?> get, Action<HaEntity, double> set)
    {
        var slider = new Slider { VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        var shown = new TextBlock { Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel();
        DockPanel.SetDock(shown, Dock.Right);
        line.Children.Add(shown);
        line.Children.Add(slider);
        var row = Row(label, line);
        Level? level = null;
        DateTime touched = DateTime.MinValue;
        var send = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        send.Tick += (_, _) =>
        {
            send.Stop();
            if (service.Entity(EntityId) is { } x) set(x, slider.Value);
        };
        slider.ValueChanged += (_, _) =>
        {
            if (level != null) shown.Text = level.Show(slider.Value);
            if (updating) return;
            touched = DateTime.Now;
            send.Stop();
            send.Start();
        };
        updaters.Add(x =>
        {
            level = get(x);
            row.Visibility = level == null ? Visibility.Collapsed : Visibility.Visible;
            if (level == null) return;
            slider.Minimum = level.Min;
            slider.Maximum = level.Max;
            slider.SmallChange = level.Step;
            slider.LargeChange = level.Step * 5;
            slider.TickFrequency = level.Step;
            slider.IsSnapToTickEnabled = true;
            // left alone while it's being moved, and for a moment after (Home Assistant's answer is on its way)
            if (slider.IsMouseCaptureWithin || send.IsEnabled || DateTime.Now - touched < TimeSpan.FromSeconds(2)) return;
            slider.Value = level.Value;
            shown.Text = level.Show(level.Value);
        });
    }

    private void Combo(string label, Func<HaEntity, List<string>> items, Func<HaEntity, string?> current, Action<string> pick, Func<string, string>? words = null)
    {
        var box = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        var row = Row(label, box);
        List<string> now = new();
        box.SelectionChanged += (_, _) =>
        {
            if (updating || box.SelectedIndex < 0 || box.SelectedIndex >= now.Count) return;
            pick(now[box.SelectedIndex]);
        };
        updaters.Add(x =>
        {
            var list = items(x);
            row.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (!list.SequenceEqual(now))
            {
                now = list;
                box.ItemsSource = list.Select(o => words != null ? words(o) : o).ToList();
            }
            box.SelectedIndex = current(x) is { } c ? list.IndexOf(c) : -1;
        });
    }

    private void Toggle(string label, Func<HaEntity, bool> get, Action<HaEntity, bool> set)
    {
        var chip = HaUi.Chip(label);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        chip.Click += (_, _) => { if (service.Entity(EntityId) is { } x) set(x, chip.IsChecked == true); };
        Row(label, chip);
        updaters.Add(x => HaUi.ShowChip(chip, get(x)));
    }

    private void Buttons(params (string Text, Action Click, bool Offered)[] buttons)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (text, click, offered) in buttons)
        {
            if (!offered) continue;
            var b = HaUi.Small(text, click);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.MinWidth = 44;
            panel.Children.Add(b);
        }
        if (panel.Children.Count > 0) body.Children.Add(panel);
    }

    /// <summary>A row of colours (and white) for a light that takes one.</summary>
    private void Colours(Action<double[]> pick)
    {
        var panel = new WrapPanel();
        foreach (var (h, s) in new[] { (0.0, 100.0), (25, 100), (50, 100), (120, 100), (175, 100), (210, 100), (240, 100), (275, 100), (310, 100), (340, 70), (0, 0) })
        {
            var c = FromHs(h, s);
            var swatch = new Ellipse
            {
                Width = 18, Height = 18, Margin = new Thickness(0, 0, 5, 4), Fill = new SolidColorBrush(c), Cursor = Cursors.Hand,
                Stroke = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)), StrokeThickness = 1,
                ToolTip = s == 0 ? T("White") : null,
            };
            double[] hs = { h, s };
            swatch.MouseLeftButtonUp += (_, ev) => { pick(hs); ev.Handled = true; };
            panel.Children.Add(swatch);
        }
        Row(T("Colour"), panel);
    }

    private static Color FromHs(double h, double s)
    {
        double sat = s / 100, c = sat, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = 1 - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
