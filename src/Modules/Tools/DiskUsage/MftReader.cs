using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using DaisysApp.Applets.Tools.Shared;

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>
/// Reads an NTFS drive's master file table (MFT) straight from the disk, as WizTree does: one long read of a file
/// record per file and folder, instead of asking Windows folder by folder, so a whole drive takes seconds. Needs
/// administrator rights (opening the drive itself). Each record gives the entry's folder, name, size and size on disk;
/// records that carry the rest of a busy file's details (extension records) add to their base record.
/// </summary>
internal static class MftReader
{
    private const uint TypeStandardInfo = 0x10, TypeFileName = 0x30, TypeData = 0x80, TypeEnd = 0xFFFFFFFF;
    public const int RootRecord = 5;

    public sealed record Result(RawEntries Entries, long VolumeTotal, long VolumeFree, long ClusterSize);

    /// <param name="drive">"C:".</param>
    /// <param name="progress">Records read so far, of all (on the reading thread).</param>
    public static Result Read(string drive, Action<long, long>? progress, CancellationToken cancel)
    {
        using var volume = Native.OpenVolume(drive);
        var vd = Native.VolumeData(volume);
        int recordSize = vd.BytesPerFileRecordSegment, cluster = vd.BytesPerCluster;
        if (recordSize < 512 || recordSize > 65536 || cluster <= 0) throw new IOException("Unexpected NTFS layout");
        long records = vd.MftValidDataLength / recordSize;
        if (records <= RootRecord || records > int.MaxValue / 2) throw new IOException("Unexpected MFT size");

        // the MFT's own record (record 0) says where the rest of it is on the disk
        int first = Math.Max(cluster, recordSize);
        var rec0 = new byte[(first + 4095) / 4096 * 4096];
        int got = RandomAccess.Read(volume, rec0, vd.MftStartLcn * cluster);
        if (got < recordSize || !Fixup(rec0.AsSpan(0, recordSize))) throw new IOException("Couldn't read the MFT's own record");
        var runs = MftRuns(rec0.AsSpan(0, recordSize));
        if (runs.Count == 0) throw new IOException("The MFT's own record has no data runs");

        var raw = new RawEntries((int)records) { Count = (int)records };
        // reading on one thread, records parsed on this one
        const int ChunkSize = 4 << 20;
        using var chunks = new BlockingCollection<(byte[] Buffer, int Length)>(boundedCapacity: 4);
        var pool = new ConcurrentBag<byte[]>();
        Exception? readError = null;
        var reader = new Thread(() =>
        {
            try
            {
                long left = records * recordSize;
                foreach (var (lcn, length) in runs)
                {
                    long offset = lcn * cluster, bytes = Math.Min(length * cluster, left);
                    while (bytes > 0)
                    {
                        if (cancel.IsCancellationRequested) return;
                        if (!pool.TryTake(out var buf)) buf = new byte[ChunkSize];
                        int want = (int)Math.Min(ChunkSize, bytes);
                        int toRead = (want + 4095) / 4096 * 4096; // whole sectors
                        int n = RandomAccess.Read(volume, buf.AsSpan(0, toRead), offset);
                        if (n <= 0) throw new IOException("The drive stopped answering");
                        n = Math.Min(n, want);
                        chunks.Add((buf, n), cancel);
                        offset += n; bytes -= n; left -= n;
                    }
                    if (left <= 0) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { readError = ex; }
            finally { chunks.CompleteAdding(); }
        }) { IsBackground = true, Name = "MFT reader" };
        reader.Start();

        long index = 0;
        var carry = new byte[recordSize];
        int carried = 0;
        foreach (var (buf, length) in chunks.GetConsumingEnumerable())
        {
            int pos = 0;
            if (carried > 0)
            {
                int take = Math.Min(recordSize - carried, length);
                Array.Copy(buf, 0, carry, carried, take);
                carried += take;
                pos = take;
                if (carried == recordSize) { Parse(carry, (int)index++, raw); carried = 0; }
            }
            for (; pos + recordSize <= length && index < records; pos += recordSize) Parse(buf.AsSpan(pos, recordSize), (int)index++, raw);
            if (pos < length && index < records) { carried = length - pos; Array.Copy(buf, pos, carry, 0, carried); }
            pool.Add(buf);
            progress?.Invoke(index, records);
            if (cancel.IsCancellationRequested) break;
        }
        reader.Join();
        cancel.ThrowIfCancellationRequested();
        if (readError != null) throw new IOException(readError.Message, readError);
        return new Result(raw, vd.TotalClusters * cluster, vd.FreeClusters * cluster, cluster);
    }

    /// <summary>Puts back the last two bytes of each 512-byte block, which NTFS swaps for a check value; false if a block doesn't match (a torn write).</summary>
    private static bool Fixup(Span<byte> r)
    {
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(r[4..]), count = BinaryPrimitives.ReadUInt16LittleEndian(r[6..]);
        if (usa + count * 2 > r.Length || count == 0) return false;
        ushort check = BinaryPrimitives.ReadUInt16LittleEndian(r[usa..]);
        for (int i = 1; i < count; i++)
        {
            int at = i * 512 - 2;
            if (at + 2 > r.Length) break;
            if (BinaryPrimitives.ReadUInt16LittleEndian(r[at..]) != check) return false;
            r[at] = r[usa + 2 * i];
            r[at + 1] = r[usa + 2 * i + 1];
        }
        return true;
    }

    private static bool IsFileRecord(ReadOnlySpan<byte> r) => r[0] == (byte)'F' && r[1] == (byte)'I' && r[2] == (byte)'L' && r[3] == (byte)'E';

    /// <summary>Where the MFT is: its record's unnamed $DATA attribute's runs (cluster, length in clusters).</summary>
    private static List<(long Lcn, long Length)> MftRuns(Span<byte> r)
    {
        var runs = new List<(long, long)>();
        if (!IsFileRecord(r)) return runs;
        int a = BinaryPrimitives.ReadUInt16LittleEndian(r[0x14..]);
        while (a + 16 <= r.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(r[a..]);
            if (type == TypeEnd) break;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[(a + 4)..]);
            if (len <= 0 || a + len > r.Length) break;
            if (type == TypeData && r[a + 8] != 0 && r[a + 9] == 0)
            {
                int p = a + BinaryPrimitives.ReadUInt16LittleEndian(r[(a + 0x20)..]);
                long lcn = 0;
                while (p < a + len && r[p] != 0)
                {
                    int lenBytes = r[p] & 0xF, offBytes = r[p] >> 4;
                    p++;
                    long runLength = 0;
                    for (int i = 0; i < lenBytes; i++) runLength |= (long)r[p + i] << (8 * i);
                    p += lenBytes;
                    long delta = 0;
                    if (offBytes > 0)
                    {
                        for (int i = 0; i < offBytes; i++) delta |= (long)r[p + i] << (8 * i);
                        if ((r[p + offBytes - 1] & 0x80) != 0) delta |= -1L << (8 * offBytes); // negative
                        p += offBytes;
                        lcn += delta;
                        runs.Add((lcn, runLength));
                    }
                    // (no offset: a sparse run, which the MFT doesn't have)
                }
                break;
            }
            a += len;
        }
        return runs;
    }

    /// <summary>One file record: its name and folder, size, size on disk and time, into the base record's entry.</summary>
    private static void Parse(Span<byte> r, int number, RawEntries raw)
    {
        if (!IsFileRecord(r) || !Fixup(r)) return;
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(r[0x16..]);
        if ((flags & 1) == 0) return; // not in use
        long baseRef = (long)(BinaryPrimitives.ReadUInt64LittleEndian(r[0x20..]) & 0xFFFFFFFFFFFF);
        int target = baseRef != 0 ? (int)baseRef : number;
        if ((uint)target >= (uint)raw.Count) return;
        if (baseRef == 0) raw.Flags[target] |= (byte)(RawEntries.Used | ((flags & 2) != 0 ? RawEntries.Dir : 0));

        int used = Math.Min((int)BinaryPrimitives.ReadUInt32LittleEndian(r[0x18..]), r.Length);
        int a = BinaryPrimitives.ReadUInt16LittleEndian(r[0x14..]);
        while (a + 16 <= used)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(r[a..]);
            if (type == TypeEnd) break;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[(a + 4)..]);
            if (len < 16 || a + len > used) break;
            bool resident = r[a + 8] == 0;
            int nameLength = r[a + 9];
            switch (type)
            {
                case TypeStandardInfo when resident:
                {
                    int v = a + BinaryPrimitives.ReadUInt16LittleEndian(r[(a + 0x14)..]);
                    if (v + 0x10 <= a + len) raw.Modified[target] = BinaryPrimitives.ReadInt64LittleEndian(r[(v + 0x08)..]);
                    break;
                }
                case TypeFileName when resident:
                {
                    int v = a + BinaryPrimitives.ReadUInt16LittleEndian(r[(a + 0x14)..]);
                    if (v + 0x42 > a + len) break;
                    int chars = r[v + 0x40];
                    byte kind = r[v + 0x41]; // 0 POSIX, 1 Win32, 2 DOS (8.3), 3 Win32 and DOS
                    if (v + 0x42 + chars * 2 > a + len || chars == 0) break;
                    bool have = raw.NameLength[target] > 0;
                    if (have && !(raw.NameKind[target] == 2 && kind != 2)) break; // keep the first long name (a hard link's other names are skipped)
                    raw.Parent[target] = (int)(BinaryPrimitives.ReadUInt64LittleEndian(r[v..]) & 0xFFFFFFFFFFFF);
                    raw.NameKind[target] = kind;
                    var name = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(r.Slice(v + 0x42, chars * 2));
                    raw.SetName(target, name);
                    break;
                }
                case TypeData when nameLength == 0: // the file's contents (named ones are alternate streams)
                {
                    if (resident)
                    {
                        raw.Size[target] = BinaryPrimitives.ReadUInt32LittleEndian(r[(a + 0x10)..]);
                        raw.Alloc[target] = 0; // inside the record itself
                    }
                    else if (BinaryPrimitives.ReadInt64LittleEndian(r[(a + 0x10)..]) == 0) // the attribute's first part has the sizes
                    {
                        ushort attrFlags = BinaryPrimitives.ReadUInt16LittleEndian(r[(a + 0x0C)..]);
                        raw.Size[target] = BinaryPrimitives.ReadInt64LittleEndian(r[(a + 0x30)..]);
                        // compressed or sparse (e.g. OneDrive's files that are only online): what's really on the disk
                        raw.Alloc[target] = (attrFlags & 0x8001) != 0 && len >= 0x48
                            ? BinaryPrimitives.ReadInt64LittleEndian(r[(a + 0x40)..])
                            : BinaryPrimitives.ReadInt64LittleEndian(r[(a + 0x28)..]);
                    }
                    break;
                }
            }
            a += len;
        }
    }
}
