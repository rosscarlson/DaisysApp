using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Rgb;

/// <summary>
/// The tab: Lights ON / OFF and Find Devices at the top, then All Devices (one effect for every device), then a card
/// per device with its effect, colour and speed. A change applies at once and is kept on the device; SMBus devices
/// (the RAM) need administrator rights, so theirs wait for Apply. RAM and Older Boards finds those.
/// </summary>
internal sealed class RgbView : ScrollViewer
{
    private readonly RgbSettings settings;
    private readonly RgbHub hub;
    private readonly ToggleButton onOff;
    private readonly TextBlock status = Secondary("");
    private readonly WrapPanel cards = new();
    private readonly StackPanel smbusCard = new();
    private readonly Dictionary<string, TextBlock> problems = new();
    private string shownIds = "\0";
    private bool busy;

    private static readonly (LightEffect Effect, string Name)[] EffectNames =
    {
        (LightEffect.Static, T("Static")), (LightEffect.Breathing, T("Breathing")), (LightEffect.Flashing, T("Flashing")),
        (LightEffect.ColourCycle, T("Colour Cycle")), (LightEffect.Rainbow, T("Rainbow")), (LightEffect.Off, T("Off")),
    };

    public RgbView(RgbSettings settings, RgbHub hub)
    {
        this.settings = settings;
        this.hub = hub;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var page = new StackPanel { Margin = new Thickness(16) };
        Content = page;

        // Lights ON / OFF, Find Devices
        onOff = new ToggleButton { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        onOff.SetResourceReference(StyleProperty, "ChipToggle");
        AutomationProperties.SetName(onOff, T("Lights on"));
        onOff.Click += async (_, _) => await SetOff(onOff.IsChecked != true);
        ShowOnOff();
        var find = new Button { Content = T("Find Devices"), Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Look for lights again") };
        find.Click += (_, _) => hub.Find();
        var top = new DockPanel { LastChildFill = true };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock { Text = T("Lights"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        left.Children.Add(onOff);
        left.Children.Add(find);
        DockPanel.SetDock(left, Dock.Left);
        top.Children.Add(left);
        status.Margin = new Thickness(14, 0, 0, 0);
        top.Children.Add(status);
        page.Children.Add(Card(top));

        page.Children.Add(Card(AllRow()));
        page.Children.Add(cards);
        page.Children.Add(Card(smbusCard));

        hub.Changed += () => Dispatcher.BeginInvoke(Render);
        Render();
    }

    private void ShowOnOff()
    {
        onOff.IsChecked = !settings.Off;
        onOff.Content = settings.Off ? T("OFF") : T("ON");
    }

    /// <summary>All lights off (or back to each device's setting); the RAM too, through Windows' question.</summary>
    private async Task SetOff(bool off)
    {
        settings.Off = off;
        settings.Save();
        ShowOnOff();
        hub.Apply(hub.List);
        await ApplyAdmin(hub.List.Where(d => d.NeedsAdmin && (off || hub.SettingOf(d) != null)));
    }

    // ---------------------------------------------------------------- devices

    private void Render()
    {
        status.Text = hub.Finding ? T("Looking…") : hub.List.Count == 0 ? T("No lights found") : P(hub.List.Count, "{0} device", "{0} devices");
        string ids = string.Join("|", hub.List.Select(d => d.Id));
        if (ids != shownIds)
        {
            shownIds = ids;
            cards.Children.Clear();
            problems.Clear();
            foreach (var d in hub.List) cards.Children.Add(DeviceCard(d));
            RenderSmbus();
        }
        foreach (var (id, text) in problems)
        {
            text.Text = hub.Problems.GetValueOrDefault(id) ?? "";
            text.Visibility = text.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Border DeviceCard(IRgbDevice d)
    {
        var s = new StackPanel();
        var name = new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = d.About };
        var kind = Secondary(d.Kind switch
        {
            DeviceKind.Motherboard => T("Motherboard"),
            DeviceKind.Header => T("Fans and strips on it"),
            DeviceKind.Ram => T("RAM"),
            _ => T("Graphics card"),
        });
        kind.Margin = new Thickness(0, 0, 0, 8);
        s.Children.Add(name);
        s.Children.Add(kind);

        var setting = hub.SettingOf(d)?.Copy() ?? new LightSetting { Effect = LightEffect.Static, Colour = "#FFFFFF" };
        Button? apply = null;
        var note = Secondary(T("Drawn while Daisy's App runs"));
        note.Margin = new Thickness(0, 6, 0, 0);
        var controls = Controls(setting, Devices.Offered(d).ToList(), e => d.HasSpeed || Devices.Drawn(d, e), changed =>
        {
            settings.Lights[d.Id] = changed.Copy();
            settings.Save();
            if (d.NeedsAdmin) { if (apply != null) apply.IsEnabled = true; }
            else if (!settings.Off) hub.Apply(new[] { d });
            note.Visibility = Devices.Drawn(d, changed.Effect) ? Visibility.Visible : Visibility.Collapsed;
        }, d.Name);
        note.Visibility = hub.SettingOf(d) is { } now && Devices.Drawn(d, now.Effect) ? Visibility.Visible : Visibility.Collapsed;
        s.Children.Add(controls);
        s.Children.Add(note);
        if (d.NeedsAdmin)
        {
            apply = new Button { Content = T("Apply"), Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false, ToolTip = T("Windows asks for administrator rights") };
            apply.SetResourceReference(StyleProperty, "AccentButton");
            AutomationProperties.SetName(apply, T("Apply") + " " + d.Name);
            var a = apply;
            a.Click += async (_, _) => { if (await ApplyAdmin(new[] { d })) a.IsEnabled = false; };
            s.Children.Add(apply);
        }
        var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        problem.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        problems[d.Id] = problem;
        s.Children.Add(problem);
        var card = Card(s);
        card.Width = 360;
        card.Margin = new Thickness(0, 0, 12, 12);
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    /// <summary>The All Devices card: one effect for every device that can do it.</summary>
    private UIElement AllRow()
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Header(T("All Devices")));
        var setting = settings.All.Copy();
        var controls = Controls(setting, Devices.All.ToList(), _ => true, _ => { }, T("All Devices"));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(controls);
        var set = new Button { Content = T("Set All"), Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Every device (the RAM through Windows' question)") };
        set.SetResourceReference(StyleProperty, "AccentButton");
        set.Click += async (_, _) => await SetAll(setting);
        row.Children.Add(set);
        s.Children.Add(row);
        return s;
    }

    private async Task SetAll(LightSetting s)
    {
        settings.All = s.Copy();
        var targets = hub.List.Where(d => Devices.Offered(d).Contains(s.Effect)).ToList();
        foreach (var d in targets) settings.Lights[d.Id] = s.Copy();
        settings.Off = false;
        settings.Save();
        ShowOnOff();
        shownIds = "\0";   // the cards show the new settings
        Render();
        hub.Apply(targets);
        await ApplyAdmin(targets.Where(d => d.NeedsAdmin));
    }

    /// <summary>Effect, colour and speed, in a row. <paramref name="changed"/> after each change.</summary>
    private static Panel Controls(LightSetting s, List<LightEffect> offered, Func<LightEffect, bool> hasSpeed, Action<LightSetting> changed, string name)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var effect = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(effect, T("Effect") + " " + name);
        foreach (var (e, n) in EffectNames.Where(x => offered.Contains(x.Effect)))
            effect.Items.Add(new ComboBoxItem { Content = n, Tag = e });
        if (!offered.Contains(s.Effect)) s.Effect = offered.Contains(LightEffect.Static) ? LightEffect.Static : offered[0];
        effect.SelectedItem = effect.Items.Cast<ComboBoxItem>().First(i => (LightEffect)i.Tag == s.Effect);
        row.Children.Add(effect);

        var colour = new ColourButton(s.Colour) { Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(colour, T("Colour") + " " + name);
        row.Children.Add(colour);

        var speed = new ComboBox { Width = 90, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Speed") };
        AutomationProperties.SetName(speed, T("Speed") + " " + name);
        foreach (var (sp, n) in new[] { (Speed.Slow, T("Slow")), (Speed.Normal, T("Normal")), (Speed.Fast, T("Fast")) })
            speed.Items.Add(new ComboBoxItem { Content = n, Tag = sp });
        speed.SelectedIndex = (int)s.Speed;
        row.Children.Add(speed);

        void Show()
        {
            colour.Visibility = LightSetting.HasColour(s.Effect) ? Visibility.Visible : Visibility.Collapsed;
            speed.Visibility = LightSetting.HasSpeed(s.Effect) && hasSpeed(s.Effect) ? Visibility.Visible : Visibility.Collapsed;
        }
        Show();
        effect.SelectionChanged += (_, _) =>
        {
            if (effect.SelectedItem is not ComboBoxItem { Tag: LightEffect e }) return;
            s.Effect = e;
            Show();
            changed(s);
        };
        colour.Picked += c => { s.Colour = c; changed(s); };
        speed.SelectionChanged += (_, _) =>
        {
            if (speed.SelectedItem is not ComboBoxItem { Tag: Speed sp }) return;
            s.Speed = sp;
            changed(s);
        };
        return row;
    }

    // ---------------------------------------------------------------- RAM and older boards (SMBus)

    private void RenderSmbus()
    {
        smbusCard.Children.Clear();
        smbusCard.Children.Add(Ui.Header(T("RAM and Older Boards")));
        if (!PawnIoBus.Installed)
        {
            smbusCard.Children.Add(Secondary(T("Needs PawnIO (installed with LibreHardwareMonitor, or from pawnio.eu).")));
            var get = new Button { Content = T("Get PawnIO"), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            get.Click += (_, _) => Process.Start(new ProcessStartInfo("https://pawnio.eu") { UseShellExecute = true });
            smbusCard.Children.Add(get);
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var find = new Button { Content = T("Find RAM"), Padding = new Thickness(12, 3, 12, 3), ToolTip = T("Windows asks for administrator rights") };
        var said = Secondary(settings.Smbus.Count == 0 ? T("Not looked for yet") : string.Join(", ", settings.Smbus.Select(s => s.Name)));
        said.Margin = new Thickness(12, 0, 0, 0);
        find.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            find.IsEnabled = false;
            said.Text = T("Looking…");
            try
            {
                var r = await SmbusDevices.RunAsync(new());
                if (r == null) { said.Text = T("Not allowed"); return; }
                hub.SetSmbus(r.Devices);
                settings.Save();
                said.Text = r.Problems.Count > 0 ? string.Join("; ", r.Problems) : r.Devices.Count == 0 ? T("None found") : string.Join(", ", r.Devices.Select(s => s.Name));
            }
            finally { busy = false; find.IsEnabled = true; }
        };
        row.Children.Add(find);
        row.Children.Add(said);
        smbusCard.Children.Add(row);
    }

    /// <summary>Sets SMBus devices to what they should show, in one administrator job. False when it didn't happen.</summary>
    private async Task<bool> ApplyAdmin(IEnumerable<IRgbDevice> devices)
    {
        var apply = devices.Select(d => (d.Id, Setting: hub.Effective(d))).Where(x => x.Setting != null).ToDictionary(x => x.Id, x => x.Setting!);
        if (apply.Count == 0) return true;
        if (busy) return false;
        busy = true;
        try
        {
            status.Text = T("Waiting for Windows…");
            var r = await SmbusDevices.RunAsync(apply);
            if (r == null) { status.Text = T("RAM left as it was (not allowed)"); return false; }
            foreach (var id in apply.Keys)
            {
                if (r.Devices.Any(s => s.Id == id)) hub.Problems.TryRemove(id, out _);
                else hub.Problems[id] = r.Problems.Count > 0 ? string.Join("; ", r.Problems) : T("Not found this time");
            }
            Render();
            return r.Problems.Count == 0;
        }
        finally { busy = false; }
    }

    // ---------------------------------------------------------------- the app's styles

    private static Border Card(UIElement child) => Ui.Card(child, new Thickness(0, 0, 0, 12));

    private static TextBlock Secondary(string text) => Ui.NoteCentred(text);
}

/// <summary>A colour swatch that opens a palette of light colours and a hex box.</summary>
internal sealed class ColourButton : Button
{
    private static readonly string[] Swatches =
    {
        "#FF0000", "#FF4000", "#FF8000", "#FFC000", "#FFFF00", "#80FF00", "#00FF00", "#00FF80",
        "#00FFFF", "#0080FF", "#0000FF", "#8000FF", "#FF00FF", "#FF0080", "#FFFFFF", "#FFC080",
    };

    private readonly Border swatch = new() { Width = 40, Height = 18, CornerRadius = new CornerRadius(3) };
    private readonly Popup popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private readonly TextBox hex = new() { Width = 90, Margin = new Thickness(0, 8, 8, 0) };
    private string value;

    public event Action<string>? Picked;

    public ColourButton(string initial)
    {
        value = initial;
        Padding = new Thickness(3);
        VerticalAlignment = VerticalAlignment.Center;
        ToolTip = T("Colour");
        Content = swatch;
        Click += (_, _) => { hex.Text = value; popup.IsOpen = true; };

        var grid = new UniformGrid { Columns = 8 };
        foreach (var c in Swatches)
        {
            var b = new Button { Width = 26, Height = 22, Margin = new Thickness(2), Padding = new Thickness(0), ToolTip = c };
            b.Content = new Border { Background = Brush(c), Width = 20, Height = 16, CornerRadius = new CornerRadius(2) };
            AutomationProperties.SetName(b, c);
            string colour = c;
            b.Click += (_, _) => { Pick(colour); popup.IsOpen = false; };
            grid.Children.Add(b);
        }
        var use = new Button { Content = T("Use"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 8, 0, 0) };
        void UseHex()
        {
            if (Rgb.Parse(hex.Text) is not { } c) return;
            Pick(c.Hex);
            popup.IsOpen = false;
        }
        use.Click += (_, _) => UseHex();
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) UseHex(); };
        var panel = new Border
        {
            Padding = new Thickness(10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { grid, new StackPanel { Orientation = Orientation.Horizontal, Children = { hex, use } } } },
        };
        panel.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        popup.Child = panel;
        popup.PlacementTarget = this;
        Show();
    }

    private void Pick(string v)
    {
        value = v;
        Show();
        Picked?.Invoke(v);
    }

    private void Show() => swatch.Background = Brush(value);

    private static SolidColorBrush Brush(string hex)
    {
        var c = Rgb.Parse(hex) ?? Rgb.Black;
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
