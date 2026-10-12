using DaisysApp.Applets.Video.Gaming;
using DaisysApp.Applets.Video.MiniMirror;
using DaisysApp.Applets.Video.Resizer;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Video;

/// <summary>
/// Video tab: three tools, each on its own sub-tab. <b>Resizer</b>: saved window sizes and positions per program.
/// <b>Mini Mirror</b>: part of the screen live in its own always-on-top window. <b>Gaming</b>: the FPS overlay,
/// recording with the graphics card's encoder, benchmarks and each app's performance history. Each keeps its own
/// settings file, as when they were separate tabs (before 0.22).
/// </summary>
[Applet("Video", "Video", "", Order = 40,
    Description = "Window sizes and positions per program, part of the screen in its own window, and the FPS overlay, recording and benchmarks")]
public sealed class VideoApplet : AppletGroup
{
    public const string Resizer = "Resizer", MiniMirror = "MiniMirror", Gaming = "Gaming";

    public VideoApplet()
    {
        Add(Resizer, T("Resizer"), "", () => new ResizerApplet());
        Add(MiniMirror, T("Mini Mirror"), "", () => new MiniMirrorApplet());
        Add(Gaming, T("Gaming"), "", () => new GamingApplet());
    }
}
