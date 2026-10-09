using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace DaisysApp.Applets.Gaming;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Intersect(PixelRect o)
    {
        int l = Math.Max(X, o.X), t = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > l && b > t ? new PixelRect(l, t, r - l, b - t) : default;
    }

    public bool Contains(PixelRect o) => o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;
    public static PixelRect FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);
    public override string ToString() => $"{Width}×{Height} at {X},{Y}";
}

/// <summary>A monitor as Windows' desktop duplication sees it.</summary>
internal sealed record MonitorInfo(string DeviceName, PixelRect Bounds, bool Primary, string Label);

/// <summary>Windows, processes and monitors, in physical pixels (the app is Per-Monitor-V2 DPI aware).</summary>
internal static class Native
{
    // ---------------------------------------------------------------- our own windows

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    public static void SetBounds(Window w, PixelRect r) =>
        SetWindowPos(Handle(w), IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    public static PixelRect GetBounds(Window w) => GetBounds(Handle(w));

    public static PixelRect GetBounds(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>A tool window (not in Alt+Tab) that never takes the focus from a game.</summary>
    public static void MakeOverlay(Window w)
    {
        var h = Handle(w);
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex & ~WS_EX_APPWINDOW));
    }

    public static void SetClickThrough(Window w, bool on)
    {
        var h = Handle(w);
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        ex = on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Leaves the window out of screen capture (recordings, screenshots); Windows 10 2004 or later.</summary>
    public static void ExcludeFromCapture(Window w, bool exclude) =>
        SetWindowDisplayAffinity(Handle(w), exclude ? WDA_EXCLUDEFROMCAPTURE : 0);

    /// <summary>Back on top of everything (a game going full screen can push it down).</summary>
    public static void BringToTop(Window w) =>
        SetWindowPos(Handle(w), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // ---------------------------------------------------------------- other programs

    public static (IntPtr Hwnd, uint Pid) Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return (IntPtr.Zero, 0);
        GetWindowThreadProcessId(h, out uint pid);
        return (h, pid);
    }

    private static readonly Dictionary<uint, (string Exe, string Path)> exeCache = new();

    /// <summary>The process's exe name in lower case, e.g. "forzahorizon5.exe" (cached by process id), and its path.</summary>
    public static (string Exe, string Path) ProcessExe(uint pid)
    {
        lock (exeCache)
        {
            if (exeCache.TryGetValue(pid, out var cached)) return cached;
            string path = "";
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size)) path = sb.ToString();
                CloseHandle(h);
            }
            var result = (System.IO.Path.GetFileName(path).ToLowerInvariant(), path);
            if (exeCache.Count > 500) exeCache.Clear();
            if (path.Length > 0) exeCache[pid] = result;
            return result;
        }
    }

    public static bool IsAlive(uint pid)
    {
        IntPtr h = OpenProcess(0x1000 | 0x00100000, false, pid); // + SYNCHRONIZE
        if (h == IntPtr.Zero) return false;
        bool alive = WaitForSingleObject(h, 0) == 0x102; // WAIT_TIMEOUT
        CloseHandle(h);
        return alive;
    }

    /// <summary>Whether the window covers its whole monitor (full screen or borderless full screen).</summary>
    public static bool CoversMonitor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var mon = MonitorFromWindow(hwnd, 2);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref info)) return false;
        return r.Left <= info.rcMonitor.Left && r.Top <= info.rcMonitor.Top && r.Right >= info.rcMonitor.Right && r.Bottom >= info.rcMonitor.Bottom;
    }

    /// <summary>The window's picture area (no title bar or borders) in desktop pixels.</summary>
    public static PixelRect ClientBounds(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var r)) return default;
        var p = new POINT();
        ClientToScreen(hwnd, ref p);
        return new PixelRect(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static bool IsMinimized(IntPtr hwnd) => IsIconic(hwnd);
    public static bool IsWindowAlive(IntPtr hwnd) => IsWindow(hwnd);

    public static string WindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Visible top-level windows with a title, with their process ids.</summary>
    public static List<(IntPtr Hwnd, uint Pid, string Title)> TopLevelWindows()
    {
        var list = new List<(IntPtr, uint, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0) return true;
            string title = WindowTitle(h);
            if (title.Length == 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            list.Add((h, pid, title));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>The program's biggest visible window.</summary>
    public static IntPtr MainWindowOf(string exe)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        foreach (var (h, pid, _) in TopLevelWindows())
        {
            if (!string.Equals(ProcessExe(pid).Exe, exe, StringComparison.OrdinalIgnoreCase)) continue;
            var b = GetBounds(h);
            long area = (long)b.Width * b.Height;
            if (area > bestArea) { bestArea = area; best = h; }
        }
        return best;
    }

    // ---------------------------------------------------------------- monitors

    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        try
        {
            using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                using (adapter)
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                        using (output)
                        {
                            var d = output.Description;
                            var c = d.DesktopCoordinates;
                            var bounds = PixelRect.FromLTRB(c.Left, c.Top, c.Right, c.Bottom);
                            list.Add(new MonitorInfo(d.DeviceName, bounds, c.Left == 0 && c.Top == 0, ""));
                        }
            }
        }
        catch { }
        list.Sort((x, y) => x.Bounds.X != y.Bounds.X ? x.Bounds.X.CompareTo(y.Bounds.X) : x.Bounds.Y.CompareTo(y.Bounds.Y));
        return list.Select((m, i) => m with { Label = F("Monitor {0} ({1}×{2}){3}", i + 1, m.Bounds.Width, m.Bounds.Height, m.Primary ? " · " + T("main") : "") }).ToList();
    }

    /// <summary>The monitor that holds most of the rectangle.</summary>
    public static MonitorInfo? MonitorFor(PixelRect r, List<MonitorInfo>? monitors = null)
    {
        monitors ??= Monitors();
        return monitors.OrderByDescending(m => { var i = m.Bounds.Intersect(r); return (long)i.Width * i.Height; }).FirstOrDefault();
    }

    // ---------------------------------------------------------------- P/Invoke

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}
