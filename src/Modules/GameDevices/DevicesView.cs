using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Controllers;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// The tab: alerts on top while anything differs from normal; every device on the left as a block that opens to its
/// details, with its number and a green dot while it has input; the one picked on the right, its picture (which makes a
/// widget), its axes and buttons, and its Properties.
/// </summary>
internal sealed class DevicesView : Grid
{
    private const string Chevron = "", ChevronUp = "", Warning = "", Pencil = "", Gear = "", PopOut = "";

    private readonly DevicesHub hub;
    private readonly DevicesSettings settings;
    private readonly StackPanel blocks = new(), right = new(), alertRows = new();
    private readonly Border alertCard;
    private readonly TextBlock noneText = Ui.Note(T("No game controllers"), 4);
    private readonly FrameworkElement widgetList;
    private readonly DeviceArt art = new() { Height = 300 };
    private readonly ControllerState reading = new();
    private readonly List<(Device Device, Ellipse Dot)> dots = new();
    private readonly List<(int Axis, ProgressBar Bar, TextBlock Value)> axisRows = new();
    private readonly List<Border> buttonCaps = new();
    private TextBlock? hatText;
    private bool[] capsDown = new bool[GameControllers.MaxButtons];

    private sealed record Block(Device Device, Border Frame);
    private readonly List<Block> shown = new();

    public DevicesView(DevicesHub hub)
    {
        this.hub = hub;
        settings = hub.Settings;
        Margin = new Thickness(16);
        widgetList = Widget.ListCard();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        ColumnDefinitions.Add(new ColumnDefinition());

        var normal = Ui.Button(T("Set as Normal"), hub.SetNormal, T("What's plugged in now, in this order, is normal: the alerts go"), accent: true);
        var title = Ui.Icon(Warning, 15);
        title.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        title.Margin = new Thickness(0, 0, 8, 0);
        var alertHead = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, Ui.Header(T("Devices changed"), new Thickness(0)) } };
        alertCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(alertHead, normal), alertRows } }, new Thickness(0, 0, 0, 12));
        alertCard.SetResourceReference(Border.BorderBrushProperty, "DangerBrush");
        alertCard.Visibility = Visibility.Collapsed;
        SetColumnSpan(alertCard, 3);
        Children.Add(alertCard);

        var refresh = GameControllers.RefreshButton(T("Look for controllers again"));
        refresh.Margin = new Thickness(0);
        var left = new StackPanel { Children = { Ui.HeaderRow(T("Devices"), refresh), noneText, blocks } };
        var leftScroll = Scroll(left);
        SetRow(leftScroll, 1);
        Children.Add(leftScroll);
        var rightScroll = Scroll(new StackPanel { Children = { Ui.Card(right), widgetList } });
        SetRow(rightScroll, 1);
        SetColumn(rightScroll, 2);
        Children.Add(rightScroll);

        Widget.Offer(art, () => { if (Selected is { } d) hub.MakeWidget(d); });
        hub.Changed += Render;
        hub.Ticked += Tick;
        IsVisibleChanged += (_, _) => hub.ViewShowing(IsVisible);
        Render();
    }

    private static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private Device? Selected => hub.Devices.FirstOrDefault(d => d.Key == settings.Selected) ?? hub.Devices.FirstOrDefault();

    /// <summary>Everything again from the list.</summary>
    private void Render()
    {
        alertCard.Visibility = hub.Alerts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        alertRows.Children.Clear();
        foreach (var a in hub.Alerts)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var when = Ui.Secondary(a.When.ToString("g"));
            when.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(when, Dock.Right);
            row.Children.Add(when);
            var line = new TextBlock { Text = a.Text, TextTrimming = TextTrimming.CharacterEllipsis };
            if (a.Order) line.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            row.Children.Add(line);
            alertRows.Children.Add(row);
        }

        noneText.Visibility = hub.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        blocks.Children.Clear();
        shown.Clear();
        dots.Clear();
        foreach (var d in hub.Devices) blocks.Children.Add(MakeBlock(d));
        ShowSelected();
        ShowDevice();
    }

    private void ShowSelected()
    {
        var pick = Selected;
        foreach (var b in shown)
        {
            if (b.Device.Key == pick?.Key) b.Frame.SetResourceReference(Border.BackgroundProperty, "RowSelectedBrush");
            else b.Frame.ClearValue(Border.BackgroundProperty);
        }
    }

    // ---------------------------------------------------------------- a device's block

    private FrameworkElement MakeBlock(Device d)
    {
        var alerts = hub.Alerts.Where(a => a.Key == d.Key).ToList();
        var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Green while it has input"), StrokeThickness = 1 };
        dot.SetResourceReference(Shape.StrokeProperty, "ControlBorderStrongBrush");
        dots.Add((d, dot));

        var number = new TextBlock { Text = DevicesHub.Show(d.Number), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border { Width = 26, Height = 24, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 9, 0), Child = number,
            ToolTip = d.Number is >= 1 and <= 4 ? T("Joystick number: older games use the first four") : T("Joystick number") };
        chip.SetResourceReference(Border.BackgroundProperty, d.Number is >= 1 and <= 4 ? "AccentBrush" : "ControlBrush");
        if (d.Number is >= 1 and <= 4) number.SetResourceReference(TextBlock.ForegroundProperty, "AccentForegroundBrush");

        var nameText = new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var head = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(chip, Dock.Left);
        head.Children.Add(dot);
        head.Children.Add(chip);
        if (alerts.Count > 0)
        {
            var warn = Ui.Icon(Warning, 13);
            warn.Margin = new Thickness(6, 0, 0, 0);
            warn.ToolTip = string.Join("\n", alerts.Select(a => a.Text));
            warn.SetResourceReference(TextBlock.ForegroundProperty, alerts.Any(a => a.Order) ? "ErrorTextBrush" : "TextSecondaryBrush");
            DockPanel.SetDock(warn, Dock.Right);
            head.Children.Add(warn);
        }
        head.Children.Add(nameText);

        var pick = new Button { Content = head, Template = Ui.FlatRow, Padding = new Thickness(6), Cursor = Cursors.Hand, Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = T("Double-click to rename") };
        AutomationProperties.SetName(pick, d.Name);
        pick.Click += (_, _) =>
        {
            settings.Selected = d.Key;
            settings.Save();
            ShowSelected();
            ShowDevice();
        };

        var edit = new TextBox { Text = d.Name, Visibility = Visibility.Collapsed, Margin = new Thickness(6, 6, 6, 6) };
        AutomationProperties.SetName(edit, T("Name"));
        void Editing(bool on)
        {
            pick.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            edit.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            edit.Text = d.Name;
            edit.Focus();
            edit.SelectAll();
        }
        bool done = false;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            if (keep && edit.Text.Trim() != d.Name) hub.Rename(d, edit.Text);   // (draws every block again)
            else Editing(false);
            done = false;
        }
        pick.MouseDoubleClick += (_, e) => { e.Handled = true; Editing(true); };
        edit.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape) { e.Handled = true; Finish(e.Key == Key.Enter); }
        };
        edit.LostKeyboardFocus += (_, _) => { if (edit.IsVisible) Finish(true); };

        var details = new ContentControl { Visibility = settings.Expanded.Contains(d.Key) ? Visibility.Visible : Visibility.Collapsed };
        if (details.Visibility == Visibility.Visible) details.Content = Details(d, () => Editing(true));
        var fold = Ui.IconButton(settings.Expanded.Contains(d.Key) ? ChevronUp : Chevron, "", T("Details"));
        fold.Padding = new Thickness(6, 4, 6, 4);
        fold.VerticalAlignment = VerticalAlignment.Center;
        fold.Margin = new Thickness(0, 0, 4, 0);
        AutomationProperties.SetName(fold, F("Details of {0}", d.Name));
        fold.Click += (_, _) =>
        {
            bool open = settings.Expanded.Add(d.Key);
            if (!open) settings.Expanded.Remove(d.Key);
            settings.Save();
            details.Content = open ? Details(d, () => Editing(true)) : null;
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)((StackPanel)fold.Content).Children[0]).Text = open ? ChevronUp : Chevron;
        };

        var top = new DockPanel();
        DockPanel.SetDock(fold, Dock.Right);
        top.Children.Add(fold);
        top.Children.Add(new Grid { Children = { pick, edit } });
        var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(2), Margin = new Thickness(0, 0, 0, 6),
            Child = new StackPanel { Children = { top, details } }, Opacity = d.Connected ? 1 : 0.6 };
        frame.SetResourceReference(Border.BorderBrushProperty, alerts.Any(a => a.Order) ? "DangerBrush" : "CardBorderBrush");
        shown.Add(new Block(d, frame));
        return frame;
    }

    /// <summary>The block opened: the type at the top right, then what Windows says about the device.</summary>
    private FrameworkElement Details(Device d, Action rename)
    {
        var items = new List<(string, string)> { (F("Auto: {0}", DeviceKinds.Name(d.Detected)), "") };
        items.AddRange(DeviceKinds.All.Select(k => (DeviceKinds.Name(k), k.ToString())));
        var type = Ui.Choice(items, d.Known.Kind?.ToString() ?? "", v =>
        {
            d.Known.Kind = Enum.TryParse<DeviceKind>(v, out var k) ? k : null;
            d.Known.Roles.Clear();
            hub.Saved(d);
            if (d.Key == Selected?.Key) ShowDevice();
        }, 180);
        AutomationProperties.SetName(type, F("Type of {0}", d.Name));
        var renameButton = Ui.IconButton(Pencil, "", T("Rename"), rename);
        renameButton.Padding = new Thickness(6, 4, 6, 4);
        var header = Ui.Header(T("Details"), new Thickness(0));
        var s = new StackPanel { Margin = new Thickness(8, 6, 8, 8), Children = { Ui.HeaderRow(header, renameButton, type) } };
        void Line(string label, string value)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var v = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value };
            SetColumn(v, 1);
            g.Children.Add(Ui.Secondary(label));
            g.Children.Add(v);
            g.Margin = new Thickness(0, 1, 0, 1);
            s.Children.Add(g);
        }
        Line(T("Number"), DevicesHub.Show(d.Number));
        Line(T("DirectInput order"), d.Position.ToString());
        if (settings.Normal.FirstOrDefault(n => n.Key == d.Key) is { } normal) Line(T("Normal"), F("Number {0}, order {1}", DevicesHub.Show(normal.Number), normal.Position));
        Line(T("Windows' name"), d.Known.WindowsName);
        Line(T("VID:PID"), $"{d.Known.Vid:X4}:{d.Known.Pid:X4}");
        if (d.Info is not { } c)
        {
            Line(T("State"), T("Not connected"));
            return s;
        }
        Line(T("Joystick API"), d.InJoystickApi ? T("Yes") : T("No: older games can't see it"));
        var axes = Enumerable.Range(0, 8).Where(a => c.HasAxis[a]).Select(DeviceKinds.AxisShort).ToList();
        Line(T("Axes"), axes.Count == 0 ? T("None") : $"{axes.Count}: {string.Join(" ", axes)}");
        Line(T("Buttons"), c.Buttons.ToString());
        Line(T("Hats"), c.Povs.ToString());
        Line(T("Force feedback"), c.ForceFeedback ? T("Yes") : T("No"));
        if (c.VJoy) Line(T("Virtual"), "vJoy");
        return s;
    }

    // ---------------------------------------------------------------- the device picked

    private void ShowDevice()
    {
        right.Children.Clear();
        axisRows.Clear();
        buttonCaps.Clear();
        hatText = null;
        var d = Selected;
        if (d == null)
        {
            right.Children.Add(Ui.Header(T("Device")));
            right.Children.Add(Ui.Note(T("Pick a device")));
            return;
        }
        var make = Ui.IconButton(PopOut, "", T("Make Widget"), () => hub.MakeWidget(d));
        var properties = Ui.IconButton(Gear, "", T("Properties"), () =>
        {
            if (d.Info is not { } c) return;
            int occurrence = hub.Devices.Where(x => x.Info?.Name == c.Name).OrderBy(x => x.Position).ToList().FindIndex(x => x.Key == d.Key);
            _ = JoyProperties.Open(c.Name, Math.Max(0, occurrence));
        });
        foreach (var b in new[] { make, properties }) b.Padding = new Thickness(8, 5, 8, 5);
        make.IsEnabled = true;
        properties.IsEnabled = d.Connected;
        var name = Ui.Header(d.Name, new Thickness(0));
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        right.Children.Add(Ui.HeaderRow(name, make, properties));
        if (!d.Connected) right.Children.Add(Ui.Note(T("Not connected"), new Thickness(0, 0, 0, 6)));
        // (the picture is one element, moved in here again; its own widget has one of its own)
        art.Show(d, hub);
        right.Children.Add(art);
        if (d.Info is not { } c) return;

        var roles = DeviceKinds.Roles(d.Kind);
        var assigned = d.Roles;
        foreach (int axis in Enumerable.Range(0, 8).Where(a => c.HasAxis[a]))
            right.Children.Add(AxisRow(d, axis, roles, assigned));
        if (d.Kind is DeviceKind.HShifter or DeviceKind.SeqShifter or DeviceKind.AllInOne && c.Buttons > 1)
        {
            var gear = Ui.Choice(Enumerable.Range(1, c.Buttons).Select(b => (b.ToString(), b - 1)), d.FirstGear, v => { d.Known.FirstGear = v; hub.Saved(d); }, 80);
            AutomationProperties.SetName(gear, T("First gear button"));
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var label = Ui.Secondary(d.Kind == DeviceKind.SeqShifter ? T("Up button (Down is the next)") : T("First gear button (R is seventh, 7 eighth)"));
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(gear, Dock.Left);
            row.Children.Add(gear);
            row.Children.Add(label);
            right.Children.Add(row);
        }
        if (c.Buttons > 0)
        {
            right.Children.Add(Ui.Header(T("Buttons"), new Thickness(0, 14, 0, 6)));
            var wrap = new WrapPanel();
            Array.Clear(capsDown);
            for (int i = 0; i < c.Buttons; i++)
            {
                var cap = new Border { Width = 28, Height = 24, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 3, 3),
                    Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                cap.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
                buttonCaps.Add(cap);
                wrap.Children.Add(cap);
            }
            right.Children.Add(wrap);
        }
        if (c.Povs > 0)
        {
            hatText = Ui.Secondary();
            hatText.Margin = new Thickness(0, 8, 0, 0);
            right.Children.Add(hatText);
        }
    }

    private UIElement AxisRow(Device d, int axis, Role[] roles, Dictionary<string, int> assigned)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Ui.Secondary(DeviceKinds.AxisName(axis));
        name.VerticalAlignment = VerticalAlignment.Center;
        var bar = Ui.Bar(10);
        bar.Maximum = 100;
        bar.VerticalAlignment = VerticalAlignment.Center;
        var value = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(bar, 1);
        SetColumn(value, 2);
        g.Children.Add(name);
        g.Children.Add(bar);
        g.Children.Add(value);
        axisRows.Add((axis, bar, value));
        if (roles.Length == 0) return g;

        string current = assigned.FirstOrDefault(p => p.Value == axis).Key ?? "";
        var items = new List<(string, string)> { (T("Not used"), "") };
        items.AddRange(roles.Select(r => (r.Name, r.Id)));
        var box = Ui.Choice(items, current, id =>
        {
            // this axis does it now: what was on it or doing it moves off, and every other role stays as it shows
            var now = d.Roles;
            foreach (var (role, a) in now) d.Known.Roles[role] = a;
            foreach (var (role, a) in now) if (a == axis) d.Known.Roles[role] = -1;
            if (id != "") d.Known.Roles[id] = axis;
            hub.Saved(d);
            Dispatcher.BeginInvoke(ShowDevice);
        }, 160);
        box.Margin = new Thickness(10, 0, 0, 0);
        AutomationProperties.SetName(box, F("{0} does", DeviceKinds.AxisName(axis)));
        var invert = new CheckBox { Content = T("Invert"), IsChecked = d.Known.Inverted.Contains(axis), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), ToolTip = T("Draw it the other way up") };
        AutomationProperties.SetName(invert, F("Invert {0}", DeviceKinds.AxisName(axis)));
        Ui.OnToggle(invert, on =>
        {
            if (on) d.Known.Inverted.Add(axis);
            else d.Known.Inverted.Remove(axis);
            hub.Saved(d);
        });
        SetColumn(box, 3);
        SetColumn(invert, 4);
        g.Children.Add(box);
        g.Children.Add(invert);
        return g;
    }

    // ---------------------------------------------------------------- live

    private void Tick()
    {
        if (!IsVisible) return;
        foreach (var (d, dot) in dots)
        {
            bool on = hub.Active(d);
            if (dot.Tag is true == on) continue;
            dot.Tag = on;
            if (on) dot.SetResourceReference(Shape.FillProperty, "SuccessBrush");
            else dot.ClearValue(Shape.FillProperty);
        }
        var pick = Selected;
        if (pick == null) return;
        art.Show(pick, hub);
        if (!hub.Read(pick, reading)) return;
        foreach (var (axis, bar, value) in axisRows)
        {
            double v = reading.Axes[axis];
            bar.Value = double.IsNaN(v) ? 0 : v * 100;
            value.Text = double.IsNaN(v) ? "" : $"{v * 100:0}%";
        }
        for (int i = 0; i < buttonCaps.Count; i++)
        {
            if (reading.Buttons[i] == capsDown[i]) continue;
            capsDown[i] = reading.Buttons[i];
            buttonCaps[i].SetResourceReference(Border.BackgroundProperty, capsDown[i] ? "SuccessBrush" : "ControlBrush");
        }
        if (hatText != null)
            hatText.Text = T("Hats") + ": " + string.Join("  ", reading.Povs.Take(pick.Info?.Povs ?? 0).Select(p => p < 0 ? T("Centred") : $"{p / 100}°"));
    }
}
