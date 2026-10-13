using System.IO;
using System.Runtime.InteropServices;

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>
/// Entries as a scan finds them, before they're a tree: in any order, each pointing to its folder by index. Kept as
/// plain arrays (no object per file), so millions of entries are cheap. A file table scan fills one indexed by file
/// record number (some unused); a folder scan fills one per thread and joins them.
/// </summary>
internal sealed class RawEntries
{
    public const byte Used = 1, Dir = 2, Link = 4;

    public int Count;
    public int[] Parent;
    public byte[] Flags;
    public byte[] NameKind; // the file table's name namespace (a DOS 8.3 name is replaced by the long one)
    public long[] Size, Alloc, Modified;
    public int[] NameStart;
    public ushort[] NameLength;
    public char[] Names = new char[1 << 16];
    public int NamesUsed;

    public RawEntries(int capacity)
    {
        capacity = Math.Max(16, capacity);
        Parent = new int[capacity];
        Flags = new byte[capacity];
        NameKind = new byte[capacity];
        Size = new long[capacity];
        Alloc = new long[capacity];
        Modified = new long[capacity];
        NameStart = new int[capacity];
        NameLength = new ushort[capacity];
    }

    public int Capacity => Parent.Length;

    public void Grow(int needed)
    {
        if (needed <= Capacity) return;
        int n = Math.Max(needed, Capacity * 2);
        Array.Resize(ref Parent, n);
        Array.Resize(ref Flags, n);
        Array.Resize(ref NameKind, n);
        Array.Resize(ref Size, n);
        Array.Resize(ref Alloc, n);
        Array.Resize(ref Modified, n);
        Array.Resize(ref NameStart, n);
        Array.Resize(ref NameLength, n);
    }

    public void SetName(int i, ReadOnlySpan<char> name)
    {
        if (NamesUsed + name.Length > Names.Length) Array.Resize(ref Names, Math.Max(Names.Length * 2, NamesUsed + name.Length));
        name.CopyTo(Names.AsSpan(NamesUsed));
        NameStart[i] = NamesUsed;
        NameLength[i] = (ushort)Math.Min(name.Length, ushort.MaxValue);
        NamesUsed += name.Length;
    }

    /// <summary>Adds an entry at the end; returns its index.</summary>
    public int Add(int parent, ReadOnlySpan<char> name, byte flags, long size, long alloc, long modified)
    {
        int i = Count++;
        Grow(Count);
        Parent[i] = parent;
        Flags[i] = (byte)(flags | Used);
        Size[i] = size;
        Alloc[i] = alloc;
        Modified[i] = modified;
        SetName(i, name);
        return i;
    }

    /// <summary>Joins several (one per thread), whose Parent values are <c>part &lt;&lt; PartShift | index</c>.</summary>
    public const int PartShift = 26;

    public static RawEntries Join(IReadOnlyList<RawEntries> parts)
    {
        var offsets = new int[parts.Count];
        int total = 0;
        for (int p = 0; p < parts.Count; p++) { offsets[p] = total; total += parts[p].Count; }
        var all = new RawEntries(total) { Count = total, Names = new char[Math.Max(16, parts.Sum(x => x.NamesUsed))] };
        foreach (var (part, p) in parts.Select((x, i) => (x, i)))
        {
            int o = offsets[p], n = part.Count;
            for (int i = 0; i < n; i++)
            {
                int g = part.Parent[i];
                all.Parent[o + i] = g < 0 ? -1 : offsets[g >> PartShift] + (g & ((1 << PartShift) - 1));
            }
            Array.Copy(part.Flags, 0, all.Flags, o, n);
            Array.Copy(part.Size, 0, all.Size, o, n);
            Array.Copy(part.Alloc, 0, all.Alloc, o, n);
            Array.Copy(part.Modified, 0, all.Modified, o, n);
            for (int i = 0; i < n; i++) all.NameStart[o + i] = part.NameStart[i] + all.NamesUsed;
            Array.Copy(part.NameLength, 0, all.NameLength, o, n);
            Array.Copy(part.Names, 0, all.Names, all.NamesUsed, part.NamesUsed);
            all.NamesUsed += part.NamesUsed;
        }
        return all;
    }
}

/// <summary>
/// A scanned folder tree: every folder and file under a root, with each folder's total size, size on disk, files and
/// folders. Entries are numbered root first, then level by level, so a folder's entries are side by side
/// (<see cref="FirstChild"/>, <see cref="ChildCount"/>), largest first.
/// </summary>
internal sealed class DiskTree
{
    public int Count;
    public int[] Parent = [], FirstChild = [], ChildCount = [], Files = [], Folders = [];
    public long[] Size = [], Alloc = [], Modified = [];
    public byte[] Flags = [];
    public int[] NameStart = [];
    public ushort[] NameLength = [];
    public char[] Names = [];

    /// <summary>Removed with Delete (still in the arrays, sizes taken off its folders).</summary>
    public const byte Deleted = 8;

    /// <summary>The scan's root: a drive ("C:\") or a folder's full path.</summary>
    public string RootPath = "";
    public string Source = ""; // how it was read: "mft" or "folders"
    public long VolumeTotal, VolumeFree, ClusterSize;

    public bool IsDir(int i) => (Flags[i] & RawEntries.Dir) != 0;
    public bool IsLink(int i) => (Flags[i] & RawEntries.Link) != 0;
    public bool IsDeleted(int i) => (Flags[i] & Deleted) != 0;

    public string Name(int i) => i == 0 ? RootPath : new string(Names, NameStart[i], NameLength[i]);
    public ReadOnlySpan<char> NameSpan(int i) => Names.AsSpan(NameStart[i], NameLength[i]);

    public string PathOf(int i)
    {
        if (i == 0) return RootPath;
        var parts = new List<int>();
        for (int n = i; n > 0; n = Parent[n]) parts.Add(n);
        var sb = new System.Text.StringBuilder(RootPath.TrimEnd('\\'));
        for (int k = parts.Count - 1; k >= 0; k--) sb.Append('\\').Append(Names, NameStart[parts[k]], NameLength[parts[k]]);
        return sb.ToString();
    }

    /// <summary>The entry for a path under the root (case doesn't matter), or −1.</summary>
    public int Find(string path)
    {
        string root = RootPath.TrimEnd('\\');
        path = path.TrimEnd('\\');
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return 0;
        if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return -1;
        int at = 0;
        foreach (var part in path[(root.Length + 1)..].Split('\\'))
        {
            int found = -1;
            for (int c = FirstChild[at], end = c + ChildCount[at]; c < end; c++)
                if (NameSpan(c).Equals(part, StringComparison.OrdinalIgnoreCase)) { found = c; break; }
            if (found < 0) return -1;
            at = found;
        }
        return at;
    }

    /// <summary>
    /// Makes the tree from <paramref name="raw"/>, starting at entry <paramref name="root"/>: links each entry to its
    /// folder, adds up the folders, sorts each folder's entries largest first and numbers them root first. Entries not
    /// under the root (or unused) are left out.
    /// </summary>
    public static DiskTree Build(RawEntries raw, int root, string rootPath, CancellationToken cancel = default)
    {
        int n = raw.Count;
        // children of each entry (any order), as lists in one array
        var childCount = new int[n];
        for (int i = 0; i < n; i++)
        {
            if ((raw.Flags[i] & RawEntries.Used) == 0 || i == root) continue;
            int p = raw.Parent[i];
            if (p < 0 || p >= n || p == i || (raw.Flags[p] & (RawEntries.Used | RawEntries.Dir)) != (RawEntries.Used | RawEntries.Dir)) continue;
            childCount[p]++;
        }
        var start = new int[n + 1];
        for (int i = 0; i < n; i++) start[i + 1] = start[i] + childCount[i];
        var fill = (int[])start.Clone();
        var children = new int[start[n]];
        for (int i = 0; i < n; i++)
        {
            if ((raw.Flags[i] & RawEntries.Used) == 0 || i == root) continue;
            int p = raw.Parent[i];
            if (p < 0 || p >= n || p == i || (raw.Flags[p] & (RawEntries.Used | RawEntries.Dir)) != (RawEntries.Used | RawEntries.Dir)) continue;
            children[fill[p]++] = i;
        }
        cancel.ThrowIfCancellationRequested();

        // the entries under the root, level by level (a folder before its entries)
        var order = new int[n];
        var seen = new bool[n];
        int count = 0;
        order[count++] = root;
        seen[root] = true;
        for (int k = 0; k < count; k++)
        {
            int e = order[k];
            for (int c = start[e]; c < start[e + 1]; c++)
            {
                int ch = children[c];
                if (seen[ch]) continue; // (a loop in a damaged file table)
                seen[ch] = true;
                order[count++] = ch;
            }
        }
        cancel.ThrowIfCancellationRequested();

        // totals, deepest first
        var size = new long[n];
        var alloc = new long[n];
        var modified = new long[n];
        var files = new int[n];
        var folders = new int[n];
        for (int k = count - 1; k >= 0; k--)
        {
            int e = order[k];
            bool dir = (raw.Flags[e] & RawEntries.Dir) != 0;
            if (!dir) { size[e] = raw.Size[e]; alloc[e] = raw.Alloc[e]; files[e] = 1; }
            else { size[e] += raw.Size[e]; alloc[e] += raw.Alloc[e]; }
            modified[e] = Math.Max(modified[e], raw.Modified[e]);
            if (k == 0) break;
            int p = raw.Parent[e];
            size[p] += size[e];
            alloc[p] += alloc[e];
            files[p] += files[e];
            folders[p] += folders[e] + (dir ? 1 : 0);
            if (modified[e] > modified[p]) modified[p] = modified[e];
        }
        cancel.ThrowIfCancellationRequested();

        // each folder's entries, largest first
        var keys = new long[children.Length];
        for (int i = 0; i < children.Length; i++) keys[i] = -size[children[i]];
        for (int k = 0; k < count; k++)
        {
            int e = order[k], len = start[e + 1] - start[e];
            if (len > 1) Array.Sort(keys, children, start[e], len);
        }

        // numbered again, root first, a folder's entries side by side
        var t = new DiskTree
        {
            Count = count, RootPath = rootPath,
            Parent = new int[count], FirstChild = new int[count], ChildCount = new int[count], Files = new int[count], Folders = new int[count],
            Size = new long[count], Alloc = new long[count], Modified = new long[count], Flags = new byte[count],
            NameStart = new int[count], NameLength = new ushort[count],
        };
        var newIndex = new int[n];
        var queue = new int[count];
        int head = 0, tail = 0, next = 1;
        queue[tail++] = root;
        newIndex[root] = 0;
        t.Parent[0] = -1;
        long namesLength = 0;
        while (head < tail)
        {
            int e = queue[head++], ni = newIndex[e];
            t.FirstChild[ni] = next;
            int kids = 0;
            for (int c = start[e]; c < start[e + 1]; c++)
            {
                int ch = children[c];
                if (!seen[ch]) continue;
                seen[ch] = false; // (each once)
                newIndex[ch] = next;
                t.Parent[next] = ni;
                next++;
                kids++;
                queue[tail++] = ch;
            }
            t.ChildCount[ni] = kids;
            t.Size[ni] = size[e];
            t.Alloc[ni] = alloc[e];
            t.Modified[ni] = modified[e];
            t.Files[ni] = files[e];
            t.Folders[ni] = folders[e];
            t.Flags[ni] = raw.Flags[e];
            namesLength += raw.NameLength[e];
        }
        t.Count = next;
        t.Names = new char[namesLength];
        int at = 0;
        for (int k = 0; k < tail; k++)
        {
            int e = queue[k], ni = newIndex[e], len = raw.NameLength[e];
            Array.Copy(raw.Names, raw.NameStart[e], t.Names, at, len);
            t.NameStart[ni] = at;
            t.NameLength[ni] = (ushort)len;
            at += len;
        }
        return t;
    }

    /// <summary>
    /// Takes a deleted entry out: marked deleted (the views skip it) and its size taken off every folder above it.
    /// </summary>
    public void Remove(int i)
    {
        if (i <= 0 || IsDeleted(i)) return;
        Flags[i] |= Deleted;
        long s = Size[i], a = Alloc[i];
        int f = Files[i], d = Folders[i] + (IsDir(i) ? 1 : 0);
        for (int p = Parent[i]; p >= 0; p = Parent[p])
        {
            Size[p] -= s; Alloc[p] -= a; Files[p] -= f; Folders[p] -= d;
        }
    }

    // ---------------------------------------------------------------- saved as a file (from the scan run as administrator)

    private const int Magic = 0x45525444; // "DTRE"

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Count);
        w.Write(Names.Length);
        w.Write(RootPath);
        w.Write(Source);
        w.Write(VolumeTotal);
        w.Write(VolumeFree);
        w.Write(ClusterSize);
        void Ints(int[] a) => stream.Write(MemoryMarshal.AsBytes(a.AsSpan(0, Count)));
        void Longs(long[] a) => stream.Write(MemoryMarshal.AsBytes(a.AsSpan(0, Count)));
        Ints(Parent); Ints(FirstChild); Ints(ChildCount); Ints(Files); Ints(Folders);
        Longs(Size); Longs(Alloc); Longs(Modified);
        stream.Write(Flags.AsSpan(0, Count));
        Ints(NameStart);
        stream.Write(MemoryMarshal.AsBytes(NameLength.AsSpan(0, Count)));
        stream.Write(MemoryMarshal.AsBytes(Names.AsSpan()));
    }

    public static DiskTree Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != Magic) throw new InvalidDataException("Not a scan");
        int count = r.ReadInt32(), names = r.ReadInt32();
        var t = new DiskTree { Count = count, RootPath = r.ReadString(), Source = r.ReadString(), VolumeTotal = r.ReadInt64(), VolumeFree = r.ReadInt64(), ClusterSize = r.ReadInt64() };
        int[] Ints() { var a = new int[count]; stream.ReadExactly(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
        long[] Longs() { var a = new long[count]; stream.ReadExactly(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
        t.Parent = Ints(); t.FirstChild = Ints(); t.ChildCount = Ints(); t.Files = Ints(); t.Folders = Ints();
        t.Size = Longs(); t.Alloc = Longs(); t.Modified = Longs();
        t.Flags = new byte[count]; stream.ReadExactly(t.Flags);
        t.NameStart = Ints();
        t.NameLength = new ushort[count]; stream.ReadExactly(MemoryMarshal.AsBytes(t.NameLength.AsSpan()));
        t.Names = new char[names]; stream.ReadExactly(MemoryMarshal.AsBytes(t.Names.AsSpan()));
        return t;
    }
}
