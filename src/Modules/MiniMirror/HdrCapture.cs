using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DaisysApp.Applets.MiniMirror;

/// <summary>
/// HDR monitors: Windows hands the desktop over as 16-bit linear scRGB (1.0 = 80 nits). This turns it back into the
/// 8-bit sRGB picture an SDR game shows, on the GPU: divide by the monitor's "SDR content brightness" (the white level
/// Windows puts SDR content at), clip, and apply the sRGB curve. Without it the HDR desktop comes through washed out.
/// </summary>
internal sealed class HdrToSdr : IDisposable
{
    private const string Shader = """
        Texture2D<float4> src : register(t0);
        cbuffer Settings : register(b0) { float whiteScale; float3 pad; };

        float4 vs(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2); // one triangle covering the screen
            return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
        }

        float4 ps(float4 pos : SV_Position) : SV_Target
        {
            float3 c = saturate(src.Load(int3(pos.xy, 0)).rgb / whiteScale);
            float3 srgb = c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
            return float4(srgb, 1);
        }
        """;

    private static byte[]? vsCode, psCode;

    private readonly ID3D11Texture2D source, target;
    private readonly ID3D11ShaderResourceView sourceView;
    private readonly ID3D11RenderTargetView targetView;
    private readonly ID3D11Buffer settings;
    private readonly ID3D11VertexShader vs;
    private readonly ID3D11PixelShader ps;
    private readonly int width, height;

    public HdrToSdr(ID3D11Device device, int width, int height)
    {
        this.width = width;
        this.height = height;
        vsCode ??= Compiler.Compile(Shader, "vs", "MiniMirrorHdr", "vs_5_0").ToArray();
        psCode ??= Compiler.Compile(Shader, "ps", "MiniMirrorHdr", "ps_5_0").ToArray();
        vs = device.CreateVertexShader(vsCode);
        ps = device.CreatePixelShader(psCode);

        source = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.R16G16B16A16_Float, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
        });
        sourceView = device.CreateShaderResourceView(source);
        target = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget,
        });
        targetView = device.CreateRenderTargetView(target);
        settings = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
    }

    /// <summary>Converts a captured HDR frame into <paramref name="staging"/> (8-bit BGRA, same size).</summary>
    public unsafe void Convert(ID3D11DeviceContext context, ID3D11Texture2D frame, float whiteScale, ID3D11Texture2D staging)
    {
        context.CopyResource(source, frame);
        var map = context.Map(settings, 0, MapMode.WriteDiscard);
        *(float*)map.DataPointer = Math.Max(0.1f, whiteScale);
        context.Unmap(settings, 0);

        context.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        context.VSSetShader(vs);
        context.PSSetShader(ps);
        context.PSSetShaderResource(0, sourceView);
        context.PSSetConstantBuffer(0, settings);
        context.RSSetViewport(new Viewport(width, height));
        context.OMSetRenderTargets(targetView);
        context.Draw(3, 0);
        context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        context.PSSetShaderResource(0, null!);
        context.CopyResource(staging, target);
    }

    public void Dispose()
    {
        settings.Dispose();
        targetView.Dispose();
        target.Dispose();
        sourceView.Dispose();
        source.Dispose();
        ps.Dispose();
        vs.Dispose();
    }
}
