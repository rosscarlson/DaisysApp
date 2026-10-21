using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// The Button2Key Remote tab. Left, as Button2Key's: on or off (with its shortcut), the BigButt in use (its ButtScreens
/// in order and its games), the BigButts, and devices (port, passphrase, who's connected). Right: every ButtScreen, with a picture of it, to edit, pop
/// out, copy or delete, and New ButtScreen.
/// </summary>
internal sealed class RemoteView : UserControl
{
    private const int LeftWidth = 340;

    private readonly ButtHub hub;
    private readonly StackPanel leftStack = new();
    private readonly WrapPanel tiles = new();
    private readonly TextBlock serverStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel deviceList = new();
    private bool dirty = true;

    private RemoteSettings Settings => hub.Settings;

    public RemoteView(ButtHub hub)
    {
        this.hub = hub;
        var leftScroll = new ScrollViewer { Content = leftStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var right = new StackPanel();
        var head = new DockPanel();
        var add = new Button { Content = T("New ButtScreen"), Padding = new Thickness(12, 4, 12, 4) };
        add.SetResourceReference(StyleProperty, "AccentButton");
        add.Click += (_, _) => NewScreen();
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        head.Children.Add(Header(T("ButtScreens")));
        var screensStack = new StackPanel();
        screensStack.Children.Add(head);
        screensStack.Children.Add(Secondary(T("A ButtScreen is a screen of buttons for a phone, tablet or PC, or popped out on this one. Each button does what a Button2Key input can: keys, macros, the mouse, a program, or showing another ButtScreen.")));
        tiles.Margin = new Thickness(0, 12, 0, 0);
        screensStack.Children.Add(tiles);
        right.Children.Add(Card(screensStack));
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(leftScroll);
        Grid.SetColumn(rightScroll, 2);
        grid.Children.Add(rightScroll);
        Content = grid;

        hub.Updated += () => { dirty = true; if (IsVisible) Render(); };
        hub.Server.StatusChanged += ShowServer;
        IsVisibleChanged += (_, _) => { if (IsVisible && dirty) Render(); };
    }

    private void Render()
    {
        dirty = false;
        RenderLeft();
        RenderScreens();
    }

    // ---- the left column ----

    private void RenderLeft()
    {
        leftStack.Children.Clear();
        leftStack.Children.Add(OnOffCard());
        // as Button2Key: the BigButt showing (its ButtScreens and games), then the list of BigButts
        if (hub.Active is { } active) leftStack.Children.Add(BigButtCard(active));
        leftStack.Children.Add(BigButtsCard());
        leftStack.Children.Add(DevicesCard());
    }

    private Border OnOffCard()
    {
        var on = new StackPanel();
        on.Children.Add(Header(T("Button2Key Remote on or off")));
        var onOff = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), IsChecked = Settings.Enabled, Content = Settings.Enabled ? T("On") : T("Off"), VerticalAlignment = VerticalAlignment.Center };
        onOff.SetResourceReference(StyleProperty, "ChipToggle");
        AutomationProperties.SetName(onOff, T("Button2Key Remote on"));
        onOff.Click += (_, _) => hub.SetEnabled(onOff.IsChecked == true);
        // its shortcut, beside it: keys, or a controller / button box button
        var shortcut = new ShortcutBox { AllowControllerButtons = true, Value = Settings.Hotkey, Margin = new Thickness(10, 0, 0, 0), ToolTip = T("A shortcut that switches Button2Key Remote on and off, from anywhere") };
        AutomationProperties.SetName(shortcut, T("On / off shortcut"));
        shortcut.Attach(hub.SuspendHotkey, hub.RegisterHotkey);
        shortcut.Changed += () =>
        {
            Settings.Hotkey = shortcut.Value;
            hub.Save();
            hub.RegisterHotkey();
            dirty = true;
            Dispatcher.BeginInvoke(Render);
        };
        var line = new DockPanel();
        DockPanel.SetDock(onOff, Dock.Left);
        line.Children.Add(onOff);
        line.Children.Add(shortcut);
        on.Children.Add(line);
        if (hub.HotkeyTaken) on.Children.Add(Error(F("Another program has {0}: pick another shortcut.", Settings.Hotkey)));
        on.Children.Add(Secondary(Settings.Enabled
            ? F("In use: {0}. Devices can connect, and buttons do what they're set to.", Settings.Active)
            : T("Off: devices can't connect and buttons do nothing.")));
        if (hub.Engine.LastSent is { } last)
            on.Children.Add(Secondary(F("Last sent: {0} at {1:T}", last, hub.Engine.LastSentAt)));
        var auto = new CheckBox { Content = new TextBlock { Text = T("Use a game's BigButt while the game is running, and Default when it closes"), TextWrapping = TextWrapping.Wrap }, IsChecked = Settings.AutoSwitch, Margin = new Thickness(0, 10, 0, 0) };
        auto.Click += (_, _) => { Settings.AutoSwitch = auto.IsChecked == true; hub.Save(); };
        on.Children.Add(auto);
        var badge = new CheckBox { Content = new TextBlock { Text = T("Show Button2Key Remote in the title bar while it's on"), TextWrapping = TextWrapping.Wrap }, IsChecked = Settings.TitleBadge, Margin = new Thickness(0, 8, 0, 0) };
        badge.Click += (_, _) => { Settings.TitleBadge = badge.IsChecked == true; hub.Save(); hub.ShowBadge(); };
        on.Children.Add(badge);
        return Card(on);
    }

    /// <summary>The BigButt showing (the one in use): its name, its ButtScreens in the order a device goes through them, its games.</summary>
    private Border BigButtCard(BigButt active)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = active.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = active.Name });
        s.Children.Add(Secondary(T("The BigButt selected below is the one in use, except while another BigButt's game is in front.")));

        s.Children.Add(new TextBlock { Text = T("Its ButtScreens"), Margin = new Thickness(0, 12, 0, 2) });
        var inIt = active.Screens.Select(id => hub.Screens.FirstOrDefault(x => x.Id == id)).OfType<ButtScreen>().ToList();
        foreach (var x in inIt.Concat(hub.Screens.Where(x => !inIt.Contains(x))))
        {
            var screen = x;
            int index = active.Screens.IndexOf(screen.Id);
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            if (index >= 0)
            {
                tools.Children.Add(IconButton("", T("Move up"), index > 0, () => Swap(active, index, index - 1)));
                tools.Children.Add(IconButton("", T("Move down"), index < active.Screens.Count - 1, () => Swap(active, index, index + 1)));
            }
            DockPanel.SetDock(tools, Dock.Right);
            line.Children.Add(tools);
            var check = new CheckBox { Content = index >= 0 ? F("{0}. {1}", inIt.IndexOf(screen) + 1, screen.Name) : screen.Name, IsChecked = index >= 0, VerticalAlignment = VerticalAlignment.Center };
            check.Click += (_, _) =>
            {
                if (check.IsChecked == true) active.Screens.Add(screen.Id); else active.Screens.Remove(screen.Id);
                hub.Save();
                hub.Publish();
            };
            line.Children.Add(check);
            s.Children.Add(line);
        }
        s.Children.Add(Secondary(hub.Screens.Count == 0 ? T("No ButtScreens yet: make one with New ButtScreen.")
            : P(inIt.Count, "A device shows its {0} ButtScreen.", "A device goes through its {0} ButtScreens in this order (the arrows change it).")));

        s.Children.Add(new TextBlock { Text = T("Games"), Margin = new Thickness(0, 12, 0, 2) });
        if (active == hub.Default)
        {
            s.Children.Add(Secondary(T("Default is used while none of the other BigButts' games is running.")));
            return Card(s);
        }
        foreach (var name in active.Programs.ToList())
        {
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            string n = name;
            var take = IconButton("", F("Take {0} off this BigButt", n), true, () => { active.Programs.Remove(n); hub.Save(); hub.Publish(); });
            DockPanel.SetDock(take, Dock.Right);
            line.Children.Add(take);
            line.Children.Add(new TextBlock { Text = n, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = n });
            s.Children.Add(line);
        }
        if (active.Programs.Count == 0) s.Children.Add(Secondary(T("None yet: this BigButt is only used when it's picked in the list (or on a device).")));
        var addLine = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var game = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(game, T("Game to add"));
        var addGame = new Button { Content = T("Add"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        void AddTyped()
        {
            AddGames(active, game.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            game.Text = "";
        }
        addGame.Click += (_, _) => AddTyped();
        game.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTyped(); e.Handled = true; } };
        DockPanel.SetDock(addGame, Dock.Right);
        addLine.Children.Add(addGame);
        addLine.Children.Add(game);
        s.Children.Add(addLine);
        var pick = new Button { Content = T("Add a running program…"), Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
        pick.Click += (_, _) => ProgramMenu(pick, active);
        s.Children.Add(pick);
        s.Children.Add(Secondary(T("Each game's program name (e.g. forza_x64.exe): one BigButt can have as many as you like. While one of them is running (the one in front, if several are), this BigButt is used.")));
        return Card(s);
    }

    /// <summary>The BigButts: picking one uses it (as Button2Key's profiles); each has Rename and Delete on its row; New, Copy.</summary>
    private Border BigButtsCard()
    {
        var s = new StackPanel();
        s.Children.Add(Header(T("BigButts")));
        var list = new ListBox { BorderThickness = new Thickness(0), MinHeight = 60, ItemContainerStyle = RowStyle };
        list.SetResourceReference(BackgroundProperty, "CardBrush");
        var active = hub.Active;
        foreach (var b in Settings.BigButts)
        {
            var line = new DockPanel();
            if (b != hub.Default)
            {
                // (on every row, so any BigButt can be renamed or deleted, not only the one in use)
                var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
                var rename = SmallButton(T("Rename"), () => RenameBigButt(b));
                var delete = ConfirmButton(T("Delete"), () => DeleteBigButt(b), F("Deletes {0} (its ButtScreens stay)", b.Name));
                rename.Margin = delete.Margin = new Thickness(4, 0, 0, 0);
                rename.Padding = delete.Padding = new Thickness(6, 1, 6, 1);
                AutomationProperties.SetName(rename, F("Rename {0}", b.Name));
                AutomationProperties.SetName(delete, F("Delete {0}", b.Name));
                tools.Children.Add(rename);
                tools.Children.Add(delete);
                DockPanel.SetDock(tools, Dock.Right);
                line.Children.Add(tools);
            }
            if (b == active && Settings.Enabled)
            {
                var tag = new TextBlock { Text = T("in use"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                tag.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                DockPanel.SetDock(tag, Dock.Right);
                line.Children.Add(tag);
            }
            int count = b.Screens.Count(id => hub.Screens.Any(x => x.Id == id));
            line.Children.Add(new TextBlock { Text = F("{0} ({1})", b.Name, P(count, "{0} ButtScreen", "{0} ButtScreens")), TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem { Content = line, Tag = b, Padding = new Thickness(6, 5, 6, 5) };
            AutomationProperties.SetName(item, b.Name);
            list.Items.Add(item);
            if (b == active) list.SelectedItem = item;
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: BigButt b }) hub.UseBigButt(b.Name); };
        s.Children.Add(list);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(SmallButton(T("New"), () =>
        {
            if (AskName(T("New BigButt"), FreeBigButt(T("New BigButt"))) is not { } name) return;
            var b = new BigButt { Name = FreeBigButt(name) };
            Settings.BigButts.Add(b);
            hub.Save();
            hub.UseBigButt(b.Name);
        }));
        if (active != null)
        {
            buttons.Children.Add(SmallButton(T("Copy"), () =>
            {
                if (AskName(T("Copy BigButt"), FreeBigButt(F("{0} copy", active.Name))) is not { } name) return;
                var b = new BigButt { Name = FreeBigButt(name), Screens = active.Screens.ToList() };
                Settings.BigButts.Add(b);
                hub.Save();
                hub.UseBigButt(b.Name);
            }, T("A new BigButt with the same ButtScreens (and no games)")));
        }
        s.Children.Add(buttons);
        s.Children.Add(Secondary(T("Default can't be renamed or deleted: it's used when no BigButt's game is running.")));
        return Card(s);
    }

    private void RenameBigButt(BigButt b)
    {
        if (AskName(T("Rename BigButt"), b.Name) is not { } name || name == b.Name) return;
        bool inUse = Settings.Active.Equals(b.Name, StringComparison.OrdinalIgnoreCase);
        b.Name = ""; // (so it doesn't clash with its own old name, "forza" to "Forza")
        b.Name = FreeBigButt(name);
        if (inUse) Settings.Active = b.Name;
        hub.Save();
        hub.Publish();
    }

    private void DeleteBigButt(BigButt b)
    {
        bool inUse = Settings.Active.Equals(b.Name, StringComparison.OrdinalIgnoreCase);
        Settings.BigButts.Remove(b);
        if (inUse) Settings.Active = BigButt.DefaultName;
        hub.Save();
        hub.Publish();
    }

    /// <summary>The port and passphrase (Save applies them), then the devices connected now, a line each.</summary>
    private Border DevicesCard()
    {
        var d = new StackPanel();
        d.Children.Add(Header(T("Devices")));
        var port = new TextBox { Text = Settings.Port.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(port, T("Port"));
        d.Children.Add(Labeled(T("Port"), port));
        var pass = new PasswordBox { VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(pass, T("Passphrase"));
        pass.ToolTip = Settings.PassphraseProtected.Length > 0 ? T("Set: type a new one to change it") : T("8 characters or more: type the same on your devices");
        d.Children.Add(Labeled(T("Passphrase"), pass));
        var problem = Error("");
        problem.Visibility = Visibility.Collapsed;
        var save = new Button { Content = T("Save"), Padding = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        save.SetResourceReference(StyleProperty, "AccentButton");
        void Save()
        {
            var why = new List<string>();
            if (!int.TryParse(port.Text.Trim(), out int p) || p is < 1024 or > 65535) why.Add(T("The port is a number from 1024 to 65535."));
            if (pass.Password.Length is > 0 and < 8) why.Add(T("The passphrase is 8 characters or more."));
            problem.Text = string.Join(" ", why);
            problem.Visibility = why.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (why.Count > 0) return;
            bool changed = false;
            if (p != Settings.Port) { Settings.Port = p; changed = true; }
            if (pass.Password.Length > 0 && pass.Password != Settings.Passphrase) { Settings.Passphrase = pass.Password; changed = true; }
            pass.Password = "";
            if (Settings.PassphraseProtected.Length > 0) pass.ToolTip = T("Set: type a new one to change it");
            if (changed) { hub.Save(); hub.Server.Apply(); }
            save.Content = T("✓ Saved");
            var back = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            back.Tick += (_, _) => { back.Stop(); save.Content = T("Save"); };
            back.Start();
            ShowServer();
        }
        save.Click += (_, _) => Save();
        port.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        pass.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        d.Children.Add(save);
        d.Children.Add(problem);
        // (kept from one drawing to the next, as the server changes them: out of the old card first)
        (serverStatus.Parent as Panel)?.Children.Remove(serverStatus);
        (deviceList.Parent as Panel)?.Children.Remove(deviceList);
        d.Children.Add(serverStatus);
        var active = Header(T("Active Devices"));
        active.Margin = new Thickness(0, 14, 0, 2);
        d.Children.Add(active);
        d.Children.Add(deviceList);
        ShowServer();
        return Card(d);
    }

    /// <summary>
    /// A button for something that can't be undone (Delete): the first click turns it into Confirm? (red), a second
    /// click within a few seconds does it; otherwise it goes back by itself. No question window (see the README).
    /// </summary>
    private static Button ConfirmButton(string text, Action confirmed, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5), ToolTip = tip };
        AutomationProperties.SetName(b, text);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        void Reset()
        {
            timer.Stop();
            b.Content = text;
            b.ClearValue(StyleProperty);
        }
        timer.Tick += (_, _) => Reset();
        b.Click += (_, _) =>
        {
            if (!timer.IsEnabled)
            {
                b.Content = T("Confirm?");
                b.SetResourceReference(StyleProperty, "DangerButton");
                timer.Start();
                return;
            }
            Reset();
            confirmed();
        };
        b.Unloaded += (_, _) => timer.Stop();
        return b;
    }

    private static TextBlock Error(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        return t;
    }

    private void ShowServer()
    {
        var s = hub.Server;
        // only a problem (a port in use, no passphrase yet…) shows here: the card is otherwise just the devices
        bool problem = s.Failed || Settings.Enabled && Settings.PassphraseProtected.Length == 0;
        serverStatus.Text = s.Failed ? s.Status : T("Set a passphrase (8 characters or more) and Save: devices connect with it.");
        serverStatus.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;
        serverStatus.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        deviceList.Children.Clear();
        foreach (var c in s.Clients)
        {
            var line = new TextBlock { Text = "- " + F("{0} ({1})", c.Device, c.Platform.Length > 0 ? c.Platform + ", " + c.Wire.Endpoint : c.Wire.Endpoint), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            line.ToolTip = F("Connected since {0:t}", c.Since);
            line.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            deviceList.Children.Add(line);
        }
        if (deviceList.Children.Count == 0)
            deviceList.Children.Add(Secondary(T("None")));
    }

    private void Swap(BigButt b, int i, int j)
    {
        (b.Screens[i], b.Screens[j]) = (b.Screens[j], b.Screens[i]);
        hub.Save();
        hub.Publish();
    }

    private string FreeBigButt(string name)
    {
        if (!Settings.BigButts.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name;
        for (int i = 2; ; i++)
            if (!Settings.BigButts.Any(b => b.Name.Equals($"{name} ({i})", StringComparison.OrdinalIgnoreCase))) return $"{name} ({i})";
    }

    private void ProgramMenu(Button anchor, BigButt b)
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
            var item = new MenuItem { Header = n, IsEnabled = !b.Programs.Contains(n, StringComparer.OrdinalIgnoreCase) };
            item.Click += (_, _) => AddGames(b, [n]);
            menu.Items.Add(item);
        }
        if (names.Count == 0) menu.Items.Add(new MenuItem { Header = T("No programs with windows are running"), IsEnabled = false });
        menu.IsOpen = true;
    }

    private void AddGames(BigButt b, IEnumerable<string> names)
    {
        bool added = false;
        foreach (var raw in names)
        {
            string n = Path.GetFileName(raw.Trim().Trim('"'));
            if (n.Length == 0) continue;
            if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n += ".exe";
            if (b.Programs.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
            b.Programs.Add(n);
            added = true;
        }
        if (!added) return;
        hub.Save();
        hub.Publish();
    }

    // ---- the ButtScreens ----

    private void RenderScreens()
    {
        tiles.Children.Clear();
        foreach (var s in hub.Screens) tiles.Children.Add(Tile(s));
        if (hub.Screens.Count == 0)
            tiles.Children.Add(Secondary(T("None yet. New ButtScreen makes one and opens it to lay its buttons out.")));
    }

    private FrameworkElement Tile(ButtScreen s)
    {
        var stack = new StackPanel { Width = 250 };
        var picture = new Border { Height = 200, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, ToolTip = T("Double-click to edit"), ClipToBounds = true };
        picture.SetResourceReference(Border.BackgroundProperty, "RoomBrush");
        picture.Child = new Viewbox { Child = new ScreenVisual(s), Margin = new Thickness(8), Stretch = Stretch.Uniform };
        picture.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) hub.Edit(s); };
        stack.Children.Add(picture);
        stack.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = s.Name });
        var bigButts = Settings.BigButts.Where(b => b.Screens.Contains(s.Id)).Select(b => b.Name).ToList();
        stack.Children.Add(Secondary(F("{0} × {1} · {2} buttons", s.Width, s.Height, s.Buttons.Count)
            + (bigButts.Count > 0 ? "\n" + F("In {0}", string.Join(", ", bigButts)) : "")));
        var tools = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        tools.Children.Add(SmallButton(T("Edit"), () => hub.Edit(s)));
        bool popped = hub.IsPoppedOut(s);
        tools.Children.Add(SmallButton(popped ? T("Close pop-out") : T("Pop out"), () => { if (popped) hub.ClosePopOut(s); else hub.PopOut(s); },
            T("A window of its own, to use with the mouse or a touchscreen")));
        tools.Children.Add(SmallButton(T("Copy"), () =>
        {
            if (AskName(T("Copy ButtScreen"), ScreenStore.FreeName(F("{0} copy", s.Name), hub.Screens)) is { } name) hub.Add(name, s);
        }));
        tools.Children.Add(ConfirmButton(T("Delete"), () => hub.Delete(s), F("Deletes {0} and its {1} buttons", s.Name, s.Buttons.Count)));
        stack.Children.Add(tools);
        return new Border { Child = stack, Margin = new Thickness(0, 0, 16, 16) };
    }

    private void NewScreen()
    {
        if (AskName(T("New ButtScreen"), ScreenStore.FreeName(T("New ButtScreen"), hub.Screens)) is not { } name) return;
        var s = new ButtScreen();
        // the size of the device last connected, if there's one
        if (Settings.Devices.FirstOrDefault(d => d.Width > 0 && d.Height > 0) is { } d) { s.Width = d.Width; s.Height = d.Height; }
        if (s.Width > s.Height) (s.Columns, s.Rows) = (s.Rows, s.Columns);
        hub.Edit(hub.Add(name, s));
    }

    // ---- small parts ----

    private string? AskName(string title, string value)
    {
        var w = new TextPrompt(title, T("Name"), value) { Owner = Window.GetWindow(this) };
        return w.ShowDialog() == true ? w.Value : null;
    }

    // a list row like the app's other lists
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
        var line = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var l = new TextBlock { Text = label, Width = 84, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        line.Children.Add(l);
        line.Children.Add(content);
        return line;
    }

    private static Button SmallButton(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5), ToolTip = tip };
        b.Click += (_, _) => click();
        return b;
    }

    private static Button IconButton(string glyph, string tip, bool enabled, Action click)
    {
        var b = new Button { Content = glyph, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2, 0, 0, 0), ToolTip = tip, IsEnabled = enabled };
        b.SetResourceReference(FontFamilyProperty, "IconFont");
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
}
