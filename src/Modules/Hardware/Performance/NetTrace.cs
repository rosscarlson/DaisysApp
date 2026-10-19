using System.Runtime.InteropServices;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>
/// Each program's network traffic, the way Task Manager counts it: Windows' own event tracing (ETW) reports every TCP
/// and UDP send and receive with the process and its size (the Microsoft-Windows-Kernel-Network provider). Traffic to
/// the PC itself (127.x, ::1) is left out. Events reach one background thread in batches; <see cref="Take"/> hands
/// out what each process sent and received since the last call.
/// Needs administrator rights or membership of the Performance Log Users group (<c>TracePermission</c>).
/// </summary>
public sealed unsafe class NetTrace : IDisposable
{
    public enum State { Off, Running, NoPermission, Failed }

    private static readonly string SessionName = $"{AppPaths.ShortName}-Network";
    private static readonly Guid KernelNetwork = new("7DD42A49-5329-4832-8DFD-43D979153A88");
    // TCP send / receive (IPv4, IPv6), UDP send / receive (IPv4, IPv6)
    private static readonly ushort[] Events = { 10, 11, 26, 27, 42, 43, 58, 59 };

    private static NetTrace? current;
    private readonly object gate = new();
    private Dictionary<int, long> bytes = new();
    private long total;
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
            int stopped = StopSession(); // left over from a crash
            int err = StartSession();
            if (err != 0)
            {
                // 183: one is left over, and without the permission it can't be stopped either
                Status = err == 5 || err == 183 && stopped == 5 ? State.NoPermission : State.Failed;
                Error = new System.ComponentModel.Win32Exception(err).Message;
                return false;
            }
            err = Enable();
            if (err != 0)
            {
                StopSession();
                Status = err == 5 ? State.NoPermission : State.Failed;
                Error = new System.ComponentModel.Win32Exception(err).Message;
                return false;
            }

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
            }) { IsBackground = true, Name = "Performance-Network" };
            thread.Start();
            Status = State.Running;
            Error = null;
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Performance network trace", ex);
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
        Status = State.Off;
    }

    public void Dispose() => Stop();

    /// <summary>The bytes each process sent and received since the last call, by process id, and all of them together.</summary>
    public (Dictionary<int, long> ByPid, long Total) Take()
    {
        lock (gate)
        {
            var taken = (bytes, total);
            bytes = new();
            total = 0;
            return taken;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(byte* record)
    {
        var self = current;
        if (self == null || *(Guid*)(record + 24) != KernelNetwork) return;
        ushort id = *(ushort*)(record + 40);
        ushort length = *(ushort*)(record + 86);
        byte* data = *(byte**)(record + 96);
        // every one starts PID, size, then the far and near addresses (4 bytes each for IPv4, 16 for IPv6)
        bool v6 = id is 26 or 27 or 58 or 59;
        if (length < (v6 ? 24 : 12)) return;
        int pid = *(int*)data;
        uint size = *(uint*)(data + 4);
        if (v6 ? IsLoopback6(data + 8) : data[8] == 127) return;
        lock (self.gate)
        {
            self.bytes[pid] = self.bytes.GetValueOrDefault(pid) + size;
            self.total += size;
        }
    }

    private static bool IsLoopback6(byte* a)
    {
        for (int i = 0; i < 15; i++) if (a[i] != 0) return false;
        return a[15] == 1;
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
            p->Wnode.ClientContext = 1; // timestamps from QueryPerformanceCounter
            p->BufferSize = 64; // KB: a busy download is tens of thousands of events a second
            p->MinimumBuffers = 4;
            p->MaximumBuffers = 64;
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

    private int Enable()
    {
        int size = 4 + 2 * Events.Length;
        byte* filter = stackalloc byte[size];
        filter[0] = 1; // these ids only
        filter[1] = 0;
        *(ushort*)(filter + 2) = (ushort)Events.Length;
        for (int i = 0; i < Events.Length; i++) ((ushort*)(filter + 4))[i] = Events[i];
        var desc = new EVENT_FILTER_DESCRIPTOR { Ptr = (ulong)filter, Size = (uint)size, Type = 0x80000200 }; // EVENT_FILTER_TYPE_EVENT_ID
        var parameters = new ENABLE_TRACE_PARAMETERS { Version = 2, EnableFilterDesc = &desc, FilterDescCount = 1 };
        var provider = KernelNetwork;
        return EnableTraceEx2(session, &provider, 1, 5, 0, 0, 0, &parameters);
    }

    /// <summary>Stops the session if it's running: 0, or the Windows error (4201 = there wasn't one, 5 = not allowed).</summary>
    private static int StopSession()
    {
        int size = sizeof(EVENT_TRACE_PROPERTIES) + 512;
        var p = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)size);
        p->Wnode.BufferSize = (uint)size;
        p->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
        int err = ControlTraceW(0, SessionName, p, 1); // EVENT_TRACE_CONTROL_STOP
        NativeMemory.Free(p);
        return err;
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
