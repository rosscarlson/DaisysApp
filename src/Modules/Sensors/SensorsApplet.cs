using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Sensors;

/// <summary>
/// Sensors tab: every temperature, fan, voltage, power, clock and load sensor LibreHardwareMonitor reports, grouped by
/// hardware, with history graphs and a log of temperatures, fans and power.
/// </summary>
[Applet("Sensors", "Sensors", "", Order = 3,
    Description = "Every temperature, fan, voltage and power sensor from LibreHardwareMonitor, with history graphs and a log")]
public sealed class SensorsApplet : IApplet
{
    private readonly SensorsService service = new();
    private readonly SensorsView view;
    private readonly SensorsSettingsView settingsView;

    public SensorsApplet()
    {
        view = new SensorsView(service);
        settingsView = new SensorsSettingsView(service);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start() => service.Start();
    public void SaveSettings() => service.Settings.Save();
    public void Dispose() => service.Dispose();
}
