using System.Diagnostics;
using System.Runtime.InteropServices;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Gaming;

/// <summary>
/// Every program's frames, the way PresentMon, FrameView and CapFrameX count them: Windows' own event tracing (ETW)
/// reports each time a program hands a finished frame to Windows (DXGI for DirectX 10–12, D3D9, and the graphics
/// kernel for Vulkan and OpenGL). Nothing is injected into the game and nothing polls: Windows delivers the events in
/// batches to one background thread, at a cost of a few microseconds a frame.
/// Needs administrator rights or membership of the Performance Log Users group.
/// </summary>
internal sealed unsafe class FrameMonitor : IDisposable
{
    public enum State { Off, Running, NoPermission, Failed }

    private const string SessionName = "DaisysApp-Gaming";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const ushort DxgiPresentStart = 42, DxgiPresentMpoStart = 55, D3d9PresentStart = 1, KrnlPresentHistoryDetailed = 215;

    private static FrameMonitor? current;
    private readonly object gate = new();
    private readonly Dictionary<uint, ProcessFrames> processes = new();
    private ulong session;
    private ulong trace = InvalidHandle;
    private Thread? thread;
    private const ulong InvalidHandle = ulong.MaxValue;

    public State Status { get; private set; } = State.Off;
    public string? Error { get; private set; }

    /// <summary>Starts the trace; false (with <see cref="Status"/> saying why) if Windows refused.</summary>
    public bool Start()
    {
        if (Status == State.Running) return true;
        try
        {
            StopSession(); // left over from a crash
            int err = StartSession();
            if (err != 0)
            {
                Status = err == 5 ? State.NoPermission : State.Failed;
                Error = new System.ComponentModel.Win32Exception(err).Message;
                return false;
            }
            Enable(Dxgi, 0, new[] { DxgiPresentStart, DxgiPresentMpoStart });
            Enable(D3d9, 0, new[] { D3d9PresentStart });
            // the graphics kernel's present history: catches Vulkan and OpenGL, which don't go through DXGI
            Enable(DxgKrnl, 0x1 | 0x8000000, new[] { KrnlPresentHistoryDetailed });

            current = this;
            var log = new EVENT_TRACE_LOGFILEW();
            fixed (char* name = SessionName)
            {
                log.LoggerName = name;
                log.ProcessTraceMode = 0x100 | 0x10000000; // real time, EVENT_RECORD callbacks
                log.EventRecordCallback = &OnEvent;
                trace = OpenTraceW(&log);
            }
            if (trace == InvalidHandle)
            {
                Error = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                StopSession();
                Status = State.Failed;
                return false;
            }
            thread = new Thread(() =>
            {
                ulong handle = trace;
                ProcessTrace(&handle, 1, null, null); // returns when the session stops
            }) { IsBackground = true, Name = "Gaming-Frames" };
            thread.Start();
            Status = State.Running;
            Error = null;
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Gaming frame counter", ex);
            Status = State.Failed;
            Error = ex.Message;
            return false;
        }
    }

    public void Stop()
    {
        if (Status != State.Running) { Status = State.Off; return; }
        StopSession();
        if (trace != InvalidHandle) CloseTrace(trace);
        trace = InvalidHandle;
        thread?.Join(2000);
        thread = null;
        lock (gate) processes.Clear();
        Status = State.Off;
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- reading

    /// <summary>Whether the process showed a frame in the last couple of seconds.</summary>
    public bool IsPresenting(uint pid)
    {
        lock (gate)
            return processes.TryGetValue(pid, out var p) && p.Latest(Stopwatch.GetTimestamp()) is not null;
    }

    /// <summary>The process's frame times (QPC ticks) after <paramref name="since"/>, oldest first, and its newest.</summary>
    public int CopyFrames(uint pid, long since, List<long> into)
    {
        into.Clear();
        lock (gate)
        {
            if (!processes.TryGetValue(pid, out var p)) return 0;
            var ring = p.Preferred(Stopwatch.GetTimestamp());
            ring?.CopySince(since, into);
            return into.Count;
        }
    }

    /// <summary>Forgets processes that haven't shown a frame for a minute.</summary>
    public void Prune()
    {
        long cutoff = Stopwatch.GetTimestamp() - 60 * Stopwatch.Frequency;
        lock (gate)
            foreach (var pid in processes.Where(kv => kv.Value.LastSeen < cutoff).Select(kv => kv.Key).ToList())
                processes.Remove(pid);
    }

    // ---------------------------------------------------------------- events

    [UnmanagedCallersOnly]
    private static void OnEvent(byte* record)
    {
        var self = current;
        if (self == null) return;
        uint pid = *(uint*)(record + 12);
        long time = *(long*)(record + 16);
        var provider = *(Guid*)(record + 24);
        ushort id = *(ushort*)(record + 40);
        bool kernel;
        if (provider == Dxgi)
        {
            // DXGI_PRESENT_TEST asks whether a frame could be shown without showing one
            ushort flags = *(ushort*)(record + 4);
            int pointer = (flags & 0x20) != 0 ? 4 : 8; // EVENT_HEADER_FLAG_32_BIT_HEADER
            ushort length = *(ushort*)(record + 86);
            byte* data = *(byte**)(record + 96);
            if (length >= pointer + 4 && (*(uint*)(data + pointer) & 1) != 0) return;
            kernel = false;
        }
        else if (provider == D3d9) kernel = false;
        else if (provider == DxgKrnl) kernel = true;
        else return;

        lock (self.gate)
        {
            if (!self.processes.TryGetValue(pid, out var p)) self.processes[pid] = p = new ProcessFrames();
            (kernel ? p.Kernel : p.Api).Add(time);
            p.LastSeen = time;
        }
    }

    private sealed class ProcessFrames
    {
        public readonly FrameRing Api = new(), Kernel = new();
        public long LastSeen;

        /// <summary>DirectX's own count when the program uses it (one per frame), else the graphics kernel's.</summary>
        public FrameRing? Preferred(long now)
        {
            long recent = now - 3 * Stopwatch.Frequency;
            if (Api.Newest > recent) return Api;
            if (Kernel.Newest > recent) return Kernel;
            return null;
        }

        public long? Latest(long now) => Preferred(now)?.Newest;
    }

    /// <summary>The last 16384 frame times of one process (a minute at 270 fps).</summary>
    private sealed class FrameRing
    {
        private readonly long[] times = new long[16384];
        private int next, count;
        public long Newest;

        public void Add(long t)
        {
            times[next] = t;
            next = (next + 1) & (times.Length - 1);
            if (count < times.Length) count++;
            if (t > Newest) Newest = t;
        }

        public void CopySince(long since, List<long> into)
        {
            // walk back from the newest to the first one after `since`, then copy forwards
            int n = 0;
            while (n < count && times[(next - 1 - n) & (times.Length - 1)] > since) n++;
            for (int i = n - 1; i >= 0; i--) into.Add(times[(next - 1 - i) & (times.Length - 1)]);
        }
    }

    // ---------------------------------------------------------------- session

    private int StartSession()
    {
        int size = sizeof(EVENT_TRACE_PROPERTIES) + 512;
        var p = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)size);
        try
        {
            p->Wnode.BufferSize = (uint)size;
            p->Wnode.Flags = 0x00020000; // WNODE_FLAG_TRACED_GUID
            p->Wnode.ClientContext = 1; // timestamps from QueryPerformanceCounter, like Stopwatch
            p->BufferSize = 8; // KB: small buffers reach us sooner
            p->MinimumBuffers = 4;
            p->MaximumBuffers = 32;
            p->LogFileMode = 0x100; // EVENT_TRACE_REAL_TIME_MODE
            p->FlushTimer = 1;
            p->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
            ulong handle;
            int err = StartTraceW(&handle, SessionName, p);
            if (err == 0) session = handle;
            return err;
        }
        finally { NativeMemory.Free(p); }
    }

    private void Enable(Guid provider, ulong keywords, ushort[] ids)
    {
        int size = 4 + 2 * ids.Length;
        byte* filter = stackalloc byte[size];
        filter[0] = 1; // these ids only
        filter[1] = 0;
        *(ushort*)(filter + 2) = (ushort)ids.Length;
        for (int i = 0; i < ids.Length; i++) ((ushort*)(filter + 4))[i] = ids[i];
        var desc = new EVENT_FILTER_DESCRIPTOR { Ptr = (ulong)filter, Size = (uint)size, Type = 0x80000200 }; // EVENT_FILTER_TYPE_EVENT_ID
        var parameters = new ENABLE_TRACE_PARAMETERS { Version = 2, EnableFilterDesc = &desc, FilterDescCount = 1 };
        int err = EnableTraceEx2(session, &provider, 1, 4, keywords, 0, 0, &parameters);
        if (err != 0) ErrorLog.Write($"Gaming frame counter: enabling {provider}", new System.ComponentModel.Win32Exception(err));
    }

    private static void StopSession()
    {
        int size = sizeof(EVENT_TRACE_PROPERTIES) + 512;
        var p = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)size);
        p->Wnode.BufferSize = (uint)size;
        p->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
        ControlTraceW(0, SessionName, p, 1); // EVENT_TRACE_CONTROL_STOP
        NativeMemory.Free(p);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNODE_HEADER
    {
        public uint BufferSize, ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_TRACE_PROPERTIES
    {
        public WNODE_HEADER Wnode;
        public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset, LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EVENT_FILTER_DESCRIPTOR { public ulong Ptr; public uint Size, Type; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ENABLE_TRACE_PARAMETERS
    {
        public uint Version, EnableProperty, ControlFlags;
        public Guid SourceId;
        public EVENT_FILTER_DESCRIPTOR* EnableFilterDesc;
        public uint FilterDescCount;
    }

    /// <summary>EVENT_TRACE_LOGFILEW (64-bit layout); only the fields used are declared.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 448)]
    private struct EVENT_TRACE_LOGFILEW
    {
        [FieldOffset(8)] public char* LoggerName;
        [FieldOffset(28)] public uint ProcessTraceMode;
        [FieldOffset(424)] public delegate* unmanaged<byte*, void> EventRecordCallback;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int StartTraceW(ulong* handle, string name, EVENT_TRACE_PROPERTIES* properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int ControlTraceW(ulong handle, string name, EVENT_TRACE_PROPERTIES* properties, uint code);
    [DllImport("advapi32.dll")] private static extern int EnableTraceEx2(ulong handle, Guid* provider, uint control, byte level, ulong anyKeyword, ulong allKeyword, uint timeout, ENABLE_TRACE_PARAMETERS* parameters);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern ulong OpenTraceW(EVENT_TRACE_LOGFILEW* logfile);
    [DllImport("advapi32.dll")] private static extern int ProcessTrace(ulong* handles, uint count, void* start, void* end);
    [DllImport("advapi32.dll")] private static extern int CloseTrace(ulong handle);
}
