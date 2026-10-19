using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Logging;

/// <summary>
/// Notices the window not responding (the UI thread busy or stuck), as Windows does: every 2 seconds it asks the window
/// for an answer, waiting up to 5. It logs when the window stops answering and when it answers again, and, the first
/// time it has gone 10 seconds without answering, saves a dump of the app to the logs folder
/// (<c>DaisysApp-hang-….dmp</c>, the newest two kept), which shows what the UI thread was doing
/// (<c>dotnet-dump analyze</c>, then <c>clrstack</c> on thread 0).
/// </summary>
public static class HangWatch
{
    private const int Probe = 5000, Every = 2000;
    private static int started;

    public static void Start(IntPtr window)
    {
        if (window == IntPtr.Zero || Interlocked.Exchange(ref started, 1) == 1) return;
        new Thread(() => Run(window)) { IsBackground = true, Name = "DaisysApp-HangWatch", Priority = ThreadPriority.AboveNormal }.Start();
    }

    private static void Run(IntPtr window)
    {
        var since = (Stopwatch?)null;
        bool dumped = false;
        while (IsWindow(window))
        {
            bool answered = SendMessageTimeout(window, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, 0x0002 /* SMTO_ABORTIFHUNG */, Probe, out _) != IntPtr.Zero;
            if (answered)
            {
                if (since != null) Log.App.Warn($"The window is answering again, after {since.Elapsed.TotalSeconds:0} s not responding");
                since = null;
            }
            else if (since == null)
            {
                since = Stopwatch.StartNew();
                Log.App.Warn("The window isn't responding (the UI thread has been busy for 5 s or more)");
            }
            else if (!dumped && since.ElapsedMilliseconds >= 10000)
            {
                dumped = true;
                Dump();
            }
            Thread.Sleep(Every);
        }
    }

    private static void Dump()
    {
        try
        {
            string folder = Log.Folder;
            foreach (var old in new DirectoryInfo(folder).GetFiles($"{AppPaths.ShortName}-hang-*.dmp").OrderByDescending(f => f.LastWriteTime).Skip(1))
                try { old.Delete(); } catch { }
            string path = Path.Combine(folder, $"{AppPaths.ShortName}-hang-{DateTime.Now:yyyy-MM-dd-HHmmss}.dmp");
            var sw = Stopwatch.StartNew();
            using var process = Process.GetCurrentProcess();
            using (var file = File.Create(path))
            {
                // (the memory the runtime needs to show managed stacks and objects, not the whole address space)
                const int type = 0x1 | 0x4 | 0x20 | 0x200 | 0x800 | 0x1000; // DataSegs, HandleData, UnloadedModules, PrivateReadWriteMemory, FullMemoryInfo, ThreadInfo
                if (!MiniDumpWriteDump(process.Handle, (uint)process.Id, file.SafeFileHandle, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                    throw new IOException($"MiniDumpWriteDump failed (error {Marshal.GetLastWin32Error()})");
            }
            Log.App.Warn($"Not responding for 10 s: saved a dump of the app to {path} ({new FileInfo(path).Length / (1024 * 1024)} MB, in {sw.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex) { Log.App.Warn("Not responding for 10 s; saving a dump failed", ex); }
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file, int type, IntPtr exception, IntPtr userStream, IntPtr callback);
}
