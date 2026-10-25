using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Calibration;

/// <summary>
/// One axis's calibration: min, centre and max (each a slider over the axis's whole range, a number, and Here for
/// where it is now; Midpoint puts the centre halfway for a straight line), drawn live on <see cref="CalGraph"/>.
/// Default fills in Windows' own (uncalibrated), Original what it had before Daisy's App first changed it. Nothing
/// reaches Windows until Save.
/// </summary>
internal sealed class AxisWindow : Window
{
    private readonly Live live;
    private readonly CalibrationSettings settings;
    private readonly int offset;
    private readonly Axis axis;
    private readonly string key;
    private readonly CalGraph graph = new();
    private readonly (Slider Slider, TextBox Box)[] fields = new (Slider, TextBox)[3];
    private readonly TextBlock note = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button save;
    private readonly Button original;
    private Cal pending, saved;
    private bool syncing, didSave;

    public AxisWindow(Live live, CalibrationSettings settings, int offset)
    {
        this.live = live;
        this.settings = settings;
        this.offset = offset;
        axis = live.AxisAt(offset)!;
        key = CalibrationSettings.KeyOf(live.Info!, axis);
        saved = pending = live.Device!.Get(axis);

        Title = $"{live.Info!.Name} · {axis.Name}";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var page = new StackPanel { Margin = new Thickness(16) };
        graph.Axis = axis;
        page.Children.Add(graph);

        var rowsGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string[] names = { T("Min"), T("Centre"), T("Max") };
        for (int i = 0; i < 3; i++)
        {
            rowsGrid.RowDefinitions.Add(new RowDefinition());
            int which = i;
            var label = new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = axis.Low, Maximum = axis.High, SmallChange = 1, LargeChange = Math.Max(1, (axis.High - axis.Low) / 100), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(slider, names[i]);
            var box = new TextBox { Margin = new Thickness(0, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, names[i] + " " + T("value"));
            slider.ValueChanged += (_, _) => { if (!syncing) Change(which, (int)Math.Round(slider.Value)); };
            box.TextChanged += (_, _) =>
            {
                if (!syncing && int.TryParse(box.Text, out int n) && n >= axis.Low && n <= axis.High) Change(which, n);
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Small(T("Here"), () => { if (live.Device != null) Change(which, live.Raw[axis.Index]); }, F("{0}: where it is now", names[i])));
            if (i == 1) buttons.Children.Add(Small(T("Midpoint"), () => Change(1, Cal.Straight(pending.Min, pending.Max).Center), T("Halfway: a straight line")));
            Put(rowsGrid, label, i, 0);
            Put(rowsGrid, slider, i, 1);
            Put(rowsGrid, box, i, 2);
            Put(rowsGrid, buttons, i, 3);
            fields[i] = (slider, box);
        }
        page.Children.Add(rowsGrid);

        var ignore = new CheckBox { Content = T("Ignore in Wizard"), IsChecked = settings.Ignored.Contains(key), Margin = new Thickness(0, 10, 0, 0) };
        ignore.Click += (_, _) =>
        {
            if (ignore.IsChecked == true) settings.Ignored.Add(key); else settings.Ignored.Remove(key);
            settings.Save();
        };
        page.Children.Add(ignore);

        var bar = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = true };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Small(T("Default"), () => Fill(axis.Default), T("Windows' own: the whole range, uncalibrated")));
        original = Small(T("Original"), () => { if (settings.Original.TryGetValue(key, out var o)) Fill(o); }, T("As it was before Daisy's App changed it"));
        left.Children.Add(original);
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);
        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal };
        save = new Button { Content = T("Save"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(6, 0, 0, 0) };
        save.SetResourceReference(StyleProperty, "AccentButton");
        save.Click += (_, _) => Save();
        var close = new Button { Content = T("Close"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        rightButtons.Children.Add(save);
        rightButtons.Children.Add(close);
        DockPanel.SetDock(rightButtons, Dock.Right);
        bar.Children.Add(rightButtons);
        note.Margin = new Thickness(10, 0, 4, 0);
        bar.Children.Add(note);
        page.Children.Add(bar);
        Content = page;

        Fill(pending);
        live.Ticked += Tick;
        Closed += (_, _) => live.Ticked -= Tick;
        Tick();
    }

    private static Button Small(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
        b.Click += (_, _) => click();
        return b;
    }

    private static void Put(Grid g, UIElement e, int row, int column)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        g.Children.Add(e);
    }

    private void Change(int which, int value)
    {
        value = Math.Clamp(value, axis.Low, axis.High);
        pending = which switch
        {
            0 => pending with { Min = value },
            1 => pending with { Center = value },
            _ => pending with { Max = value },
        };
        Show(which);
    }

    private void Fill(Cal c)
    {
        pending = c;
        Show(-1);
    }

    /// <summary>Shows <see cref="pending"/> everywhere but the field being typed in (<paramref name="typing"/>).</summary>
    private void Show(int typing)
    {
        syncing = true;
        int[] v = { pending.Min, pending.Center, pending.Max };
        for (int i = 0; i < 3; i++)
        {
            fields[i].Slider.Value = v[i];
            if (i != typing || !fields[i].Box.IsKeyboardFocusWithin) fields[i].Box.Text = v[i].ToString();
        }
        syncing = false;
        graph.Cal = pending;
        ShowState(null);
    }

    private void ShowState(string? problem)
    {
        bool connected = live.Device != null;
        save.IsEnabled = connected && pending.Valid && pending != saved;
        original.Visibility = settings.Original.ContainsKey(key) ? Visibility.Visible : Visibility.Collapsed;
        if (problem != null) Note(problem, error: true);
        else if (!connected) Note(T("Not connected"), error: true);
        else if (!pending.Valid) Note(T("Min, centre and max must go up in that order"), error: true);
        else if (pending == saved) Note(didSave ? T("✓ Saved") : "", error: false, quiet: true);
        else Note(T("Not saved"), error: false, quiet: true);
    }

    private void Note(string text, bool error, bool quiet = false)
    {
        note.Text = text;
        note.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : quiet ? "TextSecondaryBrush" : "TextBrush");
    }

    private void Tick()
    {
        var dev = live.Device;
        graph.Live = dev != null && axis.Index < live.Raw.Length && live.AxisAt(offset) != null ? live.Raw[axis.Index] : null;
        bool connected = dev != null;
        if (save.IsEnabled != (connected && pending.Valid && pending != saved)) ShowState(null);
    }

    private void Save()
    {
        if (live.Device is not { } dev || live.AxisAt(offset) is not { } a) { ShowState(null); return; }
        var before = dev.Get(a);
        if (!settings.Original.ContainsKey(key))
        {
            settings.Original[key] = before;
            settings.Save();
        }
        string? problem = dev.Set(a, pending);
        if (problem == null)
        {
            saved = pending;
            didSave = true;
            Logging.Log.Here.Info($"Calibrated {live.Info?.Name} {a.Name}: {before} → {pending}");
        }
        ShowState(problem);
    }
}
