using System.Collections.Concurrent;
using System.IO;

namespace DaisysApp.Applets.Tools.Shared;

/// <summary>
/// Reads a folder tree with several threads at once (FindFirstFileEx with large fetches, the basic information only),
/// which is much faster than one folder after another, especially on SSDs and network shares. For every entry
/// <see cref="Visit"/> is called on one of the threads; it returns the state to give the entry's own entries, or null
/// not to go into it. Links to other folders (junctions, symbolic links) are reported but not followed.
/// </summary>
internal static class FastWalk
{
    /// <param name="worker">Which thread (0 to workers − 1): for per-thread collections.</param>
    /// <param name="parent">The state of the folder the entry is in.</param>
    /// <param name="folder">That folder's path (in \\?\ form).</param>
    public delegate TState? Visit<TState>(int worker, TState parent, string folder, ref Native.FindData entry) where TState : class;

    /// <summary>A folder that couldn't be read (access denied, gone).</summary>
    public delegate void Failed<TState>(TState folderState, string folder, int error);

    public static int DefaultWorkers => Math.Clamp(Environment.ProcessorCount, 2, 16);

    public static void Run<TState>(string root, TState rootState, Visit<TState> visit, int workers, CancellationToken cancel, Failed<TState>? failed = null)
        where TState : class
    {
        var work = new ConcurrentStack<(TState State, string Path)>();
        work.Push((rootState, Native.LongPath(root.TrimEnd('\\') + (root.EndsWith(":\\") || root.EndsWith(':') ? "\\" : ""))));
        int pending = 1; // folders pushed and not finished
        Exception? error = null;

        void Work(int worker)
        {
            var spin = new SpinWait();
            while (Volatile.Read(ref pending) > 0 && error == null)
            {
                if (cancel.IsCancellationRequested) return;
                if (!work.TryPop(out var item)) { spin.SpinOnce(); continue; }
                spin.Reset();
                try { ReadFolder(worker, item.State, item.Path); }
                catch (Exception ex) { error ??= ex; }
                finally { Interlocked.Decrement(ref pending); }
            }
        }

        void ReadFolder(int worker, TState state, string path)
        {
            var h = Native.FindFirst(path, out var fd);
            if (h == Native.Invalid)
            {
                int e = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (e != 2 && e != 18) failed?.Invoke(state, path, e); // (not found / no files: an empty folder)
                return;
            }
            try
            {
                string prefix = path.EndsWith('\\') ? path : path + "\\";
                do
                {
                    if (fd.IsDots) continue;
                    var child = visit(worker, state, path, ref fd);
                    if (child != null && fd.IsDirectory && !fd.IsReparsePoint)
                    {
                        Interlocked.Increment(ref pending);
                        work.Push((child, prefix + fd.Name.ToString()));
                    }
                } while (Native.FindNextFile(h, out fd));
            }
            finally { Native.FindClose(h); }
        }

        var threads = Enumerable.Range(0, Math.Max(1, workers)).Select(i => new Thread(() => Work(i)) { IsBackground = true, Name = "Folder reader " + i }).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
        if (error != null) throw new IOException(error.Message, error);
    }
}
