using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// The Joy 2 Key tab: the profiles on the left; on the right the chosen profile's games and options, and a box per
/// controller with a tile for each of its buttons, axes and POV directions. Pressing something on a controller lights
/// its tile up and selects it; double-click a tile to say what it does.
/// </summary>
internal sealed class Joy2KeyView : UserControl
{
    private const int TileWidth = 88, TileHeight = 66, LeftWidth = 330;

    private readonly Joy2KeySettings settings;
    private readonly Joy2KeyEngine engine;
    private List<J2KProfile> profiles;

    private readonly ListBox profileList = new() { MinHeight = 90, BorderThickness = new Thickness(0) };
    private readonly StackPanel right = new();
    private readonly Border profileCard = Card(new StackPanel());
    private readonly ToggleButton onOff = new() { Padding = new Thickness(12, 4, 12, 4) };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 };
    private readonly CheckBox autoSwitch = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly List<DeviceCard> cards = new();
    private string connectedKey = "";
    private bool pausedForTab;
    private bool rendering;
    private J2KAction? copied;
    private (J2KDevice Device, string Input)? selected;

    private sealed class DeviceCard
    {
        public required J2KDevice Device;
        public JoyInfo? Joy;
        public Dictionary<string, Border> Tiles = new();
        public HashSet<string> Active = new();
    }

    public Joy2KeyView(Joy2KeySettings settings, Joy2KeyEngine engine, List<J2KProfile> profiles)
    {
        this.settings = settings;
        this.engine = engine;
        this.profiles = profiles;

        // Left column, which stays put: the profile showing (its games and options), the profiles, and on / off.
        // Right: a box per controller, which scrolls.

        // the profiles
        profileList.SetResourceReference(BackgroundProperty, "CardBrush");
        profileList.ItemContainerStyle = RowStyle;
        profileList.SelectionChanged += (_, _) => { if (!rendering && profileList.SelectedItem is ListBoxItem { Tag: J2KProfile p }) { settings.Showing = p.Name; Render(); } };
        var profileButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        profileButtons.Children.Add(SmallButton(T("New"), NewProfile));
        profileButtons.Children.Add(SmallButton(T("Copy"), CopyProfile));
        profileButtons.Children.Add(SmallButton(T("Rename"), RenameProfile));
        profileButtons.Children.Add(SmallButton(T("Delete"), DeleteProfile));
        var import = SmallButton(T("Import"), Import);
        import.ToolTip = T("Import profiles from JoyToKey");
        profileButtons.Children.Add(import);
        var profilesStack = new StackPanel();
        profilesStack.Children.Add(Header(T("Profiles")));
        profilesStack.Children.Add(profileList);
        profilesStack.Children.Add(profileButtons);

        // on / off, the profile in use, auto-switching
        onOff.SetResourceReference(StyleProperty, "ChipToggle");
        onOff.IsChecked = settings.Enabled;
        onOff.Click += (_, _) => { settings.Enabled = onOff.IsChecked == true; settings.Save(); engine.Enabled = settings.Enabled; ShowStatus(); };
        AutomationProperties.SetName(onOff, T("Joy 2 Key on"));
        autoSwitch.Content = new TextBlock { Text = T("Switch to a profile when its game is in front"), TextWrapping = TextWrapping.Wrap };
        autoSwitch.Margin = new Thickness(0, 10, 0, 0);
        autoSwitch.IsChecked = settings.AutoSwitch;
        autoSwitch.Click += (_, _) => { settings.AutoSwitch = autoSwitch.IsChecked == true; settings.Save(); engine.Refresh(); };
        var onLine = new StackPanel { Orientation = Orientation.Horizontal };
        onLine.Children.Add(onOff);
        onLine.Children.Add(status);
        var statusStack = new StackPanel();
        statusStack.Children.Add(onLine);
        statusStack.Children.Add(autoSwitch);
        var hint = Secondary(T("Nothing is sent while this tab is in front, so you can press buttons to find them: what you press lights up. Double-click a tile to choose what it does."));
        hint.Margin = new Thickness(0, 8, 0, 0);
        statusStack.Children.Add(hint);

        var leftStack = new StackPanel();
        leftStack.Children.Add(profileCard);
        leftStack.Children.Add(Card(profilesStack));
        leftStack.Children.Add(Card(statusStack));
        // its own scroll bar only if the window is too short for it
        var leftScroll = new ScrollViewer { Content = leftStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(leftScroll);
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(rightScroll, 2);
        grid.Children.Add(rightScroll);
        Content = grid;

        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Render(); timer.Start(); }
            else { timer.Stop(); SetTabPause(false); }
        };
        engine.CurrentChanged += () => { ShowStatus(); FillProfileList(); };
        ShowStatus();
    }

    public IReadOnlyList<J2KProfile> Profiles => profiles;

    private J2KProfile Showing =>
        profiles.FirstOrDefault(p => p.Name.Equals(settings.Showing, StringComparison.OrdinalIgnoreCase))
        ?? profiles.FirstOrDefault(p => p.Name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase))
        ?? profiles[0];

    private void ShowStatus()
    {
        onOff.Content = settings.Enabled ? T("On") : T("Off");
        status.Text = !settings.Enabled ? T("Not sending anything") : engine.Current is { } c ? F("In use: {0}", c) : "";
    }

    /// <summary>Called when profiles change outside the tab (the tray menu).</summary>
    public void Changed()
    {
        ShowStatus();
        if (IsVisible) Render();
    }

    // ---- drawing ----

    private void FillProfileList()
    {
        rendering = true;
        profileList.Items.Clear();
        var showing = Showing;
        foreach (var p in profiles)
        {
            var line = new DockPanel();
            var tags = new List<string>();
            if (p.Name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase)) tags.Add(T("chosen"));
            if (settings.Enabled && p.Name.Equals(engine.Current, StringComparison.OrdinalIgnoreCase)) tags.Add(T("in use"));
            if (tags.Count > 0)
            {
                var tag = new TextBlock { Text = string.Join(", ", tags), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                tag.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                DockPanel.SetDock(tag, Dock.Right);
                line.Children.Add(tag);
            }
            line.Children.Add(new TextBlock { Text = p.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem { Content = line, Tag = p, Padding = new Thickness(6, 5, 6, 5) };
            AutomationProperties.SetName(item, p.Name);
            profileList.Items.Add(item);
            if (p == showing) profileList.SelectedItem = item;
        }
        rendering = false;
    }

    private void Render()
    {
        FillProfileList();
        var p = Showing;
        right.Children.Clear();
        cards.Clear();
        var connected = Joysticks.Connected();
        connectedKey = KeyOf(connected);

        // the profile showing: its name, games and options (top of the left column)
        var s = new StackPanel();
        bool chosen = p.Name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase);
        s.Children.Add(new TextBlock { Text = p.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = p.Name });
        var use = new Button { Content = chosen ? T("Chosen") : T("Use this profile"), IsEnabled = !chosen, Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        if (!chosen) use.SetResourceReference(StyleProperty, "AccentButton");
        use.ToolTip = T("The profile used when no profile's game is in front");
        use.Click += (_, _) => { settings.Active = p.Name; settings.Save(); engine.Refresh(); Render(); };
        s.Children.Add(use);

        var games = new TextBox { Text = string.Join(", ", p.Programs), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(games, T("Games"));
        games.LostKeyboardFocus += (_, _) =>
        {
            var list = games.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.SequenceEqual(p.Programs)) return;
            p.Programs = list;
            Save(p);
        };
        var gamesLabel = new TextBlock { Text = T("Games"), Margin = new Thickness(0, 12, 0, 4) };
        s.Children.Add(gamesLabel);
        s.Children.Add(games);
        var pick = new Button { Content = T("Add a running program…"), Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
        pick.Click += (_, _) => ProgramMenu(pick, p, games);
        s.Children.Add(pick);
        s.Children.Add(Secondary(T("Program names, separated by commas (e.g. eldenring.exe). While one of them is the window in front, this profile is used.")));

        s.Children.Add(new TextBlock { Text = T("Axes count as pressed past"), Margin = new Thickness(0, 12, 0, 2) });
        var thresholdLine = new DockPanel();
        var thresholdText = new TextBlock { Text = F("{0}%", p.Threshold), Width = 40, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(thresholdText, Dock.Right);
        var threshold = new Slider { Minimum = 5, Maximum = 95, Value = p.Threshold, SmallChange = 5, LargeChange = 10, IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
        threshold.ToolTip = T("How far a stick or trigger has to move before it counts as pressed");
        threshold.ValueChanged += (_, e) => { p.Threshold = (int)e.NewValue; thresholdText.Text = F("{0}%", p.Threshold); };
        threshold.LostMouseCapture += (_, _) => Save(p);
        threshold.LostKeyboardFocus += (_, _) => Save(p);
        thresholdLine.Children.Add(thresholdText);
        thresholdLine.Children.Add(threshold);
        s.Children.Add(thresholdLine);
        var pov8 = new CheckBox { Content = new TextBlock { Text = T("8-way POV (its diagonals are inputs of their own)"), TextWrapping = TextWrapping.Wrap }, IsChecked = p.Pov8Way, Margin = new Thickness(0, 10, 0, 0) };
        pov8.Click += (_, _) => { p.Pov8Way = pov8.IsChecked == true; Save(p); Render(); };
        s.Children.Add(pov8);
        var assignedOnly = new CheckBox { Content = T("Only show what's assigned"), IsChecked = settings.AssignedOnly, Margin = new Thickness(0, 8, 0, 0) };
        assignedOnly.Click += (_, _) => { settings.AssignedOnly = assignedOnly.IsChecked == true; settings.Save(); Render(); };
        s.Children.Add(assignedOnly);
        profileCard.Child = s;

        // a box per controller
        foreach (var d in p.Devices) right.Children.Add(DeviceBox(p, d, connected));

        // adding one
        var add = new Button { Content = T("Add a controller…"), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddMenu(add, p);
        var addStack = new StackPanel();
        addStack.Children.Add(add);
        var unused = connected.Where(j => !p.Devices.Any(d => Joysticks.Resolve(d, connected) == j)).ToList();
        addStack.Children.Add(Secondary(connected.Count == 0
            ? T("No controllers are plugged in. Plug one in (or turn it on) and it shows up here in a few seconds.")
            : p.Devices.Count == 0 ? T("Add the controllers this game uses, then press their buttons to find them.")
            : unused.Count > 0 ? F("Also plugged in: {0}", string.Join(", ", unused.Select(j => j.Name))) : ""));
        ((FrameworkElement)addStack.Children[1]).Margin = new Thickness(0, 8, 0, 0);
        right.Children.Add(Card(addStack));
    }

    private Border DeviceBox(J2KProfile p, J2KDevice d, IReadOnlyList<JoyInfo> connected)
    {
        var joy = Joysticks.Resolve(d, connected);
        var card = new DeviceCard { Device = d, Joy = joy };
        cards.Add(card);

        var s = new StackPanel();
        var head = new DockPanel();
        var remove = new Button { Content = "", Padding = new Thickness(8, 4, 8, 4), ToolTip = T("Remove this controller from the profile") };
        remove.SetResourceReference(FontFamilyProperty, "IconFont");
        AutomationProperties.SetName(remove, T("Remove controller"));
        remove.Click += (_, _) =>
        {
            string question = d.Inputs.Count > 0
                ? F("Remove {0} and its {1} assignments from {2}?", d.Name, d.Inputs.Count, p.Name)
                : F("Remove {0} from {1}?", d.Name, p.Name);
            if (MessageBox.Show(Window.GetWindow(this), question, T("Joy 2 Key"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            p.Devices.Remove(d);
            Save(p);
            Render();
        };
        // collapse / expand, to the right of remove
        var fold = new Button { Content = d.Collapsed ? "" : "", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0),
            ToolTip = d.Collapsed ? T("Show this controller's inputs") : T("Hide this controller's inputs") };
        fold.SetResourceReference(FontFamilyProperty, "IconFont");
        AutomationProperties.SetName(fold, d.Collapsed ? T("Expand") : T("Collapse"));
        fold.Click += (_, _) =>
        {
            d.Collapsed = !d.Collapsed;
            ProfileStore.Save(p); // only how it looks: the engine needn't know
            Render();
        };
        DockPanel.SetDock(fold, Dock.Right);
        head.Children.Add(fold);
        DockPanel.SetDock(remove, Dock.Right);
        head.Children.Add(remove);

        // which controller this is
        var link = new ComboBox { MinWidth = 260, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(link, T("Controller"));
        link.Items.Add(new ComboBoxItem { Content = joy != null ? joy.Name : F("{0} (not plugged in)", d.Name), Tag = null });
        foreach (var j in connected.Where(j => j != joy)) link.Items.Add(new ComboBoxItem { Content = j.Name, Tag = j });
        link.SelectedIndex = 0;
        link.ToolTip = T("The controller these assignments are for");
        link.SelectionChanged += (_, _) =>
        {
            if (link.SelectedItem is not ComboBoxItem { Tag: JoyInfo j }) return;
            d.Name = j.Name;
            d.Vid = j.Vid;
            d.Pid = j.Pid;
            d.Number = j.Occurrence;
            Save(p);
            Dispatcher.BeginInvoke(Render);
        };
        var state = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        state.Text = joy == null ? T("Not plugged in") : P(d.Inputs.Count, "{0} assigned", "{0} assigned");
        state.SetResourceReference(TextBlock.ForegroundProperty, joy == null ? "ErrorTextBrush" : "TextSecondaryBrush");
        var headLeft = new StackPanel { Orientation = Orientation.Horizontal };
        headLeft.Children.Add(link);
        headLeft.Children.Add(state);
        head.Children.Add(headLeft);
        s.Children.Add(head);
        if (d.Collapsed) return Card(s);

        bool xinput = joy?.IsXInput ?? (d.Vid == Joysticks.XInputVid && d.Pid == Joysticks.XInputPid);
        var inputs = Joysticks.InputsOf(joy, p.Pov8Way).ToList();
        foreach (var extra in d.Inputs.Keys) if (!inputs.Contains(extra)) inputs.Add(extra);
        if (settings.AssignedOnly) inputs = inputs.Where(d.Inputs.ContainsKey).ToList();

        foreach (var (title, group) in new[]
        {
            (T("Axes"), inputs.Where(i => i.StartsWith("Axis", StringComparison.Ordinal))),
            (T("POV hat"), inputs.Where(i => i.StartsWith("Pov", StringComparison.Ordinal))),
            (T("Buttons"), inputs.Where(i => i.StartsWith("Button", StringComparison.Ordinal))),
        })
        {
            var list = group.ToList();
            if (list.Count == 0) continue;
            var label = Secondary(title);
            label.Margin = new Thickness(0, 12, 0, 4);
            s.Children.Add(label);
            var wrap = new WrapPanel();
            foreach (var input in list) wrap.Children.Add(card.Tiles[input] = Tile(p, d, input, xinput));
            s.Children.Add(wrap);
        }
        if (inputs.Count == 0) s.Children.Add(Secondary(T("Nothing is assigned yet. Untick \"Only show what's assigned\" to see every input.")));
        return Card(s);
    }

    private Border Tile(J2KProfile p, J2KDevice d, string input, bool xinput)
    {
        d.Inputs.TryGetValue(input, out var action);
        // small tiles: the name on up to two lines, then what it does (all of it in the tooltip)
        string title = Joysticks.Describe(input, xinput);
        var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 31, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        var what = new TextBlock { Text = action?.Summary() ?? "—", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        what.SetResourceReference(TextBlock.ForegroundProperty, action != null ? "AccentBrush" : "TextSecondaryBrush");
        var dock = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(name, Dock.Top);
        DockPanel.SetDock(what, Dock.Bottom);
        dock.Children.Add(name);
        dock.Children.Add(what);
        var tile = new Border
        {
            Width = TileWidth, Height = TileHeight, Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2), Child = dock, Cursor = Cursors.Hand, Focusable = true,
            ToolTip = title + "\n" + (action != null ? action.Summary() + "\n" : "") + T("Double-click to choose what it does. Right-click for more."),
        };
        tile.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        tile.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        AutomationProperties.SetName(tile, Joysticks.Describe(input, xinput));
        tile.MouseLeftButtonDown += (_, e) =>
        {
            Select(d, input);
            if (e.ClickCount == 2) { Edit(p, d, input, xinput); e.Handled = true; }
        };
        tile.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Edit(p, d, input, xinput); e.Handled = true; } };

        var menu = new ContextMenu();
        var edit = new MenuItem { Header = T("Choose what it does…") };
        edit.Click += (_, _) => Edit(p, d, input, xinput);
        var clear = new MenuItem { Header = T("Clear"), IsEnabled = action != null };
        clear.Click += (_, _) => { d.Inputs.Remove(input); Save(p); Render(); };
        var copy = new MenuItem { Header = T("Copy"), IsEnabled = action != null };
        copy.Click += (_, _) => copied = action?.Clone();
        var paste = new MenuItem { Header = T("Paste") };
        paste.Click += (_, _) => { if (copied != null) { d.Inputs[input] = copied.Clone(); Save(p); Render(); } };
        menu.Opened += (_, _) => paste.IsEnabled = copied != null;
        menu.Items.Add(edit);
        menu.Items.Add(clear);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        tile.ContextMenu = menu;
        if (selected is { } sel && sel.Device == d && sel.Input == input) Highlight(tile, false, true);
        return tile;
    }

    private void Select(J2KDevice d, string input)
    {
        selected = (d, input);
        foreach (var c in cards)
            foreach (var (id, t) in c.Tiles)
                Highlight(t, c.Active.Contains(id), c.Device == d && id == input);
    }

    private static void Highlight(Border tile, bool active, bool isSelected)
    {
        tile.SetResourceReference(Border.BorderBrushProperty, active || isSelected ? "AccentBrush" : "CardBorderBrush");
        tile.SetResourceReference(Border.BackgroundProperty, active ? "RowSelectedBrush" : isSelected ? "RowHoverBrush" : "ControlBrush");
    }

    // ---- live ----

    private void Tick()
    {
        bool inFront = Window.GetWindow(this)?.IsActive == true;
        SetTabPause(inFront);

        var connected = Joysticks.Connected();
        if (KeyOf(connected) != connectedKey) { Render(); return; }
        var p = Showing;
        float threshold = Math.Clamp(p.Threshold, 5, 95) / 100f;
        foreach (var c in cards)
        {
            if (c.Joy == null || !Joysticks.Read(c.Joy, out var s)) continue;
            foreach (var (id, tile) in c.Tiles)
            {
                bool on = Joysticks.Amount(s, id, threshold, p.Pov8Way) > 0;
                if (on == c.Active.Contains(id)) continue;
                if (on)
                {
                    c.Active.Add(id);
                    Select(c.Device, id);
                    tile.BringIntoView();
                }
                else
                {
                    c.Active.Remove(id);
                    Highlight(tile, false, selected is { } sel && sel.Device == c.Device && sel.Input == id);
                }
            }
        }
    }

    private void SetTabPause(bool pause)
    {
        if (pause == pausedForTab) return;
        pausedForTab = pause;
        if (pause) engine.Pause(); else engine.Resume();
    }

    private static string KeyOf(IReadOnlyList<JoyInfo> list) => string.Join("|", list.Select(j => $"{j.Id}:{j.Vid}:{j.Pid}:{j.Name}"));

    // ---- editing ----

    private void Edit(J2KProfile p, J2KDevice d, string input, bool xinput)
    {
        Select(d, input);
        d.Inputs.TryGetValue(input, out var action);
        var w = new BindingWindow(F("{0} — {1}", Joysticks.Describe(input, xinput), d.Name), action, profiles.Select(x => x.Name)) { Owner = Window.GetWindow(this) };
        engine.Pause();
        try
        {
            if (w.ShowDialog() != true) return;
        }
        finally { engine.Resume(); }
        if (w.Result == null) d.Inputs.Remove(input); else d.Inputs[input] = w.Result;
        Save(p);
        Render();
    }

    private void Save(J2KProfile p)
    {
        ProfileStore.Save(p);
        engine.SetProfiles(profiles);
    }

    private void AddMenu(Button anchor, J2KProfile p)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var connected = Joysticks.Connected(force: true);
        foreach (var j in connected)
        {
            bool inProfile = p.Devices.Any(d => Joysticks.Resolve(d, connected) == j);
            var item = new MenuItem { Header = inProfile ? F("{0} (already in this profile)", j.Name) : j.Name, IsEnabled = !inProfile };
            item.Click += (_, _) =>
            {
                p.Devices.Add(new J2KDevice { Name = j.Name, Vid = j.Vid, Pid = j.Pid, Number = j.Occurrence });
                Save(p);
                Render();
            };
            menu.Items.Add(item);
        }
        if (connected.Count == 0) menu.Items.Add(new MenuItem { Header = T("No controllers are plugged in"), IsEnabled = false });
        menu.Items.Add(new Separator());
        var any = new MenuItem { Header = T("Controller number… (whichever is plugged in)") };
        for (int n = 1; n <= 4; n++)
        {
            int number = n;
            var item = new MenuItem { Header = F("Controller {0}", n) };
            item.Click += (_, _) =>
            {
                p.Devices.Add(new J2KDevice { Name = F("Controller {0}", number), Number = number });
                Save(p);
                Render();
            };
            any.Items.Add(item);
        }
        menu.Items.Add(any);
        menu.IsOpen = true;
    }

    private void ProgramMenu(Button anchor, J2KProfile p, TextBox games)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id != self && proc.MainWindowHandle != 0 && proc.ProcessName != "explorer") names.Add(proc.ProcessName + ".exe");
            }
            catch { }
            finally { proc.Dispose(); }
        }
        foreach (var n in names)
        {
            var item = new MenuItem { Header = n, IsEnabled = !p.Programs.Contains(n, StringComparer.OrdinalIgnoreCase) };
            item.Click += (_, _) =>
            {
                p.Programs.Add(n);
                games.Text = string.Join(", ", p.Programs);
                Save(p);
            };
            menu.Items.Add(item);
        }
        if (names.Count == 0) menu.Items.Add(new MenuItem { Header = T("No programs with windows are running"), IsEnabled = false });
        menu.IsOpen = true;
    }

    // ---- profiles ----

    private string? AskName(string title, string value)
    {
        var w = new TextPrompt(title, T("Name"), value) { Owner = Window.GetWindow(this) };
        return w.ShowDialog() == true ? w.Value : null;
    }

    private void NewProfile()
    {
        var name = AskName(T("New profile"), ProfileStore.FreeName(T("New profile"), profiles));
        if (name == null) return;
        Add(new J2KProfile { Name = ProfileStore.FreeName(name, profiles) });
    }

    private void CopyProfile()
    {
        var copy = Showing.Clone();
        var name = AskName(T("Copy profile"), ProfileStore.FreeName(F("{0} copy", copy.Name), profiles));
        if (name == null) return;
        copy.Name = ProfileStore.FreeName(name, profiles);
        Add(copy);
    }

    private void Add(J2KProfile p)
    {
        profiles.Add(p);
        profiles = profiles.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        settings.Showing = p.Name;
        settings.Save();
        Save(p);
        Render();
    }

    private void RenameProfile()
    {
        var p = Showing;
        var name = AskName(T("Rename profile"), p.Name);
        if (name == null || name == p.Name) return;
        name = ProfileStore.FreeName(name, profiles.Where(x => x != p));
        string old = p.Name;
        ProfileStore.Delete(old);
        p.Name = name;
        foreach (var other in profiles)
        {
            bool changed = false;
            foreach (var a in other.Devices.SelectMany(d => d.Inputs.Values))
                if (a.Kind == ActionKind.Profile && string.Equals(a.Profile, old, StringComparison.OrdinalIgnoreCase)) { a.Profile = name; changed = true; }
            if (changed && other != p) ProfileStore.Save(other);
        }
        if (settings.Active.Equals(old, StringComparison.OrdinalIgnoreCase)) settings.Active = name;
        settings.Showing = name;
        settings.Save();
        profiles = profiles.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Save(p);
        engine.Refresh();
        Render();
    }

    private void DeleteProfile()
    {
        var p = Showing;
        if (profiles.Count == 1) { MessageBox.Show(Window.GetWindow(this), T("This is the only profile, so it can't be deleted."), T("Joy 2 Key")); return; }
        if (MessageBox.Show(Window.GetWindow(this), F("Delete the profile {0}?", p.Name), T("Joy 2 Key"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        profiles.Remove(p);
        ProfileStore.Delete(p.Name);
        if (settings.Active.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) settings.Active = profiles[0].Name;
        settings.Showing = null;
        settings.Save();
        engine.SetProfiles(profiles);
        engine.Refresh();
        Render();
    }

    private void Import()
    {
        var w = new ImportWindow(settings, profiles) { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() != true) return;
        profiles = ProfileStore.LoadAll();
        if (w.Imported.Count > 0) { settings.Showing = w.Imported[0].Name; settings.Save(); }
        engine.SetProfiles(profiles);
        Render();
    }

    // ---- pieces ----

    // a list row like the app's other lists (the system one is light even in the dark theme)
    private static readonly Style RowStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border x:Name="Bg" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}" Margin="0,0,0,2">
                            <ContentPresenter/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource RowHoverBrush}"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource RowSelectedBrush}"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        """);

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

    private static FrameworkElement Labeled(string label, FrameworkElement content)
    {
        var line = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var l = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        line.Children.Add(l);
        line.Children.Add(content);
        return line;
    }

    private static Button SmallButton(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5) };
        b.Click += (_, _) => click();
        return b;
    }
}
