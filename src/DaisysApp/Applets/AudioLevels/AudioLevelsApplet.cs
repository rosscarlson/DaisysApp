using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AudioLevels;

/// <summary>Audio Levels tab: every playback and recording device with its Windows volume and mute, live.</summary>
[Applet("AudioLevels", "Audio Levels", "", Order = 15,
    Description = "A volume slider and mute for every playback and recording device, kept up to date")]
public sealed class AudioLevelsApplet : IApplet
{
    private readonly AudioLevelsView view = new();

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() => view.Refresh(force: true);
    public void SaveSettings() { } // nothing to save: the volumes live in Windows
    public void Dispose() => view.Shutdown();
}
