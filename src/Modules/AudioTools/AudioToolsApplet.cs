using System.Windows;
using System.Windows.Input;
using DaisysApp.Applets.AudioTools.Delay;
using DaisysApp.Applets.AudioTools.Levels;
using DaisysApp.Applets.AudioTools.Tests;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AudioTools;

/// <summary>
/// Audio tab: three tools, each on its own sub-tab. <b>Levels</b>: every device's and app's volume and mute, with
/// shortcuts. <b>Tests</b>: test signals, per-speaker levels and the auto-level and EQ wizards (Voicemeeter).
/// <b>Delay</b>: syncs two Voicemeeter outputs with output delay. Each keeps its own settings file, as when they were
/// separate tabs; the Id stays "AudioLevel" (Tests was the Audio tab before the others joined it).
/// </summary>
[Applet("AudioLevel", "Audio", "", Order = 10,
    Description = "Every device's and app's volume with shortcuts, speaker tests and leveling, and syncing two Voicemeeter outputs")]
public sealed class AudioToolsApplet : IApplet
{
    private const string Id = "AudioLevel";
    private readonly AudioToolsSettings toolsSettings = AudioToolsSettings.Load();
    private readonly AudioLevelsSettings levelsSettings = AudioLevelsSettings.Load();
    private readonly AudioLevelSettings testsSettings = AudioLevelSettings.Load();
    private readonly AudioDelaySettings delaySettings = AudioDelaySettings.Load();
    // (each null when it's switched off in Settings → General → Modules: not made at all)
    private readonly VolumeHotkeyService? hotkeys;
    private readonly AudioLevelsView? levels;
    private readonly AudioLevelView? tests;
    private readonly AudioDelayView? delay;
    private readonly FrameworkElement view;
    private readonly AudioToolsView? tabs;
    private readonly FrameworkElement? settingsView;

    public AudioToolsApplet()
    {
        // shown as Volume, Levels and Delay (Volume was called Levels, and Levels Tests, before 0.26; the ids stay)
        bool On(string tool, string title)
        {
            ToolSwitches.Declare(Id, tool, title);
            return ToolSwitches.IsOn(Id, tool);
        }
        var pages = new List<(string, string, FrameworkElement)>();
        FrameworkElement? volumeCard = null, levelsCards = null;
        if (On(AudioToolsSettings.Levels, T("Volume")))
        {
            hotkeys = new VolumeHotkeyService(levelsSettings);
            levels = new AudioLevelsView(levelsSettings, hotkeys);
            pages.Add((AudioToolsSettings.Levels, T("Volume"), levels));
            volumeCard = new LevelsSettingsCard(levelsSettings);
        }
        if (On(AudioToolsSettings.Tests, T("Levels")))
        {
            tests = new AudioLevelView(testsSettings);
            pages.Add((AudioToolsSettings.Tests, T("Levels"), tests));
            levelsCards = new AudioLevelSettingsView(tests, testsSettings, volumeCard);
        }
        if (On(AudioToolsSettings.Delay, T("Delay")))
        {
            delay = new AudioDelayView(delaySettings);
            pages.Add((AudioToolsSettings.Delay, T("Delay"), delay));
        }
        // Settings → Audio: Volume's step, then Levels' options, two by two
        settingsView = levelsCards ?? volumeCard;
        if (pages.Count > 0) view = tabs = new AudioToolsView(toolsSettings, pages);
        else
        {
            var none = new System.Windows.Controls.TextBlock { Text = T("Every Audio tool is switched off: switch them on in Settings → General → Modules."), TextWrapping = TextWrapping.Wrap };
            none.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
            view = none;
        }
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        hotkeys?.Register();
        levels?.Refresh(force: true);
        // Levels and Delay connect to the audio devices (and Voicemeeter) as they're created
    }

    /// <summary>Keyboard shortcuts go to the tool that's showing.</summary>
    public void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (tabs?.Current == AudioToolsSettings.Tests) tests?.HandlePreviewKeyDown(e);
        else if (tabs?.Current == AudioToolsSettings.Delay) delay?.HandlePreviewKeyDown(e);
    }

    public void OnWindowHidden()
    {
        tests?.OnWindowHidden();
        delay?.OnWindowHidden();
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
        hotkeys?.Dispose();
        levels?.Shutdown();
        tests?.Shutdown();
        delay?.Shutdown();
    }
}
