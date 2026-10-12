using System.Runtime.InteropServices;
using DaisysApp.Logging;
using Vortice.MediaFoundation;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>A webcam (any video capture device Windows has): its id (the device's symbolic link) and name.</summary>
public sealed record CameraDevice(string Id, string Name);

/// <summary>
/// One webcam's live picture, shared by everything that shows it (a recording, the virtual webcam, the camera layout's
/// preview): <see cref="Open"/> starts it the first time and each <see cref="Dispose"/> lets go, the last one stopping it.
/// A thread reads it through Media Foundation, which converts whatever the camera sends (MJPG, NV12, YUY2…) to BGRA, at
/// the camera's best mode up to 1920×1080; the newest frame is kept for <see cref="ReadLatest"/>.
/// </summary>
internal sealed class CameraFeed : IDisposable
{
    private static readonly Dictionary<string, CameraFeed> open = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Logger log = Log.Here;

    /// <summary>The cameras Windows has, by name.</summary>
    public static List<CameraDevice> List()
    {
        var list = new List<CameraDevice>();
        try
        {
            Encoders.Startup();
            using var devices = MediaFactory.MFEnumVideoDeviceSources();
            foreach (var a in devices)
                using (a)
                {
                    try { list.Add(new CameraDevice(a.GetAllocatedString(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink) ?? "", a.GetAllocatedString(CaptureDeviceAttributeKeys.FriendlyName) ?? T("Camera"))); }
                    catch { }
                }
        }
        catch (Exception ex) { log.Warn("Couldn't list the cameras", ex); }
        // (our own virtual webcam isn't a Media Foundation camera, so it can't show up here and feed itself)
        return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The camera's feed, started if it isn't already. Dispose it when done.</summary>
    public static CameraFeed Open(string id, string name)
    {
        lock (open)
        {
            if (!open.TryGetValue(id, out var feed))
            {
                feed = new CameraFeed(id, name);
                open[id] = feed;
            }
            feed.users++;
            return feed;
        }
    }

    public string Id { get; }
    public string Name { get; }

    /// <summary>Why the camera isn't sending pictures (not found, in use, unplugged), or null.</summary>
    public string? Error { get; private set; }

    private readonly object frameGate = new();
    private byte[] frame = [];
    private int width, height;
    private long number;
    private int users;
    private volatile bool stopping;
    private readonly Thread thread;

    private CameraFeed(string id, string name)
    {
        Id = id;
        Name = name;
        thread = new Thread(Run) { IsBackground = true, Name = "Camera " + name };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>
    /// If there's a newer frame than <paramref name="since"/>, hands it (top-down BGRA, stride width × 4) to
    /// <paramref name="use"/> while it can't change, and returns its number; otherwise returns <paramref name="since"/>.
    /// </summary>
    public long ReadLatest(long since, Action<byte[], int, int> use)
    {
        lock (frameGate)
        {
            if (number == since || width == 0) return since;
            use(frame, width, height);
            return number;
        }
    }

    public (int Width, int Height) Size { get { lock (frameGate) return (width, height); } }

    private void Run()
    {
        IMFMediaSource? source = null;
        IMFSourceReader? reader = null;
        try
        {
            Encoders.Startup();
            using (var attrs = MediaFactory.MFCreateAttributes(2))
            {
                attrs.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
                attrs.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink, Id);
                source = MediaFactory.MFCreateDeviceSource(attrs);
            }
            using (var attrs = MediaFactory.MFCreateAttributes(1))
            {
                attrs.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u); // converts to BGRA for us
                reader = MediaFactory.MFCreateSourceReaderFromMediaSource(source, attrs);
            }
            var (w, h) = PickMode(reader);
            using (var output = MediaFactory.MFCreateMediaType())
            {
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                output.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)w << 32) | (uint)h);
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);
            }
            log.Info($"Camera {Name}: {w}x{h}");
            while (!stopping)
            {
                using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if ((flags & (SourceReaderFlag.EndOfStream | SourceReaderFlag.Error)) != 0) throw new InvalidOperationException(T("The camera stopped sending pictures."));
                if (sample == null) continue;
                Store(sample, w, h);
                Error = null;
            }
        }
        catch (Exception ex) when (!stopping)
        {
            int code = ex is SharpGen.Runtime.SharpGenException sg ? sg.ResultCode.Code : ex.HResult;
            Error = code switch
            {
                unchecked((int)0xC00D3704) => T("The camera is in use by another program."), // MF_E_HW_MFT_FAILED_START_STREAMING
                unchecked((int)0x80070002) or unchecked((int)0x80070003) or unchecked((int)0xC00D36D5) or unchecked((int)0x8007048F)
                    => T("The camera isn't connected (or Windows can't find it)."),
                unchecked((int)0x80070005) => T("Windows doesn't let apps use the camera: Settings → Privacy & security → Camera."),
                _ => ex.Message,
            };
            log.Warn($"Camera {Name} stopped: {Error}", ex);
        }
        catch { }
        finally
        {
            reader?.Dispose();
            try { source?.Shutdown(); } catch { }
            source?.Dispose();
        }
    }

    /// <summary>The camera's best mode: the most pixels up to 1920×1080, at 24 frames a second or more if it can.</summary>
    private static (int W, int H) PickMode(IMFSourceReader reader)
    {
        (IMFMediaType? Type, int W, int H, double Fps) best = (null, 0, 0, 0);
        for (int i = 0; ; i++)
        {
            IMFMediaType type;
            try { type = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, i); }
            catch { break; }
            ulong size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            ulong rate = 0;
            try { rate = type.GetUInt64(MediaTypeAttributeKeys.FrameRate); } catch { }
            int w = (int)(size >> 32), h = (int)(size & 0xFFFFFFFF);
            double fps = (rate & 0xFFFFFFFF) == 0 ? 0 : (double)(rate >> 32) / (rate & 0xFFFFFFFF);
            bool fits = w * h <= 1920 * 1080;
            static double Score(int w, int h, double fps, bool fits) => (fits ? 1e12 : 0) + (fps >= 24 ? 1e10 : 0) + (fits ? w * h : -w * h) * 100.0 + Math.Min(fps, 60);
            if (best.Type == null || Score(w, h, fps, fits) > Score(best.W, best.H, best.Fps, best.W * best.H <= 1920 * 1080))
            {
                best.Type?.Dispose();
                best = (type, w, h, fps);
            }
            else type.Dispose();
        }
        if (best.Type == null) return (1280, 720);
        using (best.Type) reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, best.Type);
        return (best.W, best.H);
    }

    private unsafe void Store(IMFSample sample, int w, int h)
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
            lock (frameGate)
            {
                if (frame.Length != w * h * 4) frame = new byte[w * h * 4];
                fixed (byte* dst = frame)
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy((byte*)scan0 + (long)y * pitch, dst + (long)y * w * 4, w * 4, w * 4);
                width = w;
                height = h;
                number++;
            }
        }
        finally
        {
            if (b2 != null) b2.Unlock2D(); else buffer.Unlock();
        }
    }

    public void Dispose()
    {
        lock (open)
        {
            if (--users > 0) return;
            open.Remove(Id);
        }
        stopping = true;
        // ReadSample returns within a frame or two; don't hold the caller up longer than that
        thread.Join(1000);
    }
}
