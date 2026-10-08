using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Resizer;

public enum ApplyResult { Applied, ProcessNotFound, NoWindow, AccessDenied, Failed }

/// <summary>
/// Moves and resizes another program's window to a profile (a port of Resize Rabbit's window_manager.rs):
/// picks the largest visible window of the matching process(es), optionally strips its borders, moves it, checks it
/// landed, and can keep watching for a while in case the program moves itself back.
/// </summary>
public static class WindowMover
{
    /// <summary>Applies a profile. <paramref name="retry"/>: if the program has no window yet (still loading), try again
    /// every 5 s, up to 2 more times. <paramref name="monitor"/>: re-apply if the window drifts over the next ~35 s.</summary>
    public static async Task<ApplyResult> ApplyAsync(ResizeProfile profile, bool retry, bool monitor, int retries = 0)
    {
        // A process name can match several PIDs (a launcher next to the real game); try each until one has a window.
        var candidates = ProcessFinder.PidsFor(profile.ProcessName);
        foreach (int pid in candidates)
        {
            var r = ApplyToPid(profile, pid, monitor);
            if (r != ApplyResult.NoWindow) return r;
        }

        // Some games' windows belong to a process with a different exe name (e.g. Forza Horizon 4): last resort, a
        // visible window whose title contains the profile's name.
        if (FindPidByWindowTitle(profile.Name) is int titlePid)
        {
            var r = ApplyToPid(profile, titlePid, monitor);
            if (r != ApplyResult.NoWindow) return r;
        }

        if (candidates.Count == 0) return ApplyResult.ProcessNotFound;

        if (retry && retries < 2)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return await ApplyAsync(profile, retry: true, monitor, retries + 1);
        }
        return ApplyResult.NoWindow;
    }

    public static string Describe(ApplyResult result, ResizeProfile profile) => result switch
    {
        ApplyResult.Applied => F("Applied {0}.", profile.Name),
        ApplyResult.ProcessNotFound => F("{0}: {1} isn't running. Start it, then apply again.", profile.Name, profile.ProcessName),
        ApplyResult.NoWindow => F("{0}: {1} is running but has no visible window yet. Try again once it's loaded.", profile.Name, profile.ProcessName),
        ApplyResult.AccessDenied => F("{0}: Windows refused to move that window (it's probably running as administrator). Run Daisy's App as administrator to control it.", profile.Name),
        _ => F("{0}: the window didn't end up where it should. Try again, or check the size and position.", profile.Name),
    };

    private static ApplyResult ApplyToPid(ResizeProfile profile, int pid, bool monitor)
    {
        IntPtr hwnd = FindLargestWindow(pid);
        if (hwnd == IntPtr.Zero) return ApplyResult.NoWindow;
        var result = MoveAndValidate(hwnd, profile);
        if (result == ApplyResult.Applied && monitor) _ = WatchForOverridesAsync(hwnd, profile, pid);
        return result;
    }

    /// <summary>The window's current rect, for "Use current window" in the editor.</summary>
    public static (int X, int Y, int Width, int Height)? CurrentRect(string processName)
    {
        foreach (int pid in ProcessFinder.PidsFor(processName))
        {
            IntPtr hwnd = FindLargestWindow(pid);
            if (hwnd != IntPtr.Zero && Native.GetWindowRect(hwnd, out var r))
                return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
        return null;
    }

    // ---------------------------------------------------------------- moving

    private static ApplyResult MoveAndValidate(IntPtr hwnd, ResizeProfile profile)
    {
        RemoveBorders(hwnd, profile);
        var (x, y, w, h) = EffectiveTargetRect(hwnd, profile);
        if (!Native.MoveWindow(hwnd, x, y, w, h, true))
        {
            int error = Marshal.GetLastWin32Error();
            ErrorLog.Write("Resizer", new Exception($"MoveWindow failed for {profile.Name}: error {error}"));
            return error == 5 ? ApplyResult.AccessDenied : ApplyResult.Failed;
        }
        if (!MatchesProfile(hwnd, profile)) return ApplyResult.Failed;
        if (profile.RemoveBorders) HideUwpTitleBar(hwnd);
        return ApplyResult.Applied;
    }

    private static bool MatchesProfile(IntPtr hwnd, ResizeProfile profile)
    {
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var (x, y, w, h) = EffectiveTargetRect(hwnd, profile);
        return r.Left == x && r.Top == y && r.Right - r.Left == w && r.Bottom - r.Top == h;
    }

    /// <summary>Re-applies if the program moves or resizes its window again: every second for 10 s, then every 5 s for 25 s.</summary>
    private static async Task WatchForOverridesAsync(IntPtr hwnd, ResizeProfile profile, int pid)
    {
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(i < 10 ? 1 : 5));
            if (!ProcessFinder.IsRunning(pid) || !Native.IsWindow(hwnd)) return;
            if (!MatchesProfile(hwnd, profile)) MoveAndValidate(hwnd, profile);
        }
    }

    /// <summary>Profile size, or the window's current size where the profile leaves it unset (position-only profile).</summary>
    private static (int W, int H) ResolvedSize(IntPtr hwnd, ResizeProfile profile)
    {
        if (profile.WindowWidth is int w && profile.WindowHeight is int h) return (w, h);
        Native.GetWindowRect(hwnd, out var r);
        return (profile.WindowWidth ?? r.Right - r.Left, profile.WindowHeight ?? r.Bottom - r.Top);
    }

    /// <summary>
    /// Where the window should go. With "remove title bar" on a Store/UWP frame (ApplicationFrameWindow) whose content
    /// starts below the frame top, the frame is moved up and made taller by that amount so the game content lands
    /// exactly on the profile's rect and the title strip sits above the screen. Otherwise just the profile's rect.
    /// </summary>
    private static (int X, int Y, int W, int H) EffectiveTargetRect(IntPtr hwnd, ResizeProfile profile)
    {
        var (w, h) = ResolvedSize(hwnd, profile);
        if (profile.ShiftTitlebarOffscreen && MeasureUwpContentTopOffset(hwnd) is int shift)
            return (profile.WindowPosX, profile.WindowPosY - shift, w, h + shift);
        return (profile.WindowPosX, profile.WindowPosY, w, h);
    }

    private static void RemoveBorders(IntPtr hwnd, ResizeProfile profile)
    {
        if (!profile.RemoveBorders) return;
        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
        style &= ~(long)(Native.WS_THICKFRAME | Native.WS_DLGFRAME | Native.WS_BORDER);
        Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style));

        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        ex &= ~(long)(Native.WS_EX_DLGMODALFRAME | Native.WS_EX_WINDOWEDGE | Native.WS_EX_CLIENTEDGE | Native.WS_EX_STATICEDGE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE |
            Native.SWP_NOOWNERZORDER | Native.SWP_NOSENDCHANGING | Native.SWP_FRAMECHANGED);
    }

    /// <summary>How far a UWP frame's "Windows.UI.Core.CoreWindow" child sits below the frame's top; null for anything else.</summary>
    private static int? MeasureUwpContentTopOffset(IntPtr hwnd)
    {
        if (ClassName(hwnd) != "ApplicationFrameWindow" || !Native.GetWindowRect(hwnd, out var frame)) return null;
        int? coreTop = null;
        Native.EnumChildWindows(hwnd, (child, _) =>
        {
            if (ClassName(child) != "Windows.UI.Core.CoreWindow") return true;
            if (Native.GetWindowRect(child, out var r)) coreTop = r.Top;
            return false;
        }, IntPtr.Zero);
        return coreTop is int top && top - frame.Top > 0 ? top - frame.Top : null;
    }

    /// <summary>UWP frames draw their title strip as a separate child window; hide it (never touches the game surface).</summary>
    private static void HideUwpTitleBar(IntPtr hwnd)
    {
        if (ClassName(hwnd) != "ApplicationFrameWindow") return;
        Native.EnumChildWindows(hwnd, (child, _) =>
        {
            if (ClassName(child) == "ApplicationFrameTitleBarWindow") Native.ShowWindow(child, Native.SW_HIDE);
            return true;
        }, IntPtr.Zero);
    }

    // ---------------------------------------------------------------- finding windows

    /// <summary>The largest visible top-level window owned by <paramref name="pid"/> (a process can own several).</summary>
    private static IntPtr FindLargestWindow(int pid)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = -1;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != pid || !Native.IsWindowVisible(hwnd) || !Native.GetWindowRect(hwnd, out var r)) return true;
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { bestArea = area; best = hwnd; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static int? FindPidByWindowTitle(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        int? found = null;
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(256);
            if (Native.GetWindowText(hwnd, title, title.Capacity) <= 0) return true;
            if (!title.ToString().Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            found = (int)pid;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return Native.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }
}

/// <summary>Finds running processes by exe name (case-insensitive, ".exe" optional) and lists them for the picker.</summary>
public static class ProcessFinder
{
    public static string Normalize(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public static List<int> PidsFor(string processName)
    {
        string target = Normalize(processName);
        if (target.Length == 0) return new();
        var pids = new List<int>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
                if (string.Equals(p.ProcessName, target, StringComparison.OrdinalIgnoreCase)) pids.Add(p.Id);
        }
        return pids;
    }

    public static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>Running exe names ("name.exe"), sorted; only ones with a visible window unless <paramref name="all"/>.</summary>
    public static List<string> List(bool all)
    {
        var withWindows = new HashSet<uint>();
        if (!all)
            Native.EnumWindows((hwnd, _) =>
            {
                if (Native.IsWindowVisible(hwnd))
                {
                    Native.GetWindowThreadProcessId(hwnd, out uint pid);
                    withWindows.Add(pid);
                }
                return true;
            }, IntPtr.Zero);

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == 0 || p.Id == 4) continue; // idle and system
                if (all || withWindows.Contains((uint)p.Id)) names.Add(p.ProcessName + ".exe");
            }
        }
        return names.ToList();
    }

    /// <summary>Lower-case names (without ".exe") of every running process, for the watcher and "running" markers.</summary>
    public static HashSet<string> RunningNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
            using (p) set.Add(p.ProcessName);
        return set;
    }
}

internal static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, SW_HIDE = 0;
    public const uint WS_BORDER = 0x00800000, WS_DLGFRAME = 0x00400000, WS_THICKFRAME = 0x00040000;
    public const uint WS_EX_DLGMODALFRAME = 0x1, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
                      SWP_FRAMECHANGED = 0x20, SWP_NOOWNERZORDER = 0x200, SWP_NOSENDCHANGING = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
}
