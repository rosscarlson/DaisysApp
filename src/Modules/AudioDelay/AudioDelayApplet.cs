using System.Windows;
using System.Windows.Input;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AudioDelay;

/// <summary>Audio Delay tab: syncs two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker) with output delay.</summary>
[Applet("AudioDelay", "Audio Delay", "", Order = 20,
    Description = "Syncs two Voicemeeter outputs (e.g. Bluetooth or VBAN) with output delay")]
public sealed class AudioDelayApplet : IApplet
{
    private readonly AudioDelaySettings settings = AudioDelaySettings.Load();
    private readonly AudioDelayView view;

    public AudioDelayApplet() => view = new AudioDelayView(settings);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }
    public void OnPreviewKeyDown(KeyEventArgs e) => view.HandlePreviewKeyDown(e);
    public void OnWindowHidden() => view.OnWindowHidden();
    public void SaveSettings() => settings.Save();
    public void Dispose() => view.Shutdown();
}
