using System.Windows;
using System.Windows.Input;
using DaisysApp.Applets.AudioTools.Delay;
using DaisysApp.Applets.AudioTools.Levels;
using DaisysApp.Applets.AudioTools.Tests;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AudioTools;

/// <summary>
/// Audio Tools tab: three tools, each on its own sub-tab. <b>Levels</b>: every device's and app's volume and mute, with
/// shortcuts. <b>Tests</b>: test signals, per-speaker levels and the auto-level and EQ wizards (Voicemeeter).
/// <b>Delay</b>: syncs two Voicemeeter outputs with output delay. Each keeps its own settings file, as when they were
/// separate tabs; the Id stays "AudioLevel" (Tests was the Audio Tools tab before the others joined it).
/// </summary>
[Applet("AudioLevel", "Audio Tools", "", Order = 10,
    Description = "Levels (every device's and app's volume, with shortcuts), Tests (test signals, speaker levels, auto-level) and Delay (syncs two Voicemeeter outputs)")]
public sealed class AudioToolsApplet : IApplet
{
    private readonly AudioToolsSettings toolsSettings = AudioToolsSettings.Load();
    private readonly AudioLevelsSettings levelsSettings = AudioLevelsSettings.Load();
    private readonly AudioLevelSettings testsSettings = AudioLevelSettings.Load();
    private readonly AudioDelaySettings delaySettings = AudioDelaySettings.Load();
    private readonly VolumeHotkeyService hotkeys;
    private readonly AudioLevelsView levels;
    private readonly AudioLevelView tests;
    private readonly AudioDelayView delay;
    private readonly AudioToolsView view;
    private readonly AudioLevelSettingsView settingsView;

    public AudioToolsApplet()
    {
        hotkeys = new VolumeHotkeyService(levelsSettings);
        levels = new AudioLevelsView(levelsSettings, hotkeys);
        tests = new AudioLevelView(testsSettings);
        delay = new AudioDelayView(delaySettings);
        settingsView = new AudioLevelSettingsView(tests, testsSettings);
        view = new AudioToolsView(toolsSettings, new (string, string, FrameworkElement)[]
        {
            (AudioToolsSettings.Levels, T("Levels"), levels),
            (AudioToolsSettings.Tests, T("Tests"), tests),
            (AudioToolsSettings.Delay, T("Delay"), delay),
        });
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        hotkeys.Register();
        levels.Refresh(force: true);
        // Tests and Delay connect to the audio devices (and Voicemeeter) as they're created
    }

    /// <summary>Keyboard shortcuts go to the tool that's showing.</summary>
    public void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (view.Current == AudioToolsSettings.Tests) tests.HandlePreviewKeyDown(e);
        else if (view.Current == AudioToolsSettings.Delay) delay.HandlePreviewKeyDown(e);
    }

    public void OnWindowHidden()
    {
        tests.OnWindowHidden();
        delay.OnWindowHidden();
    }

    public void SaveSettings()
    {
        toolsSettings.Save();
        levelsSettings.Save(); // the volumes themselves live in Windows
        testsSettings.Save();
        delaySettings.Save();
    }

    public void Dispose()
    {
        hotkeys.Dispose();
        levels.Shutdown();
        tests.Shutdown();
        delay.Shutdown();
    }
}
