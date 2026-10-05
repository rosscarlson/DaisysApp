using DaisysApp.Logging;
using Microsoft.Win32;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DaisysApp.Applets.MiniMirror;

/// <summary>
/// One shared screen capture per monitor, reference-counted across every mirror that reads from it. Starts a monitor's
/// capture when the first mirror needs it and stops it when the last one lets go.
/// </summary>
internal sealed class CaptureManager : IDisposable
{
    private sealed class Entry
    {
        public required MonitorCapture Capture;
        public int RefCount;
    }

    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private bool disposed;

    /// <summary>Raised (on a background thread) when monitors are added, removed or change resolution.</summary>
    public event Action? DisplaysChanged;

    public CaptureManager() => SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

    public MonitorFrameBuffer Acquire(string deviceName)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (entries.TryGetValue(deviceName, out var entry))
            {
                entry.RefCount++;
                return entry.Capture.FrameBuffer;
            }
            var capture = new MonitorCapture(deviceName);
            capture.Start();
            entries[deviceName] = new Entry { Capture = capture, RefCount = 1 };
            return capture.FrameBuffer;
        }
    }

    /// <summary>Starts every running capture again (e.g. after the HDR setting changed).</summary>
    public void RebuildAll()
    {
        lock (gate)
            foreach (var entry in entries.Values) entry.Capture.RequestRebuild();
    }

    /// <summary>Whether any monitor is being captured as HDR right now.</summary>
    public bool AnyHdr
    {
        get { lock (gate) return entries.Values.Any(e => e.Capture.IsHdr); }
    }

    public void Release(string deviceName)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(deviceName, out var entry)) return;
            if (--entry.RefCount > 0) return;
            entry.Capture.Dispose();
            entries.Remove(deviceName);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        lock (gate)
            foreach (var entry in entries.Values) entry.Capture.RequestRebuild();
        DisplaysChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            foreach (var entry in entries.Values) entry.Capture.Dispose();
            entries.Clear();
        }
    }
}

/// <summary>
/// A DXGI Desktop Duplication session for one monitor, on its own D3D11 device (created on the GPU that drives that
/// monitor) and background thread, copying each new frame into <see cref="FrameBuffer"/>. AcquireNextFrame times out
/// when nothing on screen changed, so unchanged frames are skipped for free.
/// </summary>
internal sealed class MonitorCapture : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
        { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };

    public string DeviceName { get; }
    public MonitorFrameBuffer FrameBuffer { get; } = new();

    private Thread? thread;
    private volatile bool running;
    private volatile bool rebuildRequested;

    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Texture2D? staging;
    private HdrToSdr? hdr;
    private bool hdrFailed;
    private float whiteScale = 2.5f;
    private long nextWhiteCheck;
    private int width, height;
    private string? lastError;

    /// <summary>Convert HDR monitors' pictures to SDR (Settings → Mini Mirror). Off: take Windows' 8-bit picture as is.</summary>
    public static volatile bool HdrConversion = true;

    /// <summary>Whether this monitor is being captured as HDR (and converted).</summary>
    public bool IsHdr => hdr != null;

    public MonitorCapture(string deviceName) => DeviceName = deviceName;

    public void Start()
    {
        if (thread != null) return;
        running = true;
        thread = new Thread(CaptureLoop) { IsBackground = true, Name = "MiniMirror-Capture-" + DeviceName };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>Tears down and recreates the duplication on the next frame (monitor layout or resolution changed).</summary>
    public void RequestRebuild() => rebuildRequested = true;

    private void CaptureLoop()
    {
        while (running)
        {
            try
            {
                if (rebuildRequested)
                {
                    rebuildRequested = false;
                    ReleaseDxgi();
                }
                if (duplication == null && !TryInitialize())
                {
                    Thread.Sleep(500);
                    continue;
                }

                Result result = duplication!.AcquireNextFrame(250, out _, out IDXGIResource? desktop);
                if (result.Failure)
                {
                    desktop?.Dispose();
                    if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) continue; // nothing changed on screen

                    // Access lost (mode change, UAC prompt, full-screen exclusive game, GPU reset…): start over.
                    ReleaseDxgi();
                    Thread.Sleep(200);
                    continue;
                }

                try
                {
                    using (desktop)
                    using (var frame = desktop!.QueryInterface<ID3D11Texture2D>())
                    {
                        if (hdr != null)
                        {
                            // the user can change the SDR brightness slider at any time; it's cheap to look again
                            if (Environment.TickCount64 >= nextWhiteCheck)
                            {
                                whiteScale = SdrWhiteLevel.ScaleFor(DeviceName);
                                nextWhiteCheck = Environment.TickCount64 + 2000;
                            }
                            hdr.Convert(context!, frame, whiteScale, staging!);
                        }
                        else context!.CopyResource(staging!, frame);
                    }

                    var map = context.Map(staging!, 0, MapMode.Read);
                    try { FrameBuffer.Update(map.DataPointer, (int)map.RowPitch, width, height); }
                    finally { context.Unmap(staging!, 0); }
                }
                finally
                {
                    duplication.ReleaseFrame();
                }
            }
            catch (Exception ex)
            {
                LogOnce(ex);
                if (hdr != null) hdrFailed = true; // the conversion broke: carry on with the 8-bit picture
                ReleaseDxgi();
                Thread.Sleep(500);
            }
        }
        ReleaseDxgi();
    }

    private bool TryInitialize()
    {
        if (!TryResolveOutput(DeviceName, out var adapter, out var output)) return false;
        using (adapter)
        using (output)
        {
            try
            {
                D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, FeatureLevels,
                    out device, out context).CheckError();
                duplication = Duplicate(output!, device!);

                var mode = duplication.Description.ModeDescription;
                width = (int)mode.Width;
                height = (int)mode.Height;
                if (mode.Format == Format.R16G16B16A16_Float)
                {
                    hdr = new HdrToSdr(device!, width, height);
                    whiteScale = SdrWhiteLevel.ScaleFor(DeviceName);
                    nextWhiteCheck = Environment.TickCount64 + 2000;
                }
                staging = device!.CreateTexture2D(new Texture2DDescription
                {
                    CPUAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = (uint)width,
                    Height = (uint)height,
                    MiscFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                });
                lastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LogOnce(ex);
                ReleaseDxgi();
                return false;
            }
        }
    }

    /// <summary>
    /// Finds the GPU and output that drive the monitor named e.g. "\\.\DISPLAY1". Duplication is per GPU, so on
    /// multi-GPU systems each monitor has to go through the adapter it's really connected to.
    /// </summary>
    private static bool TryResolveOutput(string deviceName, out IDXGIAdapter1? adapter, out IDXGIOutput1? output)
    {
        adapter = null;
        output = null;
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var candidate).Success; a++)
        {
            for (uint o = 0; candidate.EnumOutputs(o, out var candidateOutput).Success; o++)
            {
                bool match = string.Equals(candidateOutput.Description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase);
                if (match) output = candidateOutput.QueryInterface<IDXGIOutput1>();
                candidateOutput.Dispose();
                if (match)
                {
                    adapter = candidate;
                    return true;
                }
            }
            candidate.Dispose();
        }
        return false;
    }

    private void LogOnce(Exception ex)
    {
        // the same failure repeats every retry (e.g. while a UAC prompt is up); log it once
        if (ex.Message == lastError) return;
        lastError = ex.Message;
        ErrorLog.Write($"Mini Mirror capture of {DeviceName}", ex);
    }

    /// <summary>
    /// On an HDR monitor, asks for the desktop as 16-bit scRGB so it can be converted properly; otherwise (or with the
    /// conversion off, or on older Windows) the usual 8-bit picture.
    /// </summary>
    private IDXGIOutputDuplication Duplicate(IDXGIOutput1 output, ID3D11Device device)
    {
        if (HdrConversion && !hdrFailed)
        {
            using var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
            if (output6 != null && output6.Description1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020)
            {
                try
                {
                    using var output5 = output.QueryInterface<IDXGIOutput5>();
                    return output5.DuplicateOutput1(device, 0, new[] { Format.R16G16B16A16_Float });
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Mini Mirror HDR capture (using the 8-bit picture instead)", ex);
                }
            }
        }
        return output.DuplicateOutput(device);
    }

    private void ReleaseDxgi()
    {
        hdr?.Dispose();
        hdr = null;
        staging?.Dispose();
        staging = null;
        duplication?.Dispose();
        duplication = null;
        context?.Dispose();
        context = null;
        device?.Dispose();
        device = null;
    }

    public void Dispose()
    {
        running = false;
        if (thread != null && !thread.Join(2000)) return; // stuck in the driver: let the thread release its own resources
        thread = null;
    }
}

/// <summary>
/// The latest captured frame of one monitor as tightly packed BGRA32. Written by the capture thread, read by mirror
/// render threads, so everything is under one lock.
/// </summary>
internal sealed class MonitorFrameBuffer
{
    private readonly object gate = new();
    private byte[] buffer = Array.Empty<byte>();
    private int width, height, stride;
    private long version;

    public unsafe void Update(IntPtr source, int sourcePitch, int width, int height)
    {
        int destStride = width * 4;
        lock (gate)
        {
            int needed = destStride * height;
            if (buffer.Length != needed) buffer = new byte[needed];
            byte* s = (byte*)source;
            fixed (byte* d = buffer)
            {
                for (int y = 0; y < height; y++)
                    Buffer.MemoryCopy(s + (long)y * sourcePitch, d + (long)y * destStride, destStride, destStride);
            }
            this.width = width;
            this.height = height;
            stride = destStride;
            version++;
        }
    }

    public long Version
    {
        get { lock (gate) return version; }
    }

    /// <summary>
    /// Copies the part of <paramref name="localRect"/> (in this monitor's own pixels) that's on the monitor into
    /// <paramref name="dest"/> at (destX, destY). False if no frame has arrived yet or the rect is off the monitor.
    /// </summary>
    public bool CopyLocalRegion(PixelRect localRect, byte[] dest, int destStride, int destX, int destY)
    {
        lock (gate)
        {
            if (width == 0 || height == 0) return false;
            var clipped = localRect.Intersect(new PixelRect(0, 0, width, height));
            if (clipped.IsEmpty) return false;

            int rowBytes = clipped.Width * 4;
            int xInDest = destX + (clipped.X - localRect.X);
            int yInDest = destY + (clipped.Y - localRect.Y);
            for (int row = 0; row < clipped.Height; row++)
            {
                int src = (clipped.Y + row) * stride + clipped.X * 4;
                int dst = (yInDest + row) * destStride + xInDest * 4;
                if (src < 0 || src + rowBytes > buffer.Length || dst < 0 || dst + rowBytes > dest.Length) continue;
                Buffer.BlockCopy(buffer, src, dest, dst, rowBytes);
            }
            return true;
        }
    }
}
