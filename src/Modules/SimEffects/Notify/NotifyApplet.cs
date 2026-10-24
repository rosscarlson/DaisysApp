using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Arduino;
using DaisysApp.Applets.SimEffects.Lights;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Notify;

/// <summary>
/// SimEffects → Notify: the Arduinos' lights. <b>Notify</b> features (8×8 matrices) show the gear, flashing over-rev;
/// <b>Shift</b> features (rows of 8 or 16 LEDs) are shift lights. The shift points and each car's redline are the
/// shared <see cref="LightsRunner"/>'s. Shown while some Arduino has one of them (<see cref="SimEffectsApplet"/>).
/// </summary>
public sealed class NotifyApplet : IApplet
{
    private const string Who = "Notify";

    /// <summary>One feature it drives, as its settings were when the boards last changed (read on the lights' thread).</summary>
    private sealed record Target(ArduinoBoard Board, ArduinoFeature Feature, bool Matrix, int Count, bool BothEnds, bool Reverse);

    private readonly ArduinoHub hub;
    private readonly NotifySettings settings = NotifySettings.Load();
    private readonly LightsRunner runner = LightsRunner.Shared;
    private volatile Target[] targets = Array.Empty<Target>();
    private readonly NotifyView view;

    public NotifyApplet(ArduinoHub hub)
    {
        this.hub = hub;
        view = new NotifyView(this);
        hub.Changed += Retarget;
    }

    internal NotifySettings Settings => settings;
    internal LightsRunner Runner => runner;
    internal ArduinoHub Hub => hub;

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        runner.Frame += OnFrame;
        Retarget();
    }

    /// <summary>The boards changed: the features to drive, and whether the lights are wanted.</summary>
    private void Retarget()
    {
        targets = hub.FeaturesFor(ArduinoFeatures.NotifyTab).Select(t => new Target(t.Board, t.Feature, t.Feature.Info is LedMatrixFeature,
            t.Feature.Info is LedFeature led ? led.Count(t.Feature) : 0,
            t.Feature.Get(LedStripFeature.Style) == 1, t.Feature.Get(LedStripFeature.Reverse) != 0)).ToArray();
        runner.Want(Who, settings.Enabled && targets.Length > 0);
        view.Render();
    }

    internal IReadOnlyList<(ArduinoBoard Board, ArduinoFeature Feature, bool Matrix, int Count, bool BothEnds, bool Reverse)> Targets =>
        targets.Select(t => (t.Board, t.Feature, t.Matrix, t.Count, t.BothEnds, t.Reverse)).ToList();

    internal void SetEnabled(bool on)
    {
        settings.Enabled = on;
        settings.Save();
        Logging.Log.Here.Info($"Notify: switched {(on ? "on" : "off")}");
        if (!on)
            foreach (var t in targets) hub.Send(t.Board, t.Feature, ArduinoProtocol.Clear, Array.Empty<byte>());
        runner.Want(Who, on && targets.Length > 0);
    }

    private void OnFrame(RevState s)
    {
        if (!settings.Enabled) return;
        foreach (var t in targets)
        {
            if (t.Matrix) hub.Send(t.Board, t.Feature, ArduinoProtocol.Bitmap, NotifyRender.MatrixData(s, settings));
            else if (t.Count > 0) hub.Send(t.Board, t.Feature, ArduinoProtocol.Pixels, NotifyRender.StripData(NotifyRender.Strip(s, settings, t.Count, t.BothEnds, t.Reverse)));
        }
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        hub.Changed -= Retarget;
        runner.Frame -= OnFrame;
        runner.Want(Who, false);
        view.Stop();
        settings.Save();
    }
}

/// <summary>The Notify tab: on / off and what the lights follow; the game, shift points and colours (left); each
/// light as it shows now, and the cars (right).</summary>
internal sealed class NotifyView : UserControl
{
    private readonly NotifyApplet applet;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel lights = new();
    private readonly List<(LedView View, bool Matrix, int Count, bool BothEnds, bool Reverse, TextBlock Problem, ArduinoBoard Board, ArduinoFeature Feature)> previews = new();
    private readonly LightsCards.Cars cars;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private int ticks;

    public NotifyView(NotifyApplet applet)
    {
        this.applet = applet;
        var s = applet.Settings;
        var onOff = Ui.OnOff(T("Notify on"), s.Enabled, applet.SetEnabled);
        var demo = Ui.Button(T("Demo"), () => applet.Runner.Demo(), tip: T("Sweeps the RPM through the gears for a few seconds"));
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(onOff, Dock.Left);
        top.Children.Add(onOff);
        DockPanel.SetDock(demo, Dock.Right);
        top.Children.Add(demo);
        top.Children.Add(status);

        var left = new StackPanel();
        left.Children.Add(LightsCards.Game(applet.Runner));
        left.Children.Add(LightsCards.ShiftPoints(applet.Runner));
        left.Children.Add(ColoursCard());
        var right = new StackPanel();
        var lightsPanel = new StackPanel();
        lightsPanel.Children.Add(Ui.Header(T("Lights")));
        lightsPanel.Children.Add(lights);
        right.Children.Add(Ui.Card(lightsPanel));
        cars = new LightsCards.Cars(applet.Runner);
        right.Children.Add(cars);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        page.Children.Add(top);
        page.Children.Add(Ui.Columns(left, right));
        Content = page;

        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
    }

    public void Stop() => timer.Stop();

    /// <summary>The lights' previews, one per feature.</summary>
    public void Render()
    {
        lights.Children.Clear();
        previews.Clear();
        var list = applet.Targets;
        if (list.Count == 0)
        {
            lights.Children.Add(Ui.Secondary(T("No Notify or Shift lights: add them to an Arduino on the Arduino tab")));
            return;
        }
        foreach (var t in list)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(new TextBlock { Text = $"{t.Feature.Name} · {t.Board.Name}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var led = t.Matrix ? new LedView(64, 8, 16) : new LedView(t.Count, t.Count, 20);
            panel.Children.Add(led);
            var problem = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            problem.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(problem);
            lights.Children.Add(panel);
            previews.Add((led, t.Matrix, t.Count, t.BothEnds, t.Reverse, problem, t.Board, t.Feature));
        }
        ticks = 14;
    }

    private void Tick()
    {
        var s = applet.Runner.State;
        var c = applet.Settings;
        if (!c.Enabled) s = RevState.Off;
        foreach (var p in previews)
        {
            if (p.Matrix)
            {
                var (rows, on) = NotifyRender.Matrix(s, c);
                p.View.ShowPicture(rows, on);
            }
            else p.View.Show(NotifyRender.Strip(s, c, p.Count, p.BothEnds, p.Reverse));
        }
        if (++ticks % 15 != 0) return;
        status.Text = c.Enabled ? LightsCards.Describe(applet.Runner) : "";
        status.SetResourceReference(TextBlock.ForegroundProperty, s.Live ? "SuccessBrush" : "TextSecondaryBrush");
        foreach (var p in previews)
        {
            string text = applet.Hub.Problem(p.Board, p.Feature) ?? (applet.Hub.Connected(p.Board) ? "" : applet.Hub.Status(p.Board));
            p.Problem.Text = text;
            p.Problem.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        cars.Tick();
    }

    private Border ColoursCard()
    {
        var s = applet.Settings;
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header(T("Gear Colours")));
        panel.Children.Add(Ui.Field(T("Gears"), Picker(() => s.Gear, v => s.Gear = v)));
        panel.Children.Add(Ui.Field(T("Neutral"), Picker(() => s.Neutral, v => s.Neutral = v)));
        panel.Children.Add(Ui.Field(T("Reverse"), Picker(() => s.Reverse, v => s.Reverse = v)));
        panel.Children.Add(Ui.Field(T("At Shift"), Picker(() => s.GearShift, v => s.GearShift = v)));
        panel.Children.Add(Ui.Field(T("Flash"), Picker(() => s.GearFlash, v => s.GearFlash = v)));
        var shift = Ui.Header(T("Shift Light Colours"));
        shift.Margin = new Thickness(0, 14, 0, 6);
        panel.Children.Add(shift);
        panel.Children.Add(Ui.Field(T("First Half"), Picker(() => s.Low, v => s.Low = v)));
        panel.Children.Add(Ui.Field(T("Next 30%"), Picker(() => s.Mid, v => s.Mid = v)));
        panel.Children.Add(Ui.Field(T("Last 20%"), Picker(() => s.High, v => s.High = v)));
        panel.Children.Add(Ui.Field(T("At Shift"), Picker(() => s.Shift, v => s.Shift = v)));
        var flashes = new CheckBox { Content = T("Flash over-rev"), IsChecked = s.StripFlashes, Margin = new Thickness(0, 8, 0, 0) };
        flashes.Click += (_, _) => { s.StripFlashes = flashes.IsChecked == true; s.Save(); };
        panel.Children.Add(flashes);
        return Ui.Card(panel);
    }

    private ComboBox Picker(Func<string> get, Action<string> set)
    {
        var items = LightColours.All.ToList();
        string now = get();
        if (!items.Any(i => i.Hex.Equals(now, StringComparison.OrdinalIgnoreCase))) items.Add((now, now));
        var box = new ComboBox { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (name, hex) in items)
        {
            var (r, g, b) = LightColours.Parse(hex);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = hex };
            var swatch = new Rectangle { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), Fill = new SolidColorBrush(Color.FromRgb(r, g, b)), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Shape.StrokeProperty, "CardBorderBrush");
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            box.Items.Add(row);
            if (hex.Equals(now, StringComparison.OrdinalIgnoreCase)) box.SelectedItem = row;
        }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is StackPanel { Tag: string hex })
            {
                set(hex);
                applet.Settings.Save();
            }
        };
        return box;
    }
}
