using System.Windows;
using System.Windows.Input;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AudioLevel;

/// <summary>Audio Leveler tab: test signals, per-speaker level knobs and microphone leveling.</summary>
[Applet("AudioLevel", "Audio Leveler", "", Order = 10,
    Description = "Test signals, per-speaker levels and the auto-level wizard (uses Voicemeeter)")]
public sealed class AudioLevelApplet : IApplet
{
    private readonly AudioLevelSettings settings = AudioLevelSettings.Load();
    private readonly AudioLevelView view;
    private readonly AudioLevelSettingsView settingsView;

    public AudioLevelApplet()
    {
        view = new AudioLevelView(settings);
        settingsView = new AudioLevelSettingsView(view, settings);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start() { } // the view connects to the audio devices (and Voicemeeter) as it's created

    public void OnPreviewKeyDown(KeyEventArgs e) => view.HandlePreviewKeyDown(e);
    public void OnWindowHidden() => view.OnWindowHidden();
    public void SaveSettings() => settings.Save();
    public void Dispose() => view.Shutdown();
}
