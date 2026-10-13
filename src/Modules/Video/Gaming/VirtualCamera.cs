using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DaisysApp.Logging;
using DaisysApp.Shared.Hardware;
using Microsoft.Win32;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// The virtual webcam, "Daisy's App Camera": the same picture as a recording (the monitor, region or game, with the
/// cameras over it) at 30 frames a second, which Discord, Teams, OBS or a browser can pick as a camera. Windows only takes
/// a camera from a registered DirectShow filter; ours is softcam (third_party/softcam, built with tools/build-softcam.ps1),
/// which the installer registers, 64- and 32-bit. This process sends it frames through its sender API (top-down BGR).
/// The picture is fitted inside 1920×1080, keeping its shape. Its sound, if any, goes through an <see cref="AudioRelay"/>.
/// </summary>
internal sealed class VirtualCamera : IDisposable
{
    /// <summary>Our softcam's class id (tools/build-softcam.ps1).</summary>
    public const string ClassId = "{DFD81BEC-16F1-4CC2-B483-9BB6AADC1DA5}";
    public const string CameraName = "Daisy's App Camera";
    private const int Fps = 30;

    private static readonly Logger log = Log.Here;

    /// <summary>The softcam DLL beside this module: camera\x64 (this process) or camera\x86 (32-bit camera apps).</summary>
    public static string DllPath(bool x86 = false) =>
        Path.Combine(Path.GetDirectoryName(typeof(VirtualCamera).Assembly.Location)!, "camera", x86 ? "x86" : "x64", "softcam.dll");

    /// <summary>Whether Windows knows the camera (the installer registers it).</summary>
    public static bool IsRegistered
    {
        get
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64).OpenSubKey($@"CLSID\{ClassId}\InprocServer32");
                return key?.GetValue(null) is string path && File.Exists(path);
            }
            catch { return false; }
        }
    }

    /// <summary>Registers the camera (Windows asks for an administrator's OK); null if it worked, else why not.</summary>
    public static string? Register()
    {
        try
        {
            string x64 = DllPath(), x86 = DllPath(x86: true);
            if (!File.Exists(x64)) return F("The virtual webcam's file is missing: {0}. Reinstalling Daisy's App puts it back.", x64);
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System), wow = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            string script = $"\"{Path.Combine(sys, "regsvr32.exe")}\" /s \"{x64}\"" + (File.Exists(x86) ? $" & \"{Path.Combine(wow, "regsvr32.exe")}\" /s \"{x86}\"" : "");
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(30000);
            return IsRegistered ? null : T("Windows didn't register the virtual webcam.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return T("Cancelled."); }
        catch (Exception ex) { return ex.Message; }
    }

    // ---------------------------------------------------------------- softcam's sender API

    private static class Softcam
    {
        private static IntPtr lib;

        public static void Load()
        {
            if (lib != IntPtr.Zero) return;
            lib = NativeLibrary.Load(DllPath());
            create = Marshal.GetDelegateForFunctionPointer<CreateFn>(NativeLibrary.GetExport(lib, "scCreateCamera"));
            delete = Marshal.GetDelegateForFunctionPointer<DeleteFn>(NativeLibrary.GetExport(lib, "scDeleteCamera"));
            send = Marshal.GetDelegateForFunctionPointer<SendFn>(NativeLibrary.GetExport(lib, "scSendFrame"));
            connected = Marshal.GetDelegateForFunctionPointer<ConnectedFn>(NativeLibrary.GetExport(lib, "scIsConnected"));
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr CreateFn(int width, int height, float fps);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DeleteFn(IntPtr camera);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SendFn(IntPtr camera, IntPtr image);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public delegate bool ConnectedFn(IntPtr camera);

        public static CreateFn create = null!;
        public static DeleteFn delete = null!;
        public static SendFn send = null!;
        public static ConnectedFn connected = null!;
    }

    // ---------------------------------------------------------------- running it

    private Thread? thread;
    private volatile bool stopping;
    private AudioRelay? audio;

    public bool IsRunning { get; private set; }
    public string Description { get; private set; } = "";
    /// <summary>Whether a program is showing the camera.</summary>
    public bool InUse { get; private set; }

    private volatile bool muted;
    private byte[]? mutedFrame;

    /// <summary>Muted, the camera keeps running but shows <see cref="MutedFrame"/> instead of the picture (it stays muted
    /// across a restart, until it's unmuted).</summary>
    public bool Muted { get => muted; set => muted = value; }

    /// <summary>Raised on a background thread when it stops by itself, with why.</summary>
    public event Action<string>? Failed;

    /// <summary>Starts the camera (and its sound, if <paramref name="audioFrom"/> and <paramref name="audioTo"/> are set);
    /// null, or why it couldn't.</summary>
    public string? Start(RecordingRequest request, string? audioFrom, string? audioTo)
    {
        if (IsRunning) return null;
        if (!IsRegistered) return T("The virtual webcam isn't registered with Windows yet.");
        stopping = false;
        string? error = null;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Run(request, ready, e => error = e)) { IsBackground = true, Name = "Virtual webcam" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        if (!ready.Wait(15000)) { stopping = true; return T("The virtual webcam didn't start in time."); }
        if (error != null) return error;
        if (audioFrom != null && audioTo != null)
        {
            try
            {
                audio = new AudioRelay(audioFrom, audioTo);
                Description += " · " + F("sound {0}", audio.Description);
            }
            catch (Exception ex)
            {
                log.Warn("The virtual webcam's sound couldn't start", ex);
                Stop();
                return F("The sound couldn't start: {0}", ex.Message);
            }
        }
        return null;
    }

    public void Stop()
    {
        stopping = true;
        thread?.Join(5000);
        thread = null;
        audio?.Dispose();
        audio = null;
    }

    public void Dispose() => Stop();

    private void Run(RecordingRequest request, ManualResetEventSlim ready, Action<string> fail)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        IDXGIOutput1? output = null;
        IDXGIOutputDuplication? duplication = null;
        FrameConverter? converter = null;
        CameraOverlay? cameras = null;
        ID3D11Texture2D? target = null, staging = null;
        ID3D11RenderTargetView? targetView = null;
        IntPtr camera = IntPtr.Zero;
        bool started = false;
        try
        {
            var (monitor, recrop) = Recorder.Where(request);
            var crop = recrop();
            if (crop.Width < 16 || crop.Height < 16) throw new RecorderException(T("The area to show is too small (or off the screen)."));
            (device, context, output, duplication) = Recorder.OpenCapture(monitor.DeviceName, null);
            converter = new FrameConverter(device);
            if (request.Cameras.Count > 0) cameras = new CameraOverlay(device, request.Cameras, request.CameraHidden);

            var (w, h) = Size(crop);
            try { mutedFrame = MutedFrame.Render(w, h); }
            catch (Exception ex) { log.Warn("The muted picture couldn't be drawn; muted shows black", ex); mutedFrame = new byte[w * h * 3]; }
            Softcam.Load();
            camera = Softcam.create(w, h, 0); // we pace the frames ourselves
            if (camera == IntPtr.Zero) throw new RecorderException(T("The virtual webcam is already in use by another copy of Daisy's App."));
            target = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget,
            });
            targetView = device.CreateRenderTargetView(target);
            staging = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
            });
            Description = F("{0}×{1}, {2} fps", w, h, Fps) + (request.Cameras.Count > 0 ? " · " + P(request.Cameras.Count, "{0} camera", "{0} cameras") : "");
            log.Info($"Virtual webcam on: {w}x{h} {Fps} fps from {monitor.DeviceName} {crop}, cameras {request.Cameras.Count}");
            IsRunning = true;
            started = true;
            ready.Set();
            Loop(request, monitor, recrop, crop, device, context, output, ref duplication, converter, cameras, targetView, staging, camera, w, h);
        }
        catch (Exception ex)
        {
            string message = ex is RecorderException ? ex.Message : F("The virtual webcam failed: {0}", ex.Message);
            if (ex is not RecorderException) ErrorLog.Write("Virtual webcam", ex);
            if (!started) fail(message);
            else if (!stopping) Failed?.Invoke(message);
        }
        finally
        {
            if (!started) ready.Set();
            if (camera != IntPtr.Zero) Softcam.delete(camera);
            staging?.Dispose();
            targetView?.Dispose();
            target?.Dispose();
            cameras?.Dispose();
            converter?.Dispose();
            duplication?.Dispose();
            output?.Dispose();
            context?.Dispose();
            device?.Dispose();
            IsRunning = false;
            InUse = false;
            if (started) log.Info("Virtual webcam off");
        }
    }

    /// <summary>The camera's size: the picture fitted inside 1920×1080, keeping its shape, in multiples of 4 (softcam's rule).</summary>
    public static (int W, int H) Size(PixelRect crop)
    {
        double scale = Math.Min(1, Math.Min(1920.0 / crop.Width, 1080.0 / crop.Height));
        int w = Math.Max(64, (int)Math.Round(crop.Width * scale)) & ~3;
        int h = Math.Max(64, (int)Math.Round(crop.Height * scale)) & ~3;
        return (w, h);
    }

    private unsafe void Loop(RecordingRequest request, MonitorInfo monitor, Func<PixelRect> recrop, PixelRect crop,
        ID3D11Device device, ID3D11DeviceContext context, IDXGIOutput1 output, ref IDXGIOutputDuplication? duplication,
        FrameConverter converter, CameraOverlay? cameras, ID3D11RenderTargetView targetView, ID3D11Texture2D staging, IntPtr camera, int w, int h)
    {
        var bgr = new byte[w * h * 3];
        long freq = Stopwatch.Frequency, t0 = Stopwatch.GetTimestamp();
        long frame = 0, nextFollow = 0, nextWhite = 0;
        double interval = 1.0 / Fps;
        float white = SdrWhiteLevel.ScaleFor(monitor.DeviceName);
        bool cursorVisible = false;
        int cursorX = 0, cursorY = 0;
        byte[] shapeBuffer = [];
        var cursorShape = new CursorShape();
        var target = targetView.Resource;
        try
        {
            while (!stopping)
            {
                long nowTicks = Stopwatch.GetTimestamp();
                double now = (nowTicks - t0) / (double)freq, due = frame * interval;
                if (now < due - 0.001)
                {
                    if (duplication == null)
                    {
                        Thread.Sleep(Math.Max(1, (int)((due - now) * 1000)));
                        try { duplication = Recorder.OpenCapture(monitor.DeviceName, device).Duplication; } catch { }
                        continue;
                    }
                    var result = duplication.AcquireNextFrame((uint)Math.Max(1, (due - now) * 1000), out var info, out var resource);
                    if (result.Failure)
                    {
                        resource?.Dispose();
                        if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) continue;
                        duplication.Dispose(); // access lost (a mode change, a UAC prompt): keep showing the last frame
                        duplication = null;
                        continue;
                    }
                    try
                    {
                        if (info.LastPresentTime != 0)
                            using (var tex = resource!.QueryInterface<ID3D11Texture2D>()) converter.SetSource(context, tex);
                        if (info.LastMouseUpdateTime != 0)
                        {
                            cursorVisible = info.PointerPosition.Visible;
                            cursorX = info.PointerPosition.Position.X;
                            cursorY = info.PointerPosition.Position.Y;
                        }
                        if (request.Cursor && info.PointerShapeBufferSize > 0)
                        {
                            if (shapeBuffer.Length < info.PointerShapeBufferSize) shapeBuffer = new byte[info.PointerShapeBufferSize];
                            fixed (byte* p = shapeBuffer)
                                if (duplication.GetFramePointerShape((uint)shapeBuffer.Length, (IntPtr)p, out _, out var shape).Success)
                                {
                                    var (cbgra, cw, ch) = cursorShape.Convert(shapeBuffer, shape);
                                    converter.SetCursor(cbgra, cw, ch);
                                }
                        }
                    }
                    finally
                    {
                        resource?.Dispose();
                        duplication.ReleaseFrame();
                    }
                    continue;
                }
                if (now - due > interval) frame = (long)(now / interval); // behind: skip ahead

                if (request.Source == CaptureSource.App && nowTicks >= nextFollow)
                {
                    nextFollow = nowTicks + freq / 4;
                    if (!Native.IsWindowAlive(request.Window)) throw new RecorderException(T("The program shown on the virtual webcam closed."));
                    if (!Native.IsMinimized(request.Window))
                    {
                        var c = recrop();
                        if (c.Width >= 16 && c.Height >= 16) crop = c;
                    }
                }
                if (nowTicks >= nextWhite)
                {
                    nextWhite = nowTicks + 2 * freq;
                    white = SdrWhiteLevel.ScaleFor(monitor.DeviceName);
                }

                if (muted && mutedFrame is { } still && still.Length == w * h * 3)
                {
                    fixed (byte* p = still) Softcam.send(camera, (IntPtr)p);
                    InUse = Softcam.connected(camera);
                }
                else if (converter.HasSource)
                {
                    var area = crop.Intersect(new PixelRect(0, 0, converter.SourceWidth, converter.SourceHeight));
                    if (!area.IsEmpty)
                    {
                        if (cameras != null)
                        {
                            cameras.Update(context);
                            converter.SetCameras(cameras.Layers);
                        }
                        converter.ConvertBgra(context, targetView, w, h, converter.SourceIsHdr ? FrameConverter.Mode.HdrToSdr : FrameConverter.Mode.Sdr,
                            white, area, request.Cursor && cursorVisible, cursorX, cursorY);
                        context.CopyResource(staging, target);
                        var map = context.Map(staging, 0, MapMode.Read);
                        try
                        {
                            fixed (byte* dst = bgr)
                                for (int y = 0; y < h; y++)
                                {
                                    byte* s = (byte*)map.DataPointer + (long)y * map.RowPitch, d = dst + (long)y * w * 3;
                                    for (int x = 0; x < w; x++, s += 4, d += 3) { d[0] = s[0]; d[1] = s[1]; d[2] = s[2]; }
                                }
                        }
                        finally { context.Unmap(staging, 0); }
                        fixed (byte* p = bgr) Softcam.send(camera, (IntPtr)p);
                        InUse = Softcam.connected(camera);
                    }
                }
                frame++;
            }
        }
        finally { target.Dispose(); }
    }
}
