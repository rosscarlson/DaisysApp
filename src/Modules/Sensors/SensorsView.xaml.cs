using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Sensors;

/// <summary>The Sensors tab: every LibreHardwareMonitor sensor, or how to get them.</summary>
public partial class SensorsView : UserControl
{
    private readonly SensorsService service;
    private readonly SensorPanel panel;

    internal SensorsView(SensorsService service)
    {
        this.service = service;
        InitializeComponent();
        panel = new SensorPanel(SensorFilters, SensorGroups, Open);
        service.Updated += Show;
        IsVisibleChanged += (_, _) => { if (IsVisible) Show(service.Latest); };
        Show(service.Latest);
    }

    private void Show(HwSnapshot? snapshot)
    {
        SensorCard.Visibility = snapshot != null ? Visibility.Visible : Visibility.Collapsed;
        SensorBanner.Visibility = snapshot == null ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot == null)
            SensorBannerText.Text = HardwareMonitor.ProcessRunning()
                ? T("LibreHardwareMonitor is running, but its web server is off, so Daisy's App can't read its sensors (temperatures, fans, voltages, power).")
                : T("CPU, motherboard, memory and drive temperatures, fan speeds, voltages and power need LibreHardwareMonitor.");
        // the numbers only change on screen; skip the work while the tab is hidden
        else if (IsVisible) panel.Update(snapshot);
    }

    private void Setup_Click(object sender, RoutedEventArgs e) =>
        new SensorSetupWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void Open(IReadOnlyList<HwSensor> sensors, string title) =>
        new SensorHistoryWindow(service, sensors, title) { Owner = Window.GetWindow(this) }.Show();
}
