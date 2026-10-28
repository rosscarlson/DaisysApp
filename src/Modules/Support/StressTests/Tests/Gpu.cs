using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DaisysApp.Applets.Support.StressTests.Tests;

/// <summary>A Direct3D 11 device on the graphics card the settings name (or the first).</summary>
internal sealed class GpuDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDXGIAdapter1 Adapter { get; }
    public string Name { get; }

    public GpuDevice(string wanted, bool video = false)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? pick = null, first = null;
        for (uint i = 0; factory.EnumAdapters1(i, out var a).Success; i++)
        {
            var d = a.Description1;
            if ((d.Flags & AdapterFlags.Software) != 0 || d.VendorId == 0x1414) { a.Dispose(); continue; }
            if (pick == null && wanted.Length > 0 && d.Description.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase)) pick = a;
            else if (first == null) first = a;
            else a.Dispose();
        }
        Adapter = pick ?? first ?? throw new InvalidOperationException(T("No graphics card was found"));
        if (pick != null) first?.Dispose();
        Name = Adapter.Description1.Description.Trim();
        var flags = DeviceCreationFlags.BgraSupport | (video ? DeviceCreationFlags.VideoSupport : 0);
        D3D11.D3D11CreateDevice(Adapter, DriverType.Unknown, flags, [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        Context = context;
        if (video) using (var mt = device.QueryInterface<ID3D11Multithread>()) mt.SetMultithreadProtected(true);
    }

    /// <summary>Waits for the GPU to finish what it's been given.</summary>
    public void Finish()
    {
        using var q = Device.CreateQuery(new QueryDescription(QueryType.Event));
        Context.End(q);
        Context.Flush();
        while (Context.GetData(q, IntPtr.Zero, 0, AsyncGetDataFlags.None).Code == 1) Thread.Sleep(0);
    }

    /// <summary>Why the device was lost (TDR, driver reset), or null while it's fine.</summary>
    public string? Lost()
    {
        var r = Device.DeviceRemovedReason;
        return r.Success ? null : F("The graphics driver stopped the device (0x{0:X8}: {1})", r.Code, r.Description);
    }

    public static byte[] Compile(string source, string entry, string profile) => Compiler.Compile(source, entry, "StressTests", profile).ToArray();

    public void Dispose()
    {
        Context.ClearState();
        Context.Dispose();
        Device.Dispose();
        Adapter.Dispose();
    }
}

/// <summary>
/// GPU → 3D: a field of lit, spinning spheres (1,600 instances, about 2.4 million triangles a frame) with a heavy pixel
/// shader (four moving lights and procedural detail), drawn at 1920×1080 with 4× MSAA as fast as the card can, a frame
/// now and then shown on the page.
/// </summary>
internal sealed class Gpu3D : IStressTest
{
    private const int Width = 1920, Height = 1080, Grid = 40, Rings = 24, Segments = 32;

    private const string Shader = @"
cbuffer Frame : register(b0) { float4x4 viewProj; float4 lights[4]; float time; float3 eye; };
struct VsOut { float4 pos : SV_Position; float3 world : WORLD; float3 normal : NORMAL; float3 color : COLOR; };
float3 hue(float h) { return saturate(abs(frac(h + float3(0, 2.0/3, 1.0/3)) * 6 - 3) - 1); }
VsOut vs(float3 p : POSITION, uint id : SV_InstanceID)
{
    float gx = id % 40, gz = id / 40;
    float a = time * (0.6 + frac(id * 0.618) * 1.4) + id;
    float s = sin(a), c = cos(a);
    float3 r = float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
    float3 n = r;
    float3 center = float3((gx - 19.5) * 2.6, sin(time * 1.3 + gx * 0.4 + gz * 0.3) * 1.5, (gz - 19.5) * 2.6);
    VsOut o;
    o.world = center + r;
    o.pos = mul(float4(o.world, 1), viewProj);
    o.normal = n;
    o.color = hue(frac((gx + gz) / 80 + time * 0.05));
    return o;
}
float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise(float3 x)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(hash(i), hash(i + float3(1,0,0)), f.x), lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x), lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
}
float4 ps(VsOut i) : SV_Target
{
    float3 n = normalize(i.normal);
    float detail = 0, amp = 0.5;
    float3 q = i.normal * 4 + time * 0.2;
    [unroll] for (int o = 0; o < 6; o++) { detail += noise(q) * amp; q *= 2.03; amp *= 0.5; }
    float3 v = normalize(eye - i.world);
    float3 c = i.color * (0.55 + 0.45 * detail) * 0.18;
    [unroll] for (int l = 0; l < 4; l++)
    {
        float3 d = lights[l].xyz - i.world;
        float dist = length(d);
        d /= dist;
        float3 h = normalize(d + v);
        float fall = 1 / (1 + dist * dist * 0.0015);
        float3 lc = hue(l * 0.25 + 0.1);
        c += (i.color * saturate(dot(n, d)) * (0.7 + 0.3 * detail) + pow(saturate(dot(n, h)), 48) * 0.6) * lc * fall * lights[l].w;
    }
    float fog = saturate(length(eye - i.world) / 240);
    return float4(lerp(c, float3(0.02, 0.025, 0.04), fog), 1);
}";

    [StructLayout(LayoutKind.Sequential)]
    private struct Frame
    {
        public Matrix4x4 ViewProj;
        public Vector4 L0, L1, L2, L3;
        public float Time;
        public Vector3 Eye;
    }

    public void Run(TestContext c)
    {
        c.Step = T("Starting the graphics card");
        using var gpu = new GpuDevice(c.Settings.Gpu);
        c.Result.Device = gpu.Name;
        var device = gpu.Device;
        var context = gpu.Context;

        // a sphere, its normals the same as its positions
        var verts = new List<Vector3>();
        for (int r = 0; r <= Rings; r++)
            for (int s = 0; s <= Segments; s++)
            {
                double th = Math.PI * r / Rings, ph = 2 * Math.PI * s / Segments;
                verts.Add(new Vector3((float)(Math.Sin(th) * Math.Cos(ph)), (float)Math.Cos(th), (float)(Math.Sin(th) * Math.Sin(ph))));
            }
        var indices = new List<uint>();
        for (int r = 0; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                uint a = (uint)(r * (Segments + 1) + s), b = a + Segments + 1;
                indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        using var vb = device.CreateBuffer(verts.ToArray(), BindFlags.VertexBuffer);
        using var ib = device.CreateBuffer(indices.ToArray(), BindFlags.IndexBuffer);
        var vsCode = GpuDevice.Compile(Shader, "vs", "vs_5_0");
        using var vs = device.CreateVertexShader(vsCode);
        using var ps = device.CreatePixelShader(GpuDevice.Compile(Shader, "ps", "ps_5_0"));
        using var layout = device.CreateInputLayout([new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)], vsCode);
        using var cb = device.CreateBuffer(new BufferDescription((uint)((Marshal.SizeOf<Frame>() + 15) / 16 * 16), BindFlags.ConstantBuffer, ResourceUsage.Default));

        uint samples = device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, 4) > 0 ? 4u : 1u;
        using var target = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, samples, 0));
        using var rtv = device.CreateRenderTargetView(target);
        using var depth = device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, Width, Height, 1, 1, BindFlags.DepthStencil, ResourceUsage.Default, CpuAccessFlags.None, samples, 0));
        using var dsv = device.CreateDepthStencilView(depth);
        using var resolved = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1, BindFlags.None, ResourceUsage.Default));
        using var staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        using var rasterizer = device.CreateRasterizerState(new RasterizerDescription(CullMode.Back, FillMode.Solid) { MultisampleEnable = samples > 1 });
        using var depthState = device.CreateDepthStencilState(DepthStencilDescription.Default);

        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetInputLayout(layout);
        context.IASetVertexBuffer(0, vb, 12);
        context.IASetIndexBuffer(ib, Format.R32_UInt, 0);
        context.VSSetShader(vs);
        context.VSSetConstantBuffer(0, cb);
        context.PSSetShader(ps);
        context.PSSetConstantBuffer(0, cb);
        context.RSSetState(rasterizer);
        context.RSSetViewport(new Viewport(Width, Height));
        context.OMSetDepthStencilState(depthState);
        context.OMSetRenderTargets(rtv, dsv);

        // at most two frames queued, as a game would have
        var fences = Enumerable.Range(0, 3).Select(_ => device.CreateQuery(new QueryDescription(QueryType.Event))).ToArray();
        long triangles = (long)indices.Count / 3 * Grid * Grid;
        c.Add(T("Triangles a frame"), triangles / 1e6, T("million"));
        c.Add(T("MSAA"), samples, "×");
        c.Step = $"{Width}×{Height} · {(samples > 1 ? $"{samples}× MSAA" : T("no MSAA"))}";
        c.Detail(F("Drawing on {0}: {1:0.0} million triangles a frame", gpu.Name, triangles / 1e6));
        try
        {
            c.Begin();
            long frames = 0, lastFrames = 0;
            var lastReport = TimeSpan.Zero;
            var lastPreview = TimeSpan.Zero;
            var warm = TimeSpan.FromSeconds(Math.Min(2, c.Length.TotalSeconds / 10));
            long warmFrames = -1;
            while (!c.TimeUp)
            {
                float t = (float)c.Elapsed.TotalSeconds;
                var eye = new Vector3(MathF.Sin(t * 0.15f) * 48, 16 + MathF.Sin(t * 0.3f) * 6, MathF.Cos(t * 0.15f) * 48);
                var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.2f, (float)Width / Height, 0.5f, 300);
                var frame = new Frame
                {
                    ViewProj = Matrix4x4.Transpose(view * proj), Time = t, Eye = eye,
                    L0 = new(MathF.Sin(t) * 40, 12, MathF.Cos(t) * 40, 2.2f), L1 = new(MathF.Cos(t * 0.7f) * 30, 18, MathF.Sin(t * 0.9f) * 50, 2f),
                    L2 = new(-MathF.Sin(t * 0.5f) * 50, 8, MathF.Cos(t * 0.4f) * 25, 1.8f), L3 = new(0, 40, 0, 2.5f),
                };
                context.UpdateSubresource(in frame, cb);
                context.ClearRenderTargetView(rtv, new Color4(0.02f, 0.025f, 0.04f, 1));
                context.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1, 0);
                context.DrawIndexedInstanced((uint)indices.Count, Grid * Grid, 0, 0, 0);
                var fence = fences[frames % 3];
                context.End(fence);
                context.Flush();
                frames++;
                // wait for the frame two back
                if (frames >= 3)
                {
                    var old = fences[(frames - 3) % 3];
                    while (context.GetData(old, IntPtr.Zero, 0, AsyncGetDataFlags.None).Code == 1) Thread.Sleep(0);
                }
                var now = c.Elapsed;
                if (warmFrames < 0 && now >= warm) warmFrames = frames;
                if (now - lastReport >= TimeSpan.FromMilliseconds(250))
                {
                    c.Report((frames - lastFrames) / (now - lastReport).TotalSeconds);
                    lastFrames = frames;
                    lastReport = now;
                    if (gpu.Lost() is { } lost) { c.Error(lost); break; }
                }
                if (now - lastPreview >= TimeSpan.FromMilliseconds(500))
                {
                    lastPreview = now;
                    Preview(c, context, target, resolved, staging, samples);
                }
            }
            double seconds = (c.Elapsed - warm).TotalSeconds;
            c.Result.Value = warmFrames >= 0 && seconds > 0.5 ? (frames - warmFrames) / seconds : frames / Math.Max(0.001, c.Elapsed.TotalSeconds);
            c.Add(T("Frames"), frames, "");
            c.Add(T("Triangles a second"), c.Result.Value * triangles / 1e9, T("billion"));
            c.Detail(F("{0} frames, {1:0.0} fps", frames, c.Result.Value));
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (gpu.Lost() is { } lost)
        {
            c.Error(lost);
            c.Detail(ex.Message);
        }
        finally
        {
            foreach (var f in fences) f.Dispose();
        }
    }

    private static void Preview(TestContext c, ID3D11DeviceContext context, ID3D11Texture2D target, ID3D11Texture2D resolved, ID3D11Texture2D staging, uint samples)
    {
        if (samples > 1) context.ResolveSubresource(resolved, 0, target, 0, Format.B8G8R8A8_UNorm);
        else context.CopyResource(resolved, target);
        context.CopyResource(staging, resolved);
        var map = context.Map(staging, 0, MapMode.Read);
        try
        {
            // a quarter-size picture is plenty for the page
            const int W = Width / 2, H = Height / 2;
            var pixels = new byte[W * H * 4];
            unsafe
            {
                byte* src = (byte*)map.DataPointer;
                fixed (byte* dst = pixels)
                    for (int y = 0; y < H; y++)
                    {
                        uint* s = (uint*)(src + y * 2 * map.RowPitch);
                        uint* d = (uint*)(dst + y * W * 4);
                        for (int x = 0; x < W; x++) d[x] = s[x * 2];
                    }
            }
            c.ShowFrame(pixels, W, H);
        }
        finally { context.Unmap(staging, 0); }
    }
}

/// <summary>
/// GPU → Compute: a compute shader doing multiply-adds (a million threads, 2,048 steps of four float4s each) as fast as
/// it can. Each batch's results must match the first batch's bit for bit: any that don't are the GPU computing wrong.
/// </summary>
internal sealed class GpuCompute : IStressTest
{
    private const int Threads = 1 << 20, Steps = 2048, Batch = 8;

    private const string Shader = @"
cbuffer Settings : register(b0) { float m; float add; uint steps; uint pad; };
RWStructuredBuffer<float4> results : register(u0);
[numthreads(256, 1, 1)]
void cs(uint3 id : SV_DispatchThreadID)
{
    float s = 1 + (id.x & 1023) * 0.001;
    float4 a0 = float4(s, s + 1, s + 2, s + 3), a1 = a0 + 4, a2 = a0 + 8, a3 = a0 + 12;
    float4 k = float4(add, add * 1.1, add * 1.2, add * 1.3);
    for (uint i = 0; i < steps; i++)
    {
        a0 = mad(a0, m, k); a1 = mad(a1, m, k); a2 = mad(a2, m, k); a3 = mad(a3, m, k);
    }
    results[id.x] += a0 + a1 + a2 + a3;
}";

    [StructLayout(LayoutKind.Sequential)]
    private struct Settings { public float M, Add; public uint Steps, Pad; }

    public void Run(TestContext c)
    {
        c.Step = T("Starting the graphics card");
        using var gpu = new GpuDevice(c.Settings.Gpu);
        c.Result.Device = gpu.Name;
        var device = gpu.Device;
        var context = gpu.Context;
        using var cs = device.CreateComputeShader(GpuDevice.Compile(Shader, "cs", "cs_5_0"));
        var settings = new Settings { M = 0.9999f, Add = 0.0003f, Steps = Steps };
        using var cb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default));
        context.UpdateSubresource(in settings, cb);
        using var results = device.CreateBuffer(new BufferDescription(Threads * 16, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        using var uav = device.CreateUnorderedAccessView(results, new UnorderedAccessViewDescription(results, Format.Unknown, 0, Threads));
        using var staging = device.CreateBuffer(new BufferDescription(Threads * 16, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        context.CSSetShader(cs);
        context.CSSetConstantBuffer(0, cb);
        context.CSSetUnorderedAccessView(0, uav);

        uint[]? first = null;
        var current = new uint[Threads * 4];
        long flopsPerDispatch = (long)Threads * Steps * 4 * 4 * 2;
        c.Step = P(Threads, "{0:#,0} thread", "{0:#,0} threads");
        c.Begin();
        long dispatches = 0;
        var computing = TimeSpan.Zero;
        int batches = 0;
        try
        {
            while (!c.TimeUp)
            {
                var sw = Stopwatch.StartNew();
                context.ClearUnorderedAccessView(uav, new Int4(0, 0, 0, 0)); // (a float clear leaves a structured buffer as it was)
                for (int i = 0; i < Batch; i++) context.Dispatch(Threads / 256, 1, 1);
                gpu.Finish();
                computing += sw.Elapsed;
                dispatches += Batch;
                c.Report(flopsPerDispatch * Batch / sw.Elapsed.TotalSeconds / 1e9);
                if (gpu.Lost() is { } lost) { c.Error(lost); break; }
                // read the batch back and check it against the first
                context.CopyResource(staging, results);
                var map = context.Map(staging, 0, MapMode.Read);
                try { unsafe { new ReadOnlySpan<uint>((void*)map.DataPointer, current.Length).CopyTo(current); } }
                finally { context.Unmap(staging, 0); }
                batches++;
                if (first == null) first = (uint[])current.Clone();
                else
                {
                    int wrong = 0, at = -1;
                    for (int i = 0; i < current.Length; i++)
                        if (current[i] != first[i]) { wrong++; if (at < 0) at = i; }
                    if (wrong > 0) c.Error(F("Batch {0}: {1:#,0} results differ from the first batch's (first at thread {2:#,0})", batches, wrong, at / 4), wrong);
                }
                if (c.Logging == StressLogging.Maximum && batches % 20 == 0) c.Detail(F("{0} batches checked, {1:0} GFLOPS", batches, flopsPerDispatch * dispatches / computing.TotalSeconds / 1e9));
            }
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (gpu.Lost() is { } lost)
        {
            c.Error(lost);
            c.Detail(ex.Message);
        }
        c.Result.Value = computing > TimeSpan.Zero ? flopsPerDispatch * dispatches / computing.TotalSeconds / 1e9 : double.NaN;
        c.Add(T("Batches checked"), batches, "");
    }
}

/// <summary>
/// GPU → VRAM: most of the free video memory taken in 256 MB blocks, filled with patterns by a compute shader and
/// checked by another (each word from its place, the pattern changing each pass), counting the words that come back
/// wrong. Measures how fast the card writes and reads its memory.
/// </summary>
internal sealed class Vram : IStressTest
{
    private const long Chunk = 256L << 20;
    private const uint Words = (uint)(Chunk / 4), Threads = 1 << 20;

    private const string Shader = @"
cbuffer Settings : register(b0) { uint seed; uint words; uint block; uint threads; };
RWByteAddressBuffer data : register(u0);
RWByteAddressBuffer errors : register(u1);
uint pattern(uint i)
{
    uint x = i ^ (block << 26) ^ seed;
    x = (x ^ 61) ^ (x >> 16); x *= 9; x ^= x >> 4; x *= 0x27d4eb2d; x ^= x >> 15;
    return (seed & 1) ? ~x : x;
}
[numthreads(256, 1, 1)]
void fill(uint3 id : SV_DispatchThreadID)
{
    for (uint i = id.x; i < words; i += threads) data.Store(i * 4, pattern(i));
}
[numthreads(256, 1, 1)]
void check(uint3 id : SV_DispatchThreadID)
{
    uint bad = 0, first = 0xFFFFFFFF;
    for (uint i = id.x; i < words; i += threads)
        if (data.Load(i * 4) != pattern(i)) { bad++; first = min(first, i); }
    if (bad > 0)
    {
        uint old;
        errors.InterlockedAdd(0, bad, old);
        errors.InterlockedMin(4, first, old);
    }
}";

    [StructLayout(LayoutKind.Sequential)]
    private struct Settings { public uint Seed, Words, Block, Threads; }

    public void Run(TestContext c)
    {
        c.Step = T("Starting the graphics card");
        using var gpu = new GpuDevice(c.Settings.Gpu);
        c.Result.Device = gpu.Name;
        var device = gpu.Device;
        var context = gpu.Context;
        long free = 0, budget = 0;
        using (var a3 = gpu.Adapter.QueryInterfaceOrNull<IDXGIAdapter3>())
            if (a3 != null)
            {
                var info = a3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                budget = (long)info.Budget;
                free = (long)info.Budget - (long)info.CurrentUsage;
            }
        // the budget's usage counts only this program's: what every program holds comes from Windows' counter
        long dedicated = (long)(nuint)gpu.Adapter.Description1.DedicatedVideoMemory, // (as nuint: its own cast to long goes by 32 bits)
             used = AdapterUsage(gpu.Adapter.Description1.Luid.LowPart, gpu.Adapter.Description1.Luid.HighPart);
        // past Windows' budget for the card, it moves memory out to the PC's (at a fiftieth of the speed): stay half a GB inside it
        if (used > 0) free = Math.Min(free > 0 ? free : long.MaxValue, (budget > 0 ? Math.Min(budget, dedicated) : dedicated) - used - (512L << 20));
        if (free <= 0) free = dedicated / 2;
        long want = Math.Max(Chunk, free * Math.Clamp(c.Settings.VramPercent, 10, 95) / 100);
        c.Step = F("Taking {0:0.0} GB of video memory", want / 1073741824.0);
        c.Detail(F("Video memory {0:0.0} GB, {1:0.0} GB in use by every program, budget {2:0.0} GB; {3:0.0} GB free; taking {4:0.0} GB",
            dedicated / 1073741824.0, used / 1073741824.0, budget / 1073741824.0, free / 1073741824.0, want / 1073741824.0));

        using var fillCs = device.CreateComputeShader(GpuDevice.Compile(Shader, "fill", "cs_5_0"));
        using var checkCs = device.CreateComputeShader(GpuDevice.Compile(Shader, "check", "cs_5_0"));
        using var cb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default));
        using var errors = device.CreateBuffer(new BufferDescription(16, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferAllowRawViews));
        using var errorsUav = device.CreateUnorderedAccessView(errors, new UnorderedAccessViewDescription(errors, Format.R32_Typeless, 0, 4, BufferUnorderedAccessViewFlags.Raw));
        using var errorsBack = device.CreateBuffer(new BufferDescription(16, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        var blocks = new List<(ID3D11Buffer Buffer, ID3D11UnorderedAccessView View)>();
        try
        {
            for (long got = 0; got < want; got += Chunk)
            {
                try
                {
                    var b = device.CreateBuffer(new BufferDescription((uint)Chunk, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferAllowRawViews));
                    var v = device.CreateUnorderedAccessView(b, new UnorderedAccessViewDescription(b, Format.R32_Typeless, 0, Words, BufferUnorderedAccessViewFlags.Raw));
                    blocks.Add((b, v));
                }
                catch (SharpGen.Runtime.SharpGenException) { break; }
            }
            if (blocks.Count == 0) { c.Result.Failed = T("No video memory could be had"); return; }
            double gb = blocks.Count * Chunk / 1073741824.0;
            c.Add(T("Tested"), gb, "GB");
            c.Begin();
            int pass = 0;
            double writeSeconds = 0, readSeconds = 0;
            long bytesWritten = 0, bytesRead = 0;
            var lastDetail = TimeSpan.Zero;
            while (!c.TimeUp)
            {
                pass++;
                c.Step = F("Pass {0}: writing {1:0.0} GB", pass, gb);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < blocks.Count && !c.Stopping; i++)
                {
                    Set(context, cb, (uint)pass, (uint)i);
                    context.CSSetShader(fillCs);
                    context.CSSetUnorderedAccessView(0, blocks[i].View);
                    context.Dispatch(Threads / 256, 1, 1);
                    if (i % 4 == 3) { gpu.Finish(); c.Report(4 * Chunk / sw.Elapsed.TotalSeconds / 1e9 * (i + 1) / 4); }
                }
                gpu.Finish();
                writeSeconds += sw.Elapsed.TotalSeconds;
                bytesWritten += blocks.Count * Chunk;
                double write = blocks.Count * Chunk / sw.Elapsed.TotalSeconds / 1e9;
                if (c.Stopping) break;

                c.Step = F("Pass {0}: checking", pass);
                int wrong = 0;
                sw.Restart();
                for (int i = 0; i < blocks.Count && !c.Stopping; i++)
                {
                    Set(context, cb, (uint)pass, (uint)i);
                    context.UpdateSubresource(new uint[] { 0, 0xFFFFFFFF, 0, 0 }, errors);
                    context.CSSetShader(checkCs);
                    context.CSSetUnorderedAccessViews(0, [blocks[i].View, errorsUav]);
                    context.Dispatch(Threads / 256, 1, 1);
                    context.CopyResource(errorsBack, errors);
                    var map = context.Map(errorsBack, 0, MapMode.Read);
                    uint bad = (uint)Marshal.ReadInt32(map.DataPointer), firstBad = (uint)Marshal.ReadInt32(map.DataPointer, 4);
                    context.Unmap(errorsBack, 0);
                    if (bad > 0)
                    {
                        wrong += (int)bad;
                        c.Error(F("Pass {0}: {1:#,0} words in block {2} came back wrong (the first at {3:#,0} MB of it)", pass, bad, i, firstBad * 4.0 / 1048576), (int)bad);
                    }
                    c.Report(Chunk * (i + 1) / sw.Elapsed.TotalSeconds / 1e9);
                }
                readSeconds += sw.Elapsed.TotalSeconds;
                bytesRead += blocks.Count * Chunk;
                double read = blocks.Count * Chunk / sw.Elapsed.TotalSeconds / 1e9;
                // (hundreds of passes a minute: the first few, then one every two seconds, and any with errors)
                if (pass <= 3 || wrong > 0 || c.Elapsed - lastDetail >= TimeSpan.FromSeconds(2))
                {
                    lastDetail = c.Elapsed;
                    c.Detail(F("Pass {0}: written at {1:0} GB/s, checked at {2:0} GB/s, {3} words wrong", pass, write, read, wrong));
                }
                if (gpu.Lost() is { } lost) { c.Error(lost); break; }
            }
            double writeRate = bytesWritten / Math.Max(0.001, writeSeconds) / 1e9, readRate = bytesRead / Math.Max(0.001, readSeconds) / 1e9;
            c.Add(T("Write"), writeRate, "GB/s");
            c.Add(T("Check (read)"), readRate, "GB/s");
            c.Add(T("Passes"), pass, "");
            c.Result.Value = (writeRate + readRate) / 2;
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (gpu.Lost() is { } lost)
        {
            c.Error(lost);
            c.Detail(ex.Message);
        }
        finally
        {
            context.ClearState();
            foreach (var (b, v) in blocks) { v.Dispose(); b.Dispose(); }
        }
    }

    /// <summary>The video memory every program holds on the card (bytes), from Windows' GPU Adapter Memory counter; 0 if unknown.</summary>
    private static long AdapterUsage(uint lowPart, int highPart)
    {
        try
        {
            const string Path = @"\GPU Adapter Memory(*)\Dedicated Usage";
            using var pdh = new DaisysApp.Shared.Hardware.PdhQuery();
            if (!pdh.Add(Path)) return 0;
            pdh.Collect();
            string low = $"0x{lowPart:X8}", high = $"0x{highPart:X8}";
            return (long)pdh.Values(Path).Where(v => v.Key.Contains(low, StringComparison.OrdinalIgnoreCase) && v.Key.Contains(high, StringComparison.OrdinalIgnoreCase))
                .Select(v => v.Value).DefaultIfEmpty(0).Max();
        }
        catch { return 0; }
    }

    private static void Set(ID3D11DeviceContext context, ID3D11Buffer cb, uint pass, uint block)
    {
        var s = new Settings { Seed = pass * 0x9E3779B9u, Words = Words, Block = block, Threads = Threads };
        context.UpdateSubresource(in s, cb);
        context.CSSetConstantBuffer(0, cb);
    }
}
