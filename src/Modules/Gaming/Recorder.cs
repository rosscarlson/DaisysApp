using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DaisysApp.Logging;
using DaisysApp.Shared.Hardware;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

namespace DaisysApp.Applets.Gaming;

/// <summary>What to record and how.</summary>
internal sealed record RecordingRequest
{
    public CaptureSource Source { get; init; }
    public string Monitor { get; init; } = "";
    public PixelRect Region { get; init; }
    /// <summary>The window to follow when recording a program.</summary>
    public IntPtr Window { get; init; }
    /// <summary>Names the file, e.g. the game's friendly name.</summary>
    public string Title { get; init; } = "";
    public required RecordingProfile Profile { get; init; }
    public bool SystemAudio { get; init; }
    public bool Microphone { get; init; }
    public bool Cursor { get; init; }
    public HdrRecording Hdr { get; init; }
    public required string Folder { get; init; }
}

/// <summary>
/// Records the screen like the NVIDIA app's recorder: Windows' desktop duplication hands over each new frame as a GPU
/// texture, shaders convert it to YUV on the GPU, and the GPU's own video encoder (NVENC on GeForce cards) compresses it,
/// so the picture never comes back to the CPU and the game's 3D work isn't touched. The audio is AAC. An MP4 file.
/// Refuses to start rather than fall back to Windows' software encoder, which would cost games a lot of CPU.
/// </summary>
internal sealed class Recorder : IDisposable
{
    private Thread? thread;
    private volatile bool stopping;
    private readonly object writeGate = new();

    public bool IsRecording { get; private set; }
    public DateTime StartedAt { get; private set; }
    public string? FilePath { get; private set; }
    public string EncoderName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public int FramesWritten { get; private set; }
    public int FramesDropped { get; private set; }

    /// <summary>Raised on a background thread when a recording ends, with the error that ended it (or null).</summary>
    public event Action<string?>? Stopped;

    /// <summary>Starts recording; returns null, or why it couldn't start.</summary>
    public string? Start(RecordingRequest request)
    {
        if (IsRecording) return null;
        stopping = false;
        string? error = null;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Run(request, ready, e => error = e)) { IsBackground = true, Name = "Gaming-Recorder" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        if (!ready.Wait(15000)) { stopping = true; return T("The recorder didn't start in time."); }
        return error;
    }

    public void Stop()
    {
        stopping = true;
        thread?.Join(10000);
        thread = null;
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- the recording thread

    private sealed class Session : IDisposable
    {
        public ID3D11Device? Device;
        public ID3D11DeviceContext? Context;
        public IDXGIOutput1? Output;
        public IDXGIOutputDuplication? Duplication;
        public FrameConverter? Converter;
        public IMFDXGIDeviceManager? Manager;
        public IMFSinkWriter? Writer;
        public IMFVideoSampleAllocatorEx? Allocator;
        public AudioTrack? SystemAudio, Microphone;

        public void Dispose()
        {
            SystemAudio?.Dispose();
            Microphone?.Dispose();
            Allocator?.Dispose();
            Writer?.Dispose();
            Manager?.Dispose();
            Converter?.Dispose();
            Duplication?.Dispose();
            Output?.Dispose();
            Context?.Dispose();
            Device?.Dispose();
        }
    }

    private void Run(RecordingRequest request, ManualResetEventSlim ready, Action<string> fail)
    {
        var s = new Session();
        string? endError = null;
        bool started = false;
        try
        {
            Encoders.Startup();
            // ---------------- where
            var monitors = Native.Monitors();
            MonitorInfo? monitor = request.Source switch
            {
                CaptureSource.Region => Native.MonitorFor(request.Region, monitors),
                CaptureSource.App => request.Window != IntPtr.Zero ? Native.MonitorFor(Native.GetBounds(request.Window), monitors) : null,
                _ => monitors.FirstOrDefault(m => m.DeviceName == request.Monitor) ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault(),
            };
            if (monitor == null) throw new RecorderException(request.Source == CaptureSource.App ? T("The program's window wasn't found.") : T("No monitor found to record."));
            PixelRect Crop() => request.Source switch
            {
                CaptureSource.Region => Local(request.Region, monitor.Bounds),
                CaptureSource.App => Local(Native.ClientBounds(request.Window), monitor.Bounds),
                _ => new PixelRect(0, 0, monitor.Bounds.Width, monitor.Bounds.Height),
            };
            var crop = Crop();
            if (crop.Width < 16 || crop.Height < 16) throw new RecorderException(T("The area to record is too small (or off the screen)."));

            // ---------------- capture
            OpenDuplication(s, monitor.DeviceName);
            bool hdrSource = s.Duplication!.Description.ModeDescription.Format == Format.R16G16B16A16_Float;
            var profile = request.Profile;
            string codec = Encoders.Available().ContainsKey(profile.Codec) ? profile.Codec
                : throw new RecorderException(F("This graphics card can't encode {0}. Choose another codec in the profile.", Encoders.Label(profile.Codec)));
            var mode = !hdrSource ? FrameConverter.Mode.Sdr
                : request.Hdr == HdrRecording.Hdr10 && codec != "H264" ? FrameConverter.Mode.Hdr10 : FrameConverter.Mode.HdrToSdr;
            var (outW, outH) = OutputSize(crop, profile.Height, codec);
            int fps = Math.Clamp(profile.Fps, 10, 240);

            // ---------------- the encoder
            Directory.CreateDirectory(request.Folder);
            string name = string.Concat((string.IsNullOrWhiteSpace(request.Title) ? T("Desktop") : request.Title).Split(Path.GetInvalidFileNameChars()));
            FilePath = Path.Combine(request.Folder, $"{name} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
            s.Manager = MediaFactory.MFCreateDXGIDeviceManager();
            s.Manager.ResetDevice(s.Device!).CheckError();
            using (var attrs = MediaFactory.MFCreateAttributes(3))
            {
                attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
                attrs.Set(SinkWriterAttributeKeys.D3DManager, s.Manager);
                s.Writer = MediaFactory.MFCreateSinkWriterFromURL(FilePath, null, attrs);
            }
            bool p010 = mode == FrameConverter.Mode.Hdr10;
            using var inType = VideoType(Encoders.Fourcc(p010 ? "P010" : "NV12"), outW, outH, fps, p010);
            int video;
            using (var outType = VideoType(Encoders.Subtype(codec), outW, outH, fps, p010))
            {
                outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)(profile.BitrateMbps * 1e6));
                // H.264 High; HEVC Main or Main 10
                if (codec == "H264") outType.Set(Mpeg2Profile, 100u);
                else if (codec == "HEVC") outType.Set(Mpeg2Profile, p010 ? 2u : 1u);
                video = s.Writer.AddStream(outType);
            }
            using (var enc = MediaFactory.MFCreateAttributes(5))
            {
                bool cbr = profile.RateControl == RateControl.Cbr;
                enc.Set(RateControlMode, cbr ? 0u : 1u); // CBR, or peak-constrained VBR
                enc.Set(MeanBitRate, (uint)(profile.BitrateMbps * 1e6));
                if (!cbr) enc.Set(MaxBitRate, (uint)(Math.Max(profile.MaxBitrateMbps, profile.BitrateMbps) * 1e6));
                enc.Set(GopSize, (uint)(fps * Math.Clamp(profile.KeyframeSeconds, 1, 10)));
                enc.Set(QualityVsSpeed, (uint)Math.Clamp(profile.Quality, 0, 100));
                s.Writer.SetInputMediaType(video, inType, enc);
            }
            EncoderName = HardwareEncoderName(s.Writer, video)
                ?? throw new RecorderException(F("The graphics card's encoder didn't accept {0} at {1}×{2} {3} fps, and Windows' software encoder would slow games down, so the recording was stopped. Try a lower resolution or frame rate, or another codec.",
                    Encoders.Label(codec), outW, outH, fps));

            int systemStream = -1, micStream = -1;
            if (request.SystemAudio) s.SystemAudio = OpenAudio(s, false, profile.AudioKbps, ref systemStream);
            if (request.Microphone) s.Microphone = OpenAudio(s, true, profile.AudioKbps, ref micStream);

            s.Allocator = new IMFVideoSampleAllocatorEx(MediaFactory.MFCreateVideoSampleAllocatorEx(typeof(IMFVideoSampleAllocatorEx).GUID));
            s.Allocator.SetDirectXManager(s.Manager);
            using (var sa = MediaFactory.MFCreateAttributes(1))
            {
                sa.Set(SaD3D11BindFlags, (uint)(BindFlags.RenderTarget | BindFlags.VideoEncoder));
                s.Allocator.InitializeSampleAllocatorEx(4, 16, sa, inType);
            }
            s.Converter = new FrameConverter(s.Device!);
            s.Writer.BeginWriting();

            // ---------------- go
            long t0 = Stopwatch.GetTimestamp();
            s.SystemAudio?.Start(t0);
            s.Microphone?.Start(t0);
            FramesWritten = FramesDropped = 0;
            StartedAt = DateTime.Now;
            Description = F("{0}×{1} {2} fps · {3}", outW, outH, fps, Encoders.Label(codec)) + (p010 ? " HDR10" : "");
            IsRecording = true;
            started = true;
            ready.Set();

            Loop(s, monitor, request, crop, Crop, mode, video, fps, t0);
        }
        catch (Exception ex)
        {
            string message = ex is RecorderException ? ex.Message : F("Recording failed: {0}", ex.Message);
            if (ex is not RecorderException) ErrorLog.Write("Gaming recording", ex);
            if (!started) fail(message);
            else endError = message;
        }
        finally
        {
            if (!started) ready.Set();
            try { s.SystemAudio?.Stop(); s.Microphone?.Stop(); } catch { }
            bool empty = started && FramesWritten == 0;
            if (empty)
                // Windows never sent a picture (the monitor is asleep or off, or the PC is locked): an empty file is no use
                endError ??= T("Nothing was recorded: Windows sent no picture of the screen. Is the monitor asleep or switched off?");
            else if (started)
            {
                try { lock (writeGate) s.Writer?.Finalize(); }
                catch (Exception ex)
                {
                    ErrorLog.Write("Gaming recording: finishing the file", ex);
                    endError ??= F("The recording couldn't be finished: {0}", ex.Message);
                }
            }
            s.Dispose();
            if ((!started || empty) && FilePath != null) try { File.Delete(FilePath); } catch { }
            IsRecording = false;
            if (started) Stopped?.Invoke(endError);
        }
    }

    private void Loop(Session s, MonitorInfo monitor, RecordingRequest request, PixelRect crop, Func<PixelRect> recrop,
        FrameConverter.Mode mode, int video, int fps, long t0)
    {
        long freq = Stopwatch.Frequency;
        long slot = 0;
        double interval = 1.0 / fps;
        long nextFollow = 0, nextWhite = 0;
        float white = SdrWhiteLevel.ScaleFor(monitor.DeviceName);
        bool cursorVisible = false;
        int cursorX = 0, cursorY = 0;
        byte[] shapeBuffer = Array.Empty<byte>();
        var cursorShape = new CursorShape();

        while (!stopping)
        {
            long nowTicks = Stopwatch.GetTimestamp();
            double now = (nowTicks - t0) / (double)freq;
            double due = slot * interval;

            if (now < due - 0.001)
            {
                // wait for the next frame on screen, or the next slot
                if (s.Duplication == null)
                {
                    Thread.Sleep(Math.Max(1, (int)((due - now) * 1000)));
                    try { OpenDuplication(s, monitor.DeviceName, keepDevice: true); } catch { }
                    continue;
                }
                uint wait = (uint)Math.Max(1, (due - now) * 1000);
                var result = s.Duplication.AcquireNextFrame(wait, out var info, out var resource);
                if (result.Failure)
                {
                    resource?.Dispose();
                    if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) continue;
                    // access lost (resolution change, a UAC prompt, a game switching modes): keep recording the last frame
                    s.Duplication.Dispose();
                    s.Duplication = null;
                    continue;
                }
                try
                {
                    if (info.LastPresentTime != 0)
                        using (var tex = resource!.QueryInterface<ID3D11Texture2D>()) s.Converter!.SetSource(s.Context!, tex);
                    if (info.LastMouseUpdateTime != 0)
                    {
                        cursorVisible = info.PointerPosition.Visible;
                        cursorX = info.PointerPosition.Position.X;
                        cursorY = info.PointerPosition.Position.Y;
                    }
                    if (request.Cursor && info.PointerShapeBufferSize > 0)
                    {
                        if (shapeBuffer.Length < info.PointerShapeBufferSize) shapeBuffer = new byte[info.PointerShapeBufferSize];
                        unsafe
                        {
                            fixed (byte* p = shapeBuffer)
                                if (s.Duplication.GetFramePointerShape((uint)shapeBuffer.Length, (IntPtr)p, out _, out var shape).Success)
                                {
                                    var (bgra, w, h) = cursorShape.Convert(shapeBuffer, shape);
                                    s.Converter!.SetCursor(bgra, w, h);
                                }
                        }
                    }
                }
                finally
                {
                    resource?.Dispose();
                    s.Duplication.ReleaseFrame();
                }
                continue;
            }

            // more than a frame late (the PC was busy): skip ahead rather than fall further behind
            if (now - due > interval)
            {
                long next = (long)(now / interval);
                FramesDropped += (int)(next - slot);
                slot = next;
            }

            if (request.Source == CaptureSource.App && nowTicks >= nextFollow)
            {
                nextFollow = nowTicks + freq / 4;
                if (!Native.IsWindowAlive(request.Window)) throw new RecorderException(T("The recorded program closed."));
                if (!Native.IsMinimized(request.Window))
                {
                    var c = recrop();
                    if (c.Width >= 16 && c.Height >= 16) crop = c;
                }
            }
            if (nowTicks >= nextWhite)
            {
                nextWhite = nowTicks + 2 * freq;
                white = SdrWhiteLevel.ScaleFor(monitor.DeviceName); // the SDR brightness slider can move any time
            }

            if (s.Converter!.HasSource)
            {
                // the picture's size can change under us (resolution change): stay inside it
                var bounds = new PixelRect(0, 0, s.Converter.SourceWidth, s.Converter.SourceHeight);
                var area = crop.Intersect(bounds);
                if (!area.IsEmpty)
                {
                    var frameMode = s.Converter.SourceIsHdr ? mode : FrameConverter.Mode.Sdr;
                    if (!WriteVideo(s, video, frameMode, white, area, request.Cursor && cursorVisible, cursorX, cursorY, slot, fps)) FramesDropped++;
                    else FramesWritten++;
                }
            }
            slot++;
        }
    }

    private bool WriteVideo(Session s, int stream, FrameConverter.Mode mode, float white, PixelRect crop, bool cursor, int cx, int cy, long slot, int fps)
    {
        IMFSample sample;
        try { sample = s.Allocator!.AllocateSample(); }
        catch (SharpGenException) { return false; } // the encoder is behind: every buffer is still queued
        using (sample)
        {
            using var buffer = sample.GetBufferByIndex(0);
            using (var b2 = buffer.QueryInterface<IMF2DBuffer>()) buffer.CurrentLength = b2.ContiguousLength;
            using var dxgi = buffer.QueryInterface<IMFDXGIBuffer>();
            using var target = new ID3D11Texture2D(dxgi.GetResource(typeof(ID3D11Texture2D).GUID));
            s.Converter!.Convert(s.Context!, target, dxgi.SubresourceIndex, mode, white, crop, cursor, cx, cy);
            sample.SampleTime = slot * 10_000_000L / fps;
            sample.SampleDuration = 10_000_000L / fps;
            lock (writeGate) s.Writer!.WriteSample(stream, sample);
        }
        return true;
    }

    private AudioTrack? OpenAudio(Session s, bool microphone, int kbps, ref int stream)
    {
        AudioTrack? track = null;
        try
        {
            int index = -1, rate = 48000;
            track = new AudioTrack(microphone, (pcm, bytes, time) => WriteAudio(s, index, rate, pcm, bytes, time));
            rate = track.SampleRate;
            using var output = MediaFactory.MFCreateMediaType();
            output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
            output.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
            output.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)rate);
            output.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            // the AAC encoder takes 96, 128, 160 or 192 kbit/s
            int k = kbps >= 176 ? 192 : kbps >= 144 ? 160 : kbps >= 112 ? 128 : 96;
            output.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(k * 1000 / 8));
            index = s.Writer!.AddStream(output);
            using var input = MediaFactory.MFCreateMediaType();
            input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            input.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            input.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
            input.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)rate);
            input.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            input.Set(AudioBlockAlignment, 4u);
            input.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(rate * 4));
            s.Writer.SetInputMediaType(index, input, null);
            stream = index;
            return track;
        }
        catch (Exception ex)
        {
            track?.Dispose();
            ErrorLog.Write(microphone ? "Gaming recording: the microphone" : "Gaming recording: the PC's sound", ex);
            if (stream >= 0) throw;
            return null; // record without it
        }
    }

    private void WriteAudio(Session s, int stream, int rate, byte[] pcm, int bytes, long time)
    {
        if (stream < 0 || stopping) return;
        try
        {
            using var buffer = MediaFactory.MFCreateMemoryBuffer(bytes);
            buffer.Lock(out IntPtr data, out _, out _);
            Marshal.Copy(pcm, 0, data, bytes);
            buffer.Unlock();
            buffer.CurrentLength = bytes;
            using var sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = time;
            sample.SampleDuration = bytes / 4 * 10_000_000L / rate;
            lock (writeGate) if (!stopping && IsRecording) s.Writer!.WriteSample(stream, sample);
        }
        catch (Exception ex) { ErrorLog.Write("Gaming recording: audio", ex); }
    }

    // ---------------------------------------------------------------- helpers

    private static PixelRect Local(PixelRect desktop, PixelRect monitor)
    {
        var r = desktop.Intersect(monitor);
        return r.IsEmpty ? r : new PixelRect(r.X - monitor.X, r.Y - monitor.Y, r.Width, r.Height);
    }

    /// <summary>The encoded size: the recorded area scaled to the profile's height (never up), within the codec's limits, even.</summary>
    public static (int W, int H) OutputSize(PixelRect crop, int height, string codec)
    {
        double scale = height > 0 && height < crop.Height ? (double)height / crop.Height : 1;
        int max = Encoders.MaxSize(codec);
        scale = Math.Min(scale, Math.Min((double)max / crop.Width, (double)max / crop.Height));
        int w = Math.Max(64, (int)Math.Round(crop.Width * scale)) & ~1;
        int h = Math.Max(64, (int)Math.Round(crop.Height * scale)) & ~1;
        return (w, h);
    }

    private static IMFMediaType VideoType(Guid subtype, int w, int h, int fps, bool hdr)
    {
        var t = MediaFactory.MFCreateMediaType();
        t.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        t.Set(MediaTypeAttributeKeys.Subtype, subtype);
        t.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)w << 32) | (uint)h);
        t.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)fps << 32) | 1u);
        t.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1ul << 32) | 1u);
        t.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // progressive
        // say what the colours are, so players don't have to guess
        t.Set(VideoPrimaries, hdr ? 9u : 2u);    // BT.2020 / BT.709
        t.Set(TransferFunction, hdr ? 15u : 5u); // SMPTE 2084 (PQ) / BT.709
        t.Set(YuvMatrix, hdr ? 4u : 1u);         // BT.2020 / BT.709
        t.Set(NominalRange, 2u);                 // 16–235
        return t;
    }

    /// <summary>The name of the encoder the sink writer picked, if it's a hardware one; null for a software encoder.</summary>
    private static string? HardwareEncoderName(IMFSinkWriter writer, int stream)
    {
        using var ex = writer.QueryInterfaceOrNull<IMFSinkWriterEx>();
        if (ex == null) return null;
        for (int i = 0; i < 8; i++)
        {
            Guid category;
            IMFTransform transform;
            try { ex.GetTransformForStream(stream, i, out category, out transform); }
            catch { break; }
            using (transform)
            {
                if (category != TransformCategoryGuids.VideoEncoder) continue;
                using var a = transform.Attributes;
                bool hardware = false;
                try { hardware = a.GetUInt32(Encoders.TransformAsync) != 0; } catch { }
                try { hardware |= (a.GetAllocatedString(Encoders.HardwareUrl)?.Length ?? 0) > 0; } catch { }
                if (!hardware) return null;
                try { return a.GetAllocatedString(Encoders.FriendlyName); } catch { return T("Hardware encoder"); }
            }
        }
        return null;
    }

    private static void OpenDuplication(Session s, string deviceName, bool keepDevice = false)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? adapter = null;
        IDXGIOutput? output = null;
        for (uint a = 0; adapter == null && factory.EnumAdapters1(a, out var candidate).Success; a++)
        {
            for (uint o = 0; candidate.EnumOutputs(o, out var co).Success; o++)
            {
                if (string.Equals(co.Description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) { output = co; break; }
                co.Dispose();
            }
            if (output != null) adapter = candidate; else candidate.Dispose();
        }
        if (adapter == null || output == null) throw new RecorderException(T("The monitor to record wasn't found."));
        using (adapter)
        using (output)
        {
            if (!keepDevice || s.Device == null)
            {
                D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
                using (var mt = device.QueryInterface<ID3D11Multithread>()) mt.SetMultithreadProtected(true); // the encoder uses it too
                s.Device = device;
                s.Context = context;
            }
            s.Output?.Dispose();
            s.Output = output.QueryInterface<IDXGIOutput1>();
            s.Duplication = Duplicate(s.Output, s.Device!);
        }
    }

    /// <summary>HDR monitors: the real 16-bit picture (Windows' 8-bit one comes out washed out). Otherwise 8-bit.</summary>
    private static IDXGIOutputDuplication Duplicate(IDXGIOutput1 output, ID3D11Device device)
    {
        using var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
        if (output6 != null && output6.Description1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020)
        {
            try
            {
                using var output5 = output.QueryInterface<IDXGIOutput5>();
                return DuplicateOutput1(output5, device, Format.R16G16B16A16_Float);
            }
            catch (Exception ex) { ErrorLog.Write("Gaming recording: HDR capture (using the 8-bit picture instead)", ex); }
        }
        return output.DuplicateOutput(device);
    }

    /// <summary>IDXGIOutput5::DuplicateOutput1 called directly (see Mini Mirror's capture for why).</summary>
    private static unsafe IDXGIOutputDuplication DuplicateOutput1(IDXGIOutput5 output, ID3D11Device device, Format format)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, Format*, IntPtr*, int>)(*(void***)output.NativePointer)[26];
        IntPtr result;
        new Result(fn(output.NativePointer, device.NativePointer, 0, 1, &format, &result)).CheckError();
        return new IDXGIOutputDuplication(result);
    }

    private static readonly Guid RateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423"); // CODECAPI_AVEncCommonRateControlMode
    private static readonly Guid MeanBitRate = new("f7222374-2144-4815-b550-a37f8e12ee52");     // CODECAPI_AVEncCommonMeanBitRate
    private static readonly Guid MaxBitRate = new("9651eae4-39b9-4ebf-85ef-d7f444ec7465");      // CODECAPI_AVEncCommonMaxBitRate
    private static readonly Guid GopSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");         // CODECAPI_AVEncMPVGOPSize
    private static readonly Guid QualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");  // CODECAPI_AVEncCommonQualityVsSpeed
    private static readonly Guid SaD3D11BindFlags = new("eacf97ad-065c-4408-bee3-fdcbfd128be2"); // MF_SA_D3D11_BINDFLAGS
    private static readonly Guid Mpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");    // MF_MT_MPEG2_PROFILE
    private static readonly Guid VideoPrimaries = new("dbfbe4d7-0740-4ee0-8192-850ab0e21935");  // MF_MT_VIDEO_PRIMARIES
    private static readonly Guid TransferFunction = new("5fb0fce9-be5c-4935-a811-ec838f8eed93"); // MF_MT_TRANSFER_FUNCTION
    private static readonly Guid YuvMatrix = new("3e23d450-2c75-4d25-a00e-b91670d12327");       // MF_MT_YUV_MATRIX
    private static readonly Guid NominalRange = new("c21b8ee5-b956-4071-8daf-325edf5cab11");    // MF_MT_VIDEO_NOMINAL_RANGE
    private static readonly Guid AudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d"); // MF_MT_AUDIO_BLOCK_ALIGNMENT
}

internal sealed class RecorderException(string message) : Exception(message);

/// <summary>Turns desktop duplication's pointer shapes into straight-alpha BGRA.</summary>
internal sealed class CursorShape
{
    public (byte[] Bgra, int Width, int Height) Convert(byte[] buffer, OutduplPointerShapeInfo info)
    {
        int w = (int)info.Width, pitch = (int)info.Pitch;
        int type = (int)info.Type;
        int h = type == 1 ? (int)info.Height / 2 : (int)info.Height; // monochrome: AND mask, then XOR mask
        var bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                if (type == 1)
                {
                    int bit = 0x80 >> (x & 7);
                    bool and = (buffer[y * pitch + x / 8] & bit) != 0;
                    bool xor = (buffer[(y + h) * pitch + x / 8] & bit) != 0;
                    // AND 0: black or white; AND 1, XOR 1 inverts the screen (the text cursor): shown black
                    if (and && !xor) continue;
                    byte v = !and && xor ? (byte)255 : (byte)0;
                    bgra[o] = bgra[o + 1] = bgra[o + 2] = v;
                    bgra[o + 3] = 255;
                }
                else
                {
                    int i = y * pitch + x * 4;
                    if (i + 3 >= buffer.Length) continue;
                    byte a = buffer[i + 3];
                    if (type == 4) // masked colour: alpha 0 = this colour, 0xFF = XOR (shown where it isn't black)
                        a = a == 0 ? (byte)255 : (buffer[i] | buffer[i + 1] | buffer[i + 2]) != 0 ? (byte)255 : (byte)0;
                    bgra[o] = buffer[i];
                    bgra[o + 1] = buffer[i + 1];
                    bgra[o + 2] = buffer[i + 2];
                    bgra[o + 3] = a;
                }
            }
        return (bgra, w, h);
    }
}
