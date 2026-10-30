using System.IO;
using System.Runtime.InteropServices;
using DaisysApp.Settings;

namespace DaisysApp.Shared.Audio;

/// <summary>
/// The app's own sound devices (since Core 0.51), picked in Settings → General → Audio: where the app plays (the AI's
/// voice and sounds) and what it records from (dictation, a recording's microphone). Not for the tools that drive
/// Voicemeeter, nor ones that pick a device themselves (the shaker, the sound relay): they keep their own.
/// No packages: the list is Windows' Core Audio, and <see cref="Play"/> and <see cref="Record"/> use winmm, on the device
/// picked (found by its endpoint id) or Windows' default. A device picked that isn't connected counts as the default.
/// </summary>
public static class AppAudio
{
    public sealed class Choice
    {
        /// <summary>Endpoint ids ("" = Windows' default), and their names, shown while they're unplugged.</summary>
        public string Output { get; set; } = "";
        public string OutputName { get; set; } = "";
        public string Input { get; set; } = "";
        public string InputName { get; set; } = "";
    }

    public sealed record Device(string Id, string Name);

    private const string FileName = "AppAudio";
    private static Choice? choice;

    public static Choice Settings => choice ??= JsonStore.Load<Choice>(FileName);

    /// <summary>Picks the device ("" = Windows' default) and saves it.</summary>
    public static void Set(bool input, Device? device)
    {
        var s = Settings;
        if (input) { s.Input = device?.Id ?? ""; s.InputName = device?.Name ?? ""; }
        else { s.Output = device?.Id ?? ""; s.OutputName = device?.Name ?? ""; }
        JsonStore.Save(FileName, s);
        Logging.Log.Here.Info($"App audio {(input ? "input" : "output")}: {device?.Name ?? "Windows default"}");
    }

    /// <summary>The endpoint id to play on, or null for Windows' default (none picked, or it isn't connected).</summary>
    public static string? Output => Active(Settings.Output, false);

    /// <summary>The endpoint id to record from, or null for Windows' default.</summary>
    public static string? Input => Active(Settings.Input, true);

    private static string? Active(string id, bool input) =>
        id.Length > 0 && Devices(input).Any(d => d.Id == id) ? id : null;

    /// <summary>The playback (or recording) devices connected, by name.</summary>
    public static List<Device> Devices(bool input)
    {
        var list = new List<Device>();
        try
        {
            // (by its CLSID, not a [ComImport] class: NAudio, loaded by a module, declares one for the same CLSID and the runtime
            // would hand back its type)
            var e = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            try
            {
                Marshal.ThrowExceptionForHR(e.EnumAudioEndpoints(input ? 1 : 0, 1 /* active */, out var all));
                Marshal.ThrowExceptionForHR(all.GetCount(out uint n));
                for (uint i = 0; i < n; i++)
                    if (all.Item(i, out var d) == 0)
                        try
                        {
                            if (d.GetId(out string id) == 0) list.Add(new Device(id, FriendlyName(d) ?? id));
                        }
                        finally { Marshal.ReleaseComObject(d); }
                Marshal.ReleaseComObject(all);
            }
            finally { Marshal.ReleaseComObject(e); }
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Listing the {(input ? "recording" : "playback")} devices: {ex.Message}"); }
        return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string? FriendlyName(IMMDevice d)
    {
        if (d.OpenPropertyStore(0, out var store) != 0) return null;
        try
        {
            var key = new PropertyKey { Fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 };
            if (store.GetValue(ref key, out var v) != 0) return null;
            try { return v.Vt == 31 ? Marshal.PtrToStringUni(v.Pointer) : null; }
            finally { PropVariantClear(ref v); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    /// <summary>The winmm number of the app's output (for SAPI's SpMMAudioOut.DeviceId), or -1 for Windows' default.</summary>
    public static int WaveOut => WaveNumber(Output, false);

    /// <summary>The winmm number of the app's input, or -1 for Windows' default.</summary>
    public static int WaveIn => WaveNumber(Input, true);

    private const int DRV_QUERYFUNCTIONINSTANCEID = 0x0811, DRV_QUERYFUNCTIONINSTANCEIDSIZE = 0x0812;

    /// <summary>A winmm device is matched to an endpoint by the id its driver reports (its name is cut to 31 letters).</summary>
    private static int WaveNumber(string? id, bool input)
    {
        if (id == null) return -1;
        int n = input ? waveInGetNumDevs() : waveOutGetNumDevs();
        for (int i = 0; i < n; i++)
        {
            uint size = 0;
            int r = input ? waveInMessage(i, DRV_QUERYFUNCTIONINSTANCEIDSIZE, ref size, 0) : waveOutMessage(i, DRV_QUERYFUNCTIONINSTANCEIDSIZE, ref size, 0);
            if (r != 0 || size == 0) continue;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                r = input ? waveInMessage(i, DRV_QUERYFUNCTIONINSTANCEID, buf, (IntPtr)size) : waveOutMessage(i, DRV_QUERYFUNCTIONINSTANCEID, buf, (IntPtr)size);
                if (r == 0 && string.Equals(Marshal.PtrToStringUni(buf), id, StringComparison.OrdinalIgnoreCase)) return i;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return -1;
    }

    /// <summary>Plays a .wav file (PCM) on the app's output, in the background.</summary>
    public static void Play(string wavPath)
    {
        int device = WaveOut;
        new Thread(() =>
        {
            try { PlayWav(File.ReadAllBytes(wavPath), device); }
            catch (Exception ex) { Logging.Log.Here.Debug($"Playing {wavPath}: {ex.Message}"); }
        }) { IsBackground = true, Name = "App audio" }.Start();
    }

    private static void PlayWav(byte[] wav, int device)
    {
        // RIFF chunks: "fmt " (the WAVEFORMATEX) and "data"
        byte[]? format = null;
        int dataAt = -1, dataLength = 0;
        for (int at = 12; at + 8 <= wav.Length;)
        {
            string tag = System.Text.Encoding.ASCII.GetString(wav, at, 4);
            int length = BitConverter.ToInt32(wav, at + 4);
            if (tag == "fmt ") format = wav[(at + 8)..Math.Min(wav.Length, at + 8 + length)];
            else if (tag == "data") { dataAt = at + 8; dataLength = Math.Min(length, wav.Length - dataAt); }
            at += 8 + length + (length & 1);
        }
        if (format == null || dataAt < 0) throw new InvalidDataException("not a .wav file");
        if (format.Length < 18) format = [.. format, .. new byte[18 - format.Length]];

        using var done = new AutoResetEvent(false);
        var fmt = GCHandle.Alloc(format, GCHandleType.Pinned);
        IntPtr data = Marshal.AllocHGlobal(dataLength);
        IntPtr header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        try
        {
            Marshal.Copy(wav, dataAt, data, dataLength);
            Check(waveOutOpen(out IntPtr h, device, fmt.AddrOfPinnedObject(), done.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT | (device >= 0 ? WAVE_MAPPED : 0)), "waveOutOpen");
            try
            {
                Marshal.StructureToPtr(new WaveHeader { Data = data, BufferLength = (uint)dataLength }, header, false);
                Check(waveOutPrepareHeader(h, header, Marshal.SizeOf<WaveHeader>()), "waveOutPrepareHeader");
                Check(waveOutWrite(h, header, Marshal.SizeOf<WaveHeader>()), "waveOutWrite");
                var until = DateTime.UtcNow.AddMinutes(2);
                while ((Marshal.PtrToStructure<WaveHeader>(header).Flags & WHDR_DONE) == 0 && DateTime.UtcNow < until) done.WaitOne(200);
                waveOutReset(h);
                waveOutUnprepareHeader(h, header, Marshal.SizeOf<WaveHeader>());
            }
            finally { waveOutClose(h); }
        }
        finally
        {
            fmt.Free();
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(header);
        }
    }

    /// <summary>
    /// Records from the app's input as 16-bit PCM (Windows converts to the rate and channels asked for). Read blocks until
    /// sound comes; once <see cref="Recording.Finish"/> is called, what's left is read and then 0 (the end).
    /// </summary>
    public static Recording Record(int rate = 16000, int channels = 1) => new(WaveIn, rate, channels);

    public sealed class Recording : Stream
    {
        private const int Buffers = 8;
        private readonly IntPtr handle;
        private readonly IntPtr[] headers = new IntPtr[Buffers];
        private readonly AutoResetEvent ready = new(false);
        private readonly Queue<byte[]> chunks = new();
        private byte[] current = [];
        private int currentAt;
        private volatile bool finished;
        private bool closed;
        private readonly Thread worker;

        internal Recording(int device, int rate, int channels)
        {
            short block = (short)(channels * 2);
            byte[] format = new byte[18];
            BitConverter.GetBytes((short)1).CopyTo(format, 0);
            BitConverter.GetBytes((short)channels).CopyTo(format, 2);
            BitConverter.GetBytes(rate).CopyTo(format, 4);
            BitConverter.GetBytes(rate * block).CopyTo(format, 8);
            BitConverter.GetBytes(block).CopyTo(format, 12);
            BitConverter.GetBytes((short)16).CopyTo(format, 14);
            var fmt = GCHandle.Alloc(format, GCHandleType.Pinned);
            try { Check(waveInOpen(out handle, device, fmt.AddrOfPinnedObject(), ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT | (device >= 0 ? WAVE_MAPPED : 0)), "waveInOpen"); }
            finally { fmt.Free(); }
            int bytes = rate * block / 10; // 100 ms each
            for (int i = 0; i < Buffers; i++)
            {
                headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
                Marshal.StructureToPtr(new WaveHeader { Data = Marshal.AllocHGlobal(bytes), BufferLength = (uint)bytes }, headers[i], false);
                waveInPrepareHeader(handle, headers[i], Marshal.SizeOf<WaveHeader>());
                waveInAddBuffer(handle, headers[i], Marshal.SizeOf<WaveHeader>());
            }
            Check(waveInStart(handle), "waveInStart");
            worker = new Thread(Work) { IsBackground = true, Name = "App audio recording" };
            worker.Start();
            Logging.Log.Here.Info($"Recording from {(device < 0 ? "Windows' default" : Settings.InputName)} ({rate} Hz, {channels} channel(s))");
        }

        private void Work()
        {
            while (!finished)
            {
                ready.WaitOne(200);
                Collect(requeue: true);
            }
            waveInReset(handle); // (the buffers come back done)
            Collect(requeue: false);
            lock (chunks) { closed = true; Monitor.PulseAll(chunks); }
            foreach (var h in headers)
            {
                waveInUnprepareHeader(handle, h, Marshal.SizeOf<WaveHeader>());
                Marshal.FreeHGlobal(Marshal.PtrToStructure<WaveHeader>(h).Data);
                Marshal.FreeHGlobal(h);
            }
            waveInClose(handle);
            ready.Dispose();
        }

        private void Collect(bool requeue)
        {
            foreach (var h in headers)
            {
                var w = Marshal.PtrToStructure<WaveHeader>(h);
                if ((w.Flags & WHDR_DONE) == 0) continue;
                if (w.BytesRecorded > 0)
                {
                    byte[] b = new byte[w.BytesRecorded];
                    Marshal.Copy(w.Data, b, 0, b.Length);
                    lock (chunks) { chunks.Enqueue(b); Monitor.PulseAll(chunks); }
                }
                if (!requeue) continue;
                w.Flags &= ~WHDR_DONE;
                w.BytesRecorded = 0;
                Marshal.StructureToPtr(w, h, false);
                waveInAddBuffer(handle, h, Marshal.SizeOf<WaveHeader>());
            }
        }

        /// <summary>Stops recording: what's been recorded is still read, then the end.</summary>
        public void Finish()
        {
            if (finished) return;
            finished = true;
            try { ready.Set(); } catch (ObjectDisposedException) { }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (currentAt >= current.Length)
                lock (chunks)
                {
                    while (chunks.Count == 0 && !closed) Monitor.Wait(chunks);
                    if (chunks.Count == 0) return 0;
                    current = chunks.Dequeue();
                    currentAt = 0;
                }
            int n = Math.Min(count, current.Length - currentAt);
            Array.Copy(current, currentAt, buffer, offset, n);
            currentAt += n;
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            Finish();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        // (System.Speech asks these of a stream it's given, live or not)
        public override long Length => -1;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void Check(int result, string call)
    {
        if (result != 0) throw new InvalidOperationException($"{call} failed ({result})");
    }

    // ---- winmm ----

    private const int CALLBACK_EVENT = 0x50000, WAVE_MAPPED = 0x4;
    private const uint WHDR_DONE = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveOutGetNumDevs();
    [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
    [DllImport("winmm.dll")] private static extern int waveOutMessage(nint device, int msg, ref uint size, nint unused);
    [DllImport("winmm.dll")] private static extern int waveOutMessage(nint device, int msg, IntPtr buffer, IntPtr size);
    [DllImport("winmm.dll")] private static extern int waveInMessage(nint device, int msg, ref uint size, nint unused);
    [DllImport("winmm.dll")] private static extern int waveInMessage(nint device, int msg, IntPtr buffer, IntPtr size);
    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr h, int device, IntPtr format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr h, int device, IntPtr format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr h);

    // ---- Core Audio (the device list) ----

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant v);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Fmtid; public int Pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort Vt, R1, R2, R3; public IntPtr Pointer, Pointer2; }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
