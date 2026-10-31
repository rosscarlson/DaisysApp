using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>The AmbiLight tab: on / off and Test; the lights (Find, by address, Remove), the shown light's segments
/// and zones, the picture's settings (the SimEffects profile in use's); and the displays with that light's zones on them (<see cref="ZoneEditor"/>).</summary>
internal sealed class AmbiLightView : UserControl
{
    private readonly AmbiLightApplet applet;
    private readonly AmbiSettings settings;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox lightBox = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Ellipse dot = SimUi.Dot();
    private readonly TextBlock lightState = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel foundPanel = new();
    private readonly ContentControl segmentsCard = new(), pictureCard = new();
    private readonly ZoneEditor editor;
    private readonly TextBlock zoneInfo = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Windows.Controls.Primitives.ToggleButton onOff;
    private readonly Button onOffKey = VolumeControl.IconButton("");
    private int ticks;

    public AmbiLightView(AmbiLightApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        onOff = SimUi.OnOff(T("AmbiLight on"), settings.Enabled, applet.SetEnabled);
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

        var left = new StackPanel();
        left.Children.Add(SimUi.Card(LightsPanel()));
        left.Children.Add(segmentsCard);
        left.Children.Add(pictureCard);
        ShowPicture();

        editor = new ZoneEditor(applet) { Margin = new Thickness(0, 4, 0, 0) };
        editor.Picked += ShowZoneInfo;
        var displays = new StackPanel();
        displays.Children.Add(Ui.Header(T("Displays")));
        displays.Children.Add(editor);
        displays.Children.Add(zoneInfo);
        var right = new StackPanel();
        right.Children.Add(SimUi.Card(displays));

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(SimUi.Columns(left, right));
        Content = page;

        FillLights();
        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            applet.Previewing = IsVisible;
            if (IsVisible) timer.Start(); else timer.Stop();
        };
    }

    public void Stop() => timer.Stop();

    /// <summary>On or off changed (here, or by its shortcut).</summary>
    public void ShowOnOff()
    {
        SimUi.ShowOnOff(onOff, settings.Enabled);
    }

    /// <summary>The Picture card, drawn again (another SimEffects profile's settings).</summary>
    public void ShowPicture() => pictureCard.Content = SimUi.Card(PicturePanel());

    private void ShowKey()
    {
        ((TextBlock)onOffKey.Content).SetResourceReference(TextBlock.ForegroundProperty, applet.Hotkey.Value != null ? "AccentBrush" : "TextBrush");
        onOffKey.ToolTip = applet.Hotkey.Value is { } k ? F("On / off shortcut: {0}", k) : T("On / off shortcut");
    }

    private AmbiLight? Light => lightBox.SelectedItem as AmbiLight;

    private StackPanel LightsPanel()
    {
        var p = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(dot);
        head.Children.Add(Ui.Header(T("Lights")));
        p.Children.Add(head);
        lightBox.DisplayMemberPath = nameof(AmbiLight.Describe);
        AutomationProperties.SetName(lightBox, T("Light"));
        lightBox.SelectionChanged += (_, _) =>
        {
            settings.Shown = Light?.Ip;
            ShowLight();
        };
        p.Children.Add(lightBox);
        lightState.Margin = new Thickness(0, 6, 0, 0);
        p.Children.Add(lightState);

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Button? find = null;
        find = SimUi.Button(T("Find"), async () =>
        {
            find!.IsEnabled = false;
            foundPanel.Children.Clear();
            foundPanel.Children.Add(Ui.Note(T("Looking…")));
            var found = await applet.Lan.ScanAsync();
            find.IsEnabled = true;
            ShowFound(found);
        }, accent: true, tip: T("Govee lights with LAN Control on"));
        buttons.Children.Add(find);
        buttons.Children.Add(SimUi.Button(T("Test"), () => { if (Light is { } l) applet.Test(l); }, tip: T("All red for 2 s")));
        buttons.Children.Add(SimUi.Confirm(T("Remove"), () =>
        {
            if (Light is not { } l) return;
            settings.Lights.Remove(l);
            applet.Removed(l);
            FillLights();
        }));
        p.Children.Add(buttons);
        p.Children.Add(foundPanel);

        var address = new TextBox { Width = 140, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(address, T("Address"));
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        addRow.Children.Add(new TextBlock { Text = T("Address"), Width = 70, VerticalAlignment = VerticalAlignment.Center });
        addRow.Children.Add(address);
        var add = SimUi.Button(T("Add"), () =>
        {
            string ip = address.Text.Trim();
            if (!System.Net.IPAddress.TryParse(ip, out _)) return;
            Add(new GoveeFound(ip, "", ""));
            address.Text = "";
        });
        add.Margin = new Thickness(6, 0, 0, 0);
        addRow.Children.Add(add);
        p.Children.Add(addRow);
        return p;
    }

    private void ShowFound(List<GoveeFound> found)
    {
        foundPanel.Children.Clear();
        if (applet.Lan.Problem is { } problem) { foundPanel.Children.Add(Ui.Note(problem)); return; }
        var fresh = found.Where(f => !settings.Lights.Any(l => l.Ip == f.Ip || (!string.IsNullOrEmpty(f.Mac) && l.Mac == f.Mac))).ToList();
        if (found.Count == 0) foundPanel.Children.Add(Ui.Note(T("None answered")));
        else if (fresh.Count == 0) foundPanel.Children.Add(Ui.Note(T("All added")));
        foreach (var f in fresh)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var add = SimUi.Button(T("Add"), () => Add(f), accent: true);
            var test = SimUi.Button(T("Test"), () => _ = applet.TestFoundAsync(f), tip: T("All red for 2 s"));
            AutomationProperties.SetName(add, F("Add {0}", f.Ip));
            AutomationProperties.SetName(test, F("Test {0}", f.Ip));
            DockPanel.SetDock(add, Dock.Right);
            DockPanel.SetDock(test, Dock.Right);
            row.Children.Add(add);
            row.Children.Add(test);
            row.Children.Add(new TextBlock { Text = $"{GoveeModels.Name(f.Sku)} · {f.Ip}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = f.Sku });
            foundPanel.Children.Add(row);
        }
    }

    private void Add(GoveeFound f)
    {
        foundPanel.Children.Clear();
        var light = settings.Lights.FirstOrDefault(l => l.Ip == f.Ip);
        if (light == null)
        {
            light = new AmbiLight { Ip = f.Ip, Mac = string.IsNullOrEmpty(f.Mac) ? null : f.Mac, Sku = string.IsNullOrEmpty(f.Sku) ? null : f.Sku, Name = GoveeModels.Name(f.Sku) };
            applet.EnsureZones(light);
            settings.Lights.Add(light);
            Logging.Log.Here.Info($"AmbiLight: added {light.Describe}");
        }
        settings.Shown = light.Ip;
        applet.Changed(light);
        FillLights();
    }

    private void FillLights()
    {
        lightBox.ItemsSource = null;
        lightBox.ItemsSource = settings.Lights;
        lightBox.SelectedItem = settings.Lights.FirstOrDefault(l => l.Ip == settings.Shown) ?? settings.Lights.FirstOrDefault();
        ShowLight();
    }

    /// <summary>The shown light's card, and its zones on the displays.</summary>
    private void ShowLight()
    {
        var light = Light;
        editor.Light = light;
        zoneInfo.Text = "";
        segmentsCard.Content = light == null ? null : SimUi.Card(SegmentsPanel(light));
        UpdateState();
    }

    private StackPanel SegmentsPanel(AmbiLight light)
    {
        var p = new StackPanel();
        var shows = new StackPanel { Orientation = Orientation.Horizontal };
        shows.Children.Add(SimUi.Button(T("Show Segments"), () => applet.ShowSegments(light), tip: T("10 s: the first white, then red, green, blue")));
        var showLeds = SimUi.Button(T("Show LEDs"), () => applet.ShowLeds(light), tip: T("10 s: each LED sent white, then red, green, blue"));
        showLeds.Margin = new Thickness(6, 0, 0, 0);
        shows.Children.Add(showLeds);
        var segmentsHead = Ui.HeaderRow(T("Segments"), shows);
        p.Children.Add(segmentsHead);
        var name = new TextBox { Text = light.Name, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(name, T("Name"));
        name.LostFocus += (_, _) =>
        {
            string n = name.Text.Trim();
            if (n.Length == 0 || n == light.Name) { name.Text = light.Name; return; }
            light.Name = n;
            applet.Changed();
            lightBox.Items.Refresh();
        };
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
        p.Children.Add(SimUi.Field(T("Name"), name));
        var zones = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        void Counted()
        {
            zones.Text = P(light.ZoneCount, "{0} zone", "{0} zones");
            applet.EnsureZones(light);
            applet.Changed(light);
            editor.Rebuild();
        }
        TextBox? leds = null;
        var segments = SimUi.Number(light.Segments, 1, 255, n =>
        {
            light.Segments = n;
            if (light.Leds == 0 && leds != null) leds.Text = n.ToString(); // LEDs follow the segments until set
            Counted();
        }, 60);
        AutomationProperties.SetName(segments, T("Segments"));
        segments.ToolTip = T("As many as the light has (Test shows them)");
        p.Children.Add(SimUi.Field(T("Segments"), segments));
        leds = SimUi.Number(light.Leds > 0 ? light.Leds : light.Segments, 1, 255, n =>
        {
            int was = light.Leds;
            light.Leds = n == light.Segments ? 0 : n;
            if (light.Leds != was) applet.Changed(light);
        }, 60);
        AutomationProperties.SetName(leds, T("LEDs"));
        leds.ToolTip = T("Colours sent along the light, each from its own spot (with Stretch); the segments to begin with, more if the light takes them");
        p.Children.Add(SimUi.Field(T("LEDs"), leds));
        var perZone = SimUi.Number(light.PerZone, 1, 255, n => { light.PerZone = n; Counted(); }, 60);
        AutomationProperties.SetName(perZone, T("Per Zone"));
        perZone.ToolTip = T("Segments that share a zone, each taking its own part of it");
        var perRow = new StackPanel { Orientation = Orientation.Horizontal };
        perRow.Children.Add(perZone);
        perRow.Children.Add(zones);
        p.Children.Add(SimUi.Field(T("Per Zone"), perRow));
        zones.Text = P(light.ZoneCount, "{0} zone", "{0} zones");

        var stretch = new CheckBox { Content = T("Stretch"), IsChecked = light.Stretch, Margin = new Thickness(0, 8, 0, 0), ToolTip = T("The LEDs sent spread over the whole light (off: one colour per segment)") };
        stretch.Click += (_, _) => { light.Stretch = stretch.IsChecked == true; applet.Changed(); };
        var reverse = new CheckBox { Content = T("Reverse"), IsChecked = light.Reverse, Margin = new Thickness(16, 8, 0, 0), ToolTip = T("The first segment is at the other end") };
        reverse.Click += (_, _) => { light.Reverse = reverse.IsChecked == true; applet.Changed(); editor.Rebuild(); };
        var checks = new StackPanel { Orientation = Orientation.Horizontal };
        checks.Children.Add(stretch);
        checks.Children.Add(reverse);
        p.Children.Add(checks);
        p.Children.Add(SimUi.SliderRow(T("Brightness"), 1, 100, 1, light.Brightness, v => $"{v:0}%", v => { light.Brightness = (int)v; applet.Changed(); }));

        var layout = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 6, 0) };
        var names = new[] { (AmbiLayout.Top, T("Top")), (AmbiLayout.Bottom, T("Bottom")), (AmbiLayout.Sides, T("Sides and Top")), (AmbiLayout.Around, T("All Around")) };
        foreach (var (_, title) in names) layout.Items.Add(title);
        layout.SelectedIndex = Array.FindIndex(names, n => n.Item1 == light.Layout);
        AutomationProperties.SetName(layout, T("Layout"));
        layout.ToolTip = T("Left to right, or clockwise from the bottom left");
        var layOut = SimUi.Confirm(T("Lay Out"), () =>
        {
            if (layout.SelectedIndex >= 0) light.Layout = names[layout.SelectedIndex].Item1;
            applet.EnsureZones(light, again: true);
            applet.Changed(light);
            editor.Rebuild();
        }, T("Places every zone again"));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(layout);
        row.Children.Add(layOut);
        p.Children.Add(SimUi.Field(T("Layout"), row));
        return p;
    }

    private StackPanel PicturePanel()
    {
        var p = new StackPanel();
        p.Children.Add(Ui.Header(T("Picture")));
        p.Children.Add(SimUi.SliderRow(T("Frame Rate"), 10, 60, 5, settings.Fps, v => $"{v:0}/s", v => { settings.Fps = (int)v; settings.Save(); }));
        p.Children.Add(SimUi.SliderRow(T("Smoothing"), 0, 90, 5, settings.Smoothing * 100, v => $"{v:0}%", v => { settings.Smoothing = v / 100; settings.Save(); }, T("Fades from colour to colour")));
        p.Children.Add(SimUi.SliderRow(T("Saturation"), 50, 250, 10, settings.Saturation * 100, v => $"{v:0}%", v => { settings.Saturation = v / 100; settings.Save(); }));
        p.Children.Add(SimUi.SliderRow(T("Black Level"), 0, 60, 2, settings.BlackLevel, v => $"{v:0}", v => { settings.BlackLevel = (int)v; settings.Save(); }, T("Darker than this is off")));
        return p;
    }

    private void ShowZoneInfo(int zone)
    {
        if (Light is not { } light || zone < 0) { zoneInfo.Text = ""; return; }
        var segs = Enumerable.Range(0, light.Segments).Where(s => light.ZoneOf(s) == zone).Select(s => s + 1).ToList();
        string which = segs.Count == 0 ? T("no segments") : segs.Count == 1 ? F("segment {0}", segs[0]) : F("segments {0}–{1}", segs.Min(), segs.Max());
        var z = light.Zones[zone];
        int leds = Enumerable.Range(0, light.LedCount).Count(i => light.ZoneOfLed(i) == zone);
        zoneInfo.Text = F("Zone {0}: {1}", zone + 1, which) + " · " + P(leds, "{0} LED", "{0} LEDs") + $" · {(int)z.W}×{(int)z.H} px";
    }

    private void UpdateState()
    {
        var light = Light;
        if (light == null)
        {
            SimUi.Light(dot, Arduino.BoardState.Off);
            lightState.Text = settings.Lights.Count == 0 ? T("None yet") : "";
            return;
        }
        bool streaming = applet.Streaming(light), answered = applet.Lan.Answered(light.Ip);
        SimUi.Light(dot, !streaming ? Arduino.BoardState.Off : answered ? Arduino.BoardState.Connected : Arduino.BoardState.Connecting);
        lightState.Text = !streaming ? light.Ip
            : answered ? F("{0}: answering", light.Ip)
            : applet.Lan.Problem ?? F("{0}: no answer yet (LAN Control on?)", light.Ip);
    }

    private void Tick()
    {
        editor.Tick(++ticks % 3 == 0);
        if (ticks % 10 != 0) return;
        UpdateState();
        status.Text = settings.On || applet.Testing
            ? F("{0} lights · {1:0} pictures a second", settings.Lights.Count, applet.Rate) + (applet.Testing ? " · " + T("Testing") : "")
            : "";
        status.SetResourceReference(TextBlock.ForegroundProperty, settings.On ? "SuccessBrush" : "TextSecondaryBrush");
    }
}

/// <summary>
/// The displays as they're placed (their pictures live), with the shown light's zones on them, numbered and in their
/// colours now: drag a zone to move it, its edges or corners to size it.
/// </summary>
internal sealed class ZoneEditor : Canvas
{
    private const double Grip = 6;
    private readonly AmbiLightApplet applet;
    private readonly List<(Image Image, WriteableBitmap? Bitmap, Border Frame, int Number)> screens = new();
    private readonly List<Border> zones = new();
    // each LED's slice of its zone (what it's coloured from), in its colour; none for a zone of one LED
    private readonly List<(Rectangle Shape, int Led)> slices = new();
    private bool[] sliced = Array.Empty<bool>();
    private AmbiLight? light;
    private ScreenRect desktop;
    private double scale = 1, ox, oy;
    private int dragging = -1, picked = -1;
    private (bool L, bool T, bool R, bool B) edges;
    private Point from;
    private AmbiZone? start;

    public event Action<int>? Picked;

    public ZoneEditor(AmbiLightApplet applet)
    {
        this.applet = applet;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Height = 200;
        SizeChanged += (_, e) => { if (e.WidthChanged) Rebuild(); };
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => EndDrag();
    }

    public AmbiLight? Light
    {
        get => light;
        set { light = value; picked = -1; Rebuild(); }
    }

    /// <summary>Places the displays and zones again (size, displays or zone count changed).</summary>
    public void Rebuild()
    {
        Children.Clear();
        screens.Clear();
        zones.Clear();
        slices.Clear();
        double width = ActualWidth;
        if (width <= 0) return;
        var displays = applet.Sampler.Displays;
        desktop = applet.Sampler.Desktop;
        scale = width / Math.Max(1, desktop.W);
        if (desktop.H * scale > 520) scale = 520.0 / desktop.H;
        Height = Math.Max(120, desktop.H * scale);
        ox = (width - desktop.W * scale) / 2;
        oy = (Height - desktop.H * scale) / 2;
        foreach (var (number, b, name) in displays)
        {
            bool used = !applet.Sampler.Off.Contains(name);
            var image = new Image { Width = b.W * scale, Height = b.H * scale, Stretch = Stretch.Fill, Opacity = used ? 1 : 0.25 };
            Place(image, b.X, b.Y);
            Children.Add(image);
            var frame = new Border { Width = b.W * scale, Height = b.H * scale, BorderThickness = new Thickness(1), IsHitTestVisible = false };
            frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderStrongBrush");
            var label = new TextBlock { Text = number.ToString(), FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(6, 4, 0, 0), Foreground = Brushes.White, Opacity = 0.7 };
            frame.Child = label;
            Place(frame, b.X, b.Y);
            Children.Add(frame);
            screens.Add((image, null, frame, number));

            // a tick in its middle: the lights take colours only from ticked displays (above the zones, clicks around
            // it go through to them)
            var tick = new CheckBox { Content = F("Display {0}", number), IsChecked = used, ToolTip = T("Used by the lights") };
            tick.Click += (_, _) =>
            {
                applet.SetDisplay(name, tick.IsChecked == true);
                Rebuild();
            };
            var chip = new Border { Child = tick, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            chip.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            var middle = new Grid { Width = b.W * scale, Height = b.H * scale };
            middle.Children.Add(chip);
            Place(middle, b.X, b.Y);
            SetZIndex(middle, 3);
            Children.Add(middle);
        }
        if (light == null) return;
        for (int i = 0; i < light.Zones.Count; i++)
        {
            int index = i;
            var z = new Border { BorderThickness = new Thickness(1.5), BorderBrush = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), Cursor = Cursors.SizeAll };
            z.Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(z, F("Zone {0}", i + 1));
            z.MouseMove += (_, e) => { if (dragging < 0) z.Cursor = CursorFor(EdgesAt(z, e.GetPosition(z))); };
            z.MouseLeftButtonDown += (_, e) => BeginDrag(index, z, e);
            zones.Add(z);
            Children.Add(z);
        }
        PlaceZones();
        Tick(true);
    }

    private void Place(UIElement e, double x, double y)
    {
        SetLeft(e, ox + (x - desktop.X) * scale);
        SetTop(e, oy + (y - desktop.Y) * scale);
    }

    private void PlaceZones()
    {
        if (light == null) return;
        for (int i = 0; i < zones.Count && i < light.Zones.Count; i++)
        {
            var z = light.Zones[i];
            zones[i].Width = Math.Max(4, z.W * scale);
            zones[i].Height = Math.Max(4, z.H * scale);
            Place(zones[i], z.X, z.Y);
            zones[i].BorderThickness = new Thickness(i == picked ? 3 : 1.5);
            SetZIndex(zones[i], i == picked ? 2 : 1);
        }
        PlaceSlices();
    }

    /// <summary>The LEDs' slices under the zones (the zones' own fill is see-through where they're cut).</summary>
    private void PlaceSlices()
    {
        if (light == null) return;
        var parts = AmbiLightApplet.Parts(light, light.Zones.ToArray());
        while (slices.Count > parts.Length) { Children.Remove(slices[^1].Shape); slices.RemoveAt(slices.Count - 1); }
        while (slices.Count < parts.Length)
        {
            var r = new Rectangle { IsHitTestVisible = false, StrokeThickness = 0.5, Stroke = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), Fill = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)) };
            SetZIndex(r, 0);
            slices.Add((r, slices.Count));
            Children.Add(r);
        }
        sliced = new bool[light.Zones.Count];
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            int z = Math.Min(light.Zones.Count - 1, light.ZoneOfLed(i));
            bool own = part != null && z >= 0 && part != light.Zones[z];
            var r = slices[i].Shape;
            r.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
            if (!own) continue;
            sliced[z] = true;
            r.Width = Math.Max(1, part!.W * scale);
            r.Height = Math.Max(1, part.H * scale);
            Place(r, part.X, part.Y);
        }
    }

    /// <summary>The zones' colours now; the displays' pictures too when <paramref name="pictures"/>.</summary>
    public void Tick(bool pictures)
    {
        if (light != null && light.Zones.Count != zones.Count) { Rebuild(); return; }
        if (pictures)
            foreach (var t in applet.Sampler.Thumbs())
            {
                int i = screens.FindIndex(s => s.Number == t.Number);
                if (i < 0) continue;
                var s = screens[i];
                var bitmap = s.Bitmap;
                if (bitmap == null || bitmap.PixelWidth != t.Width || bitmap.PixelHeight != t.Height)
                {
                    bitmap = new WriteableBitmap(t.Width, t.Height, 96, 96, PixelFormats.Bgr32, null);
                    s.Image.Source = bitmap;
                    screens[i] = (s.Image, bitmap, s.Frame, s.Number);
                }
                bitmap.WritePixels(new Int32Rect(0, 0, t.Width, t.Height), t.Pixels, t.Width * 4, 0);
            }
        if (light == null) return;
        if (slices.Count != light.LedCount) PlaceSlices();
        if (applet.LedColours(light) is { } each)
            for (int i = 0; i < slices.Count && i < each.Length; i++)
                if (slices[i].Shape.Fill is SolidColorBrush f && !f.IsFrozen) f.Color = Color.FromArgb(220, each[i].R, each[i].G, each[i].B);
        var colours = applet.ZoneColours(light);
        if (colours == null) return;
        for (int i = 0; i < zones.Count && i < colours.Length; i++)
        {
            var c = colours[i];
            bool cut = i < sliced.Length && sliced[i];
            if (zones[i].Background is SolidColorBrush b && !b.IsFrozen) b.Color = Color.FromArgb((byte)(cut ? 1 : 220), c.R, c.G, c.B);
            if (zones[i].Child is TextBlock t) t.Foreground = c.R * 0.3 + c.G * 0.59 + c.B * 0.11 > 150 ? Brushes.Black : Brushes.White;
        }
    }

    private (bool L, bool T, bool R, bool B) EdgesAt(FrameworkElement z, Point p)
    {
        double g = Math.Min(Grip, Math.Min(z.ActualWidth, z.ActualHeight) / 3);
        return (p.X < g, p.Y < g, p.X > z.ActualWidth - g, p.Y > z.ActualHeight - g);
    }

    private static Cursor CursorFor((bool L, bool T, bool R, bool B) e) =>
        (e.L || e.R) && (e.T || e.B) ? ((e.L && e.T) || (e.R && e.B) ? Cursors.SizeNWSE : Cursors.SizeNESW)
        : e.L || e.R ? Cursors.SizeWE
        : e.T || e.B ? Cursors.SizeNS
        : Cursors.SizeAll;

    private void BeginDrag(int index, Border z, MouseButtonEventArgs e)
    {
        if (light == null || index >= light.Zones.Count) return;
        dragging = picked = index;
        applet.Hold(light, index);
        edges = EdgesAt(z, e.GetPosition(z));
        from = e.GetPosition(this);
        start = light.Zones[index].Clone();
        CaptureMouse();
        PlaceZones();
        Picked?.Invoke(index);
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (dragging < 0 || start == null || light == null || dragging >= light.Zones.Count) return;
        var p = e.GetPosition(this);
        double dx = (p.X - from.X) / scale, dy = (p.Y - from.Y) / scale;
        const double min = 8;
        var z = light.Zones[dragging];
        double x = start.X, y = start.Y, w = start.W, h = start.H;
        bool move = !(edges.L || edges.T || edges.R || edges.B);
        if (move) { x += dx; y += dy; }
        if (edges.L) { double nx = Math.Min(start.X + dx, start.X + start.W - min); w = start.X + start.W - nx; x = nx; }
        if (edges.R) w = Math.Max(min, start.W + dx);
        if (edges.T) { double ny = Math.Min(start.Y + dy, start.Y + start.H - min); h = start.Y + start.H - ny; y = ny; }
        if (edges.B) h = Math.Max(min, start.H + dy);
        // kept on the displays' rectangle
        w = Math.Min(w, desktop.W);
        h = Math.Min(h, desktop.H);
        x = Math.Clamp(x, desktop.X, desktop.Right - w);
        y = Math.Clamp(y, desktop.Y, desktop.Bottom - h);
        z.X = Math.Round(x); z.Y = Math.Round(y); z.W = Math.Round(w); z.H = Math.Round(h);
        PlaceZones();
        Picked?.Invoke(dragging);
    }

    private void EndDrag()
    {
        if (dragging < 0) return;
        dragging = -1;
        if (light != null) applet.Hold(light, -1);
        start = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        applet.Changed();
    }
}
