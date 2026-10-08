using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Applets.Performance;

/// <summary>Speed Test → gear: the schedule, how long each test runs, and when the card turns orange or red.</summary>
public partial class SpeedSettingsWindow : Window
{
    private static readonly int[] Intervals = [5, 10, 15, 30, 60, 180, 360, 720, 1440];
    private static readonly int[] Lengths = [1, 2, 3, 5, 10, 15, 20, 30];

    private readonly PerformanceSettings settings;
    private readonly SpeedResult? last;
    private readonly TextBox downWarn, downBad, upWarn, upBad, latencyWarn, latencyBad;
    private readonly bool ready;

    /// <param name="last">The latest successful result, for the data estimate.</param>
    public SpeedSettingsWindow(PerformanceSettings settings, SpeedResult? last)
    {
        this.settings = settings;
        this.last = last;
        InitializeComponent();

        foreach (int m in Intervals)
            IntervalBox.Items.Add(new ComboBoxItem { Content = m < 60 ? F("{0} minutes", m) : m == 60 ? T("hour") : m == 1440 ? T("day") : F("{0} hours", m / 60), Tag = m });
        foreach (int s in Lengths)
            LengthBox.Items.Add(new ComboBoxItem { Content = s == 1 ? T("1 second") : F("{0} seconds", s), Tag = s });

        AddHeader();
        (downWarn, downBad) = AddRow(T("Download below"), "Mbit/s");
        (upWarn, upBad) = AddRow(T("Upload below"), "Mbit/s");
        (latencyWarn, latencyBad) = AddRow(T("Latency above"), "ms");

        Show(settings.SpeedTestEnabled, settings.SpeedTestMinutes, settings.SpeedTestSeconds,
            settings.SpeedWarnDown, settings.SpeedBadDown, settings.SpeedWarnUp, settings.SpeedBadUp, settings.SpeedWarnLatency, settings.SpeedBadLatency);
        ready = true;
        UpdateText();
    }

    private void Show(bool enabled, int minutes, int seconds, double dw, double db, double uw, double ub, double lw, double lb)
    {
        ScheduleBox.IsChecked = enabled;
        IntervalBox.SelectedItem = IntervalBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == minutes) ?? IntervalBox.Items[1];
        LengthBox.SelectedItem = LengthBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == seconds) ?? LengthBox.Items[4];
        downWarn.Text = Num(dw); downBad.Text = Num(db);
        upWarn.Text = Num(uw); upBad.Text = Num(ub);
        latencyWarn.Text = Num(lw); latencyBad.Text = Num(lb);
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        IntervalBox.IsEnabled = ScheduleBox.IsChecked == true;
        UpdateText();
    }

    private int Seconds => LengthBox.SelectedItem is ComboBoxItem { Tag: int s } ? s : 10;
    private int Minutes => IntervalBox.SelectedItem is ComboBoxItem { Tag: int m } ? m : 10;

    /// <summary>How the length is timed, and roughly how much data it moves at the last result's speeds.</summary>
    private void UpdateText()
    {
        IntervalBox.IsEnabled = ScheduleBox.IsChecked == true;
        int s = Seconds;
        string text = F("Each way, the {0}-second clock starts when data starts arriving, so a slow start doesn't shorten the test. ", s) +
                      F("If nothing arrives (or it stops) for {0} seconds, the test fails.", SpeedTester.NoDataSeconds);
        if (last != null && !double.IsNaN(last.DownMbps) && !double.IsNaN(last.UpMbps))
        {
            double mb = (last.DownMbps + last.UpMbps) * s / 8;
            text += F(" At your last result ({0:0} down, {1:0} up Mbit/s) each test moves about {2}", last.DownMbps, last.UpMbps, Data(mb));
            text += ScheduleBox.IsChecked == true ? F(", {0} a day on this schedule.", Data(mb * 1440 / Minutes)) : ".";
        }
        else text += T(" On a 1 Gbit/s connection that's about 125 MB a second each way.");
        text += T(" While it runs it fills your connection, so games and calls may stutter.");
        LengthText.Text = text;
    }

    private static string Data(double mb) => mb >= 1000 ? $"{mb / 1000:0.#} GB" : $"{mb:0} MB";

    private void AddHeader()
    {
        LevelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(Label(T("Orange"), "#F7A541"), 0, 1);
        Place(Label(T("Red"), "DangerBrush"), 0, 2);
    }

    private (TextBox Warn, TextBox Bad) AddRow(string name, string unit)
    {
        int row = LevelGrid.RowDefinitions.Count;
        LevelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 0) }, row, 0);
        var warn = Box(unit, out var warnPanel);
        var bad = Box(unit, out var badPanel);
        Place(warnPanel, row, 1);
        Place(badPanel, row, 2);
        return (warn, bad);
    }

    private static TextBox Box(string unit, out FrameworkElement panel)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 6, 10, 0) };
        var unitText = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), MinWidth = 40 };
        unitText.SetResourceReference(StyleProperty, "SecondaryText");
        DockPanel.SetDock(unitText, Dock.Right);
        dock.Children.Add(unitText);
        var box = new TextBox { HorizontalContentAlignment = HorizontalAlignment.Right };
        dock.Children.Add(box);
        panel = dock;
        return box;
    }

    private static FrameworkElement Label(string text, string swatch)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        if (swatch.StartsWith('#')) dot.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(swatch));
        else dot.SetResourceReference(Border.BackgroundProperty, swatch);
        panel.Children.Add(dot);
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        panel.Children.Add(t);
        return panel;
    }

    private void Place(FrameworkElement e, int row, int col)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        LevelGrid.Children.Add(e);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var d = new PerformanceSettings();
        Show(d.SpeedTestEnabled, d.SpeedTestMinutes, d.SpeedTestSeconds,
            d.SpeedWarnDown, d.SpeedBadDown, d.SpeedWarnUp, d.SpeedBadUp, d.SpeedWarnLatency, d.SpeedBadLatency);
        UpdateText();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Read(downWarn, downBad, T("Download"), higherIsWorse: false, out double dw, out double db) ||
            !Read(upWarn, upBad, T("Upload"), higherIsWorse: false, out double uw, out double ub) ||
            !Read(latencyWarn, latencyBad, T("Latency"), higherIsWorse: true, out double lw, out double lb))
            return;

        settings.SpeedTestEnabled = ScheduleBox.IsChecked == true;
        settings.SpeedTestMinutes = Minutes;
        settings.SpeedTestSeconds = Seconds;
        (settings.SpeedWarnDown, settings.SpeedBadDown) = (dw, db);
        (settings.SpeedWarnUp, settings.SpeedBadUp) = (uw, ub);
        (settings.SpeedWarnLatency, settings.SpeedBadLatency) = (lw, lb);
        settings.Save();
        DialogResult = true;
    }

    private bool Read(TextBox warnBox, TextBox badBox, string name, bool higherIsWorse, out double warn, out double bad)
    {
        bad = 0;
        if (!double.TryParse(warnBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out warn) ||
            !double.TryParse(badBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out bad) || warn < 0 || bad < 0)
            return Fail(F("{0}: enter numbers (0 turns one off).", name));
        if (warn > 0 && bad > 0 && (higherIsWorse ? bad < warn : bad > warn))
            return Fail(higherIsWorse ? F("{0}: red has to be at or above orange.", name) : F("{0}: red has to be at or below orange.", name));
        return true;
    }

    private bool Fail(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
        return false;
    }
}
