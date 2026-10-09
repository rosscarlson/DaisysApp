using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Performance;

/// <summary>Saved in Performance.json.</summary>
public sealed class PerformanceSettings
{
    public bool LogEnabled { get; set; } = true;
    public int KeepDays { get; set; } = 30;

    /// <summary>Orange / red levels that differ from the defaults: key → [orange, red].</summary>
    public Dictionary<string, double[]> Limits { get; set; } = new();

    /// <summary>How often the process list refreshes while the Performance tab is showing.</summary>
    public int ProcessRefreshMs { get; set; } = 1000;

    /// <summary>The hosts the Network Tests card pings once a second.</summary>
    public List<NetHost> NetHosts { get; set; } = NetHost.Defaults();

    /// <summary>The cards' checkboxes: off sends nothing at all (no pings / no speed tests, scheduled or not).</summary>
    public bool NetTestsOn { get; set; } = true;
    public bool SpeedTestOn { get; set; } = true;

    /// <summary>Speed tests on a schedule (Speed Test → gear).</summary>
    public bool SpeedTestEnabled { get; set; } = true;
    public int SpeedTestMinutes { get; set; } = 10;
    /// <summary>Seconds of download, then the same of upload, counted from when data starts arriving.</summary>
    public int SpeedTestSeconds { get; set; } = 10;

    /// <summary>A result below these speeds (Mbit/s) or above this latency (ms) shows orange / red; 0 turns one off.</summary>
    public double SpeedWarnDown { get; set; } = 100;
    public double SpeedBadDown { get; set; } = 25;
    public double SpeedWarnUp { get; set; } = 20;
    public double SpeedBadUp { get; set; } = 5;
    public double SpeedWarnLatency { get; set; } = 100;
    public double SpeedBadLatency { get; set; } = 250;

    public static PerformanceSettings Load() => JsonStore.Load<PerformanceSettings>("Performance");
    public void Save() => JsonStore.Save("Performance", this);
}

public partial class PerformanceSettingsView : UserControl
{
    private static readonly int[] KeepOptions = { 7, 14, 30, 90, 365 };
    private readonly PerformanceSettings settings;
    private readonly PerfLog log;
    private readonly PerfMonitor monitor;
    private readonly DispatcherTimer disarm = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly bool loading;
    private bool armed;

    public PerformanceSettingsView(PerformanceSettings settings, PerfLog log, PerfMonitor monitor)
    {
        this.settings = settings;
        this.log = log;
        this.monitor = monitor;
        loading = true;
        InitializeComponent();
        LogBox.IsChecked = settings.LogEnabled;
        foreach (int d in KeepOptions) KeepBox.Items.Add(new ComboBoxItem { Content = d == 365 ? T("1 year") : F("{0} days", d), Tag = d });
        KeepBox.SelectedItem = KeepBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.KeepDays) ?? KeepBox.Items[2];
        loading = false;
        disarm.Tick += (_, _) => Disarm();
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowSize(); };
    }

    private void ShowSize()
    {
        var days = PerfLog.Days();
        SizeText.Text = days.Count == 0
            ? T("Nothing logged yet.")
            : (days.Count == 1 ? F("{0} day logged, from {1:d}, {2:0.0} MB.", days.Count, days[0], PerfLog.SizeBytes() / 1e6) : F("{0} days logged, from {1:d}, {2:0.0} MB.", days.Count, days[0], PerfLog.SizeBytes() / 1e6));
    }

    private void LogBox_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        settings.LogEnabled = log.Enabled = LogBox.IsChecked == true;
        settings.Save();
    }

    private void KeepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || KeepBox.SelectedItem is not ComboBoxItem { Tag: int d }) return;
        settings.KeepDays = log.KeepDays = d;
        settings.Save();
        log.Cleanup();
        ShowSize();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(PerfLog.Folder);
        Process.Start("explorer.exe", PerfLog.Folder);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!armed)
        {
            armed = true;
            DeleteButton.Style = (Style)FindResource("DangerButton");
            DeleteButton.Content = T("Click again to delete");
            disarm.Start();
            return;
        }
        log.DeleteAll();
        Disarm();
        ShowSize();
    }

    private void Disarm()
    {
        armed = false;
        disarm.Stop();
        DeleteButton.ClearValue(StyleProperty);
        DeleteButton.Content = T("Delete the log");
    }
}
