using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// (since SimEffects Analysis 0.3.3) What one program plays (and the programs it started), not the whole PC: Windows' process loopback
/// (Windows 10 2004 and later), so a recording of the game leaves out voice chat, videos and other programs. 16-bit
/// stereo at 48 kHz, as <see cref="AudioTrack"/> takes it.
/// </summary>
internal sealed class ProcessAudio : IWaveIn
{
    private readonly AudioClient client;
    private readonly AutoResetEvent ready = new(false);
    private Thread? thread;
    private volatile bool stop;

    public WaveFormat WaveFormat { get; set; } = new(48000, 16, 2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public ProcessAudio(uint processId)
    {
        client = new AudioClient(Activate(processId));
        client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm,
            2_000_000, 0, WaveFormat, Guid.Empty);
        client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
    }

    public void StartRecording()
    {
        stop = false;
        client.Start();
        thread = new Thread(Run) { IsBackground = true, Name = "Game sound", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    private void Run()
    {
        Exception? error = null;
        try
        {
            var capture = client.AudioCaptureClient;
            int frameBytes = WaveFormat.BlockAlign;
            var buffer = new byte[WaveFormat.AverageBytesPerSecond];
            while (!stop)
            {
                ready.WaitOne(100);
                for (int size = capture.GetNextPacketSize(); size > 0 && !stop; size = capture.GetNextPacketSize())
                {
                    var data = capture.GetBuffer(out int frames, out var flags);
                    int bytes = frames * frameBytes;
                    if (buffer.Length < bytes) buffer = new byte[bytes];
                    if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, bytes);
                    else Marshal.Copy(data, buffer, 0, bytes);
                    capture.ReleaseBuffer(frames);
                    if (bytes > 0) DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                }
            }
        }
        catch (Exception e) { error = e; }
        try { client.Stop(); } catch { }
        RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
    }

    public void StopRecording()
    {
        stop = true;
        ready.Set();
        if (thread != null && thread != Thread.CurrentThread) thread.Join(1000);
    }

    public void Dispose()
    {
        StopRecording();
        client.Dispose();
        ready.Dispose();
    }

    // ---------------------------------------------------------------- activation

    private const string LoopbackDevice = @"VAD\Process_Loopback";
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParams
    {
        public int Type;          // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1
        public uint ProcessId;
        public int Mode;          // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlobVariant
    {
        public ushort Vt;         // VT_BLOB = 65
        public ushort R1, R2, R3;
        public int Size;
        public IntPtr Data;
    }

    private static IAudioClient Activate(uint processId)
    {
        var parameters = new ActivationParams { Type = 1, ProcessId = processId, Mode = 0 };
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
        IntPtr v = Marshal.AllocHGlobal(Marshal.SizeOf<BlobVariant>());
        try
        {
            Marshal.StructureToPtr(parameters, p, false);
            Marshal.StructureToPtr(new BlobVariant { Vt = 65, Size = Marshal.SizeOf<ActivationParams>(), Data = p }, v, false);
            var done = new Completion();
            ActivateAudioInterfaceAsync(LoopbackDevice, AudioClientId, v, done, out _);
            if (!done.Finished.Wait(5000)) throw new TimeoutException("Windows didn't start the game's sound");
            return done.Result ?? throw done.Error!;
        }
        finally
        {
            Marshal.FreeHGlobal(v);
            Marshal.FreeHGlobal(p);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr activationParams, IActivateCompletion completionHandler, out IActivateOperation operation);

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateCompletion
    {
        void ActivateCompleted(IActivateOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateOperation
    {
        void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object? activated);
    }

    /// <summary>(Windows calls it on its own thread: it has to say it's agile)</summary>
    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }

    private sealed class Completion : IActivateCompletion, IAgileObject
    {
        public readonly ManualResetEventSlim Finished = new(false);
        public IAudioClient? Result;
        public Exception? Error;

        public void ActivateCompleted(IActivateOperation operation)
        {
            try
            {
                operation.GetActivateResult(out int hr, out object? activated);
                if (hr < 0) Error = Marshal.GetExceptionForHR(hr) ?? new COMException("", hr);
                else Result = (IAudioClient)activated!;
            }
            catch (Exception e) { Error = e; }
            Finished.Set();
        }
    }
}
