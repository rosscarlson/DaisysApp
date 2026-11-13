using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.8.0) A race's video read with Media Foundation (WPF's MediaElement can't play AV1, nor always HEVC): decoded
/// on the graphics card where it can, scaled to at most 1280 pixels across, on a thread of its own. The frame at a
/// time (<see cref="Want"/>: read on from the frame shown when it's a little ahead, else sought), or played on at its
/// own pace (<see cref="Play"/>); the newest frame is taken by <see cref="Take"/>. Its sound is <see cref="VideoSound"/>'s.
/// </summary>
internal sealed class VideoFrames : IDisposable
{
    private const int MaxWidth = 1280;
    /// <summary>How far ahead a frame is read on to rather than sought (a keyframe's every few seconds).</summary>
    private const double ReadOn = 3;

    private readonly string path;
    private readonly Thread thread;
    private readonly AutoResetEvent wake = new(false);
    private readonly object gate = new();
    private volatile bool stopping;

    private double wanted = double.NaN;
    private bool playing;
    private double playFrom;
    private long playStart;

    private byte[] pixels = Array.Empty<byte>();
    /// <summary>(since 4.15.2) The next frame's buffer, the decoder's own: the two are swapped as a frame's ready (a
    /// new 3.7 MB one each frame kept the garbage collector busy, the whole app stuttering as it played).</summary>
    private byte[] spare = Array.Empty<byte>();
    private double pixelsAt = double.NaN;
    private int number, taken;

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Its length (seconds), once open.</summary>
    public double Duration { get; private set; }
    public bool Ready { get; private set; }
    public string? Error { get; private set; }
    /// <summary>Played to its end.</summary>
    public bool Ended { get; private set; }

    public VideoFrames(string path)
    {
        this.path = path;
        thread = new Thread(Run) { IsBackground = true, Name = "Analysis-Video" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>Shows the frame at <paramref name="seconds"/> (stops playing).</summary>
    public void Want(double seconds)
    {
        lock (gate) { wanted = Math.Max(0, seconds); playing = false; }
        wake.Set();
    }

    /// <summary>Plays on from <paramref name="seconds"/>, at its own pace.</summary>
    public void Play(double seconds)
    {
        lock (gate) { playFrom = Math.Max(0, seconds); playStart = Environment.TickCount64; playing = true; wanted = playFrom; Ended = false; }
        wake.Set();
    }

    public void Pause()
    {
        lock (gate) playing = false;
    }

    /// <summary>The newest frame (BGRA, <see cref="Width"/> × <see cref="Height"/>) and its time, if there's one not
    /// taken yet, handed to <paramref name="use"/> (since 4.15.2 only while it's called: its buffer's used again).</summary>
    public bool Take(Action<byte[], double> use)
    {
        lock (gate)
        {
            if (number == taken) return false;
            taken = number;
            use(pixels, pixelsAt);
            return true;
        }
    }

    private void Run()
    {
        ID3D11Device? device = null;
        IMFDXGIDeviceManager? manager = null;
        IMFSourceReader? reader = null;
        try
        {
            MediaFactory.MFStartup(true).CheckError();
            (device, manager) = Card();
            try { reader = Open(manager); }
            catch (Exception ex) when (manager != null)
            {
                // (no graphics card to decode on: the processor does it)
                Logging.Log.Here.Info($"Analysis: the video decoded without the graphics card ({ex.Message})");
                reader?.Dispose();
                reader = Open(null);
            }
            try { Duration = Convert.ToUInt64(reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration).Value) / 1e7; } catch { }
            Ready = true;
            double at = double.NaN; // the last frame read
            while (!stopping)
            {
                bool play;
                double want;
                lock (gate) { play = playing; want = wanted; }
                if (play)
                {
                    double due;
                    lock (gate) due = playFrom + (Environment.TickCount64 - playStart) / 1000.0;
                    if (!double.IsNaN(want)) { lock (gate) wanted = double.NaN; if (double.IsNaN(at) || want < at || want - at > ReadOn) Seek(reader, want); }
                    using var sample = Read(reader, out double t, out bool end);
                    if (end) { lock (gate) playing = false; Ended = true; continue; }
                    if (sample == null) continue;
                    at = t;
                    if (t > due) wake.WaitOne(TimeSpan.FromSeconds(Math.Min(0.5, t - due)));
                    if (!stopping) Publish(sample, t);
                    continue;
                }
                if (double.IsNaN(want)) { wake.WaitOne(200); continue; }
                lock (gate) wanted = double.NaN;
                if (!double.IsNaN(at) && Math.Abs(want - at) < 0.008) continue;
                if (double.IsNaN(at) || want < at || want - at > ReadOn) Seek(reader, want);
                // read on to it (the last frame before it), unless something else is wanted meanwhile
                IMFSample? last = null;
                double lastAt = double.NaN;
                try
                {
                    while (!stopping)
                    {
                        var sample = Read(reader, out double t, out bool end);
                        if (end) break;
                        if (sample == null) continue;
                        last?.Dispose();
                        last = sample;
                        lastAt = t;
                        if (t + 0.008 >= want) break;
                        lock (gate) if (playing || (!double.IsNaN(wanted) && (wanted < t || wanted - t > ReadOn))) break;
                    }
                    if (last != null) { at = lastAt; Publish(last, lastAt); }
                }
                finally { last?.Dispose(); }
            }
        }
        catch (Exception ex) when (!stopping)
        {
            Error = ex.Message;
            Logging.Log.Here.Warn($"Analysis: the race's video couldn't be read: {ex.Message}");
        }
        catch { }
        finally
        {
            reader?.Dispose();
            manager?.Dispose();
            device?.Dispose();
        }
    }

    private IMFSourceReader Open(IMFDXGIDeviceManager? manager)
    {
        var reader = Open(path, manager, MaxWidth, out int w, out int h, out _);
        Width = w;
        Height = h;
        return reader;
    }

    /// <summary>A graphics card to decode on (since 4.9.0 also <see cref="VideoExport"/>'s), or nulls without one.</summary>
    internal static (ID3D11Device? Device, IMFDXGIDeviceManager? Manager) Card()
    {
        ID3D11Device? device = null;
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out device, out ID3D11DeviceContext context).CheckError();
            context.Dispose();
            using (var mt = device!.QueryInterface<ID3D11Multithread>()) mt.SetMultithreadProtected(true);
            var manager = MediaFactory.MFCreateDXGIDeviceManager();
            manager.ResetDevice(device).CheckError();
            return (device, manager);
        }
        catch
        {
            device?.Dispose();
            return (null, null);
        }
    }

    /// <summary>The file's picture, read as BGRA at most <paramref name="maxWidth"/> across (even sizes), and its frame rate.</summary>
    internal static IMFSourceReader Open(string path, IMFDXGIDeviceManager? manager, int maxWidth, out int w, out int h, out double fps)
    {
        IMFSourceReader reader;
        using (var attrs = MediaFactory.MFCreateAttributes(3))
        {
            attrs.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u); // converts (and scales) to BGRA for us
            if (manager != null)
            {
                attrs.Set(SourceReaderAttributeKeys.D3DManager, manager);
                attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
            }
            reader = MediaFactory.MFCreateSourceReaderFromURL(path, attrs);
        }
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
        fps = 60;
        using (var native = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0))
        {
            ulong size = native.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            w = (int)(size >> 32);
            h = (int)(size & 0xFFFFFFFF);
            try
            {
                ulong rate = native.GetUInt64(MediaTypeAttributeKeys.FrameRate);
                double n = rate >> 32, d = rate & 0xFFFFFFFF;
                if (n > 0 && d > 0 && n / d is > 5 and < 300) fps = n / d;
            }
            catch { }
        }
        if (w > maxWidth) { h = (int)Math.Round(h * (double)maxWidth / w / 2) * 2; w = maxWidth; }
        w &= ~1;
        h &= ~1;
        using (var output = MediaFactory.MFCreateMediaType())
        {
            output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            output.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)w << 32) | (uint)h);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);
        }
        return reader;
    }

    private static void Seek(IMFSourceReader reader, double seconds) => reader.SetCurrentPosition((long)(seconds * 1e7));

    private static IMFSample? Read(IMFSourceReader reader, out double at, out bool end)
    {
        var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out long time);
        end = (flags & (SourceReaderFlag.EndOfStream | SourceReaderFlag.Error)) != 0;
        at = time / 1e7;
        if (end) { sample?.Dispose(); return null; }
        return sample;
    }

    private void Publish(IMFSample sample, double at)
    {
        if (spare.Length != Width * Height * 4) spare = new byte[Width * Height * 4];
        Copy(sample, Width, Height, spare);
        lock (gate)
        {
            (pixels, spare) = (spare, pixels);
            pixelsAt = at;
            number++;
        }
    }

    /// <summary>A frame read (BGRA, <paramref name="w"/> × <paramref name="h"/>) into <paramref name="frame"/>, top row first.</summary>
    internal static unsafe void Copy(IMFSample sample, int w, int h, byte[] frame)
    {
        using var buffer = sample.ConvertToContiguousBuffer();
        using var b2 = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
        IntPtr scan0;
        int pitch;
        if (b2 != null) b2.Lock2D(out scan0, out pitch);
        else
        {
            buffer.Lock(out scan0, out _, out _);
            pitch = -w * 4; // a plain RGB32 buffer is bottom-up
            scan0 += (h - 1) * w * 4;
        }
        try
        {
            fixed (byte* dst = frame)
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy((byte*)scan0 + (long)y * pitch, dst + (long)y * w * 4, w * 4, w * 4);
        }
        finally
        {
            if (b2 != null) b2.Unlock2D(); else buffer.Unlock();
        }
    }

    public void Dispose()
    {
        stopping = true;
        wake.Set();
        // (it lets the file go when its thread ends; never hold the UI up for long)
        thread.Join(500);
    }
}
