using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects;

/// <summary>
/// SimEffects tab: SimHub's features, one sub-tab each, added as they're needed. <b>Bass Shaker</b>: a game's telemetry
/// (Forza Horizon, Forza Motorsport, iRacing, ACC) played as vibration to transducers on audio devices. See README.md.
/// </summary>
[Applet("SimEffects", "SimEffects", "", Order = 60,
    Description = "Bass Shaker: your racing game's telemetry played to bass shakers (ButtKicker, Dayton Audio…) on your audio devices, effect by effect")]
public sealed class SimEffectsApplet : AppletGroup
{
    public const string BassShaker = "BassShaker";

    public SimEffectsApplet()
    {
        Add(BassShaker, T("Bass Shaker"), "", () => new BassShakerApplet());
    }
}
