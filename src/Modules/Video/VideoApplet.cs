using DaisysApp.Applets.Video.Gaming;
using DaisysApp.Applets.Video.MiniMirror;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Video;

/// <summary>
/// Video tab: four tools, each on its own sub-tab. <b>Resizer</b>: saved window sizes and positions per program (a module
/// of its own since 1.1.68, src/Modules/Resizer, which joins this tab as its first sub-tab: [Applet(Group = "Video")]).
/// <b>Mini Mirror</b>: part of the screen live in its own always-on-top window. <b>Overlay</b>: what's running now and
/// the FPS overlay. <b>Recording</b>: the PC's load, recording with the graphics card's encoder, benchmarks and each
/// app's performance history (Overlay and Recording were the Gaming sub-tab before 0.25, and share its code and
/// settings file). Each keeps its own settings file, as when they were separate tabs (before 0.22).
/// </summary>
[Applet("Video", "Video", "", Order = 40,
    Description = "Window sizes and positions per program, part of the screen in its own window, the FPS overlay, and recording and benchmarks")]
public sealed class VideoApplet : AppletGroup
{
    // (the Overlay sub-tab keeps the id it had as Gaming, so the sub-tab remembered from before still opens)
    public const string MiniMirror = "MiniMirror", Overlay = "Gaming", Recording = "Recording";

    public VideoApplet()
    {
        Add(MiniMirror, T("Mini Mirror"), "", () => new MiniMirrorApplet());
        GamingApplet? overlay = null;
        Add(Overlay, T("Overlay"), "", () => overlay = new GamingApplet());
        // (Recording uses the Overlay's capture: off with it)
        Add(Recording, T("Recording"), "", () => new RecordingApplet(overlay ?? throw new InvalidOperationException(T("the Overlay couldn't load"))), order: null, needs: Overlay);
    }
}
