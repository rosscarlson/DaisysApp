using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Turns a captured frame into what the video encoder takes, entirely on the GPU: crops and scales it, draws the mouse
/// pointer and any cameras over it (rounded, cropped to fill their boxes), and converts it to YUV 4:2:0 — 8-bit NV12
/// (BT.709), or 10-bit P010 with the HDR10 curve (BT.2020 PQ). HDR monitors' 16-bit scRGB pictures can also be brought
/// down to SDR, the way Windows shows SDR content on them. <see cref="ConvertBgra"/> draws the same picture as plain
/// SDR BGRA instead, for the virtual webcam.
/// </summary>
internal sealed class FrameConverter : IDisposable
{
    public enum Mode { Sdr = 0, HdrToSdr = 1, Hdr10 = 2 }

    /// <summary>A camera to draw: its picture, where it goes, and its shape (width / height).</summary>
    public readonly record struct CameraLayer(ID3D11ShaderResourceView View, CameraPlacement Place, float Aspect);

    private const string Shader = """
        Texture2D<float4> src : register(t0);
        Texture2D<float4> cur : register(t1);
        Texture2D<float4> cam0 : register(t2);
        Texture2D<float4> cam1 : register(t3);
        Texture2D<float4> cam2 : register(t4);
        Texture2D<float4> cam3 : register(t5);
        SamplerState smp : register(s0);
        cbuffer Settings : register(b0)
        {
            float4 srcRect;    // the part recorded, in source uv
            float2 texel;      // one output pixel, in source uv
            float whiteScale;  // SDR white on an HDR monitor, in scRGB units (1.0 = 80 nits)
            int mode;          // 0 SDR in, 1 HDR in → SDR, 2 HDR in → HDR10
            float4 cursorRect; // the pointer, in source uv
            int cursorOn;
            float2 outSize;    // the output picture, in pixels
            int camCount;
            float4 camRect[4];  // each camera's box: x, y, width, height as fractions of the output picture
            float4 camParam[4]; // its corner radius (output pixels), flips (1 left–right, 2 top–bottom), its picture's shape as shown (cropped) and quarter turns clockwise
            float4 camCrop[4];  // the part of its picture shown, as it's seen (turned and flipped): x, y, width, height, 0–1
        };

        struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        V vs(uint id : SV_VertexID)
        {
            V o;
            float2 t = float2((id << 1) & 2, id & 2); // one triangle covering the target
            o.pos = float4(t * float2(2, -2) + float2(-1, 1), 0, 1);
            o.uv = srcRect.xy + t * srcRect.zw;
            return o;
        }

        float3 toSrgb(float3 c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }
        float3 fromSrgb(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
        float3 pq(float3 n)
        {
            float3 p = pow(saturate(n), 0.1593017578125);
            return pow((0.8359375 + 18.8515625 * p) / (1 + 18.6875 * p), 78.84375);
        }

        float3 camSample(int i, float2 k)
        {
            if (i == 0) return cam0.SampleLevel(smp, k, 0).rgb;
            if (i == 1) return cam1.SampleLevel(smp, k, 0).rgb;
            if (i == 2) return cam2.SampleLevel(smp, k, 0).rgb;
            return cam3.SampleLevel(smp, k, 0).rgb;
        }

        // the cameras over the picture: each fills its rounded box (cropped to its shape, not squashed), with a smooth edge
        float3 cameras(float3 c, float2 uv)
        {
            float2 q = (uv - srcRect.xy) / srcRect.zw; // where in the output picture, 0–1
            [loop] for (int i = 0; i < 4; i++)
            {
                if (i >= camCount) break;
                float4 r = camRect[i];
                float4 p = camParam[i];
                float2 k = (q - r.xy) / r.zw;
                if (any(k < 0) || any(k > 1)) continue;
                float2 size = r.zw * outSize;
                float2 d = abs((k - 0.5) * size) - size * 0.5 + p.x;
                float edge = length(max(d, 0)) + min(max(d.x, d.y), 0) - p.x; // distance outside the rounded box, in pixels
                float a = saturate(0.5 - edge);
                if (a <= 0) continue;
                float box = size.x / size.y;
                float2 t = k;
                if (p.z > box) t.x = 0.5 + (t.x - 0.5) * box / p.z; else t.y = 0.5 + (t.y - 0.5) * p.z / box;
                t = camCrop[i].xy + t * camCrop[i].zw; // into the part kept, as it's seen
                int flips = (int)(p.y + 0.5), turns = (int)(p.w + 0.5);
                if ((flips & 1) != 0) t.x = 1 - t.x;
                if ((flips & 2) != 0) t.y = 1 - t.y;
                // back from turned to the camera's own picture
                if (turns == 1) t = float2(t.y, 1 - t.x);
                else if (turns == 2) t = 1 - t;
                else if (turns == 3) t = float2(1 - t.y, t.x);
                float3 cc = camSample(i, t);
                c = lerp(c, mode == 0 ? cc : fromSrgb(cc) * whiteScale, a);
            }
            return c;
        }

        // the picture as the encoder's RGB: gamma-encoded BT.709, or PQ-encoded BT.2020
        float3 rgb(float2 uv)
        {
            float3 c = src.SampleLevel(smp, uv, 0).rgb;
            if (cursorOn)
            {
                float2 k = (uv - cursorRect.xy) / cursorRect.zw;
                if (all(k >= 0) && all(k < 1))
                {
                    float4 p = cur.SampleLevel(smp, k, 0);
                    c = lerp(c, mode == 0 ? p.rgb : fromSrgb(p.rgb) * whiteScale, p.a);
                }
            }
            if (camCount > 0) c = cameras(c, uv);
            if (mode == 0) return saturate(c);
            if (mode == 1)
            {
                c = max(c / whiteScale, 0);
                // HDR highlights above SDR white roll off instead of clipping hard
                float m = max(c.r, max(c.g, c.b));
                if (m > 0.8) c *= (0.8 + 0.2 * (1 - exp(-(m - 0.8) * 7.5))) / m;
                return toSrgb(saturate(c));
            }
            static const float3x3 toBt2020 = { 0.6274, 0.3293, 0.0433, 0.0691, 0.9195, 0.0114, 0.0164, 0.0880, 0.8956 };
            return pq(mul(toBt2020, max(c, 0)) * (80.0 / 10000.0));
        }

        float luma(float3 c) { return mode == 2 ? dot(c, float3(0.2627, 0.6780, 0.0593)) : dot(c, float3(0.2126, 0.7152, 0.0722)); }

        float psY(V i) : SV_Target
        {
            float y = luma(rgb(i.uv));
            return mode == 2 ? (64 + 876 * y) * 64 / 65535.0 : (16 + 219 * y) / 255.0; // studio range
        }

        float2 psUV(V i) : SV_Target
        {
            // one colour sample per 2×2 pixels: their average
            float2 d = texel * 0.5;
            float3 c = (rgb(i.uv + float2(-d.x, -d.y)) + rgb(i.uv + float2(d.x, -d.y)) + rgb(i.uv + float2(-d.x, d.y)) + rgb(i.uv + float2(d.x, d.y))) * 0.25;
            float y = luma(c);
            float2 uv = mode == 2 ? float2((c.b - y) / 1.8814, (c.r - y) / 1.4746) : float2((c.b - y) / 1.8556, (c.r - y) / 1.5748);
            return mode == 2 ? (512 + 896 * uv) * 64 / 65535.0 : (128 + 224 * uv) / 255.0;
        }

        // plain SDR colour (modes 0 and 1), for the virtual webcam
        float4 psRGB(V i) : SV_Target { return float4(rgb(i.uv), 1); }
        """;

    private static byte[]? vsCode, psYCode, psUvCode, psRgbCode;
    private readonly ID3D11Device device;
    private readonly ID3D11VertexShader vs;
    private readonly ID3D11PixelShader psY, psUv, psRgb;
    private IReadOnlyList<CameraLayer> cameras = [];
    private readonly ID3D11SamplerState sampler;
    private readonly ID3D11Buffer settings;
    private readonly Dictionary<(IntPtr, uint), (ID3D11RenderTargetView Y, ID3D11RenderTargetView UV)> views = new();
    private ID3D11Texture2D? source;
    private ID3D11ShaderResourceView? sourceView;
    private ID3D11Texture2D? cursor;
    private ID3D11ShaderResourceView? cursorView;
    private int cursorWidth, cursorHeight;

    public FrameConverter(ID3D11Device device)
    {
        this.device = device;
        vsCode ??= Compiler.Compile(Shader, "vs", "GamingConvert", "vs_5_0").ToArray();
        psYCode ??= Compiler.Compile(Shader, "psY", "GamingConvert", "ps_5_0").ToArray();
        psUvCode ??= Compiler.Compile(Shader, "psUV", "GamingConvert", "ps_5_0").ToArray();
        psRgbCode ??= Compiler.Compile(Shader, "psRGB", "GamingConvert", "ps_5_0").ToArray();
        vs = device.CreateVertexShader(vsCode);
        psY = device.CreatePixelShader(psYCode);
        psUv = device.CreatePixelShader(psUvCode);
        psRgb = device.CreatePixelShader(psRgbCode);
        sampler = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp));
        settings = device.CreateBuffer(new BufferDescription(256, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
    }

    public bool HasSource => source != null;
    public bool SourceIsHdr => source?.Description.Format == Format.R16G16B16A16_Float;
    public int SourceWidth => (int)(source?.Description.Width ?? 0);
    public int SourceHeight => (int)(source?.Description.Height ?? 0);

    /// <summary>Keeps a copy of the captured frame (desktop duplication's own texture has to be handed back).</summary>
    public void SetSource(ID3D11DeviceContext context, ID3D11Texture2D frame)
    {
        var d = frame.Description;
        if (source == null || source.Description.Width != d.Width || source.Description.Height != d.Height || source.Description.Format != d.Format)
        {
            sourceView?.Dispose();
            source?.Dispose();
            source = device.CreateTexture2D(new Texture2DDescription
            {
                Width = d.Width, Height = d.Height, MipLevels = 1, ArraySize = 1, Format = d.Format,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
            });
            sourceView = device.CreateShaderResourceView(source);
        }
        context.CopyResource(source, frame);
    }

    /// <summary>A new pointer picture: straight-alpha BGRA, <paramref name="width"/>×<paramref name="height"/>.</summary>
    public unsafe void SetCursor(byte[] bgra, int width, int height)
    {
        cursorView?.Dispose();
        cursor?.Dispose();
        cursor = null;
        cursorView = null;
        if (width <= 0 || height <= 0) return;
        fixed (byte* p = bgra)
        {
            cursor = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource,
            }, new SubresourceData(p, (uint)width * 4));
        }
        cursorView = device.CreateShaderResourceView(cursor);
        cursorWidth = width;
        cursorHeight = height;
    }

    /// <summary>The cameras drawn over the next frames (up to <see cref="CameraPlacement.Max"/>), or none.</summary>
    public void SetCameras(IReadOnlyList<CameraLayer> layers) => cameras = layers.Take(CameraPlacement.Max).ToList();

    /// <summary>
    /// Draws the recorded part (<paramref name="crop"/>, in source pixels) into an NV12 or P010 texture (or one slice of a
    /// texture array), with the pointer at <paramref name="cursorX"/>,<paramref name="cursorY"/> (source pixels) if shown.
    /// </summary>
    public unsafe void Convert(ID3D11DeviceContext context, ID3D11Texture2D target, uint slice, Mode mode, float whiteScale,
        PixelRect crop, bool showCursor, int cursorX, int cursorY)
    {
        if (source == null || sourceView == null) return;
        var td = target.Description;
        var key = (target.NativePointer, slice);
        if (!views.TryGetValue(key, out var v))
        {
            bool p010 = td.Format == Format.P010;
            RenderTargetViewDescription Desc(Format f) => td.ArraySize > 1
                ? new RenderTargetViewDescription(RenderTargetViewDimension.Texture2DArray, f, 0, slice, 1)
                : new RenderTargetViewDescription(RenderTargetViewDimension.Texture2D, f);
            // the luma plane through an R8 / R16 view, the chroma plane through R8G8 / R16G16
            v = (device.CreateRenderTargetView(target, Desc(p010 ? Format.R16_UNorm : Format.R8_UNorm)),
                 device.CreateRenderTargetView(target, Desc(p010 ? Format.R16G16_UNorm : Format.R8G8_UNorm)));
            views[key] = v;
        }

        Prepare(context, mode, whiteScale, crop, showCursor, cursorX, cursorY, (int)td.Width, (int)td.Height);

        context.PSSetShader(psY);
        context.RSSetViewport(new Viewport(td.Width, td.Height));
        context.OMSetRenderTargets(v.Y);
        context.Draw(3, 0);

        context.PSSetShader(psUv);
        context.RSSetViewport(new Viewport(td.Width / 2, td.Height / 2));
        context.OMSetRenderTargets(v.UV);
        context.Draw(3, 0);

        Unbind(context);
    }

    /// <summary>
    /// The same picture as <see cref="Convert"/> (modes <see cref="Mode.Sdr"/> or <see cref="Mode.HdrToSdr"/>), as plain
    /// SDR colour into a BGRA render target <paramref name="width"/>×<paramref name="height"/>: the virtual webcam's frame.
    /// </summary>
    public void ConvertBgra(ID3D11DeviceContext context, ID3D11RenderTargetView target, int width, int height, Mode mode, float whiteScale,
        PixelRect crop, bool showCursor, int cursorX, int cursorY)
    {
        if (source == null || sourceView == null) return;
        Prepare(context, mode == Mode.Hdr10 ? Mode.HdrToSdr : mode, whiteScale, crop, showCursor, cursorX, cursorY, width, height);
        context.PSSetShader(psRgb);
        context.RSSetViewport(new Viewport(width, height));
        context.OMSetRenderTargets(target);
        context.Draw(3, 0);
        Unbind(context);
    }

    /// <summary>The shader's settings for one frame, and its inputs bound.</summary>
    private unsafe void Prepare(ID3D11DeviceContext context, Mode mode, float whiteScale, PixelRect crop, bool showCursor, int cursorX, int cursorY, int outW, int outH)
    {
        float sw = source!.Description.Width, sh = source.Description.Height;
        var map = context.Map(settings, 0, MapMode.WriteDiscard);
        float* f = (float*)map.DataPointer;
        f[0] = crop.X / sw; f[1] = crop.Y / sh; f[2] = crop.Width / sw; f[3] = crop.Height / sh;
        f[4] = crop.Width / sw / outW; f[5] = crop.Height / sh / outH;
        f[6] = Math.Max(0.1f, whiteScale);
        ((int*)f)[7] = (int)mode;
        bool cursorOn = showCursor && cursorView != null;
        f[8] = cursorX / sw; f[9] = cursorY / sh; f[10] = cursorWidth / sw; f[11] = cursorHeight / sh;
        ((int*)f)[12] = cursorOn ? 1 : 0;
        f[13] = outW; f[14] = outH;
        ((int*)f)[15] = cameras.Count;
        for (int i = 0; i < cameras.Count; i++)
        {
            var p = cameras[i].Place;
            float x = (float)Math.Clamp(p.X, 0, 1), y = (float)Math.Clamp(p.Y, 0, 1);
            float w = (float)Math.Clamp(p.W, 0.01, 1), h = (float)Math.Clamp(p.H, 0.01, 1);
            f[16 + i * 4] = x; f[17 + i * 4] = y; f[18 + i * 4] = w; f[19 + i * 4] = h;
            float shorter = Math.Min(w * outW, h * outH);
            f[32 + i * 4] = (float)Math.Clamp(p.Corner, 0, 0.5) * shorter;
            f[33 + i * 4] = (p.Mirror ? 1 : 0) + (p.FlipVertical ? 2 : 0);
            float cw = (float)Math.Max(CameraPlacement.MinVisible, 1 - p.CropLeft - p.CropRight);
            float ch = (float)Math.Max(CameraPlacement.MinVisible, 1 - p.CropTop - p.CropBottom);
            f[34 + i * 4] = Math.Max(0.01f, (float)p.SeenAspect(cameras[i].Aspect) * cw / ch); // the shape of what's kept
            f[35 + i * 4] = p.QuarterTurns;
            f[48 + i * 4] = (float)p.CropLeft; f[49 + i * 4] = (float)p.CropTop; f[50 + i * 4] = cw; f[51 + i * 4] = ch;
        }
        context.Unmap(settings, 0);

        context.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        context.VSSetShader(vs);
        context.VSSetConstantBuffer(0, settings);
        context.PSSetConstantBuffer(0, settings);
        context.PSSetShaderResource(0, sourceView);
        context.PSSetShaderResource(1, cursorOn ? cursorView! : null!);
        for (int i = 0; i < CameraPlacement.Max; i++)
            context.PSSetShaderResource((uint)(2 + i), i < cameras.Count ? cameras[i].View : null!);
        context.PSSetSampler(0, sampler);
    }

    private static void Unbind(ID3D11DeviceContext context)
    {
        context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        for (uint i = 0; i < 2 + CameraPlacement.Max; i++) context.PSSetShaderResource(i, null!);
    }

    public void Dispose()
    {
        foreach (var v in views.Values) { v.Y.Dispose(); v.UV.Dispose(); }
        views.Clear();
        cursorView?.Dispose();
        cursor?.Dispose();
        sourceView?.Dispose();
        source?.Dispose();
        settings.Dispose();
        sampler.Dispose();
        psRgb.Dispose();
        psUv.Dispose();
        psY.Dispose();
        vs.Dispose();
    }
}
