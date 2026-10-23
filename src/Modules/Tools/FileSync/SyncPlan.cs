using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using DaisysApp.Applets.Tools.Shared;

namespace DaisysApp.Applets.Tools.FileSync;

/// <summary>What a sync does with one file or folder.</summary>
public enum SyncAction
{
    /// <summary>The same on both sides.</summary>
    Same,
    /// <summary>Only in the source: copied.</summary>
    Create,
    /// <summary>Different: the source's copied over the destination's.</summary>
    Update,
    /// <summary>Only in the destination (Mirror): deleted.</summary>
    Delete,
    /// <summary>Only in the destination (Update): left alone.</summary>
    Keep,
    /// <summary>Different, but left alone (Update: the destination's is newer, or a file and a folder share a name).</summary>
    Skip,
}

/// <summary>One file or folder, as it is on each side, and what the sync does with it.</summary>
public sealed class SyncItem
{
    public required string Path { get; init; } // relative, e.g. "Photos\2024\a.jpg"
    public bool LeftDir { get; init; }
    public bool RightDir { get; init; }
    public long? LeftSize { get; init; }
    public long? RightSize { get; init; }
    public DateTime? LeftTime { get; init; }
    public DateTime? RightTime { get; init; }
    public SyncAction Action { get; set; }
    /// <summary>Why it's skipped, or what's different.</summary>
    public string? Note { get; set; }

    public bool InLeft => LeftSize != null || LeftDir;
    public bool InRight => RightSize != null || RightDir;
    /// <summary>The action is on a folder (made, or deleted with everything in it).</summary>
    public bool IsDir => Action == SyncAction.Delete ? RightDir : LeftDir || (!InLeft && RightDir);
}

/// <summary>What comparing two folders found: every difference, and what syncing them does.</summary>
public sealed class SyncPlan
{
    public required SyncJob Job { get; init; }
    public List<SyncItem> Items { get; } = new();
    public int LeftFiles, LeftFolders, RightFiles, RightFolders;
    public long LeftBytes, RightBytes;
    public bool DestinationMissing;
    /// <summary>Folders that couldn't be read (path and why).</summary>
    public List<(string Path, string Error)> Unreadable { get; } = new();
    public TimeSpan Took;

    /// <summary>The same comparison with only the items <paramref name="keep"/> takes (what the sync window left ticked).</summary>
    public SyncPlan Only(Func<SyncItem, bool> keep)
    {
        var p = new SyncPlan
        {
            Job = Job, LeftFiles = LeftFiles, LeftFolders = LeftFolders, RightFiles = RightFiles, RightFolders = RightFolders,
            LeftBytes = LeftBytes, RightBytes = RightBytes, DestinationMissing = DestinationMissing, Took = Took,
        };
        p.Items.AddRange(Items.Where(keep));
        p.Unreadable.AddRange(Unreadable);
        return p;
    }

    public int Count(SyncAction a, bool? dirs = null) => Items.Count(i => i.Action == a && (dirs == null || i.IsDir == dirs));
    public long BytesToCopy => Items.Where(i => i.Action is SyncAction.Create or SyncAction.Update && !i.LeftDir).Sum(i => i.LeftSize ?? 0);
    public bool NothingToDo => !Items.Any(i => i.Action is SyncAction.Create or SyncAction.Update or SyncAction.Delete);

    /// <summary>A file's time on two sides counts as the same within this (FAT drives keep times to 2 seconds).</summary>
    public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    public sealed class Progress
    {
        public long LeftFolders, LeftFiles, RightFolders, RightFiles, Compared, ToCompare;
        public volatile string Stage = "";
        public volatile string Current = "";
    }

    private sealed record Entry(bool Dir, long Size, DateTime Time);

    private sealed class Folder(string rel) { public readonly string Rel = rel; }

    /// <summary>
    /// Reads both folders (several threads each, both at once) and works out what the sync does. Excluded names are
    /// left out on both sides (never copied, never deleted).
    /// </summary>
    public static SyncPlan Compare(SyncJob job, Progress progress, CancellationToken cancel)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var plan = new SyncPlan { Job = job.Clone() };
        var filter = new Excluder(job.Excludes);
        string src = Path.GetFullPath(job.Source), dst = Path.GetFullPath(job.Destination);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException(F("The source folder {0} isn't there.", src));
        plan.DestinationMissing = !Directory.Exists(dst);
        progress.Stage = "reading";
        Dictionary<string, Entry>? left = null, right = null;
        var unreadable = new ConcurrentBag<(string, string)>();
        Parallel.Invoke(
            () => left = Read(src, filter, progress, true, unreadable, cancel),
            () => right = plan.DestinationMissing ? new(StringComparer.OrdinalIgnoreCase) : Read(dst, filter, progress, false, unreadable, cancel));
        cancel.ThrowIfCancellationRequested();
        plan.Unreadable.AddRange(unreadable);
        foreach (var e in left!.Values) { if (e.Dir) plan.LeftFolders++; else { plan.LeftFiles++; plan.LeftBytes += e.Size; } }
        foreach (var e in right!.Values) { if (e.Dir) plan.RightFolders++; else { plan.RightFiles++; plan.RightBytes += e.Size; } }

        progress.Stage = "comparing";
        bool mirror = job.Mode == SyncMode.Mirror;
        var contentChecks = new List<SyncItem>();
        foreach (var (path, l) in left)
        {
            right.TryGetValue(path, out var r);
            var item = new SyncItem
            {
                Path = path, LeftDir = l.Dir, RightDir = r?.Dir == true,
                LeftSize = l.Dir ? null : l.Size, RightSize = r == null || r.Dir ? null : r.Size,
                LeftTime = l.Dir ? null : l.Time, RightTime = r == null || r.Dir ? null : r.Time,
            };
            if (r == null) item.Action = SyncAction.Create;
            else if (l.Dir && r.Dir) item.Action = SyncAction.Same;
            else if (l.Dir != r.Dir)
            {
                if (mirror)
                {
                    // the destination's file (or folder) of that name goes, then the source's is copied
                    plan.Items.Add(new SyncItem { Path = path, RightDir = r.Dir, RightSize = r.Dir ? null : r.Size, RightTime = r.Dir ? null : r.Time, Action = SyncAction.Delete });
                    item = new SyncItem { Path = path, LeftDir = l.Dir, LeftSize = item.LeftSize, LeftTime = item.LeftTime, Action = SyncAction.Create };
                }
                else
                {
                    item.Action = SyncAction.Skip;
                    item.Note = l.Dir ? T("The destination has a file with this folder's name") : T("The destination has a folder with this file's name");
                }
            }
            else
            {
                bool sameSize = l.Size == r.Size;
                var dt = l.Time - r.Time;
                bool sameTime = dt.Duration() <= TimeTolerance;
                // whichever of size, time and contents are ticked (none: the name alone, being on both sides, is enough)
                if (job.CompareSize && !sameSize) Differ(item, l, r, job, T("A different size"));
                else if (job.CompareTime && !sameTime) Differ(item, l, r, job, dt > TimeSpan.Zero ? T("The source's is newer") : T("The destination's is newer"));
                else if (job.CompareContent)
                {
                    if (!sameSize) Differ(item, l, r, job, T("Different contents"));
                    else contentChecks.Add(item); // decided below, by reading both
                }
                else item.Action = SyncAction.Same;
            }
            plan.Items.Add(item);
        }
        foreach (var (path, r) in right)
        {
            if (left.ContainsKey(path)) continue;
            plan.Items.Add(new SyncItem
            {
                Path = path, RightDir = r.Dir, RightSize = r.Dir ? null : r.Size, RightTime = r.Dir ? null : r.Time,
                Action = mirror ? SyncAction.Delete : SyncAction.Keep,
            });
        }

        if (contentChecks.Count > 0)
        {
            progress.Stage = "content";
            progress.ToCompare = contentChecks.Count;
            Parallel.ForEach(contentChecks, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancel }, item =>
            {
                progress.Current = item.Path;
                bool same;
                try { same = SameContent(Path.Combine(src, item.Path), Path.Combine(dst, item.Path), cancel); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { same = false; item.Note = ex.Message; }
                if (same) item.Action = SyncAction.Same;
                else
                {
                    var l = new Entry(false, item.LeftSize ?? 0, item.LeftTime ?? default);
                    var r = new Entry(false, item.RightSize ?? 0, item.RightTime ?? default);
                    Differ(item, l, r, job, item.Note ?? T("Different contents"));
                }
                Interlocked.Increment(ref progress.Compared);
            });
        }

        plan.Items.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        plan.Took = sw.Elapsed;
        return plan;
    }

    private static void Differ(SyncItem item, Entry l, Entry r, SyncJob job, string note)
    {
        item.Note = note;
        // Update leaves a destination file that's newer, unless the times aren't being trusted (Date and time not ticked)
        if (job.Mode == SyncMode.Mirror || !job.CompareTime || l.Time - r.Time > -TimeTolerance) item.Action = SyncAction.Update;
        else { item.Action = SyncAction.Skip; item.Note = T("The destination's is newer: Update mode leaves it"); }
    }

    private static Dictionary<string, Entry> Read(string root, Excluder filter, Progress progress, bool source, ConcurrentBag<(string, string)> unreadable, CancellationToken cancel)
    {
        int workers = Math.Max(2, FastWalk.DefaultWorkers / 2);
        var parts = Enumerable.Range(0, workers).Select(_ => new List<(string, Entry)>()).ToArray();
        FastWalk.Run(root, new Folder(""), (int worker, Folder parent, string _, ref Native.FindData fd) =>
        {
            string name = fd.Name.ToString();
            string rel = parent.Rel.Length == 0 ? name : parent.Rel + "\\" + name;
            bool dir = fd.IsDirectory;
            if (filter.Excludes(rel, name, dir)) return null;
            parts[worker].Add((rel, new Entry(dir, dir ? 0 : fd.Size, Native.FromFileTime(fd.LastWriteTime))));
            if (source) Interlocked.Increment(ref dir ? ref progress.LeftFolders : ref progress.LeftFiles);
            else Interlocked.Increment(ref dir ? ref progress.RightFolders : ref progress.RightFiles);
            return dir && !fd.IsReparsePoint ? new Folder(rel) : null;
        }, workers, cancel, (f, path, error) => unreadable.Add((f.Rel.Length == 0 ? root : f.Rel, new System.ComponentModel.Win32Exception(error).Message)));
        var all = new Dictionary<string, Entry>(parts.Sum(p => p.Count), StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts) foreach (var (k, v) in p) all[k] = v;
        return all;
    }

    private static bool SameContent(string a, string b, CancellationToken cancel)
    {
        const int Block = 1 << 20;
        using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Block, FileOptions.SequentialScan);
        using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Block, FileOptions.SequentialScan);
        if (fa.Length != fb.Length) return false;
        var ba = new byte[Block];
        var bb = new byte[Block];
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            int na = fa.ReadAtLeast(ba, Block, throwOnEndOfStream: false);
            int nb = fb.ReadAtLeast(bb, Block, throwOnEndOfStream: false);
            if (na != nb) return false;
            if (na == 0) return true;
            if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
        }
    }
}

/// <summary>
/// The Exclude list: names or paths separated by ";" (or new lines), with * and ? wildcards. A pattern without a "\"
/// is matched against the name; with one, against the path from the top folder. A trailing "\" means folders only.
/// </summary>
public sealed class Excluder
{
    private readonly List<(Regex Pattern, bool OnPath, bool DirsOnly)> rules = new();

    public Excluder(string? patterns)
    {
        foreach (var raw in (patterns ?? "").Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string p = raw.Replace('/', '\\');
            bool dirsOnly = p.EndsWith('\\');
            p = p.Trim('\\');
            if (p.Length == 0) continue;
            bool onPath = p.Contains('\\');
            var rx = new Regex("^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            rules.Add((rx, onPath, dirsOnly));
        }
    }

    public bool Excludes(string relPath, string name, bool dir)
    {
        foreach (var (rx, onPath, dirsOnly) in rules)
        {
            if (dirsOnly && !dir) continue;
            if (rx.IsMatch(onPath ? relPath : name)) return true;
        }
        return false;
    }
}
