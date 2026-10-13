using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// The cameras of a layout on one Direct3D device: each camera's feed, and a texture its newest frame is copied into
/// before each frame is drawn (<see cref="Update"/>). <see cref="Layers"/> is what <see cref="FrameConverter.SetCameras"/>
/// takes. A camera that hasn't sent a picture yet (or can't) is left out until it does.
/// </summary>
internal sealed class CameraOverlay : IDisposable
{
    private sealed class Layer(CameraPlacement place, CameraFeed feed)
    {
        public readonly CameraPlacement Place = place;
        public readonly CameraFeed Feed = feed;
        public ID3D11Texture2D? Texture;
        public ID3D11ShaderResourceView? View;
        public int Width, Height;
        public long Seen;
    }

    private readonly ID3D11Device device;
    private readonly List<Layer> layers = new();
    private readonly Func<CameraPlacement, bool>? hidden;

    /// <param name="hidden">Asked each frame: a camera it says is hidden (by its shortcut) is left off the picture.</param>
    public CameraOverlay(ID3D11Device device, IEnumerable<CameraPlacement> placements, Func<CameraPlacement, bool>? hidden = null)
    {
        this.device = device;
        this.hidden = hidden;
        foreach (var p in placements.Take(CameraPlacement.Max))
            layers.Add(new Layer(p.Clone(), CameraFeed.Open(p.DeviceId, p.Name)));
    }

    public bool IsEmpty => layers.Count == 0;

    /// <summary>The cameras' feeds, e.g. to report one that isn't sending.</summary>
    public IEnumerable<CameraFeed> Feeds => layers.Select(l => l.Feed);

    /// <summary>Copies each camera's newest frame, if it has a new one, into its texture.</summary>
    public unsafe void Update(ID3D11DeviceContext context)
    {
        foreach (var l in layers)
        {
            l.Seen = l.Feed.ReadLatest(l.Seen, (bgra, w, h) =>
            {
                if (l.Texture == null || l.Width != w || l.Height != h)
                {
                    l.View?.Dispose();
                    l.Texture?.Dispose();
                    l.Texture = device.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Dynamic,
                        BindFlags = BindFlags.ShaderResource, CPUAccessFlags = CpuAccessFlags.Write,
                    });
                    l.View = device.CreateShaderResourceView(l.Texture);
                    l.Width = w;
                    l.Height = h;
                }
                var map = context.Map(l.Texture, 0, MapMode.WriteDiscard);
                try
                {
                    fixed (byte* src = bgra)
                        for (int y = 0; y < h; y++)
                            Buffer.MemoryCopy(src + (long)y * w * 4, (byte*)map.DataPointer + (long)y * map.RowPitch, map.RowPitch, w * 4);
                }
                finally { context.Unmap(l.Texture, 0); }
            });
        }
    }

    public IReadOnlyList<FrameConverter.CameraLayer> Layers =>
        layers.Where(l => l.View != null && hidden?.Invoke(l.Place) != true).Select(l => new FrameConverter.CameraLayer(l.View!, l.Place, (float)l.Width / Math.Max(1, l.Height))).ToList();

    public void Dispose()
    {
        foreach (var l in layers)
        {
            l.View?.Dispose();
            l.Texture?.Dispose();
            l.Feed.Dispose();
        }
        layers.Clear();
    }
}
