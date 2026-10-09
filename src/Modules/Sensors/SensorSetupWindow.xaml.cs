using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Sensors;

/// <summary>How to get LibreHardwareMonitor's sensors, with a live check that turns green once it's answering.</summary>
public partial class SensorSetupWindow : Window
{
    private readonly DispatcherTimer recheck = new() { Interval = TimeSpan.FromSeconds(3) };

    public SensorSetupWindow()
    {
        InitializeComponent();
        SensorHub.Updated += OnSensors;
        recheck.Tick += (_, _) => { if (SensorHub.Latest == null) { SensorHub.CheckNow(); ShowStatus(); } };
        recheck.Start();
        Closed += (_, _) =>
        {
            recheck.Stop();
            SensorHub.Updated -= OnSensors;
        };
        ShowStatus();
    }

    private void OnSensors(HwSnapshot? snapshot) => Dispatcher.BeginInvoke(ShowStatus);

    private void ShowStatus()
    {
        var latest = SensorHub.Latest;
        StatusText.Text = latest != null
            ? F("Connected — reading {0} sensors.", latest.Sensors.Count)
            : HardwareMonitor.ProcessRunning()
                ? F("LibreHardwareMonitor is running, but its web server isn't answering at {0} (step 3).", SensorHub.Address)
                : T("LibreHardwareMonitor isn't running.");
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, latest != null ? "SuccessBrush" : "ControlBorderBrush");
    }

    private void Download_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(HardwareMonitor.DownloadPage) { UseShellExecute = true });

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        SensorHub.CheckNow();
        StatusText.Text = T("Checking…");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
