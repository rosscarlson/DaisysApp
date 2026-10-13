using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Network.Logs;

/// <summary>Logs, a sub-tab of the Network tab: Daisy's App's own logs, Windows' event logs and WSL's, each in a tab of its own (see README.md).</summary>
public sealed class LogsApplet : IApplet
{
    private readonly LogsSettings settings = LogsSettings.Load();
    private readonly LogsView view;

    public LogsApplet() => view = new LogsView(settings);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() => Log.Here.Debug($"Open tabs: {string.Join(", ", settings.Open)}");

    public void SaveSettings() => settings.Save();

    public void Dispose() => view.Dispose();
}
