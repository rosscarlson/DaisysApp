using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.QuickTools;

/// <summary>
/// Tools, a sub-tab of the Tools tab (since 3.20.2): buttons for Windows' resets and admin jobs (the graphics driver's
/// reset as Win+Ctrl+Shift+B, Explorer, DNS, the network, audio, the clock, power) and Windows' own admin tools.
/// No settings.
/// </summary>
public sealed class QuickToolsApplet : IApplet
{
    private readonly QuickToolsView view = new();

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }

    public void SaveSettings() { }

    public void Dispose() { }
}
