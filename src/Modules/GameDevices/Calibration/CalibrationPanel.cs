using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// The picked device's Calibration block, under its axes and buttons (the Calibration module until 4.6.7): the axis
/// clicked above, its raw reading with Windows' min, centre and max as ticks, what games get, a graph and min / centre /
/// max to set by hand. <b>Find Ranges</b> is the old wizard: every axis moved end to end gets its lowest and highest
/// readings as min and max, the centre halfway. Nothing reaches Windows until <see cref="Apply"/>; <see cref="Cancel"/>
/// drops the changes. The device is open only while the tab shows.
/// </summary>
internal sealed class CalibrationPanel : StackPanel
{
    private static readonly Brush Amber = Ui.Brush(Color.FromRgb(0xD2, 0x99, 0x22));

    private readonly CalibrationSettings settings;
    private readonly IntPtr window;
    private readonly Dictionary<int, Cal> pending = new(), saved = new();   // by place (DIJOYSTATE2's, 0–7)
    private readonly Dictionary<int, RangeFinder> finders = new();
    private readonly HashSet<int> byHand = new();
    private readonly Dictionary<int, (int? Last, DateTime MovedAt)> motion = new();
    private readonly ToggleButton find;
    private readonly TextBlock status = Ui.Note("", new Thickness(0, 0, 0, 6));
    private readonly ContentControl body = new();
    private ControllerInfo? info;
    private IController? device;
    private int[] raw = Array.Empty<int>();
    private double[] output = Array.Empty<double>();
    private bool showing, paused, failed, syncing;
    private int selected = -1;

    // the axis showing: its live pieces
    private AxisBar? rawBar, outBar;
    private TextBlock? rawText, outText, calText, lightText;
    private Ellipse? light;
    private CalGraph? graph;
    private (Slider Slider, TextBox Box)[]? fields;
    private Button? original;

    /// <summary>Green, at the top beside the device's name while there are changes: gives Windows every axis changed.</summary>
    public Button Apply { get; }

    /// <summary>Red, beside Apply: drops the changes (✕ first).</summary>
    public Button Cancel { get; }

    /// <summary>The axis picked, changes made or an axis moving: the axes above show it.</summary>
    public event Action? Changed;

    public CalibrationPanel(CalibrationSettings settings, IntPtr window)
    {
        this.settings = settings;
        this.window = window;
        Margin = new Thickness(0, 14, 0, 0);

        Apply = Ui.Button(T("Apply"), ApplyAll, T("Give Windows the new calibration, for every game"));
        Apply.SetResourceReference(Control.BackgroundProperty, "SuccessBrush");
        Apply.SetResourceReference(Control.BorderBrushProperty, "SuccessBrush");
        Apply.SetResourceReference(Control.ForegroundProperty, "AccentForegroundBrush");
        Cancel = Ui.Button(T("Cancel"), () => { });
        Cancel.SetResourceReference(Control.BackgroundProperty, "DangerBrush");
        Cancel.SetResourceReference(Control.BorderBrushProperty, "DangerBrush");
        Cancel.SetResourceReference(Control.ForegroundProperty, "DangerForegroundBrush");
        Cancel.ToolTip = T("Drop the changes");
        AutomationProperties.SetName(Cancel, T("Cancel"));
        ConfirmButton.ArmIcon(Cancel, DropAll);
        foreach (var b in new[] { Cancel, Apply })
        {
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Visibility = Visibility.Collapsed;
        }

        find = Ui.Chip(T("Find Ranges"), false, T("Move each axis all the way, both ways: its min and max are found"));
        find.Margin = new Thickness(0);
        Ui.OnToggle(find, on =>
        {
            foreach (var f in finders.Values) f.Reset();
            byHand.Clear();
            ShowAxis();
        });
        status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        status.Visibility = Visibility.Collapsed;
        Children.Add(Ui.HeaderRow(Ui.Header(T("Calibration"), new Thickness(0)), find));
        Children.Add(status);
        Children.Add(body);
    }

    /// <summary>The places (0–7) the device has axes at, as it's open now or as listed.</summary>
    private IEnumerable<int> Places => device != null ? device.Axes.Select(a => a.Offset / 4) : Enumerable.Range(0, 8).Where(i => info?.HasAxis[i] == true);

    private Axis? AxisAt(int place) => device?.Axes.FirstOrDefault(a => a.Offset / 4 == place);

    public int Selected => selected;
    public bool Has(int place) => pending.ContainsKey(place);

    /// <summary>Moved in the last 0.4 s (a hundredth of its range at a time): which pedal is which.</summary>
    public bool Moving(int place) => motion.TryGetValue(place, out var m) && DateTime.UtcNow - m.MovedAt < TimeSpan.FromSeconds(0.4);

    /// <summary>The device picked (the same one again after the list changed keeps its changes).</summary>
    public void Show(ControllerInfo? c)
    {
        if (c?.Instance != info?.Instance)
        {
            Close();
            info = c;
            Forget();
            selected = Places.DefaultIfEmpty(-1).First();
        }
        else failed = false;   // (listed again: worth trying again)
        ShowAxis();
    }

    /// <summary>The tab shows (the device open while it does) or doesn't.</summary>
    public void Showing(bool on)
    {
        showing = on;
        if (!on) Close();
    }

    /// <summary>Let go of the device (Devices' Apply Order, which Windows won't do while a program has one open).</summary>
    public bool Paused
    {
        get => paused;
        set
        {
            paused = value;
            if (value) Close();
            failed = false;
        }
    }

    public void Select(int place)
    {
        if (place == selected) return;
        selected = place;
        ShowAxis();
        Changed?.Invoke();
    }

    private void Forget()
    {
        pending.Clear();
        saved.Clear();
        finders.Clear();
        byHand.Clear();
        motion.Clear();
        find.IsChecked = false;
        Buttons();
    }

    private void Close()
    {
        device?.Dispose();
        device = null;
    }

    private bool Open()
    {
        if (device != null) return true;
        if (info == null || failed || paused || !showing) return false;
        device = Calibrator.Open(info, window, out string? problem);
        if (device == null)
        {
            failed = true;
            Status(problem ?? T("Not connected"));
            return false;
        }
        Status(null);
        raw = new int[device.Axes.Count];
        output = new double[device.Axes.Count];
        saved.Clear();
        foreach (var a in device.Axes)
        {
            saved[a.Offset / 4] = device.Get(a);
            if (!finders.ContainsKey(a.Offset / 4)) finders[a.Offset / 4] = new RangeFinder(a);
        }
        if (AxisAt(selected) == null) selected = Places.DefaultIfEmpty(-1).First();
        ShowAxis();
        Changed?.Invoke();
        return true;
    }

    private void Status(string? text)
    {
        status.Text = text ?? "";
        status.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Buttons()
    {
        var v = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Apply.Visibility = Cancel.Visibility = v;
        Apply.IsEnabled = device != null;
    }

    // ---------------------------------------------------------------- the axis picked

    private void ShowAxis()
    {
        rawBar = outBar = null;
        graph = null;
        fields = null;
        light = null;
        var s = new StackPanel();
        body.Content = s;
        Buttons();
        if (AxisAt(selected) is not { } a)
        {
            if (device != null || !failed) s.Children.Add(Ui.Note(device == null ? T("Not connected") : T("Click an axis above")));
            return;
        }
        string key = CalibrationSettings.KeyOf(info!, a);

        var name = new TextBlock { Text = a.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        calText = Ui.Secondary();
        calText.Margin = new Thickness(10, 0, 0, 0);
        calText.VerticalAlignment = VerticalAlignment.Center;
        var lit = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        light = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        lightText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        lit.Children.Add(light);
        lit.Children.Add(lightText);
        lit.Visibility = find.IsChecked == true && !settings.Ignored.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(lit, Dock.Right);
        title.Children.Add(lit);
        title.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { name, calText } });
        s.Children.Add(title);

        var bars = new Grid();
        bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        bars.ColumnDefinitions.Add(new ColumnDefinition());
        bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        bars.RowDefinitions.Add(new RowDefinition());
        bars.RowDefinitions.Add(new RowDefinition());
        rawBar = new AxisBar { Fill = false, Margin = new Thickness(0, 3, 8, 3) };
        outBar = new AxisBar { Margin = new Thickness(0, 3, 8, 3) };
        rawText = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        outText = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Put(bars, Ui.Secondary(T("Raw")), 0, 0);
        Put(bars, rawBar, 0, 1);
        Put(bars, rawText, 0, 2);
        Put(bars, Ui.Secondary(T("Output")), 1, 0);
        Put(bars, outBar, 1, 1);
        Put(bars, outText, 1, 2);
        s.Children.Add(bars);

        graph = new CalGraph { Axis = a, Height = 200, Margin = new Thickness(0, 8, 0, 0) };
        s.Children.Add(graph);

        var rows = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        rows.ColumnDefinitions.Add(new ColumnDefinition());
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string[] names = { T("Min"), T("Centre"), T("Max") };
        fields = new (Slider, TextBox)[3];
        for (int i = 0; i < 3; i++)
        {
            rows.RowDefinitions.Add(new RowDefinition());
            int which = i;
            var label = new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = a.Low, Maximum = a.High, SmallChange = 1, LargeChange = Math.Max(1, (a.High - a.Low) / 100), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(slider, names[i]);
            var box = new TextBox { Margin = new Thickness(0, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, names[i] + " " + T("value"));
            slider.ValueChanged += (_, _) => { if (!syncing) Change(which, (int)Math.Round(slider.Value)); };
            box.TextChanged += (_, _) =>
            {
                if (!syncing && int.TryParse(box.Text, out int n) && n >= a.Low && n <= a.High) Change(which, n);
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Small(T("Here"), () => { if (AxisAt(selected) is { } x && x.Index < raw.Length) Change(which, raw[x.Index]); }, F("{0}: where it is now", names[i])));
            if (i == 1) buttons.Children.Add(Small(T("Midpoint"), () => Change(1, Cal.Straight(Now.Min, Now.Max).Center), T("Halfway: a straight line")));
            Put(rows, label, i, 0);
            Put(rows, slider, i, 1);
            Put(rows, box, i, 2);
            Put(rows, buttons, i, 3);
            fields[i] = (slider, box);
        }
        s.Children.Add(rows);

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var ignore = new CheckBox { Content = T("Leave out of Find Ranges"), IsChecked = settings.Ignored.Contains(key), VerticalAlignment = VerticalAlignment.Center };
        ignore.Click += (_, _) =>
        {
            if (ignore.IsChecked == true) settings.Ignored.Add(key); else settings.Ignored.Remove(key);
            settings.Save();
            lit.Visibility = find.IsChecked == true && ignore.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        };
        DockPanel.SetDock(ignore, Dock.Right);
        bottom.Children.Add(ignore);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Small(T("Reset"), () =>
        {
            finders.GetValueOrDefault(selected)?.Reset();
            byHand.Remove(selected);
            Fill(saved.GetValueOrDefault(selected, a.Default));
        }, T("Back to what Windows has, and find its range again")));
        left.Children.Add(Small(T("Default"), () => Fill(a.Default), T("Windows' own: the whole range, uncalibrated")));
        original = Small(T("Original"), () => { if (settings.Original.TryGetValue(key, out var o)) Fill(o); }, T("As it was before Daisy's App changed it"));
        original.Visibility = settings.Original.ContainsKey(key) ? Visibility.Visible : Visibility.Collapsed;
        left.Children.Add(original);
        bottom.Children.Add(left);
        s.Children.Add(bottom);

        ShowValues(-1);
        Tick();
    }

    /// <summary>The axis picked's calibration as it'll be: the change, or what Windows has.</summary>
    private Cal Now => pending.TryGetValue(selected, out var c) ? c : saved.GetValueOrDefault(selected, AxisAt(selected)?.Default ?? default);

    private void Change(int which, int value)
    {
        if (AxisAt(selected) is not { } a) return;
        value = Math.Clamp(value, a.Low, a.High);
        var c = Now;
        Pend(selected, which switch
        {
            0 => c with { Min = value },
            1 => c with { Center = value },
            _ => c with { Max = value },
        });
        byHand.Add(selected);
        ShowValues(which);
    }

    private void Fill(Cal c)
    {
        Pend(selected, c);
        ShowValues(-1);
    }

    private void Pend(int place, Cal c)
    {
        bool had = pending.ContainsKey(place);
        if (saved.TryGetValue(place, out var s) && s == c) pending.Remove(place);
        else pending[place] = c;
        Buttons();
        if (had != pending.ContainsKey(place)) Changed?.Invoke();
    }

    /// <summary>The axis picked's min, centre and max everywhere but the field being typed in (<paramref name="typing"/>).</summary>
    private void ShowValues(int typing)
    {
        if (fields == null || graph == null || calText == null) return;
        var c = Now;
        syncing = true;
        int[] v = { c.Min, c.Center, c.Max };
        for (int i = 0; i < 3; i++)
        {
            fields[i].Slider.Value = v[i];
            if (i != typing || !fields[i].Box.IsKeyboardFocusWithin) fields[i].Box.Text = v[i].ToString();
        }
        syncing = false;
        graph.Cal = c;
        var a = AxisAt(selected);
        var now = saved.GetValueOrDefault(selected, a?.Default ?? default);
        string windows = a != null && now == a.Default ? T("Not calibrated") : now.ToString();
        calText.Text = !pending.ContainsKey(selected) ? windows
            : !c.Valid ? T("Min, centre and max must go up in that order")
            : $"{windows}  →  {c}";
        if (pending.ContainsKey(selected) && !c.Valid) calText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else calText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        if (rawBar != null && a != null)
        {
            double range = Math.Max(1, a.High - a.Low);
            rawBar.Marks = new[] { (c.Min - a.Low) / range, (c.Center - a.Low) / range, (c.Max - a.Low) / range };
        }
    }

    // ---------------------------------------------------------------- live

    /// <summary>About 30 times a second while the tab shows (Devices' tick).</summary>
    public void Tick()
    {
        if (!Open() || device is not { } dev) return;
        if (!dev.Read(raw, output))
        {
            Logging.Log.Here.Info($"{info?.Name} went while calibrating (unplugged)");
            Close();
            failed = true;
            Status(T("Not connected"));
            ShowAxis();
            Changed?.Invoke();
            return;
        }
        var now = DateTime.UtcNow;
        bool finding = find.IsChecked == true, moved = false, found = false;
        foreach (var a in dev.Axes)
        {
            int place = a.Offset / 4, r = raw[a.Index];
            var m = motion.GetValueOrDefault(place);
            bool was = Moving(place);
            if (m.Last is int last && Math.Abs(r - last) >= Math.Max(1, (a.High - a.Low) / 100)) m.MovedAt = now;
            m.Last = r;
            motion[place] = m;
            moved |= was != Moving(place);
            if (!finding || byHand.Contains(place) || settings.Ignored.Contains(CalibrationSettings.KeyOf(info!, a)) || finders.GetValueOrDefault(place) is not { } f) continue;
            f.See(r);
            if (f.Found && (!pending.TryGetValue(place, out var p) || p != f.Result))
            {
                Pend(place, f.Result);
                found |= place == selected;
            }
        }
        if (found) ShowValues(-1);
        if (moved) Changed?.Invoke();

        if (AxisAt(selected) is not { } sel || rawBar == null || outBar == null || graph == null) return;
        int v = raw[sel.Index];
        double range = Math.Max(1, sel.High - sel.Low);
        rawBar.Value = (v - sel.Low) / range;
        rawText!.Text = v.ToString();
        outBar.Value = output[sel.Index];
        outText!.Text = $"{output[sel.Index] * 100:0.0}%";
        graph.Live = v;
        var finder = finders.GetValueOrDefault(selected);
        if (finding && finder is { Low: int lo, High: int hi } && !byHand.Contains(selected))
        {
            rawBar.Span = ((lo - sel.Low) / range, (hi - sel.Low) / range);
            graph.Seen = (lo, hi);
        }
        else
        {
            rawBar.Span = null;
            graph.Seen = null;
        }
        if (light != null && lightText != null)
        {
            string text = byHand.Contains(selected) ? T("Set by hand") : finder?.Found == true ? T("Range found") : T("Move it");
            if (lightText.Text != text)
            {
                lightText.Text = text;
                if (byHand.Contains(selected)) light.SetResourceReference(Shape.FillProperty, "TextSecondaryBrush");
                else if (finder?.Found == true) light.SetResourceReference(Shape.FillProperty, "SuccessBrush");
                else light.Fill = Amber;
            }
        }
    }

    // ---------------------------------------------------------------- Apply, Cancel

    private void ApplyAll()
    {
        if (device is not { } dev || info == null) return;
        var wrong = new List<string>();
        foreach (var (place, cal) in pending.OrderBy(p => p.Key).ToList())
        {
            if (AxisAt(place) is not { } a) continue;
            string key = CalibrationSettings.KeyOf(info, a);
            var before = dev.Get(a);
            if (!settings.Original.ContainsKey(key)) settings.Original[key] = before;
            string? problem = dev.Set(a, cal);
            if (problem == null)
            {
                Logging.Log.Here.Info($"Calibrated {info.Name} {a.Name}: {before} → {cal}");
                pending.Remove(place);
                saved[place] = cal;
            }
            else wrong.Add($"{a.Name}: {problem}");
        }
        settings.Save();
        if (wrong.Count == 0)
        {
            byHand.Clear();
            find.IsChecked = false;
        }
        Status(wrong.Count == 0 ? null : string.Join("\n", wrong));
        ShowAxis();
        Changed?.Invoke();
    }

    private void DropAll()
    {
        pending.Clear();
        byHand.Clear();
        foreach (var f in finders.Values) f.Reset();
        find.IsChecked = false;
        Status(null);
        ShowAxis();
        Changed?.Invoke();
    }

    private static Button Small(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
        b.Click += (_, _) => click();
        AutomationProperties.SetName(b, text);
        return b;
    }

    private static void Put(Grid g, UIElement e, int row, int column)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        g.Children.Add(e);
    }
}
