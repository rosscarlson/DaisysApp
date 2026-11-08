using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Lights;
using DaisysApp.Applets.SimEffects.Notify;
using DaisysApp.Settings;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Wheel;

/// <summary>The wheels whose rev LEDs it drives.</summary>
public enum WheelBrand { Fanatec, Moza }

/// <summary>Saved in %APPDATA%\DaisysApp\Fanatec.json (the tool's name before 4.11.0, kept so its settings carry over).</summary>
public sealed class WheelSettings : IStripColours
{
    private const string FileName = "Fanatec";

    public bool Enabled { get; set; }

    /// <summary>On, and the SimEffects profile is playing (<see cref="DaisysApp.Applets.SimEffects.Profiles.SimProfiles.Playing"/>): what runs.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool On => Enabled && DaisysApp.Applets.SimEffects.Profiles.SimProfiles.Playing;
    public WheelBrand Brand { get; set; }
    /// <summary>LEDs light from the other end.</summary>
    public bool Reverse { get; set; }
    /// <summary>The gear on the rim's display (Fanatec).</summary>
    public bool Display { get; set; }
    // an RGB rim's colours (Moza's newer ones), as Notify's strips
    public string Low { get; set; } = "#00FF00";
    public string Mid { get; set; } = "#FFFF00";
    public string High { get; set; } = "#FF0000";
    public string Shift { get; set; } = "#0000FF";
    public bool StripFlashes { get; set; } = true;

    public static WheelSettings Load() => JsonStore.Load<WheelSettings>(FileName);

    public void Save()
    {
        JsonStore.Save(FileName, this);
        Profiles.SimProfiles.Saved(this);
    }
}

/// <summary>A wheel's rev LEDs, whichever make (<see cref="FanatecWheel"/>, <see cref="MozaWheel"/>).</summary>
internal interface IWheelLights : IDisposable
{
    /// <summary>What was found (base and rim, or the port).</summary>
    string Name { get; }
    int Leds { get; }
    /// <summary>Its LEDs take the colours sent; else they're the rim's own.</summary>
    bool Rgb { get; }
    bool HasDisplay { get; }
    /// <summary>Each LED from the first, black for off: sent when something changed (or <paramref name="force"/>);
    /// false when the wheel stopped taking it.</summary>
    bool Show((byte R, byte G, byte B)[] leds, bool force);
    bool SetDisplay(string text);
    /// <summary>The LEDs off, and the wheel left as it was found.</summary>
    void Off();
}

/// <summary>
/// SimEffects → Wheel: the shift lights on a wheel's rev LEDs, from the shared <see cref="LightsRunner"/>: a Fanatec
/// rim's (the ClubSport Steering Wheel RS's nine, and the other single-colour ones; the gear on its display if wanted)
/// or a Moza rim's ten. Looks for the wheel when switched on, on a Refresh, and every 3 s after losing it.
/// </summary>
public sealed class WheelApplet : IApplet
{
    private const string Who = "Wheel";

    private readonly WheelSettings settings = WheelSettings.Load();
    private readonly LightsRunner runner = LightsRunner.Shared;
    private readonly object gate = new();
    private readonly System.Threading.Timer finder;
    private readonly WheelView view;
    private IWheelLights? wheel;
    private bool force = true;
    private string lastDisplay = "?";
    private bool closing;

    public WheelApplet()
    {
        finder = new System.Threading.Timer(_ => Look(), null, Timeout.Infinite, Timeout.Infinite);
        // (looked for when switched on, and again on a Refresh button, in any module; since 3.25.3, every 3 s until found
        // before: finding it opens every HID device, which a game feels)
        GameControllers.Changed += () => { if (settings.On && wheel == null) finder.Change(0, Timeout.Infinite); };
        Profiles.SimProfiles.Register(new Profiles.ToolScope
        {
            Id = "Fanatec", Title = T("Wheel"), Settings = settings, Props = new[] { nameof(WheelSettings.Enabled) },
            IsOn = () => settings.Enabled, SetOn = SetEnabled, Loaded = () => { if (!settings.On) Off(); Apply(); view.ShowOnOff(); },
        });
        view = new WheelView(this);
    }

    internal WheelSettings Settings => settings;
    internal LightsRunner Runner => runner;

    /// <summary>The wheel's LEDs, or the make's usual number while there's none.</summary>
    internal int Leds { get { lock (gate) return wheel?.Leds ?? (settings.Brand == WheelBrand.Moza ? 10 : 9); } }
    internal bool Rgb { get { lock (gate) return wheel?.Rgb ?? settings.Brand == WheelBrand.Moza; } }

    /// <summary>The wheel found, or why there's none.</summary>
    internal string Found { get; private set; } = "";
    internal bool Connected { get { lock (gate) return wheel != null; } }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        runner.Frame += OnFrame;
        Apply();
    }

    internal void SetEnabled(bool on)
    {
        settings.Enabled = on;
        settings.Save();
        Logging.Log.Here.Info($"Wheel: switched {(on ? "on" : "off")}");
        if (!settings.On) Off();
        Apply();
    }

    /// <summary>Another make picked: the wheel let go and the new one looked for.</summary>
    internal void SetBrand(WheelBrand brand)
    {
        if (settings.Brand == brand) return;
        Off();
        settings.Brand = brand;
        settings.Save();
        Logging.Log.Here.Info($"Wheel: {brand}");
        Found = "";
        Apply();
    }

    private void Apply()
    {
        runner.Want(Who, settings.On);
        finder.Change(settings.On ? 0 : Timeout.Infinite, Timeout.Infinite);
        if (!settings.On) Found = "";
    }

    /// <summary>The settings changed: everything sent again.</summary>
    internal void Changed()
    {
        settings.Save();
        lock (gate) { force = true; lastDisplay = "?"; }
    }

    private void Look()
    {
        if (closing || !settings.On) return;
        lock (gate) if (wheel != null) return;
        try
        {
            var brand = settings.Brand;
            string why;
            IWheelLights? found = brand == WheelBrand.Moza ? MozaWheel.Find(out why) : FanatecWheel.Find(out why);
            if (found == null) { Found = why; return; }
            lock (gate)
            {
                if (closing || !settings.On || settings.Brand != brand) { found.Off(); found.Dispose(); return; }
                wheel = found;
                finder.Change(Timeout.Infinite, Timeout.Infinite);
                force = true;
                lastDisplay = "?";
            }
            Found = found.Name;
        }
        catch (Exception ex)
        {
            Found = ex.Message;
            Logging.Log.Here.Warn("Wheel: looking for the wheel", ex);
        }
    }

    /// <summary>Each LED's colour for a moment, from the first (black: off; reversed if asked).</summary>
    internal (byte R, byte G, byte B)[] Colours(RevState s, int leds) => NotifyRender.Strip(s, settings, leds, false, settings.Reverse);

    private void OnFrame(RevState s)
    {
        if (!settings.On) return;
        lock (gate)
        {
            if (wheel == null) return;
            bool ok = wheel.Show(Colours(s, wheel.Leds), force);
            force = false;
            string display = settings.Display && s.Live ? s.GearText : "";
            if (ok && wheel.HasDisplay && settings.Display && display != lastDisplay) { ok = wheel.SetDisplay(display); lastDisplay = display; }
            if (!ok)
            {
                Logging.Log.Here.Warn("Wheel: the wheel stopped taking reports (unplugged?)");
                Found = T("Lost the wheel");
                wheel.Dispose();
                wheel = null;
                // (the one it had: looked for again every few seconds until it's back)
                finder.Change(3000, 3000);
            }
        }
    }

    /// <summary>The LEDs (and display) off, and the wheel let go.</summary>
    private void Off()
    {
        lock (gate)
        {
            if (wheel == null) return;
            wheel.Off();
            if (wheel.HasDisplay && (settings.Display || lastDisplay != "?")) wheel.SetDisplay("");
            wheel.Dispose();
            wheel = null;
        }
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        closing = true;
        finder.Dispose();
        runner.Frame -= OnFrame;
        runner.Want(Who, false);
        view.Stop();
        Off();
        settings.Save();
    }
}

/// <summary>The Wheel tab: on / off, the make and the wheel found; the LEDs as they show now and its options (an RGB
/// rim's colours); the shared game, shift points and cars.</summary>
internal sealed class WheelView : UserControl
{
    private readonly WheelApplet applet;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock found = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ContentControl ledHolder = new();
    private readonly CheckBox display;
    private readonly Border colours;
    private LedView? ledView;
    private readonly LightsCards.Cars cars;
    private readonly DispatcherTimer timer;
    private readonly System.Windows.Controls.Primitives.ToggleButton onOff;
    private int ticks;

    /// <summary>On or off changed (another SimEffects profile).</summary>
    public void ShowOnOff()
    {
        SimUi.ShowOnOff(onOff, applet.Settings.Enabled);
    }

    public WheelView(WheelApplet applet)
    {
        this.applet = applet;
        var s = applet.Settings;
        onOff = SimUi.OnOff(T("Wheel on"), s.Enabled, applet.SetEnabled);
        var demo = SimUi.Button(T("Demo"), () => applet.Runner.Demo(), tip: T("Sweeps the RPM through the gears for a few seconds"));
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(demo, Dock.Right);
        top.Children.Add(demo);
        top.Children.Add(status);

        var wheelPanel = new StackPanel();
        var refresh = GameControllers.RefreshButton(T("Look for the wheel again"));
        refresh.Margin = new Thickness(0);
        wheelPanel.Children.Add(Ui.HeaderRow(T("Wheel"), refresh));
        var brand = new ComboBox { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        brand.Items.Add("Fanatec");
        brand.Items.Add("Moza");
        brand.SelectedIndex = (int)s.Brand;
        brand.SelectionChanged += (_, _) => { applet.SetBrand((WheelBrand)brand.SelectedIndex); Shown(); };
        wheelPanel.Children.Add(SimUi.Field(T("Make"), brand));
        wheelPanel.Children.Add(found);
        wheelPanel.Children.Add(ledHolder);
        var reverse = new CheckBox { Content = T("Right to Left"), IsChecked = s.Reverse, Margin = new Thickness(0, 10, 0, 0) };
        reverse.Click += (_, _) => { s.Reverse = reverse.IsChecked == true; applet.Changed(); };
        wheelPanel.Children.Add(reverse);
        display = new CheckBox { Content = T("Gear on the Display"), IsChecked = s.Display, Margin = new Thickness(0, 6, 0, 0), ToolTip = T("For rims with a display") };
        display.Click += (_, _) => { s.Display = display.IsChecked == true; applet.Changed(); };
        wheelPanel.Children.Add(display);

        var colourPanel = new StackPanel();
        colourPanel.Children.Add(Ui.Header(T("Shift Light Colours")));
        ComboBox Picker(Func<string> get, Action<string> set) => SimUi.ColourPicker(get, set, applet.Changed);
        colourPanel.Children.Add(SimUi.Field(T("First Half"), Picker(() => s.Low, v => s.Low = v)));
        colourPanel.Children.Add(SimUi.Field(T("Next 30%"), Picker(() => s.Mid, v => s.Mid = v)));
        colourPanel.Children.Add(SimUi.Field(T("Last 20%"), Picker(() => s.High, v => s.High = v)));
        colourPanel.Children.Add(SimUi.Field(T("At Shift"), Picker(() => s.Shift, v => s.Shift = v)));
        var flashes = new CheckBox { Content = T("Flash over-rev"), IsChecked = s.StripFlashes, Margin = new Thickness(0, 8, 0, 0) };
        flashes.Click += (_, _) => { s.StripFlashes = flashes.IsChecked == true; applet.Changed(); };
        colourPanel.Children.Add(flashes);
        colours = SimUi.Card(colourPanel);

        var left = new StackPanel();
        left.Children.Add(SimUi.Card(wheelPanel));
        left.Children.Add(colours);
        left.Children.Add(LightsCards.ShiftPoints(applet.Runner));
        cars = new LightsCards.Cars(applet.Runner);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(SimUi.Columns(left, cars));
        Content = page;

        Shown();
        timer = Ui.Every(this, 33, Tick);
    }

    /// <summary>What the make has: Fanatec's display, an RGB rim's colours.</summary>
    private void Shown()
    {
        display.Visibility = applet.Settings.Brand == WheelBrand.Fanatec ? Visibility.Visible : Visibility.Collapsed;
        colours.Visibility = applet.Rgb ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Stop() => timer.Stop();

    private void Tick()
    {
        int n = applet.Leds;
        if (ledView == null || ledView.Width != n * 22)
        {
            ledView = new LedView(n, n, 22);
            ledHolder.Content = ledView;
        }
        var s = applet.Settings.On ? applet.Runner.State : RevState.Off;
        // drawn as the wheel shows them, left to right (a single-colour rim's own colours shown here in one)
        var leds = applet.Colours(s, n);
        bool rgb = applet.Rgb;
        if (!rgb)
            for (int i = 0; i < n; i++)
                if (leds[i] != default) leds[i] = (230, 40, 30);
        ledView.Show(leds);
        if (++ticks % 15 != 0) return;
        Shown();
        found.Text = applet.Settings.On ? applet.Found : T("Off");
        found.SetResourceReference(TextBlock.ForegroundProperty, applet.Connected ? "SuccessBrush" : "TextSecondaryBrush");
        status.Text = applet.Settings.On ? LightsCards.Describe(applet.Runner) : "";
        status.SetResourceReference(TextBlock.ForegroundProperty, s.Live ? "SuccessBrush" : "TextSecondaryBrush");
        cars.Tick();
    }
}
