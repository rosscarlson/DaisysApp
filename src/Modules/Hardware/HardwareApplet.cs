using DaisysApp.Applets.Hardware.Performance;
using DaisysApp.Applets.Hardware.Sensors;
using DaisysApp.Applets.Hardware.UsbMonitor;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Hardware;

/// <summary>
/// Hardware tab: three tools, each on its own sub-tab. <b>Performance</b>: live graphs, processes and a log with
/// history. <b>USB Monitor</b>: every device connect, disconnect and status change. <b>Sensors</b>: every
/// LibreHardwareMonitor sensor with history. Each keeps its own settings file, as when they were separate tabs
/// (before 0.22).
/// </summary>
[Applet("Hardware", "Hardware", "", Order = 1,
    Description = "Live performance graphs and processes with a log, every USB device change, and every temperature, fan and power sensor")]
public sealed class HardwareApplet : AppletGroup
{
    public const string Performance = "Performance", UsbMonitor = "UsbMonitor", Sensors = "Sensors";

    public HardwareApplet()
    {
        Add(Performance, T("Performance"), "", () => new PerformanceApplet());
        Add(UsbMonitor, T("USB Monitor"), "", () => new UsbMonitorApplet());
        Add(Sensors, T("Sensors"), "", () => new SensorsApplet());
    }
}
