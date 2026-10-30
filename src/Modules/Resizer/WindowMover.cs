using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Video.Resizer;

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
        // (where it was, unless it's still where this profile put it: pressing the shortcut again puts it back)
        lock (before)
            if (!(before.TryGetValue(profile.Uuid, out var had) && had.Hwnd == hwnd && MatchesProfile(hwnd, profile)) && Saved.Read(hwnd) is { } now)
                before[profile.Uuid] = now;
        var result = MoveAndValidate(hwnd, profile);
        if (result == ApplyResult.Applied && monitor) _ = WatchForOverridesAsync(hwnd, profile, pid);
        return result;
    }

    // ---------------------------------------------------------------- putting it back

    /// <summary>A window's place, styles and maximized state before a profile moved it.</summary>
    private sealed record Saved(IntPtr Hwnd, Native.WINDOWPLACEMENT Placement, IntPtr Style, IntPtr ExStyle)
    {
        public static Saved? Read(IntPtr hwnd)
        {
            var wp = new Native.WINDOWPLACEMENT { Length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
            return Native.GetWindowPlacement(hwnd, ref wp)
                ? new(hwnd, wp, Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE), Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE))
                : null;
        }
    }

    private static readonly Dictionary<Guid, Saved> before = new();

    /// <summary>The profile's window is still where the profile put it (so the shortcut puts it back).</summary>
    public static bool IsApplied(ResizeProfile profile)
    {
        lock (before)
            return before.TryGetValue(profile.Uuid, out var s) && Native.IsWindow(s.Hwnd) && MatchesProfile(s.Hwnd, profile);
    }

    /// <summary>Puts the profile's window back where it was before it was applied (borders, size, place, maximized).</summary>
    public static bool Restore(ResizeProfile profile)
    {
        Saved? s;
        lock (before)
            if (!before.Remove(profile.Uuid, out s)) return false;
        if (!Native.IsWindow(s.Hwnd)) return false;
        Native.SetWindowLongPtr(s.Hwnd, Native.GWL_STYLE, s.Style);
        Native.SetWindowLongPtr(s.Hwnd, Native.GWL_EXSTYLE, s.ExStyle);
        Native.SetWindowPos(s.Hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_FRAMECHANGED);
        if (profile.RemoveBorders && ClassName(s.Hwnd) == "ApplicationFrameWindow")
            Native.EnumChildWindows(s.Hwnd, (child, _) =>
            {
                if (ClassName(child) == "ApplicationFrameTitleBarWindow") Native.ShowWindow(child, Native.SW_SHOWNA);
                return true;
            }, IntPtr.Zero);
        // (normal first: moved while maximized, it still is, and asking for maximized again would do nothing)
        var wp = s.Placement with { ShowCmd = Native.SW_SHOWNOACTIVATE };
        bool ok = Native.SetWindowPlacement(s.Hwnd, ref wp);
        if (s.Placement.ShowCmd == Native.SW_SHOWMAXIMIZED) Native.ShowWindow(s.Hwnd, Native.SW_SHOWMAXIMIZED);
        wp = s.Placement;
        var r = wp.NormalPosition;
        Logging.Log.Here.Info($"{profile.Name}: window 0x{s.Hwnd:X} put back at {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}{(wp.ShowCmd == Native.SW_SHOWMAXIMIZED ? ", maximized" : "")}");
        return ok;
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
        Logging.Log.Here.Debug(() => Native.GetWindowRect(hwnd, out var was)
            ? $"{profile.Name}: moving window 0x{hwnd:X} ({ClassName(hwnd)}) from {was.Left},{was.Top} {was.Right - was.Left}x{was.Bottom - was.Top} to {x},{y} {w}x{h}{(profile.RemoveBorders ? ", no borders" : "")}"
            : $"{profile.Name}: moving window 0x{hwnd:X} to {x},{y} {w}x{h}");
        if (!Native.MoveWindow(hwnd, x, y, w, h, true))
        {
            int error = Marshal.GetLastWin32Error();
            ErrorLog.Write("Resizer", new Exception($"MoveWindow failed for {profile.Name}: error {error}"));
            return error == 5 ? ApplyResult.AccessDenied : ApplyResult.Failed;
        }
        if (!MatchesProfile(hwnd, profile))
        {
            Logging.Log.Here.Debug(() => Native.GetWindowRect(hwnd, out var r) ? $"{profile.Name}: the window ended up at {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}" : $"{profile.Name}: the window is gone");
            return ApplyResult.Failed;
        }
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
            lock (before) if (!before.ContainsKey(profile.Uuid)) return; // put back by its shortcut
            if (!MatchesProfile(hwnd, profile))
            {
                Logging.Log.Here.Info($"{profile.Name}: the program moved its window again; putting it back");
                MoveAndValidate(hwnd, profile);
            }
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
    public static string Normalize(string name) => Shared.RunningPrograms.Bare(name);

    public static List<int> PidsFor(string processName) => Shared.RunningPrograms.Get(500).PidsOf(processName);

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
    public static List<string> List(bool all) => Shared.RunningPrograms.Windowed(all).ToList();
}

internal static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, SW_HIDE = 0, SW_SHOWMAXIMIZED = 3, SW_SHOWNOACTIVATE = 4, SW_SHOWNA = 8;
    public const uint WS_BORDER = 0x00800000, WS_DLGFRAME = 0x00400000, WS_THICKFRAME = 0x00040000;
    public const uint WS_EX_DLGMODALFRAME = 0x1, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
                      SWP_FRAMECHANGED = 0x20, SWP_NOOWNERZORDER = 0x200, SWP_NOSENDCHANGING = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT { public int Length, Flags, ShowCmd, MinX, MinY, MaxX, MaxY; public RECT NormalPosition; }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
}
