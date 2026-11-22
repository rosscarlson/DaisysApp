using System.Runtime.InteropServices;
using static DaisysApp.Shared.Win32.Native;

namespace DaisysApp.Shared.Widgets;

/// <summary>
/// A right-click on a widget clicks go through (since 4.19.0: Windows gives it to what's under the widget, so one
/// couldn't be edited without knowing of Ctrl+Shift). While the pointer is over one, a low-level mouse hook on a thread
/// of its own keeps the right button back from what's under it; every other click, and the right button elsewhere,
/// goes on as it was. Not hooked (so nothing to slow the mouse) while the pointer is hidden, as a game steering its
/// view with it does, or over no such widget.
/// </summary>
internal static class WidgetRightClick
{
    private static readonly HookProc proc = MouseHook; // (kept: Windows calls it)
    private static volatile IntPtr[] targets = Array.Empty<IntPtr>();
    private static Thread? thread;
    private static uint threadId;
    /// <summary>The widget whose right button is down (kept back, so its release is too).</summary>
    private static volatile IntPtr pressed;

    /// <summary>A widget (by its window) was right-clicked: on the hook's thread.</summary>
    public static event Action<IntPtr>? Clicked;

    /// <summary>The widgets under the pointer that let clicks through (none: not hooked).</summary>
    public static void Watch(IntPtr[] under)
    {
        targets = under;
        if (under.Length > 0) Start();
        else if (pressed == IntPtr.Zero) Stop();
    }

    /// <summary>The pointer's place (false: it's hidden).</summary>
    public static bool Pointer(out POINT at)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        bool shown = GetCursorInfo(ref info) && (info.flags & CURSOR_SHOWING) != 0;
        at = info.ptScreenPos;
        return shown;
    }

    public static bool Over(IntPtr hwnd, POINT p) =>
        GetWindowRect(hwnd, out var r) && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    private static void Start()
    {
        if (thread != null) return;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Widget right-click" };
        thread.Start();
        ready.Wait();
    }

    private static void Stop()
    {
        if (thread == null) return;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread = null;
    }

    private static void Loop(ManualResetEventSlim ready)
    {
        threadId = GetCurrentThreadId();
        SetThreadDpiAwarenessContext(new IntPtr(-4)); // real pixels on every monitor, as the hook's points are
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // the thread's message queue, before anyone posts to it
        var hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
        int error = Marshal.GetLastWin32Error();
        ready.Set();
        if (hook == IntPtr.Zero) Logging.Log.Here.Warn($"Widgets: right-click hook not set (error {error})");
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    private static IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        int m = (int)wParam;
        if (code >= 0 && (m == WM_RBUTTONDOWN || m == WM_RBUTTONUP)
            && Right(m == WM_RBUTTONDOWN, Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt)) return 1;
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>True: kept back.</summary>
    private static bool Right(bool down, POINT p)
    {
        if (down)
        {
            pressed = Pointer(out _) ? At(p) : IntPtr.Zero;
            return pressed != IntPtr.Zero;
        }
        var was = pressed;
        pressed = IntPtr.Zero;
        if (was == IntPtr.Zero) return false;
        if (Over(was, p)) Clicked?.Invoke(was);
        return true;
    }

    /// <summary>The widget at a point that nothing else covers there (none: zero).</summary>
    private static IntPtr At(POINT p)
    {
        var best = IntPtr.Zero;
        foreach (var h in targets)
            if (IsWindowVisible(h) && Over(h, p) && (best == IntPtr.Zero || Above(h, best))) best = h;
        if (best == IntPtr.Zero) return best;
        // (the widget itself lets the point through, so this is what's next there: below it, or a window over it)
        var next = GetAncestor(WindowFromPoint(p), GA_ROOT);
        return next == IntPtr.Zero || Above(best, next) ? best : IntPtr.Zero;
    }

    /// <summary>Whether a window is in front of another.</summary>
    private static bool Above(IntPtr a, IntPtr b)
    {
        int n = 0; // (bounded: the order can change as it's walked)
        for (var h = GetWindow(a, GW_HWNDNEXT); h != IntPtr.Zero && n < 5000; h = GetWindow(h, GW_HWNDNEXT), n++)
            if (h == b) return true;
        return false;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, CURSOR_SHOWING = 1;
    private const uint WM_QUIT = 0x12, GA_ROOT = 2, GW_HWNDNEXT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
}
