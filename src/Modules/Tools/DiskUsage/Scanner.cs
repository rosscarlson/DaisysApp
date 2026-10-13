using System.Diagnostics;
using System.IO;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>
/// Scans a drive or folder into a <see cref="DiskTree"/>: from the drive's file table when it's NTFS and administrator
/// rights are there (or given: Windows asks, and a second copy of the app reads it, <see cref="FileTableTask"/>), else
/// folder by folder with several threads (<see cref="FastWalk"/>).
/// </summary>
internal static class Scanner
{
    /// <summary>What a scan is doing, for the progress line.</summary>
    public sealed class Progress
    {
        public volatile string Stage = "";
        public long Files, Folders, Bytes;
        public long Records, RecordsTotal;
    }

    public sealed record Outcome(DiskTree Tree, TimeSpan Took, string? Note);

    private sealed class Folder(int id) { public readonly int Id = id; }

    /// <summary>Folder by folder, several at once: no special rights needed, works on any drive or share.</summary>
    public static DiskTree ScanFolders(string root, Progress progress, CancellationToken cancel)
    {
        progress.Stage = "folders";
        int workers = FastWalk.DefaultWorkers;
        var parts = Enumerable.Range(0, workers).Select(_ => new RawEntries(1 << 14)).ToArray();
        int rootId = parts[0].Add(-1, root, RawEntries.Dir, 0, 0, 0);
        long cluster = ClusterSize(root);
        FastWalk.Run(root, new Folder(rootId), (int worker, Folder parent, string _, ref Native.FindData fd) =>
        {
            var part = parts[worker];
            bool dir = fd.IsDirectory;
            long size = dir ? 0 : fd.Size;
            long alloc = dir || fd.IsOnlineOnly ? 0 : (size + cluster - 1) / cluster * cluster;
            byte flags = (byte)((dir ? RawEntries.Dir : 0) | (fd.IsReparsePoint ? RawEntries.Link : 0));
            int local = part.Add(parent.Id, fd.Name, flags, size, alloc, fd.LastWriteTime);
            int id = (worker << RawEntries.PartShift) | local;
            if (dir) Interlocked.Increment(ref progress.Folders);
            else { Interlocked.Increment(ref progress.Files); Interlocked.Add(ref progress.Bytes, size); }
            return dir ? new Folder(id) : null;
        }, workers, cancel);
        cancel.ThrowIfCancellationRequested();
        progress.Stage = "building";
        var raw = RawEntries.Join(parts);
        var tree = DiskTree.Build(raw, 0, root, cancel);
        tree.Source = "folders";
        tree.ClusterSize = cluster;
        Volume(root, tree);
        return tree;
    }

    /// <summary>From the drive's file table, in this process (which has to be running as administrator).</summary>
    public static DiskTree ScanFileTable(string drive, Progress progress, CancellationToken cancel)
    {
        progress.Stage = "table";
        var r = MftReader.Read(drive, (n, all) => { progress.Records = n; progress.RecordsTotal = all; }, cancel);
        progress.Stage = "building";
        var tree = DiskTree.Build(r.Entries, MftReader.RootRecord, drive.TrimEnd('\\') + "\\", cancel);
        tree.Source = "mft";
        tree.VolumeTotal = r.VolumeTotal;
        tree.VolumeFree = r.VolumeFree;
        tree.ClusterSize = r.ClusterSize;
        return tree;
    }

    /// <summary>
    /// Scans <paramref name="path"/>: the drive's file table when <paramref name="useTable"/> and it's an NTFS drive on
    /// this PC (as administrator: asked for if needed), else folder by folder. A folder from the table is the whole
    /// drive's table, shown from that folder.
    /// </summary>
    public static async Task<Outcome> ScanAsync(string path, bool useTable, Progress progress, CancellationToken cancel)
    {
        var sw = Stopwatch.StartNew();
        path = Path.GetFullPath(path);
        string? note = null;
        if (useTable && TableDrive(path) is { } drive)
        {
            try
            {
                DiskTree? whole = ModuleTasks.IsElevated
                    ? await Task.Run(() => ScanFileTable(drive, progress, cancel), cancel)
                    : await ScanElevatedAsync(drive, progress, cancel);
                if (whole != null)
                {
                    var tree = path.TrimEnd('\\').Length <= 2 ? whole : Narrow(whole, path);
                    if (tree != null) return new Outcome(tree, sw.Elapsed, null);
                    note = T("The folder wasn't in the drive's file table (it may be a link to somewhere else), so it was read folder by folder.");
                }
                else note = T("Without administrator permission the drive was read folder by folder (slower).");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Here.Warn($"Reading {drive}'s file table failed, reading folders instead: {ex.Message}");
                note = F("The drive's file table couldn't be read ({0}), so it was read folder by folder.", ex.Message);
            }
            progress.Files = progress.Folders = progress.Bytes = 0;
        }
        var folders = await Task.Run(() => ScanFolders(path, progress, cancel), cancel);
        return new Outcome(folders, sw.Elapsed, note);
    }

    /// <summary>A drive's whole tree, shown from one folder: the folder becomes the root (entries renumbered).</summary>
    private static DiskTree? Narrow(DiskTree whole, string path)
    {
        int at = whole.Find(path);
        if (at < 0 || !whole.IsDir(at)) return null;
        // (rebuilt as entries so the folder's tree is numbered from 0, as a folder scan's is)
        var raw = new RawEntries(whole.Count) { Count = whole.Count };
        for (int i = 0; i < whole.Count; i++)
        {
            raw.Parent[i] = whole.Parent[i];
            raw.Flags[i] = (byte)(whole.Flags[i] | RawEntries.Used);
            if (whole.IsDir(i)) { raw.Size[i] = 0; raw.Alloc[i] = 0; }
            else { raw.Size[i] = whole.Size[i]; raw.Alloc[i] = whole.Alloc[i]; }
            raw.Modified[i] = whole.IsDir(i) ? 0 : whole.Modified[i];
            raw.NameStart[i] = whole.NameStart[i];
            raw.NameLength[i] = whole.NameLength[i];
        }
        raw.Names = whole.Names;
        raw.NamesUsed = whole.Names.Length;
        var tree = DiskTree.Build(raw, at, path.TrimEnd('\\'));
        tree.Source = whole.Source;
        tree.VolumeTotal = whole.VolumeTotal;
        tree.VolumeFree = whole.VolumeFree;
        tree.ClusterSize = whole.ClusterSize;
        return tree;
    }

    /// <summary>"C:" when the path is on a local NTFS drive, else null.</summary>
    public static string? TableDrive(string path)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            var root = Path.GetPathRoot(path);
            if (root == null || root.Length < 2 || root[1] != ':') return null;
            var d = new DriveInfo(root);
            return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable && d.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)
                ? root[..2] : null;
        }
        catch { return null; }
    }

    /// <summary>The drive's table read by a second copy of the app as administrator (Windows asks); null if the user said no.</summary>
    private static async Task<DiskTree?> ScanElevatedAsync(string drive, Progress progress, CancellationToken cancel)
    {
        progress.Stage = "elevating";
        string file = Path.Combine(Path.GetTempPath(), $"DaisysApp-scan-{Guid.NewGuid():N}.bin");
        try
        {
            int? code = await ModuleTasks.RunAsync<FileTableTask>([drive, file], elevated: true, cancel);
            if (code == null) return null;
            if (code != 0)
            {
                string why = File.Exists(file + ".error") ? File.ReadAllText(file + ".error") : F("error {0}", code);
                throw new IOException(why);
            }
            progress.Stage = "loading";
            return await Task.Run(() =>
            {
                using var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                return DiskTree.Read(f);
            }, cancel);
        }
        finally
        {
            try { File.Delete(file); } catch { }
            try { File.Delete(file + ".error"); } catch { }
            // stopped while the other copy still reads: its file comes later (and is big), so it's deleted when it does
            if (cancel.IsCancellationRequested) _ = Task.Run(async () =>
            {
                for (int i = 0; i < 90; i++)
                {
                    await Task.Delay(2000);
                    try { if (File.Exists(file)) { File.Delete(file); return; } } catch { }
                    try { File.Delete(file + ".error"); } catch { }
                }
            });
        }
    }

    private static long ClusterSize(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (root != null && GetDiskFreeSpace(root, out uint spc, out uint bps, out _, out _)) return Math.Max(512, (long)spc * bps);
        }
        catch { }
        return 4096;
    }

    private static void Volume(string path, DiskTree tree)
    {
        try
        {
            if (GetDiskFreeSpaceEx(path, out _, out ulong total, out ulong free)) { tree.VolumeTotal = (long)total; tree.VolumeFree = (long)free; }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetDiskFreeSpaceW")]
    private static extern bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetDiskFreeSpaceExW")]
    private static extern bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);
}

/// <summary>
/// Run as administrator by a second copy of the app (<see cref="ModuleTasks"/>): reads a drive's file table and writes
/// the tree to a file. Arguments: the drive ("C:") and the file; a failure's reason goes to the file + ".error".
/// </summary>
public sealed class FileTableTask : IModuleTask
{
    public int Run(string[] args)
    {
        if (args.Length < 2 || args[0].Length != 2 || args[0][1] != ':') return 2;
        string file = args[1];
        try
        {
            // (only its own temporary file, in the user's temporary folder)
            if (!Path.GetFileName(file).StartsWith("DaisysApp-scan-", StringComparison.Ordinal) ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(file))?.TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return 3;
            var sw = Stopwatch.StartNew();
            var tree = Scanner.ScanFileTable(args[0], new Scanner.Progress(), CancellationToken.None);
            using (var f = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20)) tree.Write(f);
            Log.Here.Info($"Read {args[0]}'s file table as administrator: {tree.Count:#,0} entries in {sw.ElapsedMilliseconds} ms");
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(file + ".error", ex.Message); } catch { }
            Log.Here.Error($"Reading {args[0]}'s file table failed", ex);
            return 1;
        }
    }
}
