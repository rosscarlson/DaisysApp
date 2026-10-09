using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Turns a captured frame into what the video encoder takes, entirely on the GPU: crops and scales it, draws the mouse
/// pointer, and converts it to YUV 4:2:0 — 8-bit NV12 (BT.709), or 10-bit P010 with the HDR10 curve (BT.2020 PQ).
/// HDR monitors' 16-bit scRGB pictures can also be brought down to SDR, the way Windows shows SDR content on them.
/// </summary>
internal sealed class FrameConverter : IDisposable
{
    public enum Mode { Sdr = 0, HdrToSdr = 1, Hdr10 = 2 }

    private const string Shader = """
        Texture2D<float4> src : register(t0);
        Texture2D<float4> cur : register(t1);
        SamplerState smp : register(s0);
        cbuffer Settings : register(b0)
        {
            float4 srcRect;    // the part recorded, in source uv
            float2 texel;      // one output pixel, in source uv
            float whiteScale;  // SDR white on an HDR monitor, in scRGB units (1.0 = 80 nits)
            int mode;          // 0 SDR in, 1 HDR in → SDR, 2 HDR in → HDR10
            float4 cursorRect; // the pointer, in source uv
            int cursorOn;
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
        """;

    private static byte[]? vsCode, psYCode, psUvCode;
    private readonly ID3D11Device device;
    private readonly ID3D11VertexShader vs;
    private readonly ID3D11PixelShader psY, psUv;
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
        vs = device.CreateVertexShader(vsCode);
        psY = device.CreatePixelShader(psYCode);
        psUv = device.CreatePixelShader(psUvCode);
        sampler = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp));
        settings = device.CreateBuffer(new BufferDescription(64, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
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

        float sw = source.Description.Width, sh = source.Description.Height;
        var map = context.Map(settings, 0, MapMode.WriteDiscard);
        float* f = (float*)map.DataPointer;
        f[0] = crop.X / sw; f[1] = crop.Y / sh; f[2] = crop.Width / sw; f[3] = crop.Height / sh;
        f[4] = crop.Width / sw / td.Width; f[5] = crop.Height / sh / td.Height;
        f[6] = Math.Max(0.1f, whiteScale);
        ((int*)f)[7] = (int)mode;
        bool cursorOn = showCursor && cursorView != null;
        f[8] = cursorX / sw; f[9] = cursorY / sh; f[10] = cursorWidth / sw; f[11] = cursorHeight / sh;
        ((int*)f)[12] = cursorOn ? 1 : 0;
        context.Unmap(settings, 0);

        context.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        context.VSSetShader(vs);
        context.VSSetConstantBuffer(0, settings);
        context.PSSetConstantBuffer(0, settings);
        context.PSSetShaderResource(0, sourceView);
        context.PSSetShaderResource(1, cursorOn ? cursorView! : null!);
        context.PSSetSampler(0, sampler);

        context.PSSetShader(psY);
        context.RSSetViewport(new Viewport(td.Width, td.Height));
        context.OMSetRenderTargets(v.Y);
        context.Draw(3, 0);

        context.PSSetShader(psUv);
        context.RSSetViewport(new Viewport(td.Width / 2, td.Height / 2));
        context.OMSetRenderTargets(v.UV);
        context.Draw(3, 0);

        context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        context.PSSetShaderResource(0, null!);
        context.PSSetShaderResource(1, null!);
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
        psUv.Dispose();
        psY.Dispose();
        vs.Dispose();
    }
}
