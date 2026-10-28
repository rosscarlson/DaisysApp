using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.Support.StressTests.Tests;

/// <summary>
/// Storage: a test file on the chosen drive written and read in 1 MB blocks (4 at a time), then read in random 4 KB
/// pieces (8 at a time), past Windows' cache (the file opened for overlapped I/O, so the threads' requests really are
/// out at the same time: a synchronous handle does one at a time). Every 4 KB carries its place and pass, checked as it's read back, so data
/// that comes back wrong is counted. The file is deleted after.
/// </summary>
internal sealed unsafe class StorageTest : IStressTest
{
    private const int Block = 1 << 20, Sector = 4096, Writers = 4, Readers = 8;
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    public void Run(TestContext c)
    {
        string root = c.Settings.Drive;
        if (!Directory.Exists(root)) { c.Result.Failed = F("{0} isn't there", root); return; }
        var drive = new DriveInfo(root);
        long size = Math.Min((long)Math.Clamp(c.Settings.StorageMiB, 64, 16384) << 20, drive.AvailableFreeSpace / 2) / Block * Block;
        if (size < 64L << 20) { c.Result.Failed = F("{0} hasn't room for a test file", root); return; }
        string folder = Path.Combine(root, AppPaths.ShortName + "-StressTest");
        try { Directory.CreateDirectory(folder); }
        catch (UnauthorizedAccessException) { folder = Path.Combine(Path.GetTempPath(), AppPaths.ShortName + "-StressTest"); Directory.CreateDirectory(folder); }
        string path = Path.Combine(folder, "stresstest.tmp");
        var (model, kind) = DiskInfo.Describe(root);
        c.Result.Device = string.Join(" · ", new[] { root.TrimEnd('\\'), model, kind }.Where(s => s.Length > 0));
        c.Detail(F("Test file {0}, {1:0} MB", path, size / 1048576.0));
        var buffers = new byte*[Math.Max(Writers, Readers)];
        try
        {
            for (int i = 0; i < buffers.Length; i++) buffers[i] = (byte*)NativeMemory.AlignedAlloc(Block, Sector);
            // random bytes (so a drive that compresses doesn't score for it), each 4 KB stamped when written
            var random = new Random(77);
            for (int i = 0; i < buffers.Length; i++) random.NextBytes(new Span<byte>(buffers[i], Block));
            using var file = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                NoBuffering | FileOptions.WriteThrough | FileOptions.DeleteOnClose | FileOptions.Asynchronous, size);
            long blocks = size / Block;
            // the pass each block was last written in, so a read knows what it should find
            var written = new int[blocks];
            c.Begin();
            var writeUntil = TimeSpan.FromTicks(c.Length.Ticks * 35 / 100);
            var readUntil = TimeSpan.FromTicks(c.Length.Ticks * 70 / 100);

            // ---- sequential writes: the whole file at least once
            int pass = 0;
            long bytesWritten = 0;
            var sw = Stopwatch.StartNew();
            do
            {
                pass++;
                c.Step = F("Writing (pass {0})", pass);
                bytesWritten += Sequential(c, file, blocks, buffers, Writers, pass, written, write: true, pass == 1 ? TimeSpan.MaxValue : writeUntil);
            }
            while (!c.Stopping && c.Elapsed < writeUntil);
            double writeRate = bytesWritten / sw.Elapsed.TotalSeconds / 1e6;
            if (c.Stopping) return;

            // ---- sequential reads, checked
            c.Step = T("Reading");
            long read = 0;
            sw.Restart();
            do read += Sequential(c, file, blocks, buffers, Readers / 2, 0, written, write: false, readUntil);
            while (!c.Stopping && c.Elapsed < readUntil);
            double readRate = read / sw.Elapsed.TotalSeconds / 1e6;
            if (c.Stopping) return;

            // ---- random 4 KB reads, checked
            c.Step = T("Random 4 KB reads");
            sw.Restart();
            long ios = RandomReads(c, file, size, buffers, written);
            double iops = ios / sw.Elapsed.TotalSeconds;

            c.Add(T("Sequential write"), writeRate, "MB/s");
            c.Add(T("Sequential read"), readRate, "MB/s");
            c.Add(T("Random 4 KB read"), iops * Sector / 1e6, "MB/s");
            c.Add(T("Random 4 KB IOPS"), iops, "IOPS");
            c.Add(T("Test file"), size / 1048576.0, "MB");
            c.Result.Value = (writeRate + readRate) / 2;
            // the score counts random reads as much as sequential speed (the reference PC: 260 MB/s at 4 KB)
            c.Result.Score = 1000 * Math.Sqrt(c.Result.Value / c.Info.Reference * (iops * Sector / 1e6) / 260);
        }
        finally
        {
            foreach (var b in buffers) if (b != null) NativeMemory.AlignedFree(b);
            try { File.Delete(path); Directory.Delete(folder); } catch { }
        }
    }

    /// <summary>Writes or reads the file block by block on <paramref name="threads"/> threads; bytes moved.</summary>
    private static long Sequential(TestContext c, SafeFileHandle file, long blocks, byte*[] buffers, int threads, int pass, int[] written, bool write, TimeSpan until)
    {
        long next = -1, moved = 0;
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            byte* buf = buffers[t];
            var span = new Span<byte>(buf, Block);
            long b;
            while (!c.Stopping && (b = Interlocked.Increment(ref next)) < blocks && (c.Elapsed < until || until == TimeSpan.MaxValue))
            {
                long offset = b * Block;
                try
                {
                    if (write)
                    {
                        for (int s = 0; s < Block; s += Sector) Stamp(buf + s, offset + s, pass);
                        RandomAccess.Write(file, span, offset);
                        written[b] = pass;
                    }
                    else
                    {
                        int got = RandomAccess.Read(file, span, offset);
                        for (int s = 0; s < got; s += Sector) Verify(c, buf + s, offset + s, written[b]);
                    }
                    Interlocked.Add(ref moved, Block);
                }
                catch (IOException ex) { c.Error(F("{0} at {1:0,0} MB failed: {2}", write ? T("Writing") : T("Reading"), offset / 1048576.0, ex.Message)); }
            }
        }) { IsBackground = true, Name = $"StressTests-Storage-{t}" }).ToList();
        workers.ForEach(w => w.Start());
        Watch(c, workers, () => Interlocked.Read(ref moved), 1e6);
        return moved;
    }

    private static long RandomReads(TestContext c, SafeFileHandle file, long size, byte*[] buffers, int[] written)
    {
        long ios = 0, sectors = size / Sector;
        var workers = Enumerable.Range(0, Readers).Select(t => new Thread(() =>
        {
            byte* buf = buffers[t];
            var span = new Span<byte>(buf, Sector);
            var rng = new Random(t * 31 + 5);
            while (!c.TimeUp)
            {
                long offset = rng.NextInt64(sectors) * Sector;
                try
                {
                    RandomAccess.Read(file, span, offset);
                    Verify(c, buf, offset, written[offset / Block]);
                    Interlocked.Increment(ref ios);
                }
                catch (IOException ex) { c.Error(F("Reading at {0:0,0} MB failed: {1}", offset / 1048576.0, ex.Message)); }
            }
        }) { IsBackground = true, Name = $"StressTests-Storage-r{t}" }).ToList();
        workers.ForEach(w => w.Start());
        Watch(c, workers, () => Interlocked.Read(ref ios) * Sector, 1e6);
        return ios;
    }

    /// <summary>Reports the rate (bytes → MB/s) until the threads are done.</summary>
    private static void Watch(TestContext c, List<Thread> workers, Func<long> moved, double per)
    {
        var sw = Stopwatch.StartNew();
        long lastMoved = 0;
        var last = TimeSpan.Zero;
        while (workers.Any(w => w.IsAlive))
        {
            Thread.Sleep(250);
            long m = moved();
            var at = sw.Elapsed;
            if (at > last) c.Report((m - lastMoved) / (at - last).TotalSeconds / per, "MB/s");
            lastMoved = m;
            last = at;
        }
    }

    private const ulong Magic = 0x5354524553535431UL; // "STRESST1"

    private static void Stamp(byte* sector, long offset, int pass)
    {
        var w = (ulong*)sector;
        w[0] = Magic;
        w[1] = (ulong)offset;
        w[2] = (ulong)pass;
        w[3] = (ulong)offset * 0x9E3779B97F4A7C15UL ^ (ulong)pass;
    }

    private static void Verify(TestContext c, byte* sector, long offset, int pass)
    {
        var w = (ulong*)sector;
        if (w[0] == Magic && w[1] == (ulong)offset && w[2] == (ulong)pass && w[3] == ((ulong)offset * 0x9E3779B97F4A7C15UL ^ (ulong)pass)) return;
        c.Error(F("The 4 KB at {0:0,0} MB came back wrong (it holds {1}, pass {2})", offset / 1048576.0,
            w[0] == Magic ? F("the data for {0:0,0} MB", (long)w[1] / 1048576.0) : T("something else"), (long)w[2]));
    }
}
