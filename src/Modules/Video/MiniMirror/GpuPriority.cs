using DaisysApp.Logging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DaisysApp.Applets.Video.MiniMirror;

/// <summary>
/// (since 4.18.4) A capture device's work put ahead on the graphics card. With a game keeping the card busy, copying
/// each picture of a friend's monitor waited 40 ms to 3 s behind it (the mirrors at a few frames a second); a capture
/// only copies, so ahead of the game it costs the game next to nothing (as OBS does).
/// </summary>
internal static class GpuPriority
{
    private static readonly Logger log = Log.Here;
    private static int logged;

    public static void Raise(ID3D11Device device, string what)
    {
        try
        {
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            var result = dxgi.SetGPUThreadPriority(7);
            if (Interlocked.Increment(ref logged) <= 2)
                log.Info(result.Success ? $"{what}: on the graphics card at high priority" : $"{what}: high priority on the graphics card refused (0x{(uint)result.Code:X8})");
        }
        catch (Exception ex) { log.Debug($"{what}: no high priority on the graphics card ({ex.Message})"); }
    }
}
