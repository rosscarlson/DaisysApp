using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>
/// Stress Tests, a sub-tab of the Support tab: CPU, memory, storage, GPU and video tests that score a PC and load it
/// hard enough to show what breaks, logged to the Support log (Standard or Maximum), each run kept, exported and
/// compared with another (this PC's or another's) in a report.
/// </summary>
public sealed class StressApplet : IApplet
{
    private readonly StressSettings settings = StressSettings.Load();
    private readonly Runner runner;
    private readonly StressView view;

    public StressApplet()
    {
        runner = new Runner(settings);
        view = new StressView(settings, runner);
    }

    public FrameworkElement View => view;

    public FrameworkElement? SettingsView => null;

    public void Start() { }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        runner.Stop();
        view.Dispose();
        settings.Save();
    }
}
