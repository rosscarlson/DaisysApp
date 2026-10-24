using DaisysApp.Applets.SimEffects.Arduino;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Applets.SimEffects.Fanatec;
using DaisysApp.Applets.SimEffects.Notify;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects;

/// <summary>
/// SimEffects tab: SimHub's features, one sub-tab each, added as they're needed. <b>Shaker</b>: a game's telemetry
/// (Forza Horizon, Forza Motorsport, iRacing, ACC) played as vibration to transducers on audio devices.
/// <b>Arduino</b>: Arduinos and what's on them; the sub-tabs that drive what's on them show while one has some
/// (<b>Notify</b>: gear and shift lights). <b>Fanatec</b>: a Fanatec wheel's rev LEDs as shift lights. See README.md.
/// </summary>
[Applet("SimEffects", "SimEffects", "", Order = 60,
    Description = "Shaker: your racing game's telemetry played to bass shakers (ButtKicker, Dayton Audio…) on your audio devices, effect by effect; Arduino gear and shift lights; a Fanatec wheel's shift lights")]
public sealed class SimEffectsApplet : AppletGroup
{
    public const string BassShaker = "BassShaker";
    public const string Arduino = "Arduino";
    public const string Notify = "Notify";
    public const string Fanatec = "Fanatec";

    private ArduinoHub? hub;

    public SimEffectsApplet()
    {
        Add(BassShaker, T("Shaker"), "", () => new BassShakerApplet());
        Add(Arduino, T("Arduino"), "", () => new ArduinoApplet(Hub));
        Add(Notify, T("Notify"), "", () => new NotifyApplet(Hub), null, Arduino);
        Add(Fanatec, T("Fanatec"), "", () => new FanatecApplet());
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
