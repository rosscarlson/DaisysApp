using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// The WheelEmu tab. Left: the emulator (start, stop, which vJoy device), the vJoy device's setup checked against what
/// Forza needs (and set up in one click), Hush, and the bindings file. Right: every control of the game's controller,
/// each with what drives it, its live value, Set (press the real control), add another, and clear; axes also invert and
/// deadzone. The 96 spare buttons fold away.
/// </summary>
internal sealed class WheelEmuView : UserControl
{
    private const string VJoyDownload = "https://github.com/jshafer817/vJoy/releases";
    private const int LeftWidth = 360;

    private readonly WheelEmuSettings settings;
    private readonly WheelEmulator emulator;
    private readonly IntPtr window;

    private readonly ToggleButton runToggle = new() { Padding = new Thickness(12, 4, 12, 4) };
    private readonly TextBlock runStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox deviceBox = new() { Width = 70, Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel vjoyChecks = new();
    private readonly WrapPanel vjoyButtons = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel hushDevices = new();
    private readonly TextBlock hushStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly WrapPanel hushButtons = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock fileStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel right = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Dictionary<string, Row> rows = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? capturing;
    private WheelDiagram? diagram;

    /// <summary>One role's row: what drives it, and its live value.</summary>
    private sealed class Row
    {
        public required Role Role;
        public required TextBlock Input;
        public required FrameworkElement Live;
        public TextBlock? Note;
        public Button? Set;
    }

    public WheelEmuView(WheelEmuSettings settings, WheelEmulator emulator, IntPtr window)
    {
        this.settings = settings;
        this.emulator = emulator;
        this.window = window;

        var left = new StackPanel();
        left.Children.Add(Card(EmulatorCard()));
        left.Children.Add(Card(VJoyCard()));
        left.Children.Add(Card(HushCard()));
        left.Children.Add(Card(FileCard()));
        left.Children.Add(Card(StepsCard()));
        var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        BuildRows();
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(leftScroll);
        Grid.SetColumn(rightScroll, 2);
        grid.Children.Add(rightScroll);
        Content = grid;

        emulator.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            emulator.Preview = IsVisible;
            if (IsVisible) { Refresh(); CheckVJoy(); ShowHush(); timer.Start(); }
            else { timer.Stop(); capturing?.Cancel(); }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && capturing != null) { capturing.Cancel(); e.Handled = true; } };
        Refresh();
    }

    // ---------------------------------------------------------------- left: the emulator

    private UIElement EmulatorCard()
    {
        var s = new StackPanel();
        s.Children.Add(Header(T("Emulator")));
        runToggle.SetResourceReference(StyleProperty, "ChipToggle");
        runToggle.Click += (_, _) =>
        {
            if (emulator.Running) emulator.Stop();
            else emulator.Start();
            Refresh();
        };
        for (uint i = 1; i <= 16; i++) deviceBox.Items.Add(i);
        deviceBox.SelectedItem = settings.Config.VJoyDevice;
        deviceBox.SelectionChanged += (_, _) =>
        {
            if (deviceBox.SelectedItem is not uint id || id == settings.Config.VJoyDevice) return;
            bool was = emulator.Running;
            emulator.Stop();
            settings.Config.VJoyDevice = id;
            settings.Save();
            if (was) emulator.Start();
            CheckVJoy();
            Refresh();
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(runToggle);
        var dev = new TextBlock { Text = T("vJoy device"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        line.Children.Add(dev);
        line.Children.Add(deviceBox);
        s.Children.Add(line);
        s.Children.Add(runStatus);
        var auto = new CheckBox { Content = T("Start it when Daisy's App starts"), IsChecked = settings.StartWithApp, Margin = new Thickness(0, 10, 0, 0) };
        auto.Click += (_, _) => { settings.StartWithApp = auto.IsChecked == true; settings.Save(); };
        s.Children.Add(auto);
        return s;
    }

    public void Refresh()
    {
        runToggle.IsChecked = emulator.Running;
        runToggle.Content = emulator.Running ? T("Running") : T("Start");
        var (open, missing) = emulator.Devices;
        if (emulator.Running)
        {
            runStatus.Text = F("Feeding vJoy device {0} from {1}.", settings.Config.VJoyDevice, P(open, "{0} controller", "{0} controllers"))
                + (missing > 0 ? " " + P(missing, "{0} bound controller isn't plugged in (it's looked for every 2 seconds).", "{0} bound controllers aren't plugged in (they're looked for every 2 seconds).") : "");
            runStatus.ClearValue(TextBlock.ForegroundProperty);
        }
        else if (emulator.Problem is { } why)
        {
            runStatus.Text = why;
            runStatus.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        }
        else
        {
            runStatus.Text = T("Stopped: the game doesn't see the vJoy controller move.");
            runStatus.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    // ---------------------------------------------------------------- left: vJoy

    private UIElement VJoyCard()
    {
        var s = new StackPanel();
        s.Children.Add(Header(T("vJoy device")));
        s.Children.Add(vjoyChecks);
        s.Children.Add(vjoyButtons);
        return s;
    }

    private void CheckVJoy()
    {
        vjoyChecks.Children.Clear();
        vjoyButtons.Children.Clear();
        if (!VJoy.Available)
        {
            vjoyChecks.Children.Add(Line(false, T("vJoy isn't installed. Install vJoy 2.1.9.1 (the version for Windows 10 and 11), then restart Daisy's App.")));
            vjoyButtons.Children.Add(SmallButton(T("Get vJoy"), () => Open(VJoyDownload)));
            return;
        }
        uint id = settings.Config.VJoyDevice;
        var setup = VJoy.Read(id);
        vjoyChecks.Children.Add(Secondary(F("vJoy {0}, device {1}. What Forza needs of it (as EmuWheel):", VJoy.Version ?? "?", id)));
        var checks = VJoy.Checks(setup, settings.Config.HighestButton);
        foreach (var c in checks) vjoyChecks.Children.Add(Line(c.Ok, c.What, c.Required));
        bool allOk = checks.All(c => c.Ok);
        if (allOk) vjoyChecks.Children.Add(Secondary(T("It's set up right.")));
        if (!allOk && (VJoy.ConfigTool != null || !GameInputRegistry.VJoyIsWheel()))
        {
            var fix = SmallButton(T("Set up vJoy device"), FixVJoy);
            fix.ToolTip = F("As administrator: vJoyConfig sets device {0} up with all 8 axes, 128 buttons, one 4-direction hat and force feedback, and Windows is told it's a racing wheel (as EmuWheel's vjoy.reg).", id);
            fix.SetResourceReference(StyleProperty, "AccentButton");
            vjoyButtons.Children.Add(fix);
        }
        if (VJoy.ConfigWindow is { } conf) vjoyButtons.Children.Add(SmallButton(T("Configure vJoy…"), () => Run(conf)));
        vjoyButtons.Children.Add(SmallButton(T("Check again"), CheckVJoy));
    }

    private async void FixVJoy()
    {
        uint id = settings.Config.VJoyDevice;
        bool was = emulator.Running;
        emulator.Stop();
        vjoyChecks.Children.Add(Secondary(T("Setting it up (Windows asks for permission)…")));
        var cmds = new List<(string, string)>();
        var setup = VJoy.Read(id);
        bool deviceOk = VJoy.Checks(setup, settings.Config.HighestButton).Where(c => !c.What.Contains("vjoy.reg")).All(c => c.Ok);
        if (!deviceOk && VJoy.ConfigTool is { } tool)
        {
            cmds.Add((tool, "enable on"));
            cmds.Add((tool, VJoy.FixArguments(id)));
        }
        if (!GameInputRegistry.VJoyIsWheel()) cmds.Add(GameInputRegistry.RegisterVJoyCommand());
        var result = await Elevated.RunAsync(cmds);
        await Task.Delay(1500); // the driver restarts the device
        CheckVJoy();
        if (!result.Ok || !VJoy.Checks(VJoy.Read(id), settings.Config.HighestButton).All(c => c.Ok))
            vjoyChecks.Children.Add(Secondary(F("If something is still missing, open Configure vJoy and set device {0} by hand: tick Enable vJoy; every axis (X, Y, Z, Rx, Ry, Rz, Slider, Dial/Slider2); Number of Buttons 128; POV Hat Switch: 4 Directions, 1; Enable Effects with every effect ticked; then Apply.", id)
                + (result.Output.Length > 0 ? "\n\n" + result.Output : "")));
        if (was) emulator.Start();
        Refresh();
    }

    // ---------------------------------------------------------------- left: Hush

    private UIElement HushCard()
    {
        var s = new StackPanel();
        s.Children.Add(Header(T("Hush")));
        s.Children.Add(Secondary(T("Mutes the real devices for Forza only (as EmuWheel's Hush), so it sees just the vJoy wheel and binding isn't confused by the same press arriving twice. Other games, and Daisy's App, still see them.")));
        hushDevices.Margin = new Thickness(0, 8, 0, 0);
        s.Children.Add(hushDevices);
        s.Children.Add(hushStatus);
        s.Children.Add(hushButtons);
        return s;
    }

    private void ShowHush()
    {
        hushDevices.Children.Clear();
        hushButtons.Children.Clear();
        var used = settings.Config.Devices.Select(d => d.Device).ToHashSet();
        List<ControllerInfo> controllers;
        try { controllers = GameControllers.List(); } catch { controllers = new(); }
        int muted = 0;
        foreach (var c in controllers)
        {
            bool isMuted = GameInputRegistry.IsMuted(c.GameInputKey);
            if (isMuted) muted++;
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            text.Inlines.Add(new System.Windows.Documents.Run(c.Name));
            var state = new System.Windows.Documents.Run("  " + (isMuted ? T("muted") : T("Forza sees it"))) { FontSize = 11 };
            state.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, isMuted ? "SuccessBrush" : "TextSecondaryBrush");
            text.Inlines.Add(state);
            hushDevices.Children.Add(new CheckBox
            {
                Content = text, Tag = c.GameInputKey, IsChecked = used.Contains(c.Instance) || isMuted,
                Margin = new Thickness(0, 2, 0, 2), ToolTip = @"HKLM\SYSTEM\CurrentControlSet\Control\GameInput\Devices\" + c.GameInputKey,
            });
        }
        if (controllers.Count == 0) hushDevices.Children.Add(Secondary(T("No controllers are plugged in.")));
        hushStatus.Text = muted > 0
            ? P(muted, "{0} device is muted for Forza.", "{0} devices are muted for Forza.")
            : T("Nothing muted: Forza sees the real devices too. Tick the ones to mute (those bound are ticked).");
        var mute = SmallButton(T("Mute"), MuteAsync);
        mute.ToolTip = T("Mutes the ticked devices for Forza, and tells Windows the vJoy device is a racing wheel if it isn't told yet (Windows asks for permission)");
        mute.SetResourceReference(StyleProperty, "AccentButton");
        hushButtons.Children.Add(mute);
        if (muted > 0) hushButtons.Children.Add(SmallButton(T("Unmute"), UnmuteAsync));
    }

    private async void MuteAsync()
    {
        var keys = hushDevices.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).Distinct().ToList();
        if (keys.Count == 0) { hushStatus.Text = T("Tick the devices to mute first."); return; }
        hushStatus.Text = T("Muting them (Windows asks for permission)…");
        // Hush imports vjoy.reg each time, so Forza takes the vJoy device for a wheel
        var result = await Elevated.RunAsync(GameInputRegistry.MuteCommands(keys, !GameInputRegistry.VJoyIsWheel()));
        ShowHush();
        CheckVJoy();
        if (!result.Ok) hushStatus.Text = T("Couldn't mute them: ") + result.Output;
    }

    private async void UnmuteAsync()
    {
        var keys = hushDevices.Children.OfType<CheckBox>().Select(b => (string)b.Tag).Where(GameInputRegistry.IsMuted).Distinct().ToList();
        hushStatus.Text = T("Unmuting them (Windows asks for permission)…");
        var result = await GameInputRegistry.UnmuteAsync(keys);
        ShowHush();
        if (!result.Ok) hushStatus.Text = T("Couldn't unmute them: ") + result.Output;
    }

    // ---------------------------------------------------------------- left: the bindings file, and the steps

    private UIElement FileCard()
    {
        var s = new StackPanel();
        s.Children.Add(Header(T("Bindings file")));
        s.Children.Add(Secondary(T("The bindings are kept in WheelEmu.json in Daisy's App's settings and used as you change them. Save them to a file of their own to keep a copy or move them to another PC.")));
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(SmallButton(T("Save to file…"), SaveFile));
        buttons.Children.Add(SmallButton(T("Load from file…"), LoadFile));
        buttons.Children.Add(SmallButton(T("Clear all"), ClearAll));
        s.Children.Add(buttons);
        s.Children.Add(fileStatus);
        return s;
    }

    private void SaveFile()
    {
        var dlg = new SaveFileDialog { Filter = T("Bindings") + " (*.json)|*.json", FileName = "WheelEmu bindings.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try { settings.Config.SaveTo(dlg.FileName); fileStatus.Text = F("Saved to {0}.", dlg.FileName); }
        catch (Exception ex) { fileStatus.Text = T("Couldn't save: ") + ex.Message; }
    }

    private void LoadFile()
    {
        var dlg = new OpenFileDialog { Filter = T("Bindings") + " (*.json)|*.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var loaded = WheelConfig.LoadFrom(dlg.FileName);
            settings.Config.Bindings = loaded.Bindings;
            settings.Save();
            emulator.Reopen();
            ShowAll();
            fileStatus.Text = F("Loaded {0}.", System.IO.Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { fileStatus.Text = T("Couldn't load it: ") + ex.Message; }
    }

    private void ClearAll()
    {
        if (MessageBox.Show(Window.GetWindow(this), T("Clear every binding?"), T("WheelEmu"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        settings.Config.Bindings.Clear();
        settings.Save();
        emulator.Reopen();
        ShowAll();
    }

    private UIElement StepsCard()
    {
        var s = new StackPanel();
        s.Children.Add(Header(T("Using it with Forza")));
        s.Children.Add(Secondary(T("1. Install vJoy and set its device up (above): Forza is told it's a racing wheel.\n2. Bind each control on the right: Set, then press or move the real control. The picture at the top shows it working.\n3. Mute the real devices with Hush.\n4. Start the emulator.\n5. In Forza the vJoy wheel is your wheel: choose it in Settings → Controls, and bind the wheel buttons (1–16) to actions there by pressing the real ones.\n\nMuting only affects Forza, so there's no need to unmute for other games.")));
        return s;
    }

    // ---------------------------------------------------------------- right: the bindings

    private void BuildRows()
    {
        diagram ??= new WheelDiagram();
        right.Children.Add(Card(diagram));
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var halves = new[] { new StackPanel(), new StackPanel() };
        Grid.SetColumn(halves[1], 2);
        columns.Children.Add(halves[0]);
        columns.Children.Add(halves[1]);
        var height = new int[2];
        foreach (var group in Roles.All.GroupBy(r => r.Group))
        {
            var list = new StackPanel();
            if (group.Key == Roles.Extras)
            {
                // 102 spare buttons, built when opened
                var expander = new Expander { Header = new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, Content = list };
                expander.Expanded += (_, _) => { if (list.Children.Count == 0) { foreach (var r in group) list.Children.Add(MakeRow(r)); Tick(); } };
                if (group.Any(r => settings.Config.For(r.Id)?.Inputs.Count > 0)) expander.IsExpanded = true;
                right.Children.Add(columns);
                right.Children.Add(Card(expander));
                continue;
            }
            list.Children.Add(Header(group.Key));
            foreach (var r in group) list.Children.Add(MakeRow(r, compact: group.Key != Roles.AxesGroup));
            if (group.Key == Roles.AxesGroup) { right.Children.Add(Card(list)); continue; }
            // into the shorter column (axes count double: they have a second line)
            int i = height[0] <= height[1] ? 0 : 1;
            halves[i].Children.Add(Card(list));
            height[i] += 2 + group.Sum(r => r.Kind is RoleKind.Axis ? 2 : 1);
        }
        if (!right.Children.Contains(columns)) right.Children.Add(columns);
    }

    private FrameworkElement MakeRow(Role role, bool compact = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 150 : 230) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 20 : 70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string output = role.Kind switch
        {
            RoleKind.Axis => F("vJoy axis {0}", VJoy.AxisNames[role.Output]),
            RoleKind.CombinedPedals => T("vJoy axes Z and Rx"),
            RoleKind.Button => F("vJoy button {0}", role.Output),
            _ => T("vJoy hat"),
        };
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = role.Name, TextWrapping = TextWrapping.Wrap });
        var outText = new TextBlock { Text = output, FontSize = 11 };
        outText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        name.Children.Add(outText);
        grid.Children.Add(name);

        var input = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 0) };
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);

        FrameworkElement live = role.Kind is RoleKind.Axis or RoleKind.CombinedPedals
            ? new ProgressBar { Minimum = 0, Maximum = 1, Height = 8, VerticalAlignment = VerticalAlignment.Center }
            : new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, StrokeThickness = 1 };
        if (live is Ellipse dot) dot.SetResourceReference(Shape.StrokeProperty, "ControlBorderStrongBrush");
        live.ToolTip = T("Its value now");
        Grid.SetColumn(live, 2);
        grid.Children.Add(live);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var set = SmallButton(T("Set"), () => CaptureAsync(role, false));
        set.ToolTip = T("Press or move the real control (Esc cancels)");
        var add = IconButton("", T("Add another control for this one (any of them presses it)"), () => CaptureAsync(role, true));
        var clear = IconButton("", T("Clear"), () => { if (settings.Config.For(role.Id) is { } b) { settings.Config.Bindings.Remove(b); Changed(); } });
        buttons.Children.Add(set);
        if (role.Kind is RoleKind.Button or RoleKind.DPad) buttons.Children.Add(add);
        buttons.Children.Add(clear);
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(buttons);

        var stack = new StackPanel();
        stack.Children.Add(grid);
        var row = new Row { Role = role, Input = input, Live = live, Set = set };
        if (role.Kind is RoleKind.Axis or RoleKind.CombinedPedals) stack.Children.Add(AxisOptions(role, compact ? 0 : 230));
        row.Note = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 2) };
        row.Note.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        stack.Children.Add(row.Note);
        var border = new Border { Child = stack, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 2, 0, 4) };
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        rows[role.Id] = row;
        ShowRow(row);
        return border;
    }

    /// <summary>An axis's invert box and deadzone slider.</summary>
    private FrameworkElement AxisOptions(Role role, double indent)
    {
        var b = settings.Config.For(role.Id);
        var panel = new WrapPanel { Margin = new Thickness(indent, 0, 0, 2) };
        var invert = new CheckBox { Content = T("Invert"), IsChecked = b?.Invert == true, VerticalAlignment = VerticalAlignment.Center };
        invert.ToolTip = T("For a pedal that reads full when it's released (Set ticks it when it sees one)");
        invert.Click += (_, _) => { settings.Config.Ensure(role.Id).Invert = invert.IsChecked == true; settings.Save(); };
        var dzText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0), Width = 100 };
        var dz = new Slider { Minimum = 0, Maximum = 0.3, Width = 120, Value = b?.Deadzone ?? 0, SmallChange = 0.01, LargeChange = 0.05, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 0.01 };
        void ShowDz() => dzText.Text = F("Deadzone {0:0}%", dz.Value * 100);
        dz.ValueChanged += (_, _) => { settings.Config.Ensure(role.Id).Deadzone = dz.Value; settings.Save(); ShowDz(); };
        dz.ToolTip = role.Centred || role.Kind == RoleKind.CombinedPedals ? T("Ignored around the centre") : T("Ignored at the released end (a pedal that never quite reads 0%)");
        ShowDz();
        panel.Children.Add(invert);
        panel.Children.Add(dzText);
        panel.Children.Add(dz);
        if (role.Centred)
        {
            // Forza's wheel turns 450°: a wheel set to more turns the car less than the hands unless this makes up for it
            var sText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0), Width = 150 };
            var sens = new Slider { Minimum = 1, Maximum = 4, Width = 120, Value = Math.Clamp(b?.Sensitivity ?? 1, 1, 4), SmallChange = 0.1, LargeChange = 0.5, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 0.1 };
            void ShowSens() => sText.Text = F("Sensitivity {0:0}% (full lock at {1:0}°)", sens.Value * 100, 1080 / sens.Value);
            sens.ValueChanged += (_, _) => { settings.Config.Ensure(role.Id).Sensitivity = sens.Value; settings.Save(); ShowSens(); };
            sens.ToolTip = T("Forza's wheel turns 450°. For the car to turn as far as your hands, set your wheel's rotation ÷ 450: a 1080° wheel 240%, 900° 200%. The degrees shown are for a 1080° wheel.");
            ShowSens();
            panel.Children.Add(sText);
            panel.Children.Add(sens);
        }
        return panel;
    }

    private void ShowRow(Row row)
    {
        var b = settings.Config.For(row.Role.Id);
        if (b == null || b.Inputs.Count == 0)
        {
            row.Input.Text = T("(not bound)");
            row.Input.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        }
        else
        {
            row.Input.Text = string.Join("\n", b.Inputs.Select(i => i.Describe()));
            row.Input.ClearValue(TextBlock.ForegroundProperty);
        }
        // the same control driving something else too (EmuWheel calls it a duplicate binding)
        var others = b?.Inputs.SelectMany(i => settings.Config.Bindings.Where(o => o != b && o.Inputs.Any(i.SameControl))).Select(o => Roles.Find(o.Role)?.Name ?? o.Role).Distinct().ToList();
        if (row.Note != null)
        {
            row.Note.Visibility = others is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
            if (others is { Count: > 0 }) row.Note.Text = F("Also bound to {0}: both are pressed together.", string.Join(", ", others));
        }
    }

    /// <summary>Every row again (so the axis options show the bindings' values too).</summary>
    private void ShowAll()
    {
        right.Children.Clear();
        if (diagram?.Parent is Border holder) holder.Child = null;
        rows.Clear();
        BuildRows();
    }

    private void Changed()
    {
        settings.Save();
        emulator.Reopen();
        foreach (var row in rows.Values) ShowRow(row);
        if (IsVisible) CheckVJoy();
    }

    private async void CaptureAsync(Role role, bool add)
    {
        capturing?.Cancel();
        var cts = capturing = new CancellationTokenSource();
        var row = rows[role.Id];
        row.Input.Text = role.Kind is RoleKind.Axis or RoleKind.CombinedPedals
            ? (role.Centred ? T("Turn it left and right now… (Esc cancels)") : T("Press it all the way and let it go, now… (Esc cancels)"))
            : T("Press it now… (Esc cancels)");
        row.Input.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        Focus();
        Capture.Result? result = null;
        // what's bound to anything else is passed over; steering and the combined pedals take only an axis
        var taken = settings.Config.Bindings.Where(o => !o.Role.Equals(role.Id, StringComparison.OrdinalIgnoreCase)).SelectMany(o => o.Inputs).ToList();
        // an axis role takes only an axis (a small turn of the wheel is enough; a pedal must move further), a button or
        // the d-pad only a button or hat
        bool axis = role.Kind is RoleKind.Axis or RoleKind.CombinedPedals;
        double minMove = role.Centred || role.Kind == RoleKind.CombinedPedals ? 0.03 : 0.15;
        try { result = await Capture.NextAsync(window, TimeSpan.FromSeconds(15), taken, axis, minMove, cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logging.ErrorLog.Write("WheelEmu capture", ex); }
        if (capturing == cts) capturing = null;
        if (result != null)
        {
            var b = settings.Config.Ensure(role.Id);
            var input = result.Input;
            if (role.Kind is RoleKind.Axis or RoleKind.CombinedPedals)
            {
                if (input.Kind == InputRef.InputKind.Axis) input.Direction = 0;
                // a pedal that reads full when released
                if (!role.Centred && role.Kind == RoleKind.Axis) b.Invert = input.Kind == InputRef.InputKind.Axis && result.RestsHigh;
                b.Inputs = new List<InputRef> { input };
            }
            else if (add && !b.Inputs.Any(input.SameControl)) b.Inputs.Add(input);
            else b.Inputs = new List<InputRef> { input };
            Changed();
            if (role.Kind is RoleKind.Axis or RoleKind.CombinedPedals) ShowAll();
        }
        else ShowRow(row);
    }

    private void Tick()
    {
        var values = emulator.Values;
        diagram?.Show(values, settings.Config.Bindings.Where(b => b.Inputs.Count > 0).Select(b => b.Role));
        foreach (var row in rows.Values)
        {
            double v = values.TryGetValue(row.Role.Id, out var x) ? x : 0;
            if (row.Live is ProgressBar bar) bar.Value = v;
            else if (row.Live is Ellipse dot)
            {
                if (v > 0.5) dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
                else dot.Fill = Brushes.Transparent;
            }
        }
        if (emulator.Running) Refresh();
    }

    // ---------------------------------------------------------------- helpers

    private static FrameworkElement Line(bool ok, string text, bool required = true)
    {
        var line = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        var icon = new TextBlock { Text = ok ? "" : required ? "" : "", Width = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, ok ? "SuccessBrush" : "ErrorTextBrush");
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return line;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static void Run(string exe)
    {
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); } catch { }
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
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    private static Button SmallButton(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
        b.Click += (_, _) => click();
        return b;
    }

    private static Button IconButton(string glyph, string tip, Action click)
    {
        var t = new TextBlock { Text = glyph, FontSize = 12 };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var b = new Button { Content = t, ToolTip = tip, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 5, 5) };
        b.Click += (_, _) => click();
        return b;
    }
}
