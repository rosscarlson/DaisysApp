using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Hardware.Sensors;

/// <summary>
/// Sensors, a sub-tab of the Hardware tab: every temperature, fan, voltage, power, clock and load sensor LibreHardwareMonitor reports, grouped by
/// hardware, with history graphs and a log of temperatures, fans and power.
/// </summary>
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
