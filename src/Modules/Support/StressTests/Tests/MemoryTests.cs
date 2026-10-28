using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace DaisysApp.Applets.Support.StressTests.Tests;

/// <summary>Memory → Bandwidth: every core reading, writing and copying its own buffer, far bigger than the caches.</summary>
internal sealed unsafe class MemBandwidth : IStressTest
{
    private const long PerThread = 64L << 20;

    public void Run(TestContext c)
    {
        int threads = Math.Max(1, c.Threads > 0 ? c.Threads : Cpus.PhysicalCores() is int n && n > 0 ? n : Environment.ProcessorCount);
        c.Step = T("Getting memory");
        var src = new byte*[threads];
        var dst = new byte*[threads];
        try
        {
            for (int t = 0; t < threads; t++)
            {
                src[t] = (byte*)NativeMemory.AlignedAlloc((nuint)PerThread, 4096);
                dst[t] = (byte*)NativeMemory.AlignedAlloc((nuint)PerThread, 4096);
                new Span<byte>(src[t], (int)PerThread).Fill(0x5A);
                new Span<byte>(dst[t], (int)PerThread).Clear();
            }
            c.Begin();
            var third = TimeSpan.FromTicks(c.Length.Ticks / 3);
            double read = Phase(c, T("Reading"), threads, third, t => Read(src[t]));
            double write = Phase(c, T("Writing"), threads, third * 2, t => Write(dst[t]));
            double copy = Phase(c, T("Copying"), threads, c.Length, t => Buffer.MemoryCopy(src[t], dst[t], PerThread, PerThread), copies: true);
            c.Add(T("Read"), read, "GB/s");
            c.Add(T("Write"), write, "GB/s");
            c.Add(T("Copy"), copy, "GB/s");
            c.Add(T("Threads"), threads, "");
            c.Result.Value = (read + write + copy) / 3;
        }
        finally
        {
            for (int t = 0; t < threads; t++)
            {
                if (src[t] != null) NativeMemory.AlignedFree(src[t]);
                if (dst[t] != null) NativeMemory.AlignedFree(dst[t]);
            }
        }
    }

    /// <summary>Every thread doing <paramref name="pass"/> over its buffer until <paramref name="until"/>; GB a second.</summary>
    private static double Phase(TestContext c, string step, int threads, TimeSpan until, Action<int> pass, bool copies = false)
    {
        c.Step = step;
        long passes = 0;
        var start = c.Elapsed;
        var sw = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            while (!c.Stopping && c.Elapsed < until)
            {
                pass(t);
                Interlocked.Increment(ref passes);
            }
        }) { IsBackground = true, Name = $"StressTests-MemBandwidth-{t}", Priority = ThreadPriority.BelowNormal }).ToList();
        workers.ForEach(w => w.Start());
        long lastPasses = 0;
        var last = sw.Elapsed;
        while (workers.Any(w => w.IsAlive))
        {
            Thread.Sleep(250);
            long now = Interlocked.Read(ref passes);
            var at = sw.Elapsed;
            // a copy reads and writes each byte
            c.Report((now - lastPasses) * PerThread * (copies ? 2 : 1) / (at - last).TotalSeconds / 1e9);
            lastPasses = now;
            last = at;
        }
        double gb = passes * PerThread * (copies ? 2.0 : 1.0) / 1e9;
        double rate = gb / Math.Max(0.001, sw.Elapsed.TotalSeconds);
        c.Detail(F("{0}: {1:0.0} GB/s ({2} passes)", step, rate, passes));
        return rate;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Read(byte* p)
    {
        if (Avx.IsSupported)
        {
            Vector256<long> a = default, b = default, c = default, d = default;
            for (long i = 0; i < PerThread; i += 128)
            {
                a = Avx2.IsSupported ? Avx2.Add(a, Avx.LoadAlignedVector256((long*)(p + i))) : a ^ Avx.LoadAlignedVector256((long*)(p + i));
                b ^= Avx.LoadAlignedVector256((long*)(p + i + 32));
                c ^= Avx.LoadAlignedVector256((long*)(p + i + 64));
                d ^= Avx.LoadAlignedVector256((long*)(p + i + 96));
            }
            Sink = (a ^ b ^ c ^ d).GetElement(0);
        }
        else
        {
            long s = 0;
            for (long i = 0; i < PerThread; i += 8) s += *(long*)(p + i);
            Sink = s;
        }
    }

    public static long Sink;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Write(byte* p)
    {
        if (Avx.IsSupported)
        {
            var v = Vector256.Create(0x0123456789ABCDEFL).AsByte();
            for (long i = 0; i < PerThread; i += 128)
            {
                // straight to memory, past the caches, as memory tests measure writing
                Avx.StoreAlignedNonTemporal(p + i, v);
                Avx.StoreAlignedNonTemporal(p + i + 32, v);
                Avx.StoreAlignedNonTemporal(p + i + 64, v);
                Avx.StoreAlignedNonTemporal(p + i + 96, v);
            }
            Sse.StoreFence();
        }
        else new Span<byte>(p, (int)PerThread).Fill(0x33);
    }
}

/// <summary>
/// Memory → Latency: a chain of random reads, each needing the one before, so nothing can be read ahead: the time
/// memory takes to answer. Measured in blocks the size of each cache too.
/// </summary>
internal sealed unsafe class MemLatency : IStressTest
{
    private const int Line = 64;

    public void Run(TestContext c)
    {
        (long Bytes, string Name)[] sizes = [(32L << 10, "32 KB"), (256L << 10, "256 KB"), (8L << 20, "8 MB"), (512L << 20, "512 MB")];
        long biggest = sizes[^1].Bytes;
        c.Step = T("Getting memory");
        byte* block = (byte*)NativeMemory.AlignedAlloc((nuint)biggest, 4096);
        try
        {
            c.Begin();
            double last = double.NaN;
            for (int i = 0; i < sizes.Length && !c.Stopping; i++)
            {
                var (bytes, name) = sizes[i];
                c.Step = F("Building a chain of {0}", name);
                Chain(block, bytes / Line);
                c.Step = name;
                // the big block gets half the time; the caches share the rest
                var until = i == sizes.Length - 1 ? c.Length : TimeSpan.FromTicks(c.Length.Ticks / 2 * (i + 1) / (sizes.Length - 1));
                last = Chase(c, block, until);
                c.Add(name, last, "ns", lowerIsBetter: true);
                c.Detail(F("{0}: {1:0.0} ns", name, last));
            }
            c.Result.Value = last;
            c.Result.Device = T("one thread, 64-byte steps");
        }
        finally { NativeMemory.AlignedFree(block); }
    }

    /// <summary>Links <paramref name="lines"/> cache lines in one random loop (Sattolo's shuffle).</summary>
    private static void Chain(byte* block, long lines)
    {
        var order = new int[lines];
        for (int i = 0; i < lines; i++) order[i] = i;
        var rng = new Random(1234);
        for (long i = lines - 1; i > 0; i--)
        {
            int j = rng.Next((int)i);
            (order[i], order[j]) = (order[j], order[i]);
        }
        for (long i = 0; i < lines; i++)
            *(long*)(block + (long)order[i] * Line) = (long)order[(i + 1) % lines] * Line;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Chase(TestContext c, byte* block, TimeSpan until)
    {
        long at = 0, steps = 0;
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        long lastSteps = 0;
        while (!c.Stopping && c.Elapsed < until)
        {
            for (int i = 0; i < 100_000; i++)
            {
                at = *(long*)(block + at);
                at = *(long*)(block + at);
                at = *(long*)(block + at);
                at = *(long*)(block + at);
            }
            steps += 400_000;
            var now = sw.Elapsed;
            if (now - lastReport > TimeSpan.FromMilliseconds(250))
            {
                c.Report((now - lastReport).TotalMilliseconds * 1e6 / (steps - lastSteps));
                lastReport = now;
                lastSteps = steps;
            }
        }
        MemBandwidth.Sink = at;
        return sw.Elapsed.TotalMilliseconds * 1e6 / Math.Max(1, steps);
    }
}

/// <summary>
/// Memory → Integrity: a large part of the free memory filled with patterns (each word from its own address, then
/// inverted, walking ones, checkerboards) by every core, then read back and compared: a bit that comes back different
/// is a memory error (unstable XMP/EXPO, a failing module). Measures the GB a second written and checked.
/// </summary>
internal sealed unsafe class MemIntegrity : IStressTest
{
    private const long Chunk = 256L << 20;

    public void Run(TestContext c)
    {
        Mem.Status(out ulong total, out ulong available);
        long want = (long)(available * (ulong)Math.Clamp(c.MemoryPercent, 5, 90) / 100) - (1L << 30);
        want = Math.Max(Chunk, want / Chunk * Chunk);
        c.Step = F("Getting {0:0.0} GB", want / 1073741824.0);
        var chunks = new List<IntPtr>();
        try
        {
            for (long got = 0; got < want; got += Chunk)
            {
                var p = (IntPtr)NativeMemory.AlignedAlloc((nuint)Chunk, 4096);
                if (p == IntPtr.Zero) break;
                chunks.Add(p);
            }
        }
        catch (OutOfMemoryException) { }
        if (chunks.Count == 0) { c.Result.Failed = T("No memory could be had"); return; }
        try
        {
            long bytes = chunks.Count * Chunk;
            c.Detail(F("{0:0.0} GB in {1} blocks; {2:0.0} GB were free of {3:0.0} GB", bytes / 1073741824.0, chunks.Count, available / 1073741824.0, total / 1073741824.0));
            int threads = c.Threads > 0 ? c.Threads : Environment.ProcessorCount;
            c.Begin();
            double moved = 0;
            int pass = 0;
            while (!c.TimeUp)
            {
                pass++;
                ulong seed = (ulong)pass * 0x9E3779B97F4A7C15UL;
                int kind = (pass - 1) % 4;
                string name = kind switch { 0 => T("its address"), 1 => T("inverted"), 2 => T("walking ones"), _ => T("checkerboard") };
                c.Step = F("Pass {0}: writing ({1})", pass, name);
                var sw = Stopwatch.StartNew();
                Each(c, chunks, threads, (p, first) => Fill(p, first, seed, kind), bytes);
                double write = sw.Elapsed.TotalSeconds;
                if (c.Stopping) break;
                c.Step = F("Pass {0}: checking", pass);
                sw.Restart();
                int errors = 0;
                Each(c, chunks, threads, (p, first) =>
                {
                    int e = Check(c, p, first, seed, kind);
                    if (e > 0) Interlocked.Add(ref errors, e);
                }, bytes);
                double check = sw.Elapsed.TotalSeconds;
                moved += 2.0 * bytes;
                c.Detail(F("Pass {0} ({1}): written at {2:0.0} GB/s, checked at {3:0.0} GB/s, {4} errors", pass, name, bytes / write / 1e9, bytes / check / 1e9, errors));
            }
            double seconds = Math.Max(0.001, c.Elapsed.TotalSeconds);
            c.Result.Value = moved / seconds / 1e9;
            c.Add(T("Tested"), bytes / 1073741824.0, "GB");
            c.Add(T("Passes"), pass, "");
            c.Add(T("Threads"), threads, "");
        }
        finally
        {
            foreach (var p in chunks) NativeMemory.AlignedFree((void*)p);
        }
    }

    /// <summary>Splits the blocks into pieces for <paramref name="threads"/> threads, reporting GB/s as it goes.</summary>
    private static void Each(TestContext c, List<IntPtr> chunks, int threads, Action<IntPtr, long> work, long bytes)
    {
        const long Piece = 16L << 20;
        long pieces = chunks.Count * (Chunk / Piece), next = -1, done = 0;
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            long i;
            while (!c.Stopping && (i = Interlocked.Increment(ref next)) < pieces)
            {
                long chunk = i / (Chunk / Piece), offset = i % (Chunk / Piece) * Piece;
                work(chunks[(int)chunk] + (nint)offset, (chunk * Chunk + offset) / 8);
                Interlocked.Increment(ref done);
            }
        }) { IsBackground = true, Name = $"StressTests-MemIntegrity-{t}", Priority = ThreadPriority.BelowNormal }).ToList();
        workers.ForEach(w => w.Start());
        var sw = Stopwatch.StartNew();
        long lastDone = 0;
        var last = TimeSpan.Zero;
        while (workers.Any(w => w.IsAlive))
        {
            Thread.Sleep(250);
            long d = Interlocked.Read(ref done);
            var at = sw.Elapsed;
            if (at > last) c.Report((d - lastDone) * Piece / (at - last).TotalSeconds / 1e9);
            lastDone = d;
            last = at;
        }
    }

    private const long PieceWords = (16L << 20) / 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Pattern(long word, ulong seed, int kind) => kind switch
    {
        0 => (ulong)word * 0xD6E8FEB86659FD93UL ^ seed,
        1 => ~((ulong)word * 0xD6E8FEB86659FD93UL ^ seed),
        2 => 1UL << (int)((word + (long)(seed & 63)) & 63),
        _ => (word & 1) == 0 ? 0x5555555555555555UL : 0xAAAAAAAAAAAAAAAAUL,
    };

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Fill(IntPtr p, long first, ulong seed, int kind)
    {
        var w = (ulong*)p;
        for (long i = 0; i < PieceWords; i++) w[i] = Pattern(first + i, seed, kind);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int Check(TestContext c, IntPtr p, long first, ulong seed, int kind)
    {
        var w = (ulong*)p;
        int errors = 0;
        for (long i = 0; i < PieceWords; i++)
        {
            ulong want = Pattern(first + i, seed, kind), got = w[i];
            if (got == want) continue;
            errors++;
            c.Error(F("Memory error at {0:X} (block offset {1:0,0} MB): {2:X16} read, {3:X16} written (bits {4:X16} differ)",
                (long)(w + i), (first + i) * 8 / 1048576.0, got, want, got ^ want));
        }
        return errors;
    }
}
