using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.Tools.DiskMark;

public enum Pattern { Seq, Rnd }

/// <summary>What a run measures: reads then writes (as CrystalDiskMark 8), one of them, or 70% reads / 30% writes mixed.</summary>
public enum BenchMode { ReadWrite, Read, Write, Mix }

public enum IoKind { Read, Write, Mix }

/// <summary>One test: sequential or random, its block size, how many requests each thread keeps waiting (Q) and how many threads (T).</summary>
public sealed class TestDef
{
    public Pattern Pattern { get; set; }
    public int BlockKiB { get; set; }
    public int Queues { get; set; }
    public int Threads { get; set; }

    public TestDef() { }
    public TestDef(Pattern p, int blockKiB, int q, int t) { Pattern = p; BlockKiB = blockKiB; Queues = q; Threads = t; }

    public string Block => BlockKiB >= 1024 && BlockKiB % 1024 == 0 ? $"{BlockKiB / 1024}M" : $"{BlockKiB}K";
    /// <summary>"SEQ1M", "RND4K".</summary>
    public string Kind => (Pattern == Pattern.Seq ? "SEQ" : "RND") + Block;
    /// <summary>"Q8T1".</summary>
    public string Depth => $"Q{Queues}T{Threads}";
    /// <summary>"SEQ1M Q8T1", as CrystalDiskMark names it.</summary>
    public string Name => $"{Kind} {Depth}";

    public TestDef Copy() => new(Pattern, BlockKiB, Queues, Threads);
}

/// <summary>CrystalDiskMark 8's profiles, and the user's own.</summary>
public static class Profiles
{
    public const string Default = "Default", Nvme = "NVMe", Peak = "Peak", RealWorld = "RealWorld", Custom = "Custom";
    public static readonly string[] All = [Default, Nvme, Peak, RealWorld, Custom];

    public static string Title(string id) => id switch
    {
        Nvme => T("NVMe SSD"),
        Peak => T("Peak Performance"),
        RealWorld => T("Real World"),
        Custom => T("Custom"),
        _ => T("Default"),
    };

    public static List<TestDef> Tests(string id, List<TestDef>? custom) => id switch
    {
        Nvme => [new(Pattern.Seq, 1024, 8, 1), new(Pattern.Seq, 128, 32, 1), new(Pattern.Rnd, 4, 32, 16), new(Pattern.Rnd, 4, 1, 1)],
        Peak => [new(Pattern.Seq, 1024, 8, 1), new(Pattern.Rnd, 4, 32, 16)],
        RealWorld => [new(Pattern.Seq, 1024, 1, 1), new(Pattern.Rnd, 4, 1, 1)],
        Custom when custom is { Count: > 0 } => custom.Select(t => t.Copy()).ToList(),
        _ => DefaultTests(),
    };

    public static List<TestDef> DefaultTests() => [new(Pattern.Seq, 1024, 8, 1), new(Pattern.Seq, 1024, 1, 1), new(Pattern.Rnd, 4, 32, 1), new(Pattern.Rnd, 4, 1, 1)];
}

/// <summary>One measurement: bytes a second, operations a second and the average time each took.</summary>
public sealed class Score
{
    public double MBps { get; set; }
    public double Iops { get; set; }
    public double LatencyUs { get; set; }
}

/// <summary>
/// The measuring itself, as DiskSpd does it for CrystalDiskMark: the test file opened without Windows' cache
/// (FILE_FLAG_NO_BUFFERING, write-through), a thread per T, each keeping Q requests waiting on its own completion port
/// and sending the next as each one finishes, for the measure time. Sequential tests walk each thread's share of the
/// file in order; random ones pick any block. Writes come from a buffer of random bytes (or zeros).
/// </summary>
internal static unsafe class Bench
{
    /// <summary>What the running test is doing, for the page's live view (read without locks: it's only for drawing).</summary>
    public sealed class Live
    {
        /// <summary>Requests sent per part of the file (the file map); the page takes them and zeroes them.</summary>
        public readonly int[] Heat = new int[HeatCells];
        public long FileSize;
        public Worker[] Workers = [];
        public long MeasureStart;

        public long Bytes { get { long n = 0; foreach (var w in Workers) n += Volatile.Read(ref w.Bytes); return n; } }
        public long Ops { get { long n = 0; foreach (var w in Workers) n += Volatile.Read(ref w.Ops); return n; } }
    }

    public const int HeatCells = 512;

    public sealed class Worker
    {
        public long Bytes, Ops, LatencyTicks;
        public volatile bool Ready;
        public Exception? Failed;
    }

    private sealed class Shared
    {
        public volatile bool Measuring, Stop;
        public readonly ManualResetEventSlim Go = new(false);
    }

    /// <summary>
    /// Writes the test file (its whole size, so reads find data), reporting 0–1. The handle returned keeps it (it's
    /// deleted when that closes, even if the app ends).
    /// </summary>
    public static SafeFileHandle Prepare(string path, long size, bool zeros, Live live, Action<double> progress, CancellationToken cancel)
    {
        var keep = CreateFileW(path, GenericRead | GenericWrite, ShareAll, IntPtr.Zero, CreateAlways,
            NoBuffering | WriteThrough | DeleteOnClose, IntPtr.Zero);
        if (keep.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        const int Chunk = 8 << 20;
        byte* buf = (byte*)NativeMemory.AlignedAlloc(Chunk, 4096);
        try
        {
            Fill(buf, Chunk, zeros);
            live.FileSize = size;
            long done = 0;
            while (done < size)
            {
                cancel.ThrowIfCancellationRequested();
                int n = (int)Math.Min(Chunk, size - done);
                int wrote;
                if (!WriteFile(keep, buf, n, &wrote, null)) throw new Win32Exception(Marshal.GetLastWin32Error());
                live.Heat[(int)(done * HeatCells / size)]++;
                done += wrote;
                progress((double)done / size);
            }
            return keep;
        }
        catch
        {
            keep.Dispose();
            throw;
        }
        finally { NativeMemory.AlignedFree(buf); }
    }

    /// <summary>Runs one test for <paramref name="seconds"/>, the file already written.</summary>
    public static Score Run(string path, long fileSize, TestDef test, IoKind kind, bool zeros, double seconds, Live live, CancellationToken cancel)
    {
        var shared = new Shared();
        var workers = new Worker[test.Threads];
        var threads = new Thread[test.Threads];
        for (int i = 0; i < workers.Length; i++)
        {
            var w = workers[i] = new Worker();
            int index = i;
            threads[i] = new Thread(() => Work(path, fileSize, test, kind, zeros, index, w, shared, live))
            {
                IsBackground = true, Name = "DiskMark " + test.Name, Priority = ThreadPriority.AboveNormal,
            };
        }
        live.FileSize = fileSize;
        live.Workers = workers;
        foreach (var t in threads) t.Start();
        try
        {
            while (!workers.All(w => w.Ready || w.Failed != null)) Thread.Sleep(1);
            if (workers.FirstOrDefault(w => w.Failed != null)?.Failed is { } early) throw early;
            var sw = Stopwatch.StartNew();
            live.MeasureStart = Stopwatch.GetTimestamp();
            shared.Measuring = true;
            shared.Go.Set();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                cancel.ThrowIfCancellationRequested();
                if (workers.FirstOrDefault(w => w.Failed != null)?.Failed is { } ex) throw ex;
                Thread.Sleep(Math.Clamp((int)((seconds - sw.Elapsed.TotalSeconds) * 1000), 1, 50));
            }
            shared.Measuring = false;
            double elapsed = sw.Elapsed.TotalSeconds;
            shared.Stop = true;
            foreach (var t in threads) t.Join();
            if (workers.FirstOrDefault(w => w.Failed != null)?.Failed is { } late) throw late;
            long bytes = workers.Sum(w => w.Bytes), ops = workers.Sum(w => w.Ops), lat = workers.Sum(w => w.LatencyTicks);
            return new Score
            {
                MBps = bytes / elapsed / 1e6,
                Iops = ops / elapsed,
                LatencyUs = ops > 0 ? lat * 1e6 / Stopwatch.Frequency / ops : 0,
            };
        }
        finally
        {
            shared.Stop = true;
            shared.Go.Set();
            foreach (var t in threads) if (t.IsAlive) t.Join();
        }
    }

    private static void Work(string path, long fileSize, TestDef test, IoKind kind, bool zeros, int index, Worker me, Shared shared, Live live)
    {
        int q = Math.Max(1, test.Queues);
        int block = test.BlockKiB * 1024;
        long blocks = Math.Max(1, fileSize / block);
        // a sequential test splits the file between its threads, each walking its own part
        long share = Math.Max(1, blocks / test.Threads), first = Math.Min(index * share, blocks - 1);
        var rng = new Random(Environment.TickCount ^ (index * 7919));
        NativeOverlapped* ovs = null;
        byte* bufs = null;
        IntPtr port = IntPtr.Zero;
        long* started = null;
        int waiting = 0;
        SafeFileHandle? h = null;
        long next = 0;
        int[] heat = live.Heat;
        try
        {
            h = CreateFileW(path, GenericRead | GenericWrite, ShareAll, IntPtr.Zero, OpenExisting, NoBuffering | WriteThrough | Overlapped, IntPtr.Zero);
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            port = CreateIoCompletionPort(h, IntPtr.Zero, UIntPtr.Zero, 1);
            if (port == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            ovs = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)(q * sizeof(NativeOverlapped)));
            started = (long*)NativeMemory.AllocZeroed((nuint)(q * sizeof(long)));
            bufs = (byte*)NativeMemory.AlignedAlloc((nuint)((long)q * block), 4096);
            Fill(bufs, (long)q * block, zeros);
            me.Ready = true;
            shared.Go.Wait();
            if (shared.Stop) return;

            for (int i = 0; i < q; i++) Send(i);
            while (waiting > 0)
            {
                NativeOverlapped* done;
                bool ok = GetQueuedCompletionStatus(port, out uint got, out _, out done, 500);
                if (done == null)
                {
                    if (!ok && Marshal.GetLastWin32Error() == WaitTimeout) continue;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                waiting--;
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
                int i = (int)(done - ovs);
                if (shared.Measuring)
                {
                    me.LatencyTicks += Stopwatch.GetTimestamp() - started[i];
                    me.Bytes += got;
                    me.Ops++;
                }
                if (!shared.Stop) Send(i);
            }
        }
        catch (Exception ex)
        {
            me.Failed = ex;
            shared.Stop = true;
            // requests still out write into our buffers: cancel them and wait for them before freeing anything
            if (h != null && !h.IsInvalid && waiting > 0)
            {
                CancelIoEx(h, null);
                for (int tries = 0; waiting > 0 && tries < 100; tries++)
                {
                    GetQueuedCompletionStatus(port, out _, out _, out NativeOverlapped* d, 100);
                    if (d != null) waiting--;
                }
            }
            me.Ready = true;
        }
        finally
        {
            if (waiting == 0)
            {
                if (bufs != null) NativeMemory.AlignedFree(bufs);
                if (ovs != null) NativeMemory.Free(ovs);
                if (started != null) NativeMemory.Free(started);
            }
            if (port != IntPtr.Zero) CloseHandle(port);
            h?.Dispose();
        }

        void Send(int i)
        {
            long b = test.Pattern == Pattern.Seq ? first + next++ % share : rng.NextInt64(blocks);
            long offset = b * block;
            var ov = ovs + i;
            *ov = default;
            ov->OffsetLow = (int)offset;
            ov->OffsetHigh = (int)(offset >> 32);
            bool write = kind == IoKind.Write || (kind == IoKind.Mix && rng.Next(10) >= 7);
            // every cell of the file map the block covers
            for (long c = offset * HeatCells / fileSize, last = (offset + block - 1) * HeatCells / fileSize; c <= last && c < HeatCells; c++) heat[c]++;
            started[i] = Stopwatch.GetTimestamp();
            bool sent = write ? WriteFile(h!, bufs + (long)i * block, block, null, ov) : ReadFile(h!, bufs + (long)i * block, block, null, ov);
            if (!sent && Marshal.GetLastWin32Error() != IoPending) throw new Win32Exception(Marshal.GetLastWin32Error());
            waiting++;
        }
    }

    private static void Fill(byte* buf, long length, bool zeros)
    {
        var span = new Span<byte>(buf, (int)length);
        if (zeros) span.Clear();
        else Random.Shared.NextBytes(span);
    }

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareAll = 7, CreateAlways = 2, OpenExisting = 3;
    private const uint NoBuffering = 0x20000000, WriteThrough = 0x80000000, Overlapped = 0x40000000, DeleteOnClose = 0x04000000;
    private const int IoPending = 997, WaitTimeout = 258;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle h, byte* buffer, int count, int* read, NativeOverlapped* ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle h, byte* buffer, int count, int* written, NativeOverlapped* ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateIoCompletionPort(SafeFileHandle h, IntPtr existing, UIntPtr key, uint threads);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetQueuedCompletionStatus(IntPtr port, out uint bytes, out UIntPtr key, out NativeOverlapped* ov, uint ms);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle h, NativeOverlapped* ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
}
