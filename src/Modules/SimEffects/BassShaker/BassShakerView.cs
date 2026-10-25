using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// The Shaker tab. Top: on / off and what it's doing, the profile in use (and New), and the master volume (top
/// right). Left: the devices (each with its volume, meter and settings) and the profile's game, port and programs.
/// Right: the meters, a column per effect in their order, then the effects: each with its volume and mute even
/// folded, its settings when opened, dragged by its grip to change the order.
/// </summary>
internal sealed class BassShakerView : UserControl
{
    private const int LeftWidth = 360;
    private const string DragFormat = "DaisysApp.ShakerEffect";

    private readonly ShakerSettings settings;
    private readonly ShakerEngine engine;
    private readonly VolumeHotkeys hotkeys;
    private readonly ToggleButton onOff = new() { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button onOffKey = VolumeControl.IconButton("\uE7FC");
    private readonly StackPanel leftStack = new();
    private readonly StackPanel effectsStack = new();
    private readonly SpectrumMeter spectrum = new();
    private readonly SpectrumMeter widgetMeter = new() { Height = double.NaN };
    private Widget? metersWidget;
    private readonly Dictionary<string, MiniMeter> effectMeters = new(), deviceMeters = new();
    private readonly Dictionary<string, float> deviceLevels = new();
    private readonly Dictionary<string, TextBlock> deviceProblems = new();
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private int ticks;
    private Point dragStart;
    private string? dragKey;

    public BassShakerView(ShakerSettings settings, ShakerEngine engine, VolumeHotkeys hotkeys)
    {
        this.settings = settings;
        this.engine = engine;
        this.hotkeys = hotkeys;

        // ---- the top line ----
        onOff.SetResourceReference(StyleProperty, "ChipToggle");
        AutomationProperties.SetName(onOff, T("Shaker on"));
        onOff.Click += (_, _) => SetEnabled(onOff.IsChecked == true);
        onOffKey.Margin = new Thickness(6, 0, 0, 0);
        AutomationProperties.SetName(onOffKey, T("On / off shortcut"));
        onOffKey.Click += (_, _) =>
        {
            HotkeyWindow.Open(T("On / off shortcut"), Window.GetWindow(this), hotkeys.OnOff);
            Render();
        };
        var master = new VolumeControl(T("Master"), settings.Master, hotkeys, Save) { Width = 300, VerticalAlignment = VerticalAlignment.Center };
        var masterLabel = new TextBlock { Text = T("Master"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 4, 0) };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(masterLabel);
        right.Children.Add(master);
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(onOffKey, Dock.Left);
        top.Children.Add(onOffKey);
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(status);

        // ---- the columns ----
        var leftScroll = new ScrollViewer { Content = leftStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var rightStack = new StackPanel();
        // the meters, and a button that pops them out as a widget
        var popOut = VolumeControl.IconButton("");
        popOut.HorizontalAlignment = HorizontalAlignment.Right;
        popOut.VerticalAlignment = VerticalAlignment.Top;
        popOut.Margin = new Thickness(0, -6, -6, 0);
        popOut.ToolTip = T("Pop out");
        AutomationProperties.SetName(popOut, T("Pop out"));
        popOut.Click += (_, _) => MakeWidget();
        var meters = new Grid();
        meters.Children.Add(spectrum);
        meters.Children.Add(popOut);
        var metersCard = Card(meters);
        Widget.Offer(metersCard, MakeWidget);
        rightStack.Children.Add(metersCard);
        rightStack.Children.Add(EffectsCard());
        var rightScroll = new ScrollViewer { Content = rightStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftWidth) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.Children.Add(leftScroll);
        Grid.SetColumn(rightScroll, 2);
        columns.Children.Add(rightScroll);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(columns);
        Content = page;

        saveTimer.Tick += (_, _) => { saveTimer.Stop(); settings.Save(); };
        meterTimer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => Meters();
        hotkeys.Changed += () => { VolumeControl.Refresh(); Save(); };
    }

    public ShakerProfile? Active => settings.Profiles.FirstOrDefault(p => p.Name == settings.Active) ?? settings.Profiles.FirstOrDefault();

    /// <summary>Uses a profile (picked here, or by a game starting).</summary>
    public void Use(ShakerProfile p)
    {
        if (Active == p && settings.Active == p.Name) return;
        settings.Active = p.Name;
        Logging.Log.Here.Info($"Shaker: profile {p.Name}");
        Structure();
    }

    public void SetEnabled(bool on)
    {
        settings.Enabled = on;
        Logging.Log.Here.Info($"Shaker: switched {(on ? "on" : "off")}");
        settings.Save();
        engine.Apply(Active);
        Render();
    }

    /// <summary>Profiles, effects or devices changed: the engine and the shortcuts take them in, saved, drawn again.</summary>
    private void Structure()
    {
        engine.Apply(Active);
        hotkeys.Register();
        settings.Save();
        Render();
    }

    private LevelWizard? wizard;
    private DispatcherTimer? refreshTimer;
    private DispatcherTimer wizardRefresh => refreshTimer ??= MakeRefresh();

    private DispatcherTimer MakeRefresh()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            VolumeControl.Refresh();
            RenderEffects();
        };
        return t;
    }

    /// <summary>The Level Wizard for the profile in use (brought to the front if it's open already).</summary>
    private void OpenWizard()
    {
        if (wizard != null) { wizard.Activate(); return; }
        if (Active is not { } p || p.Effects.Count == 0) return;
        wizard = new LevelWizard(settings, p, engine, Save);
        // what it changes shows here as it happens (another screen, while driving), a tenth of a second at most behind
        wizard.Changed += () => { wizardRefresh.Stop(); wizardRefresh.Start(); };
        wizard.Closed += (_, _) =>
        {
            wizardRefresh.Stop();
            wizard = null;
            VolumeControl.Refresh();
            Render();
        };
        wizard.Show();
    }

    /// <summary>The meters' timer runs while the tab or the widget shows.</summary>
    private void Meters()
    {
        if (IsVisible || metersWidget != null) meterTimer.Start(); else meterTimer.Stop();
    }

    /// <summary>The pop-out button and the meters' right-click Make Widget: the widget, being edited (placed, sized).</summary>
    private void MakeWidget()
    {
        if (metersWidget != null) metersWidget.BeginEdit(); else ShowWidget(edit: true);
    }

    /// <summary>The meters as a widget (Core's, as Performance's): shown at the start if it was open.</summary>
    public void ShowWidget(bool edit = false)
    {
        if (metersWidget != null) return;
        settings.MetersWidget.Show = true;
        metersWidget = new Widget(settings.MetersWidget, Save, RemoveWidget, firstSize: new Size(420, 170)) { Title = T("Shaker meters"), MinHeight = 90, Body = widgetMeter };
        metersWidget.Show();
        if (edit) metersWidget.BeginEdit();
        Save();
        Meters();
    }

    /// <summary>Its Delete: closed, and not shown at the next start.</summary>
    private void RemoveWidget()
    {
        settings.MetersWidget.Show = false;
        Save();
        HideWidget();
    }

    /// <summary>Closed (the app closing: shown again at the next start).</summary>
    public void HideWidget()
    {
        metersWidget?.Close();
        metersWidget = null;
        Meters();
    }

    private void Save()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void Render()
    {
        onOff.IsChecked = settings.Enabled;
        onOff.Content = settings.Enabled ? T("ON") : T("OFF");
        ((TextBlock)onOffKey.Content).SetResourceReference(TextBlock.ForegroundProperty, settings.OnOffKey != null ? "AccentBrush" : "TextBrush");
        onOffKey.ToolTip = settings.OnOffKey is { } key ? F("On / off shortcut: {0}", key) : T("On / off shortcut");
        ShowStatus();
        RenderLeft();
        RenderEffects();
    }

    private void ShowStatus()
    {
        status.Text = settings.Enabled ? engine.Status : "";
        status.SetResourceReference(TextBlock.ForegroundProperty, engine.Receiving ? "SuccessBrush" : "TextSecondaryBrush");
    }

    // ---- meters ----

    private void Tick()
    {
        var signals = engine.Signals;
        var showing = wizard?.Showing;
        var levels = signals.Select(s => (s.Info.Title, s.Level, s.Effect.Muted, s.Effect == showing)).ToArray();
        if (metersWidget != null) widgetMeter.Update(levels, meterTimer.Interval.TotalSeconds);
        if (!IsVisible) return;
        spectrum.Update(levels, meterTimer.Interval.TotalSeconds);
        foreach (var s in signals)
            if (effectMeters.TryGetValue(s.Effect.Key, out var m)) m.Level = s.Level;
        foreach (var (key, m) in deviceMeters)
        {
            float peak = engine.TakePeak(key);
            float shown = Math.Max(peak, deviceLevels.GetValueOrDefault(key) * 0.85f);
            deviceLevels[key] = shown;
            m.Level = shown;
        }
        if (++ticks % 15 == 0)
        {
            ShowStatus();
            foreach (var (key, t) in deviceProblems)
            {
                string? p = engine.Problem(key);
                t.Text = p ?? "";
                t.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    // ---- left: devices and the profile ----

    private void RenderLeft()
    {
        leftStack.Children.Clear();
        deviceMeters.Clear();
        deviceProblems.Clear();
        leftStack.Children.Add(DevicesCard());
        if (Active is { } p) leftStack.Children.Add(ProfileCard(p));
    }

    private Border DevicesCard()
    {
        var panel = new StackPanel();
        var add = new Button { Content = T("Add Device"), Padding = new Thickness(10, 3, 10, 3) };
        add.SetResourceReference(StyleProperty, "AccentButton");
        add.Click += (_, _) =>
        {
            var w = new AddDeviceWindow { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() != true || w.Added is not { } d) return;
            settings.Devices.Add(d);
            Logging.Log.Here.Info($"Shaker: device {d.Name} added ({d.Type}, {d.Channels})");
            Structure();
        };
        panel.Children.Add(HeaderLine(T("Devices"), add));
        if (settings.Devices.Count == 0) panel.Children.Add(Secondary(T("No devices yet")));
        foreach (var d in settings.Devices) panel.Children.Add(DeviceRow(d));
        return Card(panel);
    }

    private Border DeviceRow(ShakerDevice d)
    {
        var body = new StackPanel();
        var chevron = Chevron(d.Expanded, () => { d.Expanded = !d.Expanded; Save(); RenderLeft(); });
        var meter = new MiniMeter { Margin = new Thickness(8, 0, 0, 0) };
        deviceMeters[d.Key] = meter;
        var name = new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{TransducerType.Of(d.Type).Name} · {AddDeviceWindow.ChannelNames.First(c => c.Key == d.Channels).Value}" };
        var where = new TextBlock { Text = AddDeviceWindow.PositionNames.First(p => p.Key == d.Position).Value + (d.Channels == Channels.Split ? " · " + T("Split") : ""),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        where.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var head = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Left);
        head.Children.Add(chevron);
        DockPanel.SetDock(meter, Dock.Right);
        head.Children.Add(meter);
        DockPanel.SetDock(where, Dock.Right);
        head.Children.Add(where);
        head.Children.Add(name);
        body.Children.Add(head);
        body.Children.Add(new VolumeControl(d.Name, d, hotkeys, Save) { Margin = new Thickness(0, 4, 0, 0) });
        var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        problem.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        deviceProblems[d.Key] = problem;
        body.Children.Add(problem);

        if (d.Expanded)
        {
            var type = new ComboBox { ItemsSource = TransducerType.All, DisplayMemberPath = nameof(TransducerType.Name), SelectedItem = TransducerType.Of(d.Type) };
            type.SelectionChanged += (_, _) =>
            {
                if (type.SelectedItem is not TransducerType t || t.Id == d.Type) return;
                d.Type = t.Id;
                d.MinHz = t.MinHz;
                d.MaxHz = t.MaxHz;
                Structure();
            };
            body.Children.Add(Field(T("Transducer"), type));
            var channels = new ComboBox { ItemsSource = AddDeviceWindow.ChannelNames, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = d.Channels };
            channels.SelectionChanged += (_, _) =>
            {
                if (channels.SelectedValue is Channels c && c != d.Channels) { d.Channels = c; Structure(); }
            };
            var position = new ComboBox { ItemsSource = AddDeviceWindow.PositionNames, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = d.Position,
                ToolTip = T("The wheels it stands for") };
            position.SelectionChanged += (_, _) =>
            {
                if (position.SelectedValue is Position at && at != d.Position) { d.Position = at; Structure(); }
            };
            body.Children.Add(Field(T("Position"), position));
            body.Children.Add(Field(T("Channels"), channels));
            body.Children.Add(SliderRow(T("Low"), 5, 200, 1, d.MinHz, v => $"{v:0} Hz", v => { d.MinHz = (int)v; if (d.MaxHz <= d.MinHz) d.MaxHz = d.MinHz + 1; Save(); }, T("Nothing lower is sent to it")));
            body.Children.Add(SliderRow(T("High"), 10, 250, 1, d.MaxHz, v => $"{v:0} Hz", v => { d.MaxHz = (int)v; Save(); }, T("Nothing higher is sent to it")));
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var test = new Button { Content = T("Test"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5), ToolTip = F("{0} Hz for a second", TransducerType.Of(d.Type).PeakHz) };
            test.Click += (_, _) => engine.TestDevice(d);
            buttons.Children.Add(test);
            buttons.Children.Add(ConfirmButton(T("Remove"), () =>
            {
                settings.Devices.Remove(d);
                foreach (var p in settings.Profiles) foreach (var e in p.Effects) { e.Off.Remove(d.Key); e.Levels.Remove(d.Key); }
                Logging.Log.Here.Info($"Shaker: device {d.Name} removed");
                Structure();
            }));
            body.Children.Add(buttons);
        }
        return Row(body);
    }

    private Border ProfileCard(ShakerProfile p)
    {
        var panel = new StackPanel();
        // the profile in use, and New, at the top
        var profiles = new ComboBox { ItemsSource = settings.Profiles, DisplayMemberPath = nameof(ShakerProfile.Name), SelectedItem = p, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(profiles, T("Profile"));
        profiles.SelectionChanged += (_, _) => { if (profiles.SelectedItem is ShakerProfile picked && picked != p) Use(picked); };
        var newProfile = new Button { Content = T("New"), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
        newProfile.Click += (_, _) => NewProfileMenu(newProfile);
        var pick = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var title = Header(T("Profile"));
        title.Margin = new Thickness(0, 0, 10, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(title, Dock.Left);
        pick.Children.Add(title);
        DockPanel.SetDock(newProfile, Dock.Right);
        pick.Children.Add(newProfile);
        pick.Children.Add(profiles);
        panel.Children.Add(pick);

        var name = new TextBox { Text = p.Name };
        void Rename()
        {
            string n = name.Text.Trim();
            if (n.Length == 0 || n == p.Name) { name.Text = p.Name; return; }
            if (settings.Profiles.Any(o => o != p && o.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase))) { name.Text = p.Name; return; }
            bool active = settings.Active == p.Name || Active == p;
            p.Name = n;
            if (active) settings.Active = n;
            Structure();
        }
        name.LostFocus += (_, _) => Rename();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); else if (e.Key == Key.Escape) name.Text = p.Name; };
        panel.Children.Add(Field(T("Name"), name));

        var game = new ComboBox { ItemsSource = Games.All.Select(g => new KeyValuePair<Game, string>(g, Games.Title(g))).ToList(), DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = p.Game };
        game.SelectionChanged += (_, _) =>
        {
            if (game.SelectedValue is Game g && g != p.Game) { p.Game = g; Structure(); }
        };
        panel.Children.Add(Field(T("Game"), game));

        if (Games.UsesPort(p.Game))
        {
            var port = new TextBox { Text = p.Port.ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = T("The game's Data Out: IP 127.0.0.1 and this port") };
            void SetPort()
            {
                if (int.TryParse(port.Text.Trim(), out int v) && v is >= 1024 and <= 65535 && v != p.Port) { p.Port = v; Structure(); }
                else port.Text = p.Port.ToString();
            }
            port.LostFocus += (_, _) => SetPort();
            port.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetPort(); };
            panel.Children.Add(Field(T("UDP Port"), port));
        }

        panel.Children.Add(new TextBlock { Text = T("Programs"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4),
            ToolTip = T("With Game Switching on, one of these running picks this profile") });
        foreach (var prog in p.Programs.ToList())
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var x = VolumeControl.IconButton("");
            x.ToolTip = T("Remove");
            AutomationProperties.SetName(x, F("Remove {0}", prog));
            x.Click += (_, _) => { p.Programs.Remove(prog); Structure(); };
            DockPanel.SetDock(x, Dock.Right);
            line.Children.Add(x);
            line.Children.Add(new TextBlock { Text = prog, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(line);
        }
        var newProg = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(newProg, T("Program to add"));
        var addProg = new Button { Content = T("Add"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        void AddProg()
        {
            string s = System.IO.Path.GetFileName(newProg.Text.Trim().Trim('"'));
            if (s.Length == 0) return;
            if (!s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s += ".exe";
            if (!p.Programs.Contains(s, StringComparer.OrdinalIgnoreCase)) p.Programs.Add(s);
            Structure();
        }
        addProg.Click += (_, _) => AddProg();
        newProg.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddProg(); };
        var addLine = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(addProg, Dock.Right);
        addLine.Children.Add(addProg);
        addLine.Children.Add(newProg);
        panel.Children.Add(addLine);

        var switching = new CheckBox { Content = T("Game Switching"), IsChecked = settings.GameSwitching, Margin = new Thickness(0, 10, 0, 0),
            ToolTip = T("Use a profile while one of its programs runs") };
        switching.Click += (_, _) => { settings.GameSwitching = switching.IsChecked == true; settings.Save(); };
        panel.Children.Add(switching);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(ConfirmButton(T("Reset Effects"), () =>
        {
            p.Effects = DefaultProfiles.For(p.Game).Effects;
            Structure();
        }, F("Back to {0}'s own effects and settings", Games.Title(p.Game))));
        if (settings.Profiles.Count > 1)
            buttons.Children.Add(ConfirmButton(T("Delete"), () =>
            {
                settings.Profiles.Remove(p);
                settings.Active = settings.Profiles[0].Name;
                Structure();
            }));
        panel.Children.Add(buttons);
        return Card(panel);
    }

    private void NewProfileMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var g in Games.All)
        {
            var item = new MenuItem { Header = Games.Title(g) };
            item.Click += (_, _) => AddProfile(DefaultProfiles.For(g));
            menu.Items.Add(item);
        }
        if (Active is { } current)
        {
            menu.Items.Add(new Separator());
            var copy = new MenuItem { Header = F("Copy of {0}", current.Name) };
            copy.Click += (_, _) =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(current, Settings.JsonStore.Options);
                var p = System.Text.Json.JsonSerializer.Deserialize<ShakerProfile>(json, Settings.JsonStore.Options)!;
                foreach (var e in p.Effects) e.Key = Guid.NewGuid().ToString("N")[..8];
                AddProfile(p);
            };
            menu.Items.Add(copy);
        }
        menu.IsOpen = true;
    }

    private void AddProfile(ShakerProfile p)
    {
        string baseName = p.Name;
        for (int i = 2; settings.Profiles.Any(o => o.Name.Equals(p.Name, StringComparison.CurrentCultureIgnoreCase)); i++) p.Name = $"{baseName} {i}";
        settings.Profiles.Add(p);
        settings.Active = p.Name;
        Logging.Log.Here.Info($"Shaker: profile {p.Name} added");
        Structure();
    }

    // ---- right: the effects ----

    private Border EffectsCard()
    {
        var panel = new StackPanel();
        var add = new Button { Content = T("Add Effect"), Padding = new Thickness(10, 3, 10, 3) };
        add.SetResourceReference(StyleProperty, "AccentButton");
        add.Click += (_, _) =>
        {
            if (Active is not { } p) return;
            var menu = new ContextMenu { PlacementTarget = add, Placement = PlacementMode.Bottom };
            foreach (var info in EffectKinds.All.Where(i => !i.NotIn.Contains(p.Game)))
            {
                var item = new MenuItem { Header = info.Title, ToolTip = info.Tip };
                item.Click += (_, _) =>
                {
                    var e = EffectKinds.Create(info.Kind, p.Game);
                    e.Expanded = true;
                    p.Effects.Add(e);
                    Structure();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        var wizard = new Button { Content = T("Level Wizard"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0),
            ToolTip = T("Each effect in turn, set by shortcuts while you drive") };
        wizard.Click += (_, _) => OpenWizard();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(wizard);
        buttons.Children.Add(add);
        panel.Children.Add(HeaderLine(T("Effects"), buttons));
        panel.Children.Add(effectsStack);
        return Card(panel);
    }

    private void RenderEffects()
    {
        effectsStack.Children.Clear();
        effectMeters.Clear();
        if (Active is not { } p) return;
        if (p.Effects.Count == 0) effectsStack.Children.Add(Secondary(T("No effects")));
        foreach (var e in p.Effects) effectsStack.Children.Add(EffectRow(p, e));
    }

    private Border EffectRow(ShakerProfile p, ShakerEffect e)
    {
        var info = EffectKinds.Of(e.Kind);
        var body = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // (the slider no longer than this on wide screens)
        head.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = 560 });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });

        var grip = new TextBlock { Text = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll, Padding = new Thickness(2, 4, 6, 4),
            Background = Brushes.Transparent, ToolTip = T("Drag to move") };
        grip.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        grip.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        head.Children.Add(grip);
        var chevron = Chevron(e.Expanded, () => { e.Expanded = !e.Expanded; Save(); RenderEffects(); });
        Grid.SetColumn(chevron, 1);
        head.Children.Add(chevron);
        bool showing = wizard?.Showing == e;
        var name = new TextBlock { Text = info.Title, FontWeight = showing ? FontWeights.Bold : FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, ToolTip = info.Tip, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 2);
        head.Children.Add(name);
        var meter = new MiniMeter { Margin = new Thickness(0, 0, 8, 0) };
        meter.Bright = showing;
        effectMeters[e.Key] = meter;
        Grid.SetColumn(meter, 3);
        head.Children.Add(meter);
        var volume = new VolumeControl(info.Title, e, hotkeys, Save);
        Grid.SetColumn(volume, 4);
        head.Children.Add(volume);
        body.Children.Add(head);

        if (e.Expanded)
        {
            var settingsPanel = new StackPanel { Margin = new Thickness(30, 6, 0, 0) };
            Grid.SetIsSharedSizeScope(settingsPanel, true);
            foreach (var param in info.Params)
                settingsPanel.Children.Add(SliderRow(param.Label, param.Min, param.Max, param.Step, param.Get(e), param.Show, v => { param.Set(e, v); Save(); }, param.Tip));
            // each device: on or off for this effect, and how much of it goes there
            if (settings.Devices.Count > 0)
                settingsPanel.Children.Add(new TextBlock { Text = T("Plays On"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            foreach (var d in settings.Devices) settingsPanel.Children.Add(DeviceLevel(e, d));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var test = new Button { Content = T("Test"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
            test.Click += (_, _) => engine.Test(e);
            buttons.Children.Add(test);
            buttons.Children.Add(ConfirmButton(T("Remove"), () => { p.Effects.Remove(e); Structure(); }));
            settingsPanel.Children.Add(buttons);
            // (kept as wide as the slider above, on wide screens)
            var holder = new Grid();
            holder.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = 840 });
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            holder.Children.Add(settingsPanel);
            body.Children.Add(holder);
        }

        var row = Row(body);
        if (showing)
        {
            // the Level Wizard's step: outlined in the accent colour
            row.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            row.BorderThickness = new Thickness(2);
        }
        // dragged by the grip, dropped on another effect: before it in its top half, after it in its bottom half
        grip.PreviewMouseLeftButtonDown += (_, a) => { dragStart = a.GetPosition(this); dragKey = e.Key; };
        grip.PreviewMouseMove += (_, a) =>
        {
            if (a.LeftButton != MouseButtonState.Pressed || dragKey != e.Key) return;
            var at = a.GetPosition(this);
            if (Math.Abs(at.X - dragStart.X) < 4 && Math.Abs(at.Y - dragStart.Y) < 4) return;
            dragKey = null;
            row.Opacity = 0.5;
            try { DragDrop.DoDragDrop(row, new DataObject(DragFormat, e.Key), DragDropEffects.Move); }
            finally { row.Opacity = 1; }
        };
        row.AllowDrop = true;
        bool Before(DragEventArgs a) => a.GetPosition(row).Y < row.ActualHeight / 2;
        row.DragOver += (_, a) =>
        {
            bool ok = a.Data.GetData(DragFormat) is string k && k != e.Key;
            a.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
            if (ok)
            {
                row.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                row.BorderThickness = Before(a) ? new Thickness(1, 3, 1, 1) : new Thickness(1, 1, 1, 3);
            }
            a.Handled = true;
        };
        row.DragLeave += (_, _) => Unmark(row);
        row.Drop += (_, a) =>
        {
            Unmark(row);
            if (a.Data.GetData(DragFormat) is not string k || k == e.Key || p.Effects.FirstOrDefault(x => x.Key == k) is not { } moved) return;
            bool before = Before(a);
            p.Effects.Remove(moved);
            int to = p.Effects.IndexOf(e) + (before ? 0 : 1);
            p.Effects.Insert(Math.Clamp(to, 0, p.Effects.Count), moved);
            a.Handled = true;
            Structure();
        };
        return row;
    }

    /// <summary>One device under an effect's Plays On: its name (on / off for this effect) and its level.</summary>
    private Grid DeviceLevel(ShakerEffect e, ShakerDevice d)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        // (the chips as wide as the longest name, so the sliders line up)
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Device" });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var chip = new ToggleButton { Content = ShortName(d.Name), ToolTip = d.Name, IsChecked = !e.Off.Contains(d.Key), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 0) };
        chip.SetResourceReference(StyleProperty, "ChipToggle");
        double level = e.Levels.TryGetValue(d.Key, out double l) ? l : 1;
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = level * 100, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center, IsEnabled = chip.IsChecked == true };
        AutomationProperties.SetName(slider, F("{0} level on {1}", EffectKinds.Of(e.Kind).Title, ShortName(d.Name)));
        var value = new TextBlock { Text = $"{level * 100:0}%", TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        chip.Click += (_, _) =>
        {
            if (chip.IsChecked == true) e.Off.Remove(d.Key); else if (!e.Off.Contains(d.Key)) e.Off.Add(d.Key);
            slider.IsEnabled = chip.IsChecked == true;
            engine.RoutingChanged();
            Save();
        };
        slider.ValueChanged += (_, _) =>
        {
            e.Levels[d.Key] = Math.Round(slider.Value / 100, 2);
            value.Text = $"{slider.Value:0}%";
            engine.RoutingChanged();
            Save();
        };
        g.Children.Add(chip);
        Grid.SetColumn(slider, 1);
        g.Children.Add(slider);
        Grid.SetColumn(value, 2);
        g.Children.Add(value);
        return g;
    }

    /// <summary>A device's name without Windows' description in brackets: "ButtKicker (USB Audio)" becomes "ButtKicker".</summary>
    private static string ShortName(string name)
    {
        int at = name.IndexOf(" (", StringComparison.Ordinal);
        return at > 0 ? name[..at] : name;
    }

    private static void Unmark(Border row)
    {
        row.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        row.BorderThickness = new Thickness(1);
    }

    // ---- pieces ----

    private static Border Row(UIElement child)
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return b;
    }

    private static Button Chevron(bool open, Action toggle)
    {
        var b = VolumeControl.IconButton(open ? "" : "");
        ((TextBlock)b.Content).FontSize = 11;
        b.ToolTip = open ? T("Fold") : T("Open");
        AutomationProperties.SetName(b, open ? T("Fold") : T("Open"));
        b.Click += (_, _) => toggle();
        return b;
    }

    private static Grid SliderRow(string label, double min, double max, double step, double value, Func<double, string> show, Action<double> set, string? tip = null)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 0), ToolTip = tip };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(l);
        var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(s, label);
        var v = new TextBlock { Text = show(value), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, _) => { v.Text = show(s.Value); set(s.Value); };
        Grid.SetColumn(s, 1);
        g.Children.Add(s);
        Grid.SetColumn(v, 2);
        g.Children.Add(v);
        return g;
    }

    private static Grid Field(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static DockPanel HeaderLine(string text, UIElement right)
    {
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(right, Dock.Right);
        d.Children.Add(right);
        var h = Header(text);
        h.Margin = new Thickness(0);
        h.VerticalAlignment = VerticalAlignment.Center;
        d.Children.Add(h);
        return d;
    }

    private static Border Card(UIElement child)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    private static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "CardHeader");
        return t;
    }

    private static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    /// <summary>Core's Confirm? button, spaced as this tab's buttons.</summary>
    private static Button ConfirmButton(string text, Action confirmed, string? tip = null)
    {
        var b = DaisysApp.Shared.ConfirmButton.Make(text, confirmed, tip);
        b.Padding = new Thickness(8, 3, 8, 3);
        b.Margin = new Thickness(0, 0, 5, 5);
        return b;
    }
}
