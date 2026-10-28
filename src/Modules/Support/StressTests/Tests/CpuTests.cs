using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace DaisysApp.Applets.Support.StressTests.Tests;

/// <summary>
/// Work for the CPU in small units, each giving a checksum known in advance (worked out once before the clock starts),
/// so a unit that comes out different is a computing error: an unstable overclock or undervolt, or a failing CPU.
/// </summary>
internal static class CpuWork
{
    public const int Seeds = 64;

    /// <summary>One unit: integer hashing over a table (as compression and hashing do), a patch of the Mandelbrot set
    /// in doubles, and a sort (branches). Returns its checksum and the operations it did.</summary>
    public static (ulong Sum, long Ops) Unit(int seed)
    {
        ulong x = (ulong)(seed + 1) * 0x9E3779B97F4A7C15UL;
        Span<uint> table = stackalloc uint[1024];
        for (int i = 0; i < table.Length; i++) { x ^= x << 13; x ^= x >> 7; x ^= x << 17; table[i] = (uint)x; }
        ulong h = 0xCBF29CE484222325UL;
        const int IntSteps = 24000;
        for (int i = 0; i < IntSteps; i++)
        {
            x ^= x << 13; x ^= x >> 7; x ^= x << 17;
            int at = (int)(x & 1023);
            table[at] += (uint)(x >> 32);
            h = BitOperations.RotateLeft(h ^ table[at], 5) * 0x100000001B3UL;
        }

        long iterations = 0;
        double ox = -0.75 + (seed % 8) * 0.01, oy = 0.1 + (seed / 8) * 0.01;
        for (int py = 0; py < 24; py++)
            for (int px = 0; px < 24; px++)
            {
                double cr = ox + px * 0.004, ci = oy + py * 0.004, zr = 0, zi = 0;
                int n = 0;
                while (n < 200 && zr * zr + zi * zi < 4)
                {
                    double t = zr * zr - zi * zi + cr;
                    zi = 2 * zr * zi + ci;
                    zr = t;
                    n++;
                }
                iterations += n;
                h ^= (ulong)BitConverter.DoubleToInt64Bits(zr + zi) + (ulong)n;
            }

        Span<int> values = stackalloc int[600];
        for (int i = 0; i < values.Length; i++) { x ^= x << 13; x ^= x >> 7; x ^= x << 17; values[i] = (int)(x % 100000); }
        values.Sort();
        h = (h ^ (ulong)values[17]) * 31 + (ulong)values[300] + (ulong)values[599];
        return (h, IntSteps + iterations + 600L * 10);
    }

    /// <summary>The checksums of every seed (and their operations), worked out on one thread.</summary>
    public static (ulong[] Sums, long[] Ops) Expected()
    {
        var sums = new ulong[Seeds];
        var ops = new long[Seeds];
        for (int i = 0; i < Seeds; i++) (sums[i], ops[i]) = Unit(i);
        return (sums, ops);
    }

    /// <summary>
    /// Runs units on <paramref name="threads"/> threads until the time's up; returns the operations a second. Each
    /// thread's units are checked against <paramref name="expected"/>.
    /// </summary>
    public static double RunThreads(TestContext c, int threads, Func<int, (ulong Sum, long Ops)> unit, (ulong[] Sums, long[] Ops) expected, string what, double per = 1e6)
    {
        long totalOps = 0;
        var perThread = new long[threads];
        var workers = new Thread[threads];
        using var ready = new CountdownEvent(threads);
        using var go = new ManualResetEventSlim();
        for (int t = 0; t < threads; t++)
        {
            int id = t;
            workers[t] = new Thread(() =>
            {
                ready.Signal();
                go.Wait();
                long ops = 0;
                int seed = id * 7;
                while (!c.TimeUp)
                {
                    int s = seed++ % Seeds;
                    var (sum, n) = unit(s);
                    if (sum != expected.Sums[s]) c.Error(F("{0}: thread {1} got a wrong result (unit {2}: {3:X16}, should be {4:X16})", what, id, s, sum, expected.Sums[s]));
                    ops += n;
                    Volatile.Write(ref perThread[id], ops);
                }
            }) { IsBackground = true, Name = $"StressTests-{c.Info.Id}-{t}", Priority = ThreadPriority.BelowNormal };
            workers[t].Start();
        }
        ready.Wait();
        c.Begin();
        go.Set();
        double lastOps = 0;
        var last = TimeSpan.Zero;
        while (!c.TimeUp)
        {
            Thread.Sleep(250);
            double now = perThread.Sum(v => (double)Volatile.Read(ref v));
            var at = c.Elapsed;
            if (at > last) c.Report((now - lastOps) / (at - last).TotalSeconds / per);
            if ((int)at.TotalSeconds != (int)last.TotalSeconds && c.Logging == StressLogging.Maximum)
                c.Detail(F("{0:0} s: {1}", at.TotalSeconds, string.Join(" ", perThread.Select(v => (Volatile.Read(ref v) / Math.Max(0.001, at.TotalSeconds) / per).ToString("0.0")))));
            lastOps = now;
            last = at;
        }
        foreach (var w in workers) w.Join();
        double seconds = c.Elapsed.TotalSeconds;
        totalOps = perThread.Sum();
        return totalOps / seconds / per;
    }
}

/// <summary>CPU → Single-Core: the units on one thread.</summary>
internal sealed class CpuSingle : IStressTest
{
    public void Run(TestContext c)
    {
        c.Step = T("Working out the answers");
        var expected = CpuWork.Expected();
        c.Step = T("One thread");
        double mops = CpuWork.RunThreads(c, 1, CpuWork.Unit, expected, T("Single-Core"));
        c.Result.Value = mops;
        c.Result.Device = Environment.ProcessorCount + " " + T("logical processors");
    }
}

/// <summary>CPU → Multi-Core: the units on every logical processor.</summary>
internal sealed class CpuMulti : IStressTest
{
    public void Run(TestContext c)
    {
        c.Step = T("Working out the answers");
        var expected = CpuWork.Expected();
        int threads = c.Threads > 0 ? c.Threads : Environment.ProcessorCount;
        c.Step = P(threads, "{0} thread", "{0} threads");
        double mops = CpuWork.RunThreads(c, threads, CpuWork.Unit, expected, T("Multi-Core"));
        c.Result.Value = mops;
        c.Add(T("Threads"), threads, "");
        if (c.Earlier.LastOrDefault(r => r.Id == "CpuSingle" && r.Value > 0) is { } single)
            c.Add(T("Scaling over one thread"), mops / single.Value, "×");
    }
}

/// <summary>CPU → Vector: multiply-adds on wide registers on every logical processor (the most heat a CPU makes).</summary>
internal sealed class CpuVector : IStressTest
{
    private const int Steps = 200_000;

    public static string Kind => Fma.IsSupported && Avx.IsSupported ? "AVX2 + FMA" : Avx.IsSupported ? "AVX" : Vector.IsHardwareAccelerated ? "SSE" : T("Scalar");

    /// <summary>FLOPs in one unit.</summary>
    public static long Flops => Fma.IsSupported && Avx.IsSupported ? Steps * 8L * 8 * 2 : Avx.IsSupported ? Steps * 8L * 8 * 2 : Steps * 8L * Vector<float>.Count * 2;

    public static (ulong Sum, long Ops) Unit(int seed)
    {
        float s = 1f + seed * 0.001f;
        // a method for each kind, so the JIT keeps every accumulator in a register
        ulong sum = Fma.IsSupported && Avx.IsSupported ? WithFma(s) : Avx.IsSupported ? WithAvx(s) : WithVector(s);
        return (sum, Flops);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static ulong WithFma(float s)
    {
        var m = Vector256.Create(0.99999f);
        var add = Vector256.Create(0.0001f * s);
        Vector256<float> a0 = Vector256.Create(s), a1 = Vector256.Create(s + 1), a2 = Vector256.Create(s + 2), a3 = Vector256.Create(s + 3),
            a4 = Vector256.Create(s + 4), a5 = Vector256.Create(s + 5), a6 = Vector256.Create(s + 6), a7 = Vector256.Create(s + 7);
        for (int i = 0; i < Steps; i++)
        {
            a0 = Fma.MultiplyAdd(a0, m, add); a1 = Fma.MultiplyAdd(a1, m, add); a2 = Fma.MultiplyAdd(a2, m, add); a3 = Fma.MultiplyAdd(a3, m, add);
            a4 = Fma.MultiplyAdd(a4, m, add); a5 = Fma.MultiplyAdd(a5, m, add); a6 = Fma.MultiplyAdd(a6, m, add); a7 = Fma.MultiplyAdd(a7, m, add);
        }
        return Sum(a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static ulong WithAvx(float s)
    {
        var m = Vector256.Create(0.99999f);
        var add = Vector256.Create(0.0001f * s);
        Vector256<float> a0 = Vector256.Create(s), a1 = Vector256.Create(s + 1), a2 = Vector256.Create(s + 2), a3 = Vector256.Create(s + 3),
            a4 = Vector256.Create(s + 4), a5 = Vector256.Create(s + 5), a6 = Vector256.Create(s + 6), a7 = Vector256.Create(s + 7);
        for (int i = 0; i < Steps; i++)
        {
            a0 = a0 * m + add; a1 = a1 * m + add; a2 = a2 * m + add; a3 = a3 * m + add;
            a4 = a4 * m + add; a5 = a5 * m + add; a6 = a6 * m + add; a7 = a7 * m + add;
        }
        return Sum(a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static ulong WithVector(float s)
    {
        var vm = new Vector<float>(0.99999f);
        var vadd = new Vector<float>(0.0001f * s);
        Vector<float> b0 = new(s), b1 = new(s + 1), b2 = new(s + 2), b3 = new(s + 3), b4 = new(s + 4), b5 = new(s + 5), b6 = new(s + 6), b7 = new(s + 7);
        for (int i = 0; i < Steps; i++)
        {
            b0 = b0 * vm + vadd; b1 = b1 * vm + vadd; b2 = b2 * vm + vadd; b3 = b3 * vm + vadd;
            b4 = b4 * vm + vadd; b5 = b5 * vm + vadd; b6 = b6 * vm + vadd; b7 = b7 * vm + vadd;
        }
        var all = b0 + b1 + b2 + b3 + b4 + b5 + b6 + b7;
        ulong h = 0;
        for (int i = 0; i < Vector<float>.Count; i++) h = h * 31 + BitConverter.SingleToUInt32Bits(all[i]);
        return h;
    }

    private static ulong Sum(Vector256<float> v)
    {
        ulong h = 0;
        for (int i = 0; i < 8; i++) h = h * 31 + BitConverter.SingleToUInt32Bits(v.GetElement(i));
        return h;
    }

    public void Run(TestContext c)
    {
        c.Step = T("Working out the answers");
        var sums = new ulong[CpuWork.Seeds];
        var ops = new long[CpuWork.Seeds];
        for (int i = 0; i < CpuWork.Seeds; i++) (sums[i], ops[i]) = Unit(i);
        int threads = c.Threads > 0 ? c.Threads : Environment.ProcessorCount;
        c.Step = $"{Kind} · {P(threads, "{0} thread", "{0} threads")}";
        // the units' operations are FLOPs here: GFLOPS
        c.Result.Value = CpuWork.RunThreads(c, threads, Unit, (sums, ops), T("Vector"), 1e9);
        c.Result.Device = Kind;
        c.Add(T("Threads"), threads, "");
    }
}
