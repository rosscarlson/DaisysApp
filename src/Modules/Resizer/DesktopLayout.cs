using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DaisysApp.Logging;
using static DaisysApp.Shared.Win32.Native;
using W32 = DaisysApp.Shared.Win32.Native;

namespace DaisysApp.Applets.Video.Resizer;

/// <summary>One window of a Desktop Layout: whose it is, and where it was.</summary>
public sealed class LayoutWindow
{
    /// <summary>Its program's exe name ("chrome.exe").</summary>
    public string Process { get; set; } = "";
    public string Class { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The window's handle when it was taken: the same window, still open, is matched first.</summary>
    public long Handle { get; set; }
    public bool Maximized { get; set; }
    public bool Minimized { get; set; }
    /// <summary>Where it was on the screen (physical pixels; for a maximized or minimized one, where it was last).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Its restored place, as Windows keeps it (workspace coordinates): put back with SetWindowPlacement.</summary>
    public int NormalLeft { get; set; }
    public int NormalTop { get; set; }
    public int NormalRight { get; set; }
    public int NormalBottom { get; set; }
}

/// <summary>What applying a layout did: its windows, how many are open now, how many were put back, and how many
/// Windows wouldn't let us move (a program running as administrator).</summary>
public sealed record LayoutResult(int Total, int Found, int Moved, int Denied);

/// <summary>
/// Desktop Layouts: every window as Alt+Tab lists them (visible, not owned, not a tool window, not cloaked, with a
/// title), taken as a snapshot, and put back where they were. A window is matched by its program and window class,
/// first by the same handle (the same window, as after displays were switched off and on), then by its title; windows
/// not in the layout aren't touched, and ones no longer open are skipped.
/// </summary>
public static class DesktopLayout
{
    private static readonly Logger log = Logging.Log.Here;

    /// <summary>The windows open now, top to bottom.</summary>
    public static List<LayoutWindow> Capture()
    {
        var names = new Dictionary<uint, string>();
        var list = new List<LayoutWindow>();
        foreach (var hwnd in Windows())
        {
            var wp = new Native.WINDOWPLACEMENT { Length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
            if (!Native.GetWindowPlacement(hwnd, ref wp) || !W32.GetWindowRect(hwnd, out var r)) continue;
            var n = wp.NormalPosition;
            list.Add(new LayoutWindow
            {
                Process = ProcessOf(hwnd, names), Class = ClassName(hwnd), Title = Title(hwnd), Handle = hwnd.ToInt64(),
                Maximized = Native2.IsZoomed(hwnd), Minimized = W32.IsIconic(hwnd),
                X = r.Left, Y = r.Top, Width = r.Right - r.Left, Height = r.Bottom - r.Top,
                NormalLeft = n.Left, NormalTop = n.Top, NormalRight = n.Right, NormalBottom = n.Bottom,
            });
        }
        log.Info($"Desktop layout taken: {list.Count} windows");
        return list;
    }

    /// <summary>Puts the layout's windows that are open back where they were (on a worker thread: a window can be slow
    /// to answer).</summary>
    public static Task<LayoutResult> ApplyAsync(string name, IReadOnlyList<LayoutWindow> layout) => Task.Run(async () =>
    {
        var names = new Dictionary<uint, string>();
        var open = Windows().Select(h => (Hwnd: h, Process: ProcessOf(h, names), Class: ClassName(h), Title: Title(h))).ToList();

        // the best pairs first: the same window, then the same title, then any of that program's windows of that class
        var pairs = new List<(int Score, int Saved, int Open)>();
        for (int s = 0; s < layout.Count; s++)
            for (int o = 0; o < open.Count; o++)
            {
                var w = layout[s];
                if (!string.Equals(w.Process, open[o].Process, StringComparison.OrdinalIgnoreCase) || w.Class != open[o].Class) continue;
                pairs.Add((1 + (w.Handle == open[o].Hwnd.ToInt64() ? 1000 : 0) + (w.Title == open[o].Title ? 100 : 0), s, o));
            }
        var matched = new List<(LayoutWindow Saved, IntPtr Hwnd)>();
        var usedSaved = new HashSet<int>();
        var usedOpen = new HashSet<int>();
        foreach (var p in pairs.OrderByDescending(p => p.Score))
            if (!usedSaved.Contains(p.Saved) && !usedOpen.Contains(p.Open))
            {
                usedSaved.Add(p.Saved);
                usedOpen.Add(p.Open);
                matched.Add((layout[p.Saved], open[p.Open].Hwnd));
            }

        IntPtr front = W32.GetForegroundWindow();
        int moved = 0, denied = 0;
        var again = new List<(LayoutWindow Saved, IntPtr Hwnd)>();
        foreach (var (w, hwnd) in matched)
        {
            switch (Place(hwnd, w))
            {
                case Placed.Moved: moved++; break;
                case Placed.Denied: denied++; break;
            }
            if (!w.Maximized && !w.Minimized && !W32.IsIconic(hwnd) && !Landed(hwnd, w)) again.Add((w, hwnd));
        }
        // (a window moved onto a display of another scale resizes itself once it's there: once more for those)
        if (again.Count > 0)
        {
            await Task.Delay(300);
            foreach (var (w, hwnd) in again) if (W32.IsWindow(hwnd)) Move(hwnd, w);
        }
        if (matched.Any(m => m.Saved.Maximized) && W32.IsWindow(front)) Native2.SetForegroundWindow(front);

        log.Info($"Desktop layout {name}: {matched.Count} of {layout.Count} windows open, {moved} put back" +
                 (denied > 0 ? $", {denied} refused (run as administrator)" : "") +
                 (again.Count > 0 ? $", {again.Count} moved twice" : ""));
        return new LayoutResult(layout.Count, matched.Count, moved, denied);
    });

    private enum Placed { Moved, Denied, Failed }

    private static Placed Place(IntPtr hwnd, LayoutWindow w)
    {
        if (!W32.IsWindow(hwnd) || Native2.IsHungAppWindow(hwnd)) return Placed.Failed;
        var wp = new Native.WINDOWPLACEMENT { Length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (!Native.GetWindowPlacement(hwnd, ref wp)) return Placed.Failed;
        wp.NormalPosition = new W32.RECT { Left = w.NormalLeft, Top = w.NormalTop, Right = w.NormalRight, Bottom = w.NormalBottom };
        string what = $"{w.Process} \"{w.Title}\" (0x{hwnd:X})";

        // minimized now: it stays so, and opens where it was (maximized, if it was)
        if (W32.IsIconic(hwnd))
        {
            wp.Flags = w.Maximized ? wp.Flags | Native2.WPF_RESTORETOMAXIMIZED : wp.Flags & ~Native2.WPF_RESTORETOMAXIMIZED;
            wp.ShowCmd = Native2.SW_SHOWMINNOACTIVE;
            log.Debug(() => $"Layout: {what} stays minimized, restores to {w.NormalLeft},{w.NormalTop}{(w.Maximized ? " maximized" : "")}");
            return Set(ref wp);
        }

        // normal first, at its restored place (a maximized window is maximized again on the display that place is on)
        wp.ShowCmd = Native.SW_SHOWNOACTIVATE;
        var result = Set(ref wp);
        if (result == Placed.Denied) return result;
        if (w.Maximized)
        {
            Native.ShowWindow(hwnd, Native.SW_SHOWMAXIMIZED);
            log.Debug(() => $"Layout: {what} maximized on the display at {w.NormalLeft},{w.NormalTop}");
            return Native2.IsZoomed(hwnd) ? Placed.Moved : Placed.Failed;
        }
        if (w.Minimized) return result; // (minimized in the layout, open now: at its restored place)
        log.Debug(() => $"Layout: {what} to {w.X},{w.Y} {w.Width}x{w.Height}");
        return Move(hwnd, w);

        Placed Set(ref Native.WINDOWPLACEMENT p) =>
            Native.SetWindowPlacement(hwnd, ref p) ? Placed.Moved : Marshal.GetLastWin32Error() == 5 ? Placed.Denied : Placed.Failed;
    }

    private static Placed Move(IntPtr hwnd, LayoutWindow w) =>
        W32.SetWindowPos(hwnd, IntPtr.Zero, w.X, w.Y, w.Width, w.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER)
            ? Placed.Moved
            : Marshal.GetLastWin32Error() == 5 ? Placed.Denied : Placed.Failed;

    private static bool Landed(IntPtr hwnd, LayoutWindow w) =>
        W32.GetWindowRect(hwnd, out var r) && r.Left == w.X && r.Top == w.Y && r.Right - r.Left == w.Width && r.Bottom - r.Top == w.Height;

    // ---------------------------------------------------------------- which windows

    private static readonly HashSet<string> Shell = new() { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>The windows Alt+Tab would list, top to bottom.</summary>
    private static List<IntPtr> Windows()
    {
        var list = new List<IntPtr>();
        W32.EnumWindows((hwnd, _) =>
        {
            if (!W32.IsWindowVisible(hwnd) || Native2.GetWindow(hwnd, Native2.GW_OWNER) != IntPtr.Zero) return true;
            long ex = W32.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native2.WS_EX_TOOLWINDOW) != 0 && (ex & Native2.WS_EX_APPWINDOW) == 0) return true;
            if (W32.DwmGetWindowAttribute(hwnd, Native2.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (Title(hwnd).Length == 0 || Shell.Contains(ClassName(hwnd))) return true;
            list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static string ProcessOf(IntPtr hwnd, Dictionary<uint, string> names)
    {
        W32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (names.TryGetValue(pid, out var name)) return name;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            name = p.ProcessName + ".exe";
        }
        catch { name = ""; }
        return names[pid] = name;
    }

    private static string Title(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        return Native.GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return Native.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static class Native2
    {
        public const int GW_OWNER = 4, SW_SHOWMINNOACTIVE = 7, WPF_RESTORETOMAXIMIZED = 2, DWMWA_CLOAKED = 14;
        public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;

        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}
