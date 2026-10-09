using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Performance;

/// <summary>
/// Performance tab: live graphs of CPU, GPU, memory, video memory, disk, network and GPU temperature, per-core load,
/// the process list and system details; click any graph or process for its history, from a log kept in the background.
/// </summary>
[Applet("Performance", "Performance", "", Order = 1,
    Description = "Live graphs of CPU, GPU, memory, disk, network and temperature, processes, and a log with history")]
public sealed class PerformanceApplet : IApplet
{
    private readonly PerformanceSettings settings = PerformanceSettings.Load();
    private readonly PerfLog log;
    private readonly PerfMonitor monitor;
    private readonly PerformanceView view;
    private readonly PerformanceSettingsView settingsView;
    private readonly PingMonitor pings;
    private readonly SpeedTester speed;

    public PerformanceApplet()
    {
        log = new PerfLog { Enabled = settings.LogEnabled, KeepDays = settings.KeepDays };
        monitor = new PerfMonitor(log) { ProcessIntervalMs = settings.ProcessRefreshMs };
        var limits = new PerfLimits(settings);
        limits.Changed += () => monitor.ProcessIntervalMs = settings.ProcessRefreshMs;
        pings = new PingMonitor(settings);
        speed = new SpeedTester(settings);
        view = new PerformanceView(monitor, log, limits, settings, pings, speed);
        settingsView = new PerformanceSettingsView(settings, log, monitor);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        log.Cleanup();
        monitor.Start();
        pings.Start();
        speed.Start();
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        view.Shutdown();
        monitor.Dispose();
        pings.Dispose();
        speed.Dispose();
    }
}
