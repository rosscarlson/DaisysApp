using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

namespace DaisysApp.Applets.Support.StressTests.Tests;

/// <summary>The GPU's video encoders and decoders, through Windows' Media Foundation (as Video → Recording uses them).</summary>
internal static class Media
{
    public const int Width = 1920, Height = 1080, Fps = 60;
    public static readonly string[] Codecs = ["H264", "HEVC", "AV1"];

    private static readonly Guid FriendlyName = new("314ffbae-5b41-4c95-9c19-4e7d586face3");   // MFT_FRIENDLY_NAME_Attribute
    private static readonly Guid HardwareUrl = new("2fb866ac-b078-4942-ab6c-003d05cda674");    // MFT_ENUM_HARDWARE_URL_Attribute
    private static readonly Guid TransformAsync = new("f81a699a-649a-497d-8c73-29f8fed6ad7a"); // MF_TRANSFORM_ASYNC
    public static readonly Guid SaD3D11BindFlags = new("eacf97ad-065c-4408-bee3-fdcbfd128be2"); // MF_SA_D3D11_BINDFLAGS
    private static readonly object gate = new();
    private static bool started;

    public static void Startup()
    {
        lock (gate)
        {
            if (started) return;
            MediaFactory.MFStartup(true).CheckError();
            started = true;
        }
    }

    public static Guid Fourcc(string s) => new(BitConverter.ToUInt32(System.Text.Encoding.ASCII.GetBytes(s)), 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    public static Guid Subtype(string codec) => Fourcc(codec switch { "H264" => "H264", "AV1" => "AV01", _ => "HEVC" });

    public static string Label(string codec) => codec switch { "H264" => "H.264", "HEVC" => "HEVC", _ => codec };

    /// <summary>The codecs this PC has a hardware encoder (or decoder) for, with its name.</summary>
    public static Dictionary<string, string> Hardware(bool encoders)
    {
        var found = new Dictionary<string, string>();
        Startup();
        foreach (string codec in Codecs)
        {
            var type = new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = Subtype(codec) };
            MediaFactory.MFTEnumEx(encoders ? TransformCategoryGuids.VideoEncoder : TransformCategoryGuids.VideoDecoder,
                (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter), encoders ? null : type, encoders ? type : null, out IntPtr list, out uint count);
            try
            {
                if (count > 0)
                {
                    using var activate = new IMFActivate(Marshal.ReadIntPtr(list, 0));
                    string name = codec;
                    try { name = activate.GetAllocatedString(FriendlyName) ?? codec; } catch { }
                    found[codec] = name;
                    for (int i = 1; i < count; i++) Marshal.Release(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                }
            }
            finally { if (list != IntPtr.Zero) Marshal.FreeCoTaskMem(list); }
        }
        return found;
    }

    public static IMFMediaType VideoType(Guid subtype)
    {
        var t = MediaFactory.MFCreateMediaType();
        t.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        t.Set(MediaTypeAttributeKeys.Subtype, subtype);
        t.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)Width << 32) | Height);
        t.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)Fps << 32) | 1u);
        t.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1ul << 32) | 1u);
        t.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        return t;
    }

    /// <summary>The name of a hardware transform, or null if <paramref name="transform"/> is a software one.</summary>
    public static string? HardwareName(IMFTransform transform)
    {
        using var a = transform.Attributes;
        bool hardware = false;
        try { hardware = a.GetUInt32(TransformAsync) != 0; } catch { }
        try { hardware |= (a.GetAllocatedString(HardwareUrl)?.Length ?? 0) > 0; } catch { }
        if (!hardware) return null;
        try { return a.GetAllocatedString(FriendlyName); } catch { return T("Hardware"); }
    }

    /// <summary>A transform's name ("Microsoft H264 Video Decoder MFT").</summary>
    public static string Name(IMFTransform transform)
    {
        try { using var a = transform.Attributes; return a.GetAllocatedString(FriendlyName) ?? T("Windows' decoder"); }
        catch { return T("Windows' decoder"); }
    }

    /// <summary>16 different 1080p frames (moving shapes and noise, so the encoder has work), as NV12 textures.</summary>
    public static List<ID3D11Texture2D> Frames(ID3D11Device device, int count = 16)
    {
        var list = new List<ID3D11Texture2D>();
        var data = new byte[Width * Height * 3 / 2];
        var rng = new Random(5);
        for (int f = 0; f < count; f++)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    double v = 128 + 60 * Math.Sin((x + f * 24) * 0.013) * Math.Cos((y - f * 9) * 0.017) + 30 * Math.Sin((x * y + f * 4000) * 0.00007);
                    data[y * Width + x] = (byte)Math.Clamp(v + rng.Next(-12, 13), 16, 235);
                }
            int uv = Width * Height;
            for (int y = 0; y < Height / 2; y++)
                for (int x = 0; x < Width / 2; x++)
                {
                    data[uv + y * Width + x * 2] = (byte)(128 + 50 * Math.Sin((x + f * 10) * 0.02));
                    data[uv + y * Width + x * 2 + 1] = (byte)(128 + 50 * Math.Cos((y + f * 6) * 0.025));
                }
            unsafe
            {
                fixed (byte* p = data)
                    list.Add(device.CreateTexture2D(new Texture2DDescription(Format.NV12, Width, Height, 1, 1, BindFlags.ShaderResource, ResourceUsage.Default),
                        [new SubresourceData(p, Width)]));
            }
        }
        return list;
    }

    /// <summary>
    /// Encodes <paramref name="frames"/> over and over into an MP4 at <paramref name="path"/> until <paramref name="done"/>
    /// says so; returns the frames encoded, the time it took (finishing the file included) and the encoder's name (null if
    /// Windows gave a software one).
    /// </summary>
    public static (long Frames, double Seconds, string? Encoder) Encode(GpuDevice gpu, List<ID3D11Texture2D> frames, string codec, string path, int mbps, Func<long, bool> done, Action<long, double>? progress = null)
    {
        Startup();
        using var manager = MediaFactory.MFCreateDXGIDeviceManager();
        manager.ResetDevice(gpu.Device).CheckError();
        IMFSinkWriter writer;
        using (var attrs = MediaFactory.MFCreateAttributes(2))
        {
            attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
            attrs.Set(SinkWriterAttributeKeys.D3DManager, manager);
            writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, attrs);
        }
        using (writer)
        {
            int stream;
            using (var outType = VideoType(Subtype(codec)))
            {
                outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)(mbps * 1_000_000));
                stream = writer.AddStream(outType);
            }
            using var inType = VideoType(Fourcc("NV12"));
            writer.SetInputMediaType(stream, inType, null);
            string? encoder = null;
            using (var ex = writer.QueryInterfaceOrNull<IMFSinkWriterEx>())
                if (ex != null)
                    for (int i = 0; i < 8; i++)
                    {
                        Guid category;
                        IMFTransform transform;
                        try { ex.GetTransformForStream(stream, i, out category, out transform); }
                        catch { break; }
                        using (transform)
                            if (category == TransformCategoryGuids.VideoEncoder) { encoder = HardwareName(transform); break; }
                    }
            using var allocator = new IMFVideoSampleAllocatorEx(MediaFactory.MFCreateVideoSampleAllocatorEx(typeof(IMFVideoSampleAllocatorEx).GUID));
            allocator.SetDirectXManager(manager);
            using (var sa = MediaFactory.MFCreateAttributes(1))
            {
                sa.Set(SaD3D11BindFlags, (uint)(BindFlags.RenderTarget | BindFlags.VideoEncoder));
                allocator.InitializeSampleAllocatorEx(4, 16, sa, inType);
            }
            writer.BeginWriting();
            var sw = Stopwatch.StartNew();
            long n = 0;
            while (!done(n))
            {
                IMFSample sample;
                try { sample = allocator.AllocateSample(); }
                catch (SharpGenException) { Thread.Sleep(1); continue; } // the encoder is behind: every buffer is queued
                using (sample)
                {
                    using var buffer = sample.GetBufferByIndex(0);
                    using (var b2 = buffer.QueryInterface<IMF2DBuffer>()) buffer.CurrentLength = b2.ContiguousLength;
                    using var dxgi = buffer.QueryInterface<IMFDXGIBuffer>();
                    using var target = new ID3D11Texture2D(dxgi.GetResource(typeof(ID3D11Texture2D).GUID));
                    gpu.Context.CopySubresourceRegion(target, dxgi.SubresourceIndex, 0, 0, 0, frames[(int)(n % frames.Count)], 0);
                    sample.SampleTime = n * 10_000_000L / Fps;
                    sample.SampleDuration = 10_000_000L / Fps;
                    writer.WriteSample(stream, sample);
                }
                n++;
                progress?.Invoke(n, sw.Elapsed.TotalSeconds);
            }
            writer.Finalize();
            return (n, sw.Elapsed.TotalSeconds, encoder);
        }
    }
}

/// <summary>
/// Video → Encode: 1080p frames (already on the GPU, so only the encoder is measured) through each hardware encoder
/// the card has (H.264, HEVC, AV1), the test's time shared between them. Scored on H.264.
/// </summary>
internal sealed class Encode : IStressTest
{
    public void Run(TestContext c)
    {
        c.Step = T("Finding the encoders");
        var codecs = Media.Hardware(encoders: true);
        if (codecs.Count == 0) { c.Result.Failed = T("This PC has no hardware video encoder"); return; }
        using var gpu = new GpuDevice(c.Settings.Gpu, video: true);
        c.Step = T("Making frames");
        var frames = Media.Frames(gpu.Device);
        string folder = Path.Combine(Path.GetTempPath(), AppPaths.ShortName + "-StressTest");
        Directory.CreateDirectory(folder);
        try
        {
            c.Begin();
            var list = codecs.Keys.ToList();
            var names = new List<string>();
            for (int i = 0; i < list.Count && !c.Stopping; i++)
            {
                string codec = list[i];
                var until = TimeSpan.FromTicks(c.Length.Ticks * (i + 1) / list.Count);
                string path = Path.Combine(folder, $"encode-{codec}.mp4");
                c.Step = F("{0} at 1080p", Media.Label(codec));
                double lastAt = 0;
                long lastN = 0;
                try
                {
                    var (n, seconds, encoder) = Media.Encode(gpu, frames, codec, path, 20, _ => c.TimeUp || c.Elapsed >= until, (n, at) =>
                    {
                        if (at - lastAt < 0.25) return;
                        c.Report((n - lastN) / (at - lastAt));
                        lastN = n;
                        lastAt = at;
                    });
                    double fps = n / Math.Max(0.001, seconds);
                    c.Add(F("{0} fps", Media.Label(codec)), fps, "fps");
                    names.Add(encoder ?? codecs[codec]);
                    if (encoder == null) c.Note(F("Windows used a software encoder for {0}", Media.Label(codec)));
                    if (codec == "H264" || double.IsNaN(c.Result.Value)) c.Result.Value = fps;
                    c.Detail(F("{0}: {1} frames in {2:0.0} s ({3:0} fps) by {4}", Media.Label(codec), n, seconds, fps, encoder ?? T("a software encoder")));
                }
                catch (Exception ex) when (ex is SharpGenException or COMException)
                {
                    c.Note(F("{0} couldn't be encoded: {1}", Media.Label(codec), ex.Message));
                    if (gpu.Lost() is { } lost) { c.Error(lost); break; }
                }
                finally { try { File.Delete(path); } catch { } }
            }
            c.Result.Device = string.Join(", ", names.Distinct());
            if (double.IsNaN(c.Result.Value) && !c.Stopping) c.Result.Failed ??= T("No encoder worked");
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }
}

/// <summary>
/// Video → Decode: a 1080p H.264 clip (made first by the encoder, 20 Mbit/s) read through Windows' hardware decoder into
/// video memory as fast as it goes, over and over.
/// </summary>
internal sealed class Decode : IStressTest
{
    public void Run(TestContext c)
    {
        using var gpu = new GpuDevice(c.Settings.Gpu, video: true);
        string folder = Path.Combine(Path.GetTempPath(), AppPaths.ShortName + "-StressTest");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "decode.mp4");
        try
        {
            c.Step = T("Making a clip to decode");
            var frames = Media.Frames(gpu.Device);
            try { Media.Encode(gpu, frames, "H264", path, 20, n => n >= 600 || c.Stopping); }
            finally { foreach (var f in frames) f.Dispose(); }
            if (c.Stopping) return;

            using var manager = MediaFactory.MFCreateDXGIDeviceManager();
            manager.ResetDevice(gpu.Device).CheckError();
            IMFSourceReader reader;
            using (var attrs = MediaFactory.MFCreateAttributes(2))
            {
                attrs.Set(SourceReaderAttributeKeys.D3DManager, manager);
                attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
                reader = MediaFactory.MFCreateSourceReaderFromURL(path, attrs);
            }
            using (reader)
            {
                using (var nv12 = MediaFactory.MFCreateMediaType())
                {
                    nv12.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    nv12.Set(MediaTypeAttributeKeys.Subtype, Media.Fourcc("NV12"));
                    reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, nv12);
                }
                string? decoder = null;
                using (var ex = reader.QueryInterfaceOrNull<IMFSourceReaderEx>())
                    if (ex != null)
                        for (int i = 0; i < 8; i++)
                        {
                            Guid category;
                            IMFTransform transform;
                            try { ex.GetTransformForStream((int)SourceReaderIndex.FirstVideoStream, i, out category, out transform); }
                            catch { break; }
                            using (transform)
                                if (category == TransformCategoryGuids.VideoDecoder) { decoder = Media.HardwareName(transform) ?? Media.Name(transform); break; }
                        }
                decoder ??= T("H.264 decoder");
                // Windows' own H.264 decoder decodes on the GPU (DXVA) when its frames come out in video memory
                bool onGpu = false;
                using (var first = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out _, out _))
                    if (first != null)
                    {
                        using var buffer = first.GetBufferByIndex(0);
                        using var dxgi = buffer.QueryInterfaceOrNull<IMFDXGIBuffer>();
                        onGpu = dxgi != null;
                    }
                reader.SetCurrentPosition(0);
                c.Result.Device = $"{decoder} ({(onGpu ? T("on the GPU") : T("on the CPU"))})";
                if (!onGpu) c.Note(T("Windows decoded on the CPU, not the GPU"));
                c.Step = T("H.264 at 1080p");
                c.Begin();
                long n = 0, lastN = 0, loops = 0;
                var last = TimeSpan.Zero;
                while (!c.TimeUp)
                {
                    using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                    if (sample != null) n++;
                    if ((flags & SourceReaderFlag.EndOfStream) != 0)
                    {
                        loops++;
                        reader.SetCurrentPosition(0);
                    }
                    if ((flags & SourceReaderFlag.Error) != 0) { c.Error(T("The decoder reported an error")); break; }
                    var now = c.Elapsed;
                    if (now - last >= TimeSpan.FromMilliseconds(250))
                    {
                        c.Report((n - lastN) / (now - last).TotalSeconds);
                        lastN = n;
                        last = now;
                    }
                }
                c.Result.Value = n / Math.Max(0.001, c.Elapsed.TotalSeconds);
                c.Add(T("Frames"), n, "");
                c.Add(T("Times through the clip"), loops, "");
                c.Detail(F("{0} frames decoded ({1:0} fps) by {2}", n, c.Result.Value, c.Result.Device));
            }
        }
        catch (Exception ex) when (ex is SharpGenException or COMException)
        {
            if (gpu.Lost() is { } lost) c.Error(lost);
            else c.Result.Failed = F("The decoder couldn't run: {0}", ex.Message);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
