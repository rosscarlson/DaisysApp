using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Lights;
using DaisysApp.Settings;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Fanatec;

/// <summary>Saved in %APPDATA%\DaisysApp\Fanatec.json.</summary>
public sealed class FanatecSettings
{
    public bool Enabled { get; set; }
    /// <summary>LEDs light from the other end.</summary>
    public bool Reverse { get; set; }
    /// <summary>The gear on the rim's display.</summary>
    public bool Display { get; set; }

    public static FanatecSettings Load() => JsonStore.Load<FanatecSettings>("Fanatec");

    public void Save() => JsonStore.Save("Fanatec", this);
}

/// <summary>
/// SimEffects → Fanatec: the shift lights on a Fanatec wheel's rev LEDs (the ClubSport Steering Wheel RS's nine, and
/// the other single-colour ones), from the shared <see cref="LightsRunner"/>; the gear on the rim's display if wanted.
/// Looks for the wheelbase every 3 s while on.
/// </summary>
public sealed class FanatecApplet : IApplet
{
    private const string Who = "Fanatec";

    private readonly FanatecSettings settings = FanatecSettings.Load();
    private readonly LightsRunner runner = LightsRunner.Shared;
    private readonly object gate = new();
    private readonly System.Threading.Timer finder;
    private readonly FanatecView view;
    private FanatecWheel? wheel;
    private int leds = 9;
    private int lastBits = -1;
    private string lastDisplay = "?";
    private bool closing;

    public FanatecApplet()
    {
        finder = new System.Threading.Timer(_ => Look(), null, Timeout.Infinite, Timeout.Infinite);
        view = new FanatecView(this);
    }

    internal FanatecSettings Settings => settings;
    internal LightsRunner Runner => runner;
    internal int Leds => leds;

    /// <summary>The wheelbase and rim found, or why there's none.</summary>
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
        Logging.Log.Here.Info($"Fanatec: switched {(on ? "on" : "off")}");
        if (!on) Off();
        Apply();
    }

    private void Apply()
    {
        runner.Want(Who, settings.Enabled);
        finder.Change(settings.Enabled ? 0 : Timeout.Infinite, 3000);
        if (!settings.Enabled) Found = "";
    }

    /// <summary>The settings changed: everything sent again.</summary>
    internal void Changed()
    {
        settings.Save();
        lock (gate) { lastBits = -1; lastDisplay = "?"; }
    }

    private void Look()
    {
        if (closing || !settings.Enabled) return;
        lock (gate) if (wheel != null) return;
        try
        {
            var found = FanatecWheel.Find();
            if (found == null) { Found = T("No Fanatec wheelbase found"); return; }
            byte? rim = found.ReadRim();
            string rimName;
            if (rim is { } id && FanatecWheel.Rims.TryGetValue(id, out var r))
            {
                rimName = r.Leds > 0 ? r.Name : F("{0} (its LEDs aren't supported yet)", r.Name);
                leds = r.Leds > 0 ? r.Leds : 9;
            }
            else rimName = rim is { } unknown ? F("rim 0x{0:X2}", unknown) : T("rim not read");
            Logging.Log.Here.Info($"Fanatec: {found.Name}, {rimName}");
            lock (gate)
            {
                if (closing || !settings.Enabled) { found.Dispose(); return; }
                wheel = found;
                lastBits = -1;
                lastDisplay = "?";
            }
            Found = $"{found.Name} · {rimName}";
        }
        catch (Exception ex)
        {
            Found = ex.Message;
            Logging.Log.Here.Warn("Fanatec: looking for the wheel", ex);
        }
    }

    /// <summary>The LEDs for a moment: a bit per LED, the first one's bit the highest (bit 0 the last LED; reversed if asked).</summary>
    internal int Bits(RevState s)
    {
        if (!s.Live) return 0;
        int lit = s.AtShift ? leds : s.Fraction <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(s.Fraction * leds), 1, leds);
        if (s.OverRev && !s.BlinkOn) lit = 0;
        int bits = 0;
        for (int i = 0; i < lit; i++) bits |= 1 << (settings.Reverse ? i : leds - 1 - i);
        return bits;
    }

    private void OnFrame(RevState s)
    {
        if (!settings.Enabled) return;
        lock (gate)
        {
            if (wheel == null) return;
            int bits = Bits(s);
            string display = settings.Display && s.Live ? s.GearText : "";
            bool ok = true;
            if (bits != lastBits) { ok = wheel.SetLeds((ushort)bits); lastBits = bits; }
            if (ok && settings.Display && display != lastDisplay) { ok = wheel.SetDisplay(display); lastDisplay = display; }
            if (!ok)
            {
                Logging.Log.Here.Warn("Fanatec: the wheelbase stopped taking reports (unplugged?)");
                Found = T("Lost the wheelbase");
                wheel.Dispose();
                wheel = null;
            }
        }
    }

    /// <summary>The LEDs (and display) off, and the wheel let go.</summary>
    private void Off()
    {
        lock (gate)
        {
            if (wheel == null) return;
            wheel.SetLeds(0);
            if (settings.Display || lastDisplay != "?") wheel.SetDisplay("");
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

/// <summary>The Fanatec tab: on / off and the wheel found; the LEDs as they show now and its options; the shared
/// game, shift points and cars.</summary>
internal sealed class FanatecView : UserControl
{
    private readonly FanatecApplet applet;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock found = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ContentControl ledHolder = new();
    private LedView? ledView;
    private readonly LightsCards.Cars cars;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private int ticks;

    public FanatecView(FanatecApplet applet)
    {
        this.applet = applet;
        var s = applet.Settings;
        var onOff = Ui.OnOff(T("Fanatec on"), s.Enabled, applet.SetEnabled);
        var demo = Ui.Button(T("Demo"), () => applet.Runner.Demo(), tip: T("Sweeps the RPM through the gears for a few seconds"));
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(demo, Dock.Right);
        top.Children.Add(demo);
        top.Children.Add(status);

        var wheelPanel = new StackPanel();
        wheelPanel.Children.Add(Ui.Header(T("Wheel")));
        wheelPanel.Children.Add(found);
        wheelPanel.Children.Add(ledHolder);
        var reverse = new CheckBox { Content = T("Right to Left"), IsChecked = s.Reverse, Margin = new Thickness(0, 10, 0, 0) };
        reverse.Click += (_, _) => { s.Reverse = reverse.IsChecked == true; applet.Changed(); };
        wheelPanel.Children.Add(reverse);
        var display = new CheckBox { Content = T("Gear on the Display"), IsChecked = s.Display, Margin = new Thickness(0, 6, 0, 0), ToolTip = T("For rims with a display") };
        display.Click += (_, _) => { s.Display = display.IsChecked == true; applet.Changed(); };
        wheelPanel.Children.Add(display);

        var left = new StackPanel();
        left.Children.Add(Ui.Card(wheelPanel));
        left.Children.Add(LightsCards.Game(applet.Runner));
        left.Children.Add(LightsCards.ShiftPoints(applet.Runner));
        cars = new LightsCards.Cars(applet.Runner);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(Ui.Columns(left, cars));
        Content = page;

        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
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
        var s = applet.Settings.Enabled ? applet.Runner.State : RevState.Off;
        int bits = applet.Bits(s);
        // drawn as the wheel shows them, left to right (their own colours are the rim's: shown here in one)
        var colours = new (byte, byte, byte)[n];
        for (int i = 0; i < n; i++)
        {
            int bit = applet.Settings.Reverse ? i : n - 1 - i;
            if ((bits & 1 << bit) == 0) continue;
            colours[i] = (230, 40, 30);
        }
        ledView.Show(colours);
        if (++ticks % 15 != 0) return;
        found.Text = applet.Settings.Enabled ? applet.Found : T("Off");
        found.SetResourceReference(TextBlock.ForegroundProperty, applet.Connected ? "SuccessBrush" : "TextSecondaryBrush");
        status.Text = applet.Settings.Enabled ? LightsCards.Describe(applet.Runner) : "";
        status.SetResourceReference(TextBlock.ForegroundProperty, s.Live ? "SuccessBrush" : "TextSecondaryBrush");
        cars.Tick();
    }
}
