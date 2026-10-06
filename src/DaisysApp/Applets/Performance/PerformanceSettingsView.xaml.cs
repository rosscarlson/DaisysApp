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
        foreach (int d in KeepOptions) KeepBox.Items.Add(new ComboBoxItem { Content = d == 365 ? "1 year" : $"{d} days", Tag = d });
        KeepBox.SelectedItem = KeepBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.KeepDays) ?? KeepBox.Items[2];
        loading = false;
        disarm.Tick += (_, _) => Disarm();
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowSize(); };
    }

    private void ShowSize()
    {
        var days = PerfLog.Days();
        SizeText.Text = days.Count == 0
            ? "Nothing logged yet."
            : $"{days.Count} day{(days.Count == 1 ? "" : "s")} logged, from {days[0]:d}, {PerfLog.SizeBytes() / 1e6:0.0} MB.";
        SensorStatus.Text = (monitor.HasCpuSensors ? "CPU: reading LibreHardwareMonitor." : "CPU: LibreHardwareMonitor's web server isn't answering.")
            + (monitor.Info.HasGpuSensors ? " GPU: reading NVIDIA's driver." : " GPU: no NVIDIA driver, so no GPU temperature.");
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
            DeleteButton.Content = "Click again to delete";
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
        DeleteButton.Content = "Delete the log";
    }
}
