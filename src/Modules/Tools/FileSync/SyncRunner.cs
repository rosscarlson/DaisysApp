using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Tools.FileSync;

/// <summary>
/// Carries out a <see cref="SyncPlan"/>, in this order (as FreeFileSync does): deletions first (frees the space),
/// then new folders, then the files, each copied to a temporary name beside it and renamed over the old one when
/// it's complete, so a stopped sync never leaves half a file. A failure is noted and the sync goes on.
/// </summary>
internal sealed class SyncRunner(SyncPlan plan)
{
    public sealed class Progress
    {
        public long ItemsDone, ItemsTotal, BytesDone, BytesTotal;
        public int Copied, Updated, Deleted, FoldersMade;
        public volatile string Stage = "";
        public volatile string Current = "";
        public readonly ConcurrentQueue<(string Path, string Error)> Errors = new();
    }

    public Progress State { get; } = new();

    /// <summary>Cleared to pause (between files, and inside a big file's copy).</summary>
    public ManualResetEventSlim Running { get; } = new(true);

    private const string TempSuffix = ".daisysync~";

    /// <summary>Whether the destination's drive has a Recycle Bin (a local, fixed drive).</summary>
    public static bool HasRecycleBin(string path)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }

    public void Run(CancellationToken cancel)
    {
        var job = plan.Job;
        string src = Path.GetFullPath(job.Source), dst = Path.GetFullPath(job.Destination);
        var s = State;
        var deletes = TopMost(plan.Items.Where(i => i.Action == SyncAction.Delete).ToList());
        var folders = plan.Items.Where(i => i.Action == SyncAction.Create && i.LeftDir).OrderBy(i => i.Path.Count(c => c == '\\')).ToList();
        var files = plan.Items.Where(i => i.Action is SyncAction.Create or SyncAction.Update && !i.LeftDir).ToList();
        s.ItemsTotal = deletes.Count + folders.Count + files.Count;
        s.BytesTotal = files.Sum(i => i.LeftSize ?? 0);

        if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);

        // ---- deletions
        s.Stage = "deleting";
        bool recycle = job.RecycleBin && HasRecycleBin(dst);
        const int Batch = 100;
        for (int i = 0; i < deletes.Count; i += Batch)
        {
            Wait(cancel);
            var batch = deletes.Skip(i).Take(Batch).ToList();
            s.Current = batch[0].Path;
            if (recycle)
            {
                var paths = batch.Select(d => Path.Combine(dst, d.Path)).ToList();
                foreach (var p in paths) ClearReadOnly(p);
                Native.Recycle(paths);
                foreach (var d in batch)
                {
                    string p = Path.Combine(dst, d.Path);
                    if (File.Exists(p) || Directory.Exists(p)) Delete(p, d, recycle: true); // (one at a time, for its error)
                    else s.Deleted++;
                    s.ItemsDone++;
                }
            }
            else
                foreach (var d in batch)
                {
                    Wait(cancel);
                    s.Current = d.Path;
                    Delete(Path.Combine(dst, d.Path), d, recycle: false);
                    s.ItemsDone++;
                }
        }

        // ---- new folders
        s.Stage = "folders";
        foreach (var f in folders)
        {
            Wait(cancel);
            s.Current = f.Path;
            try { Directory.CreateDirectory(Native.LongPath(Path.Combine(dst, f.Path))); s.FoldersMade++; }
            catch (Exception ex) { Fail(f.Path, ex.Message); }
            s.ItemsDone++;
        }

        // ---- files
        s.Stage = "copying";
        foreach (var f in files)
        {
            Wait(cancel);
            s.Current = f.Path;
            long before = s.BytesDone;
            try
            {
                Copy(Path.Combine(src, f.Path), Path.Combine(dst, f.Path), cancel);
                if (f.Action == SyncAction.Update) s.Updated++; else s.Copied++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Fail(f.Path, ex.Message); }
            // (a failed or partial copy: the total still moves on by the file's size)
            Interlocked.Exchange(ref s.BytesDone, before + (f.LeftSize ?? 0));
            s.ItemsDone++;
        }
        s.Stage = "done";
        s.Current = "";
    }

    private void Wait(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (!Running.IsSet) Running.Wait(cancel);
    }

    private void Fail(string path, string error)
    {
        State.Errors.Enqueue((path, error));
        Log.Here.Error($"Sync: {path}: {error}");
    }

    /// <summary>Only the outermost of the deletions (a folder's contents go with it).</summary>
    private static List<SyncItem> TopMost(List<SyncItem> deletes)
    {
        var folders = new HashSet<string>(deletes.Where(d => d.RightDir).Select(d => d.Path), StringComparer.OrdinalIgnoreCase);
        return deletes.Where(d =>
        {
            for (int i = d.Path.LastIndexOf('\\'); i > 0; i = d.Path.LastIndexOf('\\', i - 1))
                if (folders.Contains(d.Path[..i])) return false;
            return true;
        }).ToList();
    }

    private void Delete(string path, SyncItem item, bool recycle)
    {
        try
        {
            if (recycle)
            {
                int r = Native.Recycle([path]);
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException(F("It couldn't go to the Recycle Bin (error {0}): it may be open in a program.", r));
            }
            else if (item.RightDir) { ClearReadOnlyTree(path); Directory.Delete(Native.LongPath(path), recursive: true); }
            else { ClearReadOnly(path); File.Delete(Native.LongPath(path)); }
            State.Deleted++;
        }
        catch (Exception ex) { Fail(item.Path, ex.Message); }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var lp = Native.LongPath(path);
            var a = File.GetAttributes(lp);
            if ((a & FileAttributes.ReadOnly) != 0 && (a & FileAttributes.Directory) == 0) File.SetAttributes(lp, a & ~FileAttributes.ReadOnly);
        }
        catch { }
    }

    private static void ClearReadOnlyTree(string folder)
    {
        try
        {
            foreach (var f in new DirectoryInfo(Native.LongPath(folder)).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
                if (f.IsReadOnly) f.IsReadOnly = false;
        }
        catch { }
    }

    /// <summary>One file: copied beside the old one under a temporary name (with progress, pausing and stopping), then swapped in.</summary>
    private void Copy(string from, string to, CancellationToken cancel)
    {
        string temp = to + TempSuffix, lfrom = Native.LongPath(from), lto = Native.LongPath(to), ltemp = Native.LongPath(temp);
        var s = State;
        long start = s.BytesDone;
        bool stopped = false;
        Native.CopyProgress progress = (total, done, _, _, _, _, _, _, _) =>
        {
            Interlocked.Exchange(ref s.BytesDone, start + done);
            if (!Running.IsSet)
            {
                try { Running.Wait(cancel); } catch (OperationCanceledException) { }
            }
            if (cancel.IsCancellationRequested) { stopped = true; return Native.ProgressCancel; }
            return Native.ProgressContinue;
        };
        try { if (File.Exists(ltemp)) { ClearReadOnly(temp); File.Delete(ltemp); } } catch { }
        bool ok = Native.CopyFileEx(lfrom, ltemp, progress, IntPtr.Zero, IntPtr.Zero, Native.CopyAllowDecryptedDestination);
        int error = Marshal.GetLastWin32Error();
        GC.KeepAlive(progress);
        if (!ok)
        {
            try { File.Delete(ltemp); } catch { }
            if (stopped || cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
            throw new IOException(new Win32Exception(error).Message);
        }
        // the copy keeps the source's attributes: a read-only one couldn't be renamed over, or replaced next time
        ClearReadOnly(temp);
        ClearReadOnly(to);
        if (!Native.MoveFileEx(ltemp, lto, Native.MoveReplaceExisting))
        {
            int e = Marshal.GetLastWin32Error();
            try { File.Delete(ltemp); } catch { }
            throw new IOException(new Win32Exception(e).Message);
        }
        try
        {
            var a = File.GetAttributes(lfrom);
            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(lto, File.GetAttributes(lto) | FileAttributes.ReadOnly);
        }
        catch { }
    }
}
