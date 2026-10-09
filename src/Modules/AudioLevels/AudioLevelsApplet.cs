using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AudioLevels;

/// <summary>
/// Audio Levels tab: every playback and recording device's Windows volume and mute, each app's own volume, live, and
/// volume up / down / mute shortcuts (keys or controller buttons) for any of them.
/// </summary>
[Applet("AudioLevels", "Audio Levels", "", Order = 15,
    Description = "Volume and mute for every playback and recording device and every app, with shortcuts, kept up to date")]
public sealed class AudioLevelsApplet : IApplet
{
    private readonly AudioLevelsSettings settings = AudioLevelsSettings.Load();
    private readonly VolumeHotkeyService hotkeys;
    private readonly AudioLevelsView view;

    public AudioLevelsApplet()
    {
        hotkeys = new VolumeHotkeyService(settings);
        view = new AudioLevelsView(settings, hotkeys);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        hotkeys.Register();
        view.Refresh(force: true);
    }

    public void SaveSettings() => settings.Save(); // the volumes themselves live in Windows
    public void Dispose()
    {
        hotkeys.Dispose();
        view.Shutdown();
    }
}
