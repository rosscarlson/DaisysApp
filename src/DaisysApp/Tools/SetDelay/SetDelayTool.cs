using System.Windows;
using System.Windows.Input;
using DaisysApp.Shell;

namespace DaisysApp.Tools.SetDelay;

/// <summary>Set Delay tab: syncs two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker) with output delay.</summary>
public sealed class SetDelayTool : ITool
{
    private readonly SetDelaySettings settings = SetDelaySettings.Load();
    private readonly SetDelayView view;

    public SetDelayTool() => view = new SetDelayView(settings);

    public string Id => "SetDelay";
    public string Title => "Set Delay";
    public string Icon => ""; // stopwatch
    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }
    public void OnPreviewKeyDown(KeyEventArgs e) => view.HandlePreviewKeyDown(e);
    public void OnWindowHidden() => view.OnWindowHidden();
    public void SaveSettings() => settings.Save();
    public void Dispose() => view.Shutdown();
}
