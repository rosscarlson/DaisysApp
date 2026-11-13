using DaisysApp.Logging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// (since 4.9.1) A monitor's desktop duplication, shared by the recorder and the virtual webcam: Windows gives a program
/// only one per monitor, so recording the monitor the webcam showed failed (0x80070057; SimEffects → Analysis' race
/// videos never started). One thread per monitor takes its pictures into a shared texture; each user copies the newest
/// onto its own device (on the same graphics card). Lost access (a mode change, a UAC prompt) is taken back by itself.
/// </summary>
internal sealed class ScreenCapture : IDisposable
{
    private readonly Hub hub;
    private long seenFrame, seenPointer, seenShape;
    private ID3D11Texture2D? texture;
    private IDXGIKeyedMutex? mutex;
    private long textureVersion;
    private bool disposed;

    private ScreenCapture(Hub hub) => this.hub = hub;

    /// <summary>Whether the pictures are Windows' 16-bit HDR picture, not its 8-bit one.</summary>
    public bool Hdr => hub.Hdr;

    /// <summary>Whether the monitor is being captured here (Mini Mirror can't have its own capture of it then).</summary>
    public static bool Running(string deviceName) => Hub.Running(deviceName);

    /// <summary>Starts capturing the monitor, or joins its capture (throws when it can't be had).</summary>
    public static ScreenCapture Open(string deviceName) => new(Hub.Join(deviceName));

    /// <summary>
    /// Waits up to <paramref name="wait"/> ms for something new: a new picture goes into <paramref name="converter"/>, a
    /// moved pointer into the refs, a new pointer shape (when <paramref name="shapes"/>) into the converter too.
    /// </summary>
    public void Next(uint wait, ID3D11Device device, ID3D11DeviceContext context, FrameConverter converter, bool shapes, CursorShape cursorShape,
        ref bool visible, ref int x, ref int y)
    {
        bool picture;
        IntPtr handle;
        long version;
        byte[]? shape = null;
        OutduplPointerShapeInfo shapeInfo = default;
        lock (hub.Gate)
        {
            if (hub.Frame == seenFrame && hub.PointerVersion == seenPointer && hub.ShapeVersion == seenShape)
                Monitor.Wait(hub.Gate, (int)wait);
            picture = hub.Frame != seenFrame && hub.Handle != IntPtr.Zero;
            seenFrame = hub.Frame;
            handle = hub.Handle;
            version = hub.TextureVersion;
            if (hub.PointerVersion != seenPointer)
            {
                seenPointer = hub.PointerVersion;
                visible = hub.PointerVisible;
                x = hub.PointerX;
                y = hub.PointerY;
            }
            if (hub.ShapeVersion != seenShape)
            {
                seenShape = hub.ShapeVersion;
                if (shapes && hub.Shape != null) { shape = hub.Shape; shapeInfo = hub.ShapeInfo; }
            }
        }
        if (shape != null)
        {
            var (bgra, w, h) = cursorShape.Convert(shape, shapeInfo);
            converter.SetCursor(bgra, w, h);
        }
        if (!picture) return;
        if (texture == null || version != textureVersion)
        {
            mutex?.Dispose();
            texture?.Dispose();
            mutex = null;
            texture = null;
            try
            {
                texture = device.OpenSharedResource<ID3D11Texture2D>(handle);
                mutex = texture.QueryInterface<IDXGIKeyedMutex>();
                textureVersion = version;
            }
            catch (Exception ex)
            {
                texture?.Dispose();
                texture = null;
                Log.Here.Warn($"Screen capture: the shared picture couldn't be opened: {ex.Message}");
                return;
            }
        }
        if (!Acquire(mutex!)) return;
        try { converter.SetSource(context, texture); }
        finally { mutex.ReleaseSync(0); }
    }

    /// <summary>IDXGIKeyedMutex::AcquireSync called directly: Vortice's throws away its result, and a time-out
    /// (WAIT_TIMEOUT, not an error) would look like the texture was ours.</summary>
    private static unsafe bool Acquire(IDXGIKeyedMutex mutex)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)(*(void***)mutex.NativePointer)[8];
        return fn(mutex.NativePointer, 0, 100) == 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        mutex?.Dispose();
        texture?.Dispose();
        hub.Leave();
    }

    /// <summary>One monitor's duplication and the thread taking its pictures.</summary>
    private sealed class Hub
    {
        private static readonly Dictionary<string, Hub> hubs = new(StringComparer.OrdinalIgnoreCase);

        public readonly object Gate = new();
        private readonly string deviceName;
        private readonly ID3D11Device device;
        private readonly ID3D11DeviceContext context;
        private IDXGIOutputDuplication? duplication;
        private Thread? thread;
        private volatile bool stopping;
        private int users;

        public bool Hdr;
        private ID3D11Texture2D? shared;
        private IDXGIKeyedMutex? sharedMutex;
        public IntPtr Handle;
        public long TextureVersion, Frame, PointerVersion, ShapeVersion;
        public bool PointerVisible;
        public int PointerX, PointerY;
        public byte[]? Shape;
        public OutduplPointerShapeInfo ShapeInfo;

        private Hub(string deviceName, ID3D11Device device, ID3D11DeviceContext context)
        {
            this.deviceName = deviceName;
            this.device = device;
            this.context = context;
        }

        public static bool Running(string deviceName)
        {
            lock (hubs) return hubs.ContainsKey(deviceName);
        }

        public static Hub Join(string deviceName)
        {
            lock (hubs)
            {
                if (!hubs.TryGetValue(deviceName, out var hub))
                {
                    var (device, context) = Recorder.CreateDevice(deviceName);
                    hub = new Hub(deviceName, device, context);
                    try { hub.duplication = Recorder.Duplicate(deviceName, device, out hub.Hdr); }
                    catch
                    {
                        context.Dispose();
                        device.Dispose();
                        throw;
                    }
                    hub.thread = new Thread(hub.Run) { IsBackground = true, Name = $"Screen capture {deviceName}" };
                    hub.thread.Start();
                    hubs[deviceName] = hub;
                }
                hub.users++;
                return hub;
            }
        }

        public void Leave()
        {
            lock (hubs)
            {
                if (--users > 0) return;
                hubs.Remove(deviceName);
            }
            stopping = true;
            lock (Gate) Monitor.PulseAll(Gate);
            thread?.Join(2000);
            duplication?.Dispose();
            sharedMutex?.Dispose();
            shared?.Dispose();
            context.Dispose();
            device.Dispose();
        }

        private void Run()
        {
            byte[] shapeBuffer = [];
            while (!stopping)
            {
                if (duplication == null)
                {
                    Thread.Sleep(50);
                    try { duplication = Recorder.Duplicate(deviceName, device, out bool hdr); lock (Gate) Hdr = hdr; } catch { }
                    continue;
                }
                var result = duplication.AcquireNextFrame(50, out var info, out var resource);
                if (result.Failure)
                {
                    resource?.Dispose();
                    if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) continue;
                    // access lost (resolution change, a UAC prompt, a game switching modes): the last picture stays
                    duplication.Dispose();
                    duplication = null;
                    continue;
                }
                try
                {
                    bool picture = info.LastPresentTime != 0 && Publish(resource!);
                    byte[]? shape = null;
                    OutduplPointerShapeInfo shapeInfo = default;
                    if (info.PointerShapeBufferSize > 0)
                    {
                        if (shapeBuffer.Length < info.PointerShapeBufferSize) shapeBuffer = new byte[info.PointerShapeBufferSize];
                        unsafe
                        {
                            fixed (byte* p = shapeBuffer)
                                if (duplication.GetFramePointerShape((uint)shapeBuffer.Length, (IntPtr)p, out _, out shapeInfo).Success)
                                    shape = shapeBuffer[..(int)info.PointerShapeBufferSize];
                        }
                    }
                    lock (Gate)
                    {
                        if (picture) Frame++;
                        if (info.LastMouseUpdateTime != 0)
                        {
                            PointerVisible = info.PointerPosition.Visible;
                            PointerX = info.PointerPosition.Position.X;
                            PointerY = info.PointerPosition.Position.Y;
                            PointerVersion++;
                        }
                        if (shape != null) { Shape = shape; ShapeInfo = shapeInfo; ShapeVersion++; }
                        Monitor.PulseAll(Gate);
                    }
                }
                catch (Exception ex) { Log.Here.Warn($"Screen capture of {deviceName}: {ex.Message}"); }
                finally
                {
                    resource?.Dispose();
                    duplication.ReleaseFrame();
                }
            }
        }

        /// <summary>The new picture copied into the shared texture (a new one when its size or format changed).</summary>
        private bool Publish(IDXGIResource resource)
        {
            using var frame = resource.QueryInterface<ID3D11Texture2D>();
            var d = frame.Description;
            if (shared == null || shared.Description.Width != d.Width || shared.Description.Height != d.Height || shared.Description.Format != d.Format)
            {
                sharedMutex?.Dispose();
                shared?.Dispose();
                shared = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = d.Width, Height = d.Height, MipLevels = 1, ArraySize = 1, Format = d.Format,
                    SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget, MiscFlags = ResourceOptionFlags.SharedKeyedMutex,
                });
                sharedMutex = shared.QueryInterface<IDXGIKeyedMutex>();
                using var dxgi = shared.QueryInterface<IDXGIResource>();
                lock (Gate)
                {
                    Handle = dxgi.SharedHandle;
                    TextureVersion++;
                }
            }
            if (!Acquire(sharedMutex!)) return false;
            try { context.CopyResource(shared, frame); }
            finally { sharedMutex.ReleaseSync(0); }
            return true;
        }
    }
}
