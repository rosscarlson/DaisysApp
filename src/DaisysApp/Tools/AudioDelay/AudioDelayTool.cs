using System.Windows;
using System.Windows.Input;
using DaisysApp.Shell;

namespace DaisysApp.Tools.AudioDelay;

/// <summary>Audio Delay tab: syncs two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker) with output delay.</summary>
public sealed class AudioDelayTool : ITool
{
    private readonly AudioDelaySettings settings = AudioDelaySettings.Load();
    private readonly AudioDelayView view;

    public AudioDelayTool() => view = new AudioDelayView(settings);

    public string Id => "AudioDelay";
    public string Title => "Audio Delay";
    public string Icon => ""; // stopwatch
    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }
    public void OnPreviewKeyDown(KeyEventArgs e) => view.HandlePreviewKeyDown(e);
    public void OnWindowHidden() => view.OnWindowHidden();
    public void SaveSettings() => settings.Save();
    public void Dispose() => view.Shutdown();
}
