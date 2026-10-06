using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace DaisysApp.Applets.Performance;

/// <summary>How to get LibreHardwareMonitor's sensors, with a live check that turns green once it's answering.</summary>
public partial class SensorSetupWindow : Window
{
    private readonly PerfMonitor monitor;
    private readonly DispatcherTimer recheck = new() { Interval = TimeSpan.FromSeconds(3) };

    public SensorSetupWindow(PerfMonitor monitor)
    {
        this.monitor = monitor;
        InitializeComponent();
        monitor.SensorsUpdated += OnSensors;
        recheck.Tick += (_, _) => { if (monitor.Sensors == null) { monitor.CheckSensorsNow(); ShowStatus(); } };
        recheck.Start();
        Closed += (_, _) =>
        {
            recheck.Stop();
            monitor.SensorsUpdated -= OnSensors;
        };
        ShowStatus();
    }

    private void OnSensors(HwSnapshot? snapshot) => ShowStatus();

    private void ShowStatus()
    {
        bool connected = monitor.Sensors != null;
        StatusText.Text = connected
            ? $"Connected — reading {monitor.Sensors!.Sensors.Count} sensors."
            : HardwareMonitor.ProcessRunning()
                ? $"LibreHardwareMonitor is running, but its web server isn't answering at {monitor.HardwareAddress} (step 3)."
                : "LibreHardwareMonitor isn't running.";
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, connected ? "SuccessBrush" : "ControlBorderBrush");
    }

    private void Download_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(HardwareMonitor.DownloadPage) { UseShellExecute = true });

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        monitor.CheckSensorsNow();
        StatusText.Text = "Checking…";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
