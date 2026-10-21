using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Applets.Gaming.Joy2Key;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// The Button2Key Remote tab. Left: on or off, letting devices connect (port, passphrase, who's connected), and the
/// BigButts (the one in use, its games and its ButtScreens). Right: every ButtScreen, with a picture of it, to edit, pop
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

        // on or off
        var on = new StackPanel();
        on.Children.Add(Header(T("Button2Key Remote on or off")));
        var onOff = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), IsChecked = Settings.Enabled, Content = Settings.Enabled ? T("On") : T("Off"), HorizontalAlignment = HorizontalAlignment.Left };
        onOff.SetResourceReference(StyleProperty, "ChipToggle");
        AutomationProperties.SetName(onOff, T("Button2Key Remote on"));
        onOff.Click += (_, _) => hub.SetEnabled(onOff.IsChecked == true);
        on.Children.Add(onOff);
        if (hub.Engine.LastSent is { } last)
            on.Children.Add(Secondary(F("Last sent: {0} at {1:T}", last, hub.Engine.LastSentAt)));
        on.Children.Add(Secondary(Settings.Enabled ? T("Buttons pressed on devices and pop-outs do what they're set to.") : T("Off: buttons do nothing (devices can still connect and show them).")));
        var badge = new CheckBox { Content = new TextBlock { Text = T("Show Button2Key Remote in the title bar while a device is connected"), TextWrapping = TextWrapping.Wrap }, IsChecked = Settings.TitleBadge, Margin = new Thickness(0, 8, 0, 0) };
        badge.Click += (_, _) => { Settings.TitleBadge = badge.IsChecked == true; hub.Save(); hub.ShowBadge(); };
        on.Children.Add(badge);
        leftStack.Children.Add(Card(on));

        // devices
        var d = new StackPanel();
        d.Children.Add(Header(T("Devices")));
        var serve = new CheckBox { Content = new TextBlock { Text = T("Let devices connect (phones, tablets, other PCs)"), TextWrapping = TextWrapping.Wrap }, IsChecked = Settings.Serve };
        serve.Click += (_, _) => { Settings.Serve = serve.IsChecked == true; hub.Save(); hub.Server.Apply(); };
        d.Children.Add(serve);
        var port = new TextBox { Text = Settings.Port.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(port, T("Port"));
        void SetPort()
        {
            if (int.TryParse(port.Text.Trim(), out int p) && p is > 1023 and < 65536 && p != Settings.Port)
            {
                Settings.Port = p;
                hub.Save();
                hub.Server.Apply();
            }
            port.Text = Settings.Port.ToString();
        }
        port.LostKeyboardFocus += (_, _) => SetPort();
        port.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetPort(); };
        d.Children.Add(Labeled(T("Port"), port));
        var pass = new PasswordBox { VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(pass, T("Passphrase"));
        pass.ToolTip = Settings.PassphraseProtected.Length > 0 ? T("Set: type a new one to change it") : T("8 characters or more: type the same on your devices");
        void SetPass()
        {
            if (pass.Password.Length == 0 || pass.Password == Settings.Passphrase) return;
            Settings.Passphrase = pass.Password;
            hub.Save();
            hub.Server.Apply();
            pass.Password = "";
            pass.ToolTip = T("Set: type a new one to change it");
            ShowServer();
        }
        pass.LostKeyboardFocus += (_, _) => SetPass();
        pass.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetPass(); };
        d.Children.Add(Labeled(T("Passphrase"), pass));
        // (kept from one drawing to the next, as the server changes them: out of the old card first)
        (serverStatus.Parent as Panel)?.Children.Remove(serverStatus);
        (deviceList.Parent as Panel)?.Children.Remove(deviceList);
        d.Children.Add(serverStatus);
        d.Children.Add(deviceList);
        d.Children.Add(Secondary(T("Devices find this PC by itself on the same network, or connect to its address and port. The first time, Windows may ask to let Daisy's App through its firewall: allow it on private networks.")));
        leftStack.Children.Add(Card(d));
        ShowServer();

        // BigButts
        leftStack.Children.Add(BigButtsCard());
    }

    private void ShowServer()
    {
        var s = hub.Server;
        serverStatus.Text = s.Status + (Settings.PassphraseProtected.Length > 0 ? "" : " " + T("No passphrase set yet."));
        serverStatus.SetResourceReference(TextBlock.ForegroundProperty, s.Failed ? "ErrorTextBrush" : "TextSecondaryBrush");
        deviceList.Children.Clear();
        foreach (var c in s.Clients)
        {
            var line = new TextBlock { Text = F("{0} ({1}), since {2:t}", c.Device, c.Platform.Length > 0 ? c.Platform + ", " + c.Wire.Endpoint : c.Wire.Endpoint, c.Since), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            line.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            deviceList.Children.Add(line);
        }
    }

    private Border BigButtsCard()
    {
        var s = new StackPanel();
        s.Children.Add(Header(T("BigButts")));
        s.Children.Add(Secondary(T("A BigButt is a set of ButtScreens, usually for one game: devices show the one in use. Pick one to use it.")));

        var list = new ListBox { BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 0), ItemContainerStyle = RowStyle };
        list.SetResourceReference(BackgroundProperty, "CardBrush");
        var active = hub.Active;
        foreach (var b in Settings.BigButts)
        {
            var line = new DockPanel();
            if (b == active)
            {
                var tag = new TextBlock { Text = T("in use"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                tag.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                DockPanel.SetDock(tag, Dock.Right);
                line.Children.Add(tag);
            }
            line.Children.Add(new TextBlock { Text = F("{0} ({1})", b.Name, P(b.Screens.Count(id => hub.Screens.Any(x => x.Id == id)), "{0} ButtScreen", "{0} ButtScreens")), TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem { Content = line, Tag = b, Padding = new Thickness(6, 5, 6, 5) };
            AutomationProperties.SetName(item, b.Name);
            list.Items.Add(item);
            if (b == active) list.SelectedItem = item;
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: BigButt b }) hub.UseBigButt(b.Name); };
        s.Children.Add(list);

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(SmallButton(T("New"), () =>
        {
            if (AskName(T("New BigButt"), FreeBigButt(T("New BigButt"))) is not { } name) return;
            var b = new BigButt { Name = FreeBigButt(name) };
            Settings.BigButts.Add(b);
            hub.Save();
            hub.UseBigButt(b.Name);
        }));
        if (active != null && active != hub.Default)
        {
            buttons.Children.Add(SmallButton(T("Rename"), () =>
            {
                if (AskName(T("Rename BigButt"), active.Name) is not { } name || name == active.Name) return;
                active.Name = FreeBigButt(name);
                Settings.Active = active.Name;
                hub.Save();
                hub.Publish();
            }));
            buttons.Children.Add(SmallButton(T("Delete"), () =>
            {
                if (MessageBox.Show(Window.GetWindow(this), F("Delete the BigButt {0}? Its ButtScreens stay.", active.Name), T("Button2Key Remote"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                Settings.BigButts.Remove(active);
                Settings.Active = BigButt.DefaultName;
                hub.Save();
                hub.Publish();
            }));
        }
        s.Children.Add(buttons);

        var auto = new CheckBox { Content = new TextBlock { Text = T("Use a game's BigButt while the game is running, and Default when it closes"), TextWrapping = TextWrapping.Wrap }, IsChecked = Settings.AutoSwitch, Margin = new Thickness(0, 6, 0, 0) };
        auto.Click += (_, _) => { Settings.AutoSwitch = auto.IsChecked == true; hub.Save(); };
        s.Children.Add(auto);

        if (active != null)
        {
            // the one in use: its ButtScreens (in order) and its games
            var title = new TextBlock { Text = active.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2) };
            s.Children.Add(title);
            s.Children.Add(new TextBlock { Text = T("Its ButtScreens, in the order devices show them"), Margin = new Thickness(0, 6, 0, 2) });
            var inIt = active.Screens.Select(id => hub.Screens.FirstOrDefault(x => x.Id == id)).OfType<ButtScreen>().ToList();
            foreach (var x in inIt.Concat(hub.Screens.Where(x => !inIt.Contains(x))))
            {
                var screen = x;
                int index = active.Screens.IndexOf(screen.Id);
                var line = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
                var tools = new StackPanel { Orientation = Orientation.Horizontal };
                if (index >= 0)
                {
                    tools.Children.Add(IconButton("", T("Move up"), index > 0, () => { Swap(active, index, index - 1); }));
                    tools.Children.Add(IconButton("", T("Move down"), index < active.Screens.Count - 1, () => { Swap(active, index, index + 1); }));
                }
                DockPanel.SetDock(tools, Dock.Right);
                line.Children.Add(tools);
                var check = new CheckBox { Content = screen.Name, IsChecked = index >= 0, VerticalAlignment = VerticalAlignment.Center };
                check.Click += (_, _) =>
                {
                    if (check.IsChecked == true) active.Screens.Add(screen.Id); else active.Screens.Remove(screen.Id);
                    hub.Save();
                    hub.Publish();
                };
                line.Children.Add(check);
                s.Children.Add(line);
            }
            if (hub.Screens.Count == 0) s.Children.Add(Secondary(T("No ButtScreens yet: make one with New ButtScreen.")));

            s.Children.Add(new TextBlock { Text = T("Games"), Margin = new Thickness(0, 12, 0, 2) });
            if (active == hub.Default)
                s.Children.Add(Secondary(T("Default is used while none of the other BigButts' games is running.")));
            else
            {
                foreach (var name in active.Programs.ToList())
                {
                    var line = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
                    string n = name;
                    var take = IconButton("", F("Take {0} off this BigButt", n), true, () => { active.Programs.Remove(n); hub.Save(); hub.Publish(); });
                    DockPanel.SetDock(take, Dock.Right);
                    line.Children.Add(take);
                    line.Children.Add(new TextBlock { Text = n, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
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
                s.Children.Add(Secondary(T("Each game's program name (e.g. forza_x64.exe). While one of them runs, devices show this BigButt.")));
            }
        }
        return Card(s);
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
        tools.Children.Add(SmallButton(T("Delete"), () =>
        {
            if (MessageBox.Show(Window.GetWindow(this), F("Delete the ButtScreen {0} and its {1} buttons?", s.Name, s.Buttons.Count), T("Button2Key Remote"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                hub.Delete(s);
        }));
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
