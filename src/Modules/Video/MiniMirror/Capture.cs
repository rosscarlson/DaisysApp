using DaisysApp.Logging;
using DaisysApp.Shared.Hardware;
using Microsoft.Win32;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DaisysApp.Applets.Video.MiniMirror;

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

    private static readonly Logger log = Log.Here;
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
            log.Info($"Capture of {deviceName} started");
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
            log.Info($"Capture of {deviceName} stopped (no mirror uses it now)");
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        log.Info("Display settings changed: restarting the captures");
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
    private readonly ManualResetEventSlim stopping = new();
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
    private Format frameFormat;
    private bool hdrDuplication;
    private string? lastError;
    private int accessLost;
    // While Windows won't duplicate the screen (secure desktop: lock screen, UAC, Ctrl+Alt+Del), the D3D device is
    // kept and only the duplication asked for again, waiting longer each time: making and freeing a device twice a
    // second for minutes was where two crashes on a friend's PC died (D3D11CreateDevice, a device's Release; 3.18.5).
    private const int RetryStartMs = 500, RetryMaxMs = 5000;
    private int retryMs = RetryStartMs;
    private long deviceLuid;
    // A duplication can go quiet for good while the screen keeps changing (a driver or Windows update, a game going
    // full-screen; seen as one picture and then nothing). While it's quiet, a GDI grab of the monitor each second
    // tells a really still screen from a stuck duplication: if GDI sees two changes in a row and the duplication sent
    // nothing, it's set up again (which always delivers a fresh picture). Stuck twice in a row (fewer than LiveFrames
    // between): an HDR capture goes to Windows' 8-bit picture (what the SimHub plugin always used), then an 8-bit one
    // to GDI for good (since 2.6.1). If GDI can't look, the old way: set up again after StallStartMs, doubling.
    private const int StallStartMs = 2000, StallMaxMs = 30000, LiveFrames = 30;
    private int stallMs = StallStartMs, framesSinceInit, stalls, stuckSetups, probeChanges, setups;
    private long lastFrameAt, nextStats, nextProbe;
    private int statFrames, statTimeouts, statRebuilds;
    private ScreenGrab? grab;
    private bool gdi, grabFailed;
    private PixelRect gdiBounds;
    private long nextBounds, statGdiMs;
    private int gdiFps = 60;
    // monitors where duplication got stuck this run: new captures of them go straight to what worked
    private static readonly HashSet<string> HdrStuck = new(StringComparer.OrdinalIgnoreCase), GdiOnly = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Logger log = Log.Here;

    /// <summary>Convert HDR monitors' pictures to SDR (Settings → Video → Mini Mirror). Off: take Windows' 8-bit picture as is.</summary>
    public static volatile bool HdrConversion = true;

    /// <summary>Whether this monitor is being captured as HDR (and converted).</summary>
    public bool IsHdr => hdr != null;

    public MonitorCapture(string deviceName)
    {
        DeviceName = deviceName;
        lock (HdrStuck)
        {
            hdrFailed = HdrStuck.Contains(deviceName);
            gdi = GdiOnly.Contains(deviceName);
        }
        if (gdi) log.Info($"Capture of {deviceName}: through GDI, as desktop duplication got stuck on it earlier");
    }

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
                    log.Debug($"Capture of {DeviceName}: rebuilding");
                    ReleaseDxgi();
                    nextBounds = 0;
                }
                if (gdi)
                {
                    GdiFrame();
                    continue;
                }
                if (duplication == null && !TryInitialize())
                {
                    Wait(retryMs);
                    retryMs = Math.Min(RetryMaxMs, retryMs * 2);
                    continue;
                }

                Result result = duplication!.AcquireNextFrame(250, out var info, out IDXGIResource? desktop);
                if (result.Failure)
                {
                    desktop?.Dispose();
                    if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) // nothing changed on screen
                    {
                        statTimeouts++;
                        CheckStall();
                        continue;
                    }

                    // Access lost (mode change, UAC prompt, full-screen exclusive game, GPU reset…): start over.
                    // Only a lost device needs a new one; otherwise the duplication alone is set up again.
                    bool removed = device!.DeviceRemovedReason.Failure;
                    if (++accessLost <= 20 || accessLost % 100 == 0) log.Debug($"Capture of {DeviceName}: frame failed (0x{result.Code:X8}{(removed ? ", device lost" : "")}), starting over (time {accessLost})");
                    if (removed) ReleaseDxgi();
                    else ReleaseDuplication();
                    Wait(200);
                    continue;
                }

                try
                {
                    using (desktop)
                    using (var frame = desktop!.QueryInterface<ID3D11Texture2D>())
                    {
                        Prepare(frame.Description);
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

                    var map = context!.Map(staging!, 0, MapMode.Read);
                    try { FrameBuffer.Update(map.DataPointer, (int)map.RowPitch, width, height); }
                    finally { context.Unmap(staging!, 0); }
                    OnFrame(info);
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
                Wait(500);
            }
        }
        ReleaseDxgi();
        grab?.Dispose();
        grab = null;
    }

    private bool TryInitialize()
    {
        CaptureDiagnostics.LogSystemOnce();
        if (!TryResolveOutput(DeviceName, out var adapter, out var output))
        {
            if (lastError != "no output") log.Warn($"Capture of {DeviceName}: no GPU output drives that monitor now");
            lastError = "no output";
            return false;
        }
        using (adapter)
        using (output)
        {
            try
            {
                var id = adapter!.Description1.Luid;
                long luid = ((long)id.HighPart << 32) | id.LowPart;
                if (device != null && (luid != deviceLuid || device.DeviceRemovedReason.Failure)) ReleaseDxgi();
                if (device == null)
                {
                    D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, FeatureLevels,
                        out device, out context).CheckError();
                    deviceLuid = luid;
                }
                try { duplication = Duplicate(output!, device!); }
                catch (SharpGenException ex) when (NotNow(ex))
                {
                    LogOnce(ex);
                    return false; // the device stays for the next try
                }
                retryMs = RetryStartMs;

                // The mode's format isn't the pictures': an SDR monitor with Windows' colour management or Auto HDR
                // has a 16-bit mode too, while the 8-bit duplication still sends 8-bit pictures; taking those as HDR
                // made every mirror black (2.6.1). Prepare looks at each picture's own format.
                var mode = duplication.Description.ModeDescription;
                width = (int)mode.Width;
                height = (int)mode.Height;
                frameFormat = Format.Unknown;
                CreateStaging();
                lastError = null;
                framesSinceInit = 0;
                lastFrameAt = Environment.TickCount64;
                grab?.ForgetLast();
                probeChanges = 0;
                // the first set-up in full (later ones, after a stall or access lost, only on Debug)
                string text = $"Capturing {DeviceName}: {width}x{height}, mode {mode.Format}, {(hdrDuplication ? "16-bit HDR duplication" : "8-bit duplication")} on {adapter!.Description1.Description}";
                if (++setups == 1)
                    log.Info($"{text} ({CaptureDiagnostics.Describe(output!)}{(duplication.Description.DesktopImageInSystemMemory ? ", picture in system memory" : "")}{(HdrConversion ? "" : ", HDR conversion off")})");
                else log.Debug($"{text} (set-up {setups})");
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
    /// Sets up for a picture of this format and size: the HDR conversion only for 16-bit pictures, and the staging
    /// texture at the picture's size (CopyResource quietly does nothing when they don't match: a black mirror).
    /// </summary>
    private void Prepare(Texture2DDescription d)
    {
        if (d.Format == frameFormat && d.Width == width && d.Height == height) return;
        bool first = frameFormat == Format.Unknown;
        if (!first || setups == 1)
            log.Info($"Capture of {DeviceName}: pictures come as {d.Width}x{d.Height} {d.Format}{(first ? "" : $" (were {width}x{height} {frameFormat})")}{(d.Format == Format.R16G16B16A16_Float ? ", converting HDR to SDR" : "")}");
        frameFormat = d.Format;
        if (d.Width != width || d.Height != height)
        {
            width = (int)d.Width;
            height = (int)d.Height;
            CreateStaging();
        }
        hdr?.Dispose();
        hdr = null;
        if (d.Format == Format.R16G16B16A16_Float)
        {
            hdr = new HdrToSdr(device!, width, height);
            whiteScale = SdrWhiteLevel.ScaleFor(DeviceName);
            nextWhiteCheck = Environment.TickCount64 + 2000;
            log.Info($"Capture of {DeviceName}: SDR white at {whiteScale:0.##}× (Windows' SDR content brightness)");
        }
        else if (d.Format is not (Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb))
            log.Warn($"Capture of {DeviceName}: pictures in {d.Format}, which Mini Mirror doesn't expect; they may show black");
    }

    private void CreateStaging()
    {
        staging?.Dispose();
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
    }

    private void OnFrame(OutduplFrameInfo info)
    {
        statFrames++;
        lastFrameAt = Environment.TickCount64;
        grab?.ForgetLast();
        probeChanges = 0;
        framesSinceInit++;
        // the first set-up's first pictures, and the first after getting stuck: what the duplication says about them
        if (framesSinceInit <= 3 && (setups == 1 || stalls is > 0 and <= 3))
            log.Info($"Capture of {DeviceName}: picture {framesSinceInit} (set-up {setups}): {info.AccumulatedFrames} frame(s) presented{(info.LastPresentTime == 0 ? " (only the pointer moved)" : "")}{(info.ProtectedContentMaskedOut ? ", protected content blacked out" : "")}; brightness {FrameBuffer.Brightness()} of 255");
        // the first frame after setting up is always there; a run of them means the screen is coming through live
        if (framesSinceInit == LiveFrames)
        {
            if (stalls > 0) log.Info($"Capture of {DeviceName}: live again after {stalls} restart(s)");
            stallMs = StallStartMs;
            stalls = stuckSetups = 0;
        }
        Stats();
    }

    private void CheckStall()
    {
        Stats();
        long now = Environment.TickCount64, quiet = now - lastFrameAt;
        if (!grabFailed)
        {
            if (quiet < 1000 || now < nextProbe) return;
            nextProbe = now + (quiet < 10000 ? 1000 : 3000);
            bool? changed = Probe();
            if (changed == null) return;
            if (changed == false) { probeChanges = 0; return; }
            if (++probeChanges >= 2) Stuck(quiet);
            return;
        }
        // GDI can't look: set it up again after a while anyway, waiting longer while that brings only one picture
        if (quiet < stallMs) return;
        stalls++;
        statRebuilds++;
        if (stalls <= 3 || stalls % 50 == 0)
            log.Info($"Capture of {DeviceName}: no new frame for {stallMs / 1000.0:0.#} s ({framesSinceInit} since set-up), setting it up again (time {stalls})");
        if (framesSinceInit < 2) stallMs = Math.Min(StallMaxMs, stallMs * 2);
        ReleaseDxgi();
    }

    /// <summary>A GDI grab of the monitor: whether it changed since the last one (null: nothing to compare yet).</summary>
    private bool? Probe()
    {
        long start = Environment.TickCount64;
        if (start >= nextBounds) { gdiBounds = MonitorBounds(); nextBounds = start + 2000; }
        grab ??= new ScreenGrab();
        if (!grab.Grab(gdiBounds))
        {
            grabFailed = true;
            log.Info($"Capture of {DeviceName}: GDI can't grab {gdiBounds} to check a quiet capture; setting it up again after a while instead");
            return null;
        }
        return grab.ChangedSinceLast();
    }

    /// <summary>The screen changes but the duplication sends nothing: set it up again, and after twice, use something else.</summary>
    private void Stuck(long quiet)
    {
        stalls++;
        statRebuilds++;
        probeChanges = 0;
        if (stalls <= 3 || stalls % 50 == 0)
            log.Info($"Capture of {DeviceName}: the screen changes but desktop duplication sent nothing for {quiet / 1000.0:0.#} s ({framesSinceInit} picture(s) since set-up{(hdr != null ? ", HDR" : "")}), setting it up again (time {stalls}); {CaptureDiagnostics.Foreground(gdiBounds)}");
        if (++stuckSetups >= 2)
        {
            stuckSetups = 0;
            if (hdrDuplication)
            {
                hdrFailed = true;
                lock (HdrStuck) HdrStuck.Add(DeviceName);
                log.Warn($"Capture of {DeviceName}: HDR desktop duplication keeps getting stuck here; using Windows' 8-bit picture instead until the app restarts");
            }
            else
            {
                gdi = true;
                gdiFps = Math.Clamp(MonitorService.GetRefreshRateHz(DeviceName), 1, 60);
                lock (HdrStuck) GdiOnly.Add(DeviceName);
                log.Warn($"Capture of {DeviceName}: desktop duplication keeps getting stuck here; capturing this monitor through GDI instead until the app restarts (slower, at most {gdiFps} frames a second)");
            }
        }
        ReleaseDxgi();
    }

    /// <summary>One frame through GDI, at most <see cref="gdiFps"/> a second.</summary>
    private void GdiFrame()
    {
        long start = Environment.TickCount64;
        if (start >= nextBounds) { gdiBounds = MonitorBounds(); nextBounds = start + 2000; }
        grab ??= new ScreenGrab();
        if (!grab.Grab(gdiBounds))
        {
            if (lastError != "gdi") log.Warn($"Capture of {DeviceName}: GDI can't grab {gdiBounds}");
            lastError = "gdi";
            Thread.Sleep(500);
            return;
        }
        lastError = null;
        FrameBuffer.Update(grab.Bits, grab.Stride, grab.Width, grab.Height);
        long took = Environment.TickCount64 - start;
        statFrames++;
        statGdiMs += took;
        Stats();
        Thread.Sleep((int)Math.Max(1, 1000 / gdiFps - took));
    }

    /// <summary>This monitor's place on the virtual screen, in physical pixels (empty if it's gone).</summary>
    private PixelRect MonitorBounds() =>
        MonitorService.GetMonitors().FirstOrDefault(m => string.Equals(m.DeviceName, DeviceName, StringComparison.OrdinalIgnoreCase))?.Bounds ?? default;

    /// <summary>Debug logging: frames, timeouts and restarts once a minute, to tell a still screen from a stuck capture.</summary>
    private void Stats()
    {
        long now = Environment.TickCount64;
        if (now < nextStats) return;
        if (nextStats != 0)
            log.Debug(gdi
                ? $"Capture of {DeviceName}: {statFrames} frames through GDI in the last minute, {(statFrames > 0 ? statGdiMs / (double)statFrames : 0):0.#} ms each"
                : $"Capture of {DeviceName}: {statFrames} frames, {statTimeouts} timeouts, {statRebuilds} restarts in the last minute{(hdr != null ? " (HDR)" : "")}");
        statFrames = statTimeouts = statRebuilds = 0;
        statGdiMs = 0;
        nextStats = now + 60000;
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
        hdrDuplication = false;
        if (HdrConversion && !hdrFailed)
        {
            using var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
            if (output6 != null && output6.Description1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020)
            {
                try
                {
                    using var output5 = output.QueryInterface<IDXGIOutput5>();
                    var result = DuplicateOutput1(output5, device, Format.R16G16B16A16_Float);
                    hdrDuplication = true;
                    return result;
                }
                catch (Exception ex) when (!(ex is SharpGenException sg && NotNow(sg)))
                {
                    ErrorLog.Write("Mini Mirror HDR capture (using the 8-bit picture instead)", ex);
                }
            }
        }
        return output.DuplicateOutput(device);
    }

    /// <summary>
    /// IDXGIOutput5::DuplicateOutput1, called directly. Vortice's wrapper takes (device, count, formats), which is easy
    /// to call as (device, flags, formats): passing 0 there sent Windows a format count of 0 with a list, and repeated
    /// set-ups (after every access-lost) eventually corrupted memory and crashed the app (0.6.0–0.6.3, HDR only).
    /// </summary>
    private static unsafe IDXGIOutputDuplication DuplicateOutput1(IDXGIOutput5 output, ID3D11Device device, Format format)
    {
        // vtable slot 26: HRESULT DuplicateOutput1(IUnknown* device, UINT flags, UINT count, const DXGI_FORMAT* formats, IDXGIOutputDuplication** result)
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, Format*, IntPtr*, int>)(*(void***)output.NativePointer)[26];
        IntPtr result;
        int hr = fn(output.NativePointer, device.NativePointer, 0, 1, &format, &result);
        new Result(hr).CheckError();
        return new IDXGIOutputDuplication(result);
    }

    /// <summary>
    /// Windows won't duplicate the screen just now (E_ACCESSDENIED on the secure desktop, NOT_CURRENTLY_AVAILABLE,
    /// SESSION_DISCONNECTED): wait and ask again, on the same device.
    /// </summary>
    private static bool NotNow(SharpGenException ex) =>
        (uint)ex.ResultCode.Code is 0x80070005 or 0x887A0022 or 0x887A0028;

    private void Wait(int ms) => stopping.Wait(ms);

    /// <summary>The duplication and what's made for its pictures; the device stays.</summary>
    private void ReleaseDuplication()
    {
        hdr?.Dispose();
        hdr = null;
        staging?.Dispose();
        staging = null;
        duplication?.Dispose();
        duplication = null;
    }

    private void ReleaseDxgi()
    {
        ReleaseDuplication();
        context?.Dispose();
        context = null;
        device?.Dispose();
        device = null;
    }

    public void Dispose()
    {
        running = false;
        stopping.Set();
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

    /// <summary>How bright the picture is, 0–255 (the brightest channel, averaged over a grid of pixels): 0 is black.</summary>
    public int Brightness()
    {
        lock (gate) return Pixels.Brightness(buffer, width, height, stride);
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

internal static class Pixels
{
    /// <summary>
    /// The brightest channel of a BGRA picture averaged over a 64 × 64 grid of pixels, 0–255; -1 with no picture. For
    /// the log: a mirror that shows black with pictures coming in has 0 here.
    /// </summary>
    public static int Brightness(byte[] buffer, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || buffer.Length < stride * height) return -1;
        long sum = 0;
        int n = 0;
        for (int gy = 0; gy < 64; gy++)
            for (int gx = 0; gx < 64; gx++)
            {
                int i = (int)((long)gy * height / 64) * stride + (int)((long)gx * width / 64) * 4;
                sum += Math.Max(buffer[i], Math.Max(buffer[i + 1], buffer[i + 2]));
                n++;
            }
        return (int)(sum / n);
    }
}
