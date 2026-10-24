using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Theming;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// The Level Wizard: the profile's effects one step at a time, to set each one's level (and frequency, sensitivity,
/// threshold, length) by shortcuts while driving. Step 0 sets the shortcuts (Previous / Next and Level Up / Down are
/// needed; the rest are optional, and a setting without one says so but keeps its slider). Solo / All: only the
/// step's effect plays, or every effect as it was before the wizard opened (with what's been changed since); closing
/// it puts every effect's mute back as it was.
/// </summary>
internal sealed class LevelWizard : Window
{
    private sealed record Binding(string Id, string Label, Func<WizardKeys, string?> Get, Action<WizardKeys, string?> Set, bool Required = false);

    private static readonly Binding[] Bindings =
    {
        new("Previous", T("Previous Step"), k => k.Previous, (k, v) => k.Previous = v, true),
        new("Next", T("Next Step"), k => k.Next, (k, v) => k.Next = v, true),
        new("LevelUp", T("Level Up"), k => k.LevelUp, (k, v) => k.LevelUp = v, true),
        new("LevelDown", T("Level Down"), k => k.LevelDown, (k, v) => k.LevelDown = v, true),
        new("FrequencyUp", T("Frequency Up"), k => k.FrequencyUp, (k, v) => k.FrequencyUp = v),
        new("FrequencyDown", T("Frequency Down"), k => k.FrequencyDown, (k, v) => k.FrequencyDown = v),
        new("SensitivityUp", T("Sensitivity Up"), k => k.SensitivityUp, (k, v) => k.SensitivityUp = v),
        new("SensitivityDown", T("Sensitivity Down"), k => k.SensitivityDown, (k, v) => k.SensitivityDown = v),
        new("ThresholdUp", T("Threshold Up"), k => k.ThresholdUp, (k, v) => k.ThresholdUp = v),
        new("ThresholdDown", T("Threshold Down"), k => k.ThresholdDown, (k, v) => k.ThresholdDown = v),
        new("LengthUp", T("Length Up"), k => k.LengthUp, (k, v) => k.LengthUp = v),
        new("LengthDown", T("Length Down"), k => k.LengthDown, (k, v) => k.LengthDown = v),
        new("Solo", T("Solo / All"), k => k.Solo, (k, v) => k.Solo = v),
    };

    private const string Prefix = "ShakerWizard.";
    private static Hotkey[] hotkeys = Array.Empty<Hotkey>();
    private static LevelWizard? open;

    /// <summary>The wizard's shortcuts (Core's Hotkeys, so in Settings → Shortcuts too): registered only while it's open.</summary>
    public static void AddKeys(ShakerSettings settings)
    {
        hotkeys = Hotkeys.Replace(Prefix, Bindings.Select(b => (new HotkeySpec
        {
            Id = Prefix + b.Id, Module = T("Shaker"), Name = b.Label, Where = T("Level Wizard"), Plain = true,
            Get = () => b.Get(settings.Wizard), Set = v => { b.Set(settings.Wizard, v); settings.Save(); },
        }, (Action)(() => open?.Press(b.Id))))).ToArray();
        Listen(open != null);
    }

    public static void RemoveKeys() => Hotkeys.Replace(Prefix, Array.Empty<(HotkeySpec, Action)>());

    private static void Listen(bool on)
    {
        foreach (var h in hotkeys) h.Enabled = on;
    }

    private readonly ShakerSettings settings;
    private readonly ShakerProfile profile;
    private readonly ShakerEngine engine;
    private readonly Action save;
    private readonly Dictionary<ShakerEffect, bool> wasMuted;
    private int step;
    private bool solo = true;

    private readonly TextBlock title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock status = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly ToggleButton soloChip = new() { Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl body = new() { Margin = new Thickness(0, 14, 0, 0) };
    private readonly Button previous = new() { Content = T("Previous"), MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button next = new() { Content = T("Next"), MinWidth = 90 };
    private readonly MiniMeter meter = new() { Width = 260, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Bright = true };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    public LevelWizard(ShakerSettings settings, ShakerProfile profile, ShakerEngine engine, Action save)
    {
        this.settings = settings;
        this.profile = profile;
        this.engine = engine;
        this.save = save;
        wasMuted = profile.Effects.ToDictionary(e => e, e => e.Muted);

        Title = T("Level Wizard");
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        status.SetResourceReference(StyleProperty, "SecondaryText");
        soloChip.SetResourceReference(StyleProperty, "ChipToggle");
        AutomationProperties.SetName(soloChip, T("Solo / All"));
        soloChip.Click += (_, _) => SetSolo(soloChip.IsChecked == true);
        previous.Click += (_, _) => Go(step - 1);
        next.Click += (_, _) => { if (step >= profile.Effects.Count) Close(); else Go(step + 1); };

        var head = new DockPanel();
        DockPanel.SetDock(soloChip, Dock.Right);
        head.Children.Add(soloChip);
        var titles = new StackPanel();
        titles.Children.Add(title);
        titles.Children.Add(status);
        head.Children.Add(titles);

        var onTop = new CheckBox { Content = T("On Top"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Stays above the game") };
        onTop.Click += (_, _) => Topmost = onTop.IsChecked == true;
        var foot = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var stepButtons = new StackPanel { Orientation = Orientation.Horizontal };
        stepButtons.Children.Add(previous);
        stepButtons.Children.Add(next);
        DockPanel.SetDock(stepButtons, Dock.Right);
        foot.Children.Add(stepButtons);
        foot.Children.Add(onTop);

        var page = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(head, Dock.Top);
        page.Children.Add(head);
        DockPanel.SetDock(foot, Dock.Bottom);
        page.Children.Add(foot);
        page.Children.Add(body);
        Content = page;

        timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            open = this;
            Listen(true);
            Go(0);
            timer.Start();
        };
        Closed += (_, _) => Finish();
        Logging.Log.Here.Info($"Shaker: Level Wizard opened ({profile.Name}, {profile.Effects.Count} effects)");
    }

    /// <summary>The step showing or something on it changed (the tab shows it as it happens).</summary>
    public event Action? Changed;

    /// <summary>The effect the step is on (null on the shortcuts step).</summary>
    public ShakerEffect? Showing => Current;

    private WizardKeys Keys => settings.Wizard;
    private ShakerEffect? Current => step >= 1 && step <= profile.Effects.Count ? profile.Effects[step - 1] : null;
    private bool Ready => Bindings.Where(b => b.Required).All(b => b.Get(Keys) != null);

    // ---- steps ----

    private void Go(int to)
    {
        if (to > 0 && !Ready) to = 0;
        step = Math.Clamp(to, 0, profile.Effects.Count);
        ApplySolo();
        ShowStep();
    }

    private void ShowStep()
    {
        Changed?.Invoke();
        previous.IsEnabled = step > 0;
        next.IsEnabled = step > 0 || Ready;
        next.Content = step >= profile.Effects.Count && step > 0 ? T("Finish") : T("Next");
        soloChip.IsChecked = solo;
        soloChip.Content = solo ? T("Solo") : T("All");
        soloChip.ToolTip = Keys.Solo is { } k ? F("Solo / All: {0}", k) : T("Solo / All: no shortcut");
        soloChip.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Current is { } e)
        {
            title.Text = F("{0} of {1}: {2}", step, profile.Effects.Count, EffectKinds.Of(e.Kind).Title);
            body.Content = EffectPage(e);
        }
        else
        {
            title.Text = T("Shortcuts");
            body.Content = KeysPage();
        }
    }

    // ---- step 0: the shortcuts ----

    private UIElement KeysPage()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        for (int i = 0; i < Bindings.Length; i++)
        {
            var b = Bindings[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = b.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 6) };
            if (b.Required) label.FontWeight = FontWeights.SemiBold;
            Grid.SetRow(label, i);
            grid.Children.Add(label);
            var box = new ShortcutBox(hotkeys[i]) { Margin = new Thickness(0, 0, 0, 6) };
            box.Changed += ShowStep;
            Grid.SetRow(box, i);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            var note = new TextBlock { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 6, 0, 6) };
            if (b.Required && b.Get(Keys) == null)
            {
                note.Text = T("Needed");
                note.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            }
            Grid.SetRow(note, i);
            Grid.SetColumn(note, 2);
            grid.Children.Add(note);
        }
        return grid;
    }

    // ---- an effect's step ----

    private UIElement EffectPage(ShakerEffect e)
    {
        var info = EffectKinds.Of(e.Kind);
        var panel = new StackPanel();
        if (meter.Parent is Panel old) old.Children.Remove(meter);
        panel.Children.Add(meter);
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        int row = 0;
        void Row(string label, double min, double max, double stepSize, double value, Func<double, string> show, Action<double> set, string? up, string? down, bool hasKeys)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            Grid.SetRow(l, row);
            grid.Children.Add(l);
            var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = stepSize, IsSnapToTickEnabled = true, SmallChange = stepSize, LargeChange = stepSize * 5, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(s, label);
            var v = new TextBlock { Text = show(value), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            s.ValueChanged += (_, _) => { set(s.Value); v.Text = show(s.Value); save(); Changed?.Invoke(); };
            Grid.SetRow(s, row);
            Grid.SetColumn(s, 1);
            grid.Children.Add(s);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 2);
            grid.Children.Add(v);
            if (hasKeys)
            {
                var k = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                if (up == null && down == null)
                {
                    k.Text = T("No shortcut");
                    k.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
                }
                else
                {
                    k.Text = $"{up ?? "–"}  /  {down ?? "–"}";
                    k.ToolTip = F("Up: {0}\nDown: {1}", up ?? T("no shortcut"), down ?? T("no shortcut"));
                    k.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                }
                Grid.SetRow(k, row);
                Grid.SetColumn(k, 3);
                grid.Children.Add(k);
            }
            row++;
        }
        Row(T("Level"), 0, 100, 1, e.Volume * 100, x => $"{x:0}%", x => e.Volume = Math.Round(x / 100, 2), Keys.LevelUp, Keys.LevelDown, true);
        foreach (var p in info.Params)
        {
            var (up, down) = KeysOf(p.Knob);
            Row(p.Label, p.Min, p.Max, p.Step, p.Get(e), p.Show, x => p.Set(e, x), up, down, p.Knob != Knob.None);
        }
        panel.Children.Add(grid);
        if (e.Muted && !solo)
        {
            var muted = new TextBlock { Text = T("Muted"), Margin = new Thickness(0, 8, 0, 0) };
            muted.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            panel.Children.Add(muted);
        }
        return panel;
    }

    private (string? Up, string? Down) KeysOf(Knob k) => k switch
    {
        Knob.Frequency => (Keys.FrequencyUp, Keys.FrequencyDown),
        Knob.Sensitivity => (Keys.SensitivityUp, Keys.SensitivityDown),
        Knob.Threshold => (Keys.ThresholdUp, Keys.ThresholdDown),
        Knob.Length => (Keys.LengthUp, Keys.LengthDown),
        _ => (null, null),
    };

    // ---- Solo / All ----

    private void SetSolo(bool on)
    {
        solo = on;
        Logging.Log.Here.Debug(() => $"Shaker Level Wizard: {(on ? "solo" : "all")}");
        ApplySolo();
        ShowStep();
    }

    /// <summary>Solo: only the step's effect plays. All (and the shortcuts page): every effect muted or not as it was
    /// before the wizard opened.</summary>
    private void ApplySolo()
    {
        var current = Current;
        foreach (var e in profile.Effects)
            e.Muted = solo && current != null ? e != current : wasMuted.GetValueOrDefault(e);
    }

    // ---- shortcuts ----

    private void Press(string id)
    {
        bool Is(string? k) => k == id;
        var e = Current;
        if (Is("Previous")) Go(step - 1);
        else if (Is("Next")) Go(step + 1);
        else if (Is("Solo")) { if (step > 0) SetSolo(!solo); }
        else if (e == null) return;
        else if (Is("LevelUp")) Change(e, () => e.Volume = Math.Min(1, Math.Round(e.Volume + VolumeHotkeys.Step, 2)));
        else if (Is("LevelDown")) Change(e, () => e.Volume = Math.Max(0, Math.Round(e.Volume - VolumeHotkeys.Step, 2)));
        else
        {
            foreach (var knob in new[] { Knob.Frequency, Knob.Sensitivity, Knob.Threshold, Knob.Length })
            {
                int dir = Is(knob + "Up") ? 1 : Is(knob + "Down") ? -1 : 0;
                if (dir == 0) continue;
                Change(e, () => Nudge(e, knob, dir));
                break;
            }
        }
    }

    private void Change(ShakerEffect e, Action change)
    {
        change();
        save();
        ShowStep();
    }

    /// <summary>One step of a setting up or down (frequency 2 Hz; the Engine's whole range moves with it).</summary>
    private static void Nudge(ShakerEffect e, Knob knob, int dir)
    {
        foreach (var p in EffectKinds.Of(e.Kind).Params.Where(p => p.Knob == knob))
        {
            double by = p.Step * (knob == Knob.Frequency ? 2 : 1) * dir;
            double before = p.Get(e);
            double now = Math.Round(Math.Clamp(before + by, p.Min, p.Max), 4);
            p.Set(e, now);
            if (knob == Knob.Frequency && e.Kind == EffectKind.Engine)
                e.FrequencyHigh = Math.Clamp(e.FrequencyHigh + (now - before), 5, 150);
        }
    }

    private void Tick()
    {
        status.Text = engine.Status;
        if (Current is { } e && engine.Signals.FirstOrDefault(s => s.Effect == e) is { } sig) meter.Level = sig.Level;
        else meter.Level = 0;
    }

    private void Finish()
    {
        timer.Stop();
        foreach (var (e, muted) in wasMuted) e.Muted = muted;
        if (open == this) open = null;
        Listen(false);
        settings.Save();
        Logging.Log.Here.Info("Shaker: Level Wizard closed");
    }
}
