using DaisysApp.Applets.SimEffects.AmbiLight;
using DaisysApp.Applets.SimEffects.Analysis;
using DaisysApp.Applets.SimEffects.Arduino;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Applets.SimEffects.Fanatec;
using DaisysApp.Applets.SimEffects.InputOverlay;
using DaisysApp.Applets.SimEffects.Notify;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shell;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects;

/// <summary>
/// SimEffects tab: SimHub's features, one sub-tab each, added as they're needed. <b>Shaker</b>: a game's telemetry
/// (Forza Horizon, Forza Motorsport, iRacing, ACC) played as vibration to transducers on audio devices.
/// <b>Arduino</b>: Arduinos and what's on them; the sub-tabs that drive what's on them show while one has some
/// (<b>Notify</b>: gear and shift lights). <b>Fanatec</b>: a Fanatec wheel's rev LEDs as shift lights. <b>AmbiLight</b>:
/// the displays' colours on Govee lights behind them. <b>Input Overlay</b>: the pedals and wheel, and the speed, over
/// the game. <b>Analysis</b> (since 3.23.1): the car, tyres and race as blocks and widgets, and every lap recorded by
/// track, car and class. <b>Profiles</b> (since 3.18.0): the module's profiles, one per game (and the desktop): the profile in use
/// (the Profile drop-down at the right of the sub-tabs, or a game starting) is the game every tool reads, once
/// (<see cref="SimData"/>), and every tool's settings (<see cref="SimProfiles"/>). See README.md.
/// </summary>
[Applet("SimEffects", "SimEffects", "", Order = 60,
    Description = "Shaker: your racing game's telemetry played to bass shakers (ButtKicker, Dayton Audio…) on your audio devices, effect by effect; Arduino gear and shift lights; a Fanatec wheel's shift lights; AmbiLight: your screens' colours on Govee lights behind them; Input Overlay: your pedals, wheel and speed over the game; Analysis: your car, tyres and laps, every lap kept by track, car and class")]
public sealed class SimEffectsApplet : AppletGroup
{
    public const string BassShaker = "BassShaker";
    public const string Arduino = "Arduino";
    public const string Notify = "Notify";
    public const string Fanatec = "Fanatec";
    public const string AmbiLight = "AmbiLight";
    public const string InputOverlay = "InputOverlay";
    public const string Profiles = "Profiles";
    public const string Analysis = "Analysis";

    private ArduinoHub? hub;

    public SimEffectsApplet()
    {
        // the profiles first: every tool takes the profile in use's settings as it's made
        SimProfiles.Load();
        Add(Profiles, T("Profiles"), "", () => new ProfilesApplet());
        Add(BassShaker, T("Shaker"), "", () => new BassShakerApplet());
        Add(Arduino, T("Arduino"), "", () => new ArduinoApplet(Hub));
        Add(Notify, T("Notify"), "", () => new NotifyApplet(Hub), null, Arduino);
        Add(Fanatec, T("Fanatec"), "", () => new FanatecApplet());
        Add(AmbiLight, T("AmbiLight"), "", () => new AmbiLightApplet());
        Add(InputOverlay, T("Input Overlay"), "", () => new InputOverlayApplet());
        Add(Analysis, T("Analysis"), "", () => new AnalysisApplet());
        HeaderRight = ProfilesApplet.Picker();
        if (hub != null)
        {
            // Notify shows while some Arduino has its lights
            hub.Changed += ShowFeatureTabs;
            ShowFeatureTabs();
        }
    }

    private ArduinoHub Hub => hub ??= new ArduinoHub(ArduinoSettings.Load());

    private void ShowFeatureTabs() => ShowTool(Notify, hub!.Has(ArduinoFeatures.NotifyTab));
}
