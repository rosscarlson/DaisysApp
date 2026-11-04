using System.Runtime.InteropServices;
using System.Text;
using DaisysApp.Logging;
using static DaisysApp.Shared.Win32.Native;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>
/// Always on top: an <b>Always on top</b> item (ticked while it is) in every window's title bar menu, the menu Windows
/// shows on a right-click on the title bar or Alt+Space. The item goes into a window's system menu (Windows' own,
/// changed from outside: <c>GetSystemMenu</c> + <c>InsertMenu</c>) when the window comes to the front or is
/// right-clicked, so it's there before the menu shows. Picking it sends the program a WM_SYSCOMMAND it doesn't know
/// (and ignores); we can't see that without a hook DLL in every program, so the pick is seen here instead: while one of
/// these menus is open (a WinEvent says so), a click or Enter with our item lit, or its T, sets the window on top or
/// not. Hooks and WinEvents on a thread of their own, only while it's on; switching it off takes the item out again.
/// <see cref="Toggle"/> (the shortcut, for the window in front) works without any of that.
/// </summary>
internal sealed class TopMost : IDisposable
{
    /// <summary>Our item's command (under Windows' own SC_ ones, its low 4 bits clear as theirs are).</summary>
    private const uint ItemId = 0xDA70;

    private readonly Logger log;
    private readonly string label;
    private readonly HookProc keyProc, mouseProc;
    private readonly WinEventProc eventProc;
    private Thread? thread;
    private uint threadId;

    /// <summary>The system menus we put the item in, and their windows (on the hook thread only).</summary>
    private readonly Dictionary<IntPtr, IntPtr> menus = new();
    // the menu showing, if it's one of ours
    private IntPtr openMenu, openPopup, openWindow;

    public TopMost(Logger log, string label)
    {
        this.log = log;
        this.label = label;
        keyProc = KeyHook;
        mouseProc = MouseHook;
        eventProc = OnEvent;
    }

    public void Start()
    {
        if (thread != null) return;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Always on top" };
        thread.Start();
        ready.Wait(2000);
    }

    public void Stop()
    {
        if (thread == null) return;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(2000);
        thread = null;
    }

    public void Dispose() => Stop();

    /// <summary>Sets the window on top or not (the opposite of what it is), and ticks our item to match. False: Windows
    /// didn't let us (a program running as administrator while we aren't).</summary>
    public static bool Toggle(IntPtr window, out bool on)
    {
        on = !IsOnTop(window);
        if (!SetWindowPos(window, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS)) return false;
        var menu = GetSystemMenu(window, false);
        if (menu != IntPtr.Zero && GetMenuState(menu, ItemId, MF_BYCOMMAND) != uint.MaxValue)
            CheckMenuItem(menu, ItemId, MF_BYCOMMAND | (on ? MF_CHECKED : MF_UNCHECKED));
        return true;
    }

    /// <summary>The window in front, if it's one that can be on top (not the desktop or the taskbar).</summary>
    public static IntPtr Front()
    {
        var w = GetForegroundWindow();
        if (w != IntPtr.Zero) w = GetAncestor(w, GA_ROOT);
        return Usable(w) ? w : IntPtr.Zero;
    }

    public static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.Length > 0 ? text.ToString() : $"0x{window.ToInt64():X}";
    }

    private void Loop(ManualResetEventSlim ready)
    {
        threadId = GetCurrentThreadId();
        SetThreadDpiAwarenessContext(new IntPtr(-4)); // real pixels on every monitor, as the hooks' points are
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // the thread's message queue, before anyone posts to it
        var module = GetModuleHandle(null);
        var keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, module, 0);
        var mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
        var front = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, eventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        var popups = SetWinEventHook(EVENT_SYSTEM_MENUPOPUPSTART, EVENT_SYSTEM_MENUPOPUPEND, IntPtr.Zero, eventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (keyHook == IntPtr.Zero || mouseHook == IntPtr.Zero || front == IntPtr.Zero || popups == IntPtr.Zero)
            log.Warn($"Always on top: hooks not set (error {Marshal.GetLastWin32Error()})");
        openMenu = openPopup = openWindow = IntPtr.Zero;
        Prepare(Front());
        ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.hwnd == IntPtr.Zero && msg.message == WM_PREPARE) { Safely(() => Prepare(RootAt(msg.lParam))); continue; }
            if (msg.hwnd == IntPtr.Zero && msg.message == WM_TOGGLE) { Safely(() => Picked(msg.wParam)); continue; }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        if (front != IntPtr.Zero) UnhookWinEvent(front);
        if (popups != IntPtr.Zero) UnhookWinEvent(popups);
        // the item out of every menu it went into
        foreach (var (menu, window) in menus)
            if (IsWindow(window)) DeleteMenu(menu, ItemId, MF_BYCOMMAND);
        menus.Clear();
    }

    private void Safely(Action a)
    {
        try { a(); }
        catch (Exception ex) { log.Warn("Always on top", ex); }
    }

    /// <summary>Puts our item in the window's title bar menu (once), ticked if it's on top.</summary>
    private void Prepare(IntPtr window)
    {
        if (!Usable(window) || (GetWindowLong(window, GWL_STYLE) & WS_SYSMENU) == 0) return;
        var menu = GetSystemMenu(window, false);
        if (menu == IntPtr.Zero) return;
        if (GetMenuState(menu, ItemId, MF_BYCOMMAND) == uint.MaxValue)
        {
            // before Close and the line over it, else at the end
            int count = GetMenuItemCount(menu), at = count;
            for (int i = 0; i < count; i++)
                if (GetMenuItemID(menu, i) == SC_CLOSE) { at = i; break; }
            if (at > 0 && at < count && (GetMenuState(menu, (uint)(at - 1), MF_BYPOSITION) & MF_SEPARATOR) != 0) at--;
            if (!InsertMenu(menu, (uint)at, MF_BYPOSITION | MF_STRING, (UIntPtr)ItemId, label)) return;
            if (menus.Count > 200)
                foreach (var gone in menus.Where(p => !IsWindow(p.Value)).Select(p => p.Key).ToList()) menus.Remove(gone);
        }
        menus[menu] = window;
        CheckMenuItem(menu, ItemId, MF_BYCOMMAND | (IsOnTop(window) ? MF_CHECKED : MF_UNCHECKED));
    }

    private void Picked(IntPtr window)
    {
        if (!IsWindow(window)) return;
        var title = TitleOf(window);
        if (Toggle(window, out var on)) log.Info(on ? $"Always on top on: {title}" : $"Always on top off: {title}");
        else log.Warn($"Always on top: Windows didn't let it change {title} (run as administrator?)");
    }

    private void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            switch (ev)
            {
                case EVENT_SYSTEM_FOREGROUND:
                    Prepare(hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOT));
                    break;
                case EVENT_SYSTEM_MENUPOPUPSTART:
                    // the popup's menu: one of ours if our item is in it
                    if (SendMessageTimeout(hwnd, MN_GETHMENU, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 200, out var menu) == IntPtr.Zero || menu == IntPtr.Zero) break;
                    if (GetMenuState(menu, ItemId, MF_BYCOMMAND) == uint.MaxValue) break;
                    openMenu = menu;
                    openPopup = hwnd;
                    openWindow = menus.TryGetValue(menu, out var w) ? w : Front();
                    break;
                case EVENT_SYSTEM_MENUPOPUPEND:
                    if (hwnd == openPopup || !IsWindow(openPopup)) openMenu = openPopup = openWindow = IntPtr.Zero;
                    break;
            }
        }
        catch (Exception ex) { log.Warn("Always on top: event", ex); }
    }

    private bool ItemLit() =>
        openMenu != IntPtr.Zero && (GetMenuState(openMenu, ItemId, MF_BYCOMMAND) is var s && s != uint.MaxValue && (s & MF_HILITE) != 0);

    private void Pick()
    {
        if (openWindow != IntPtr.Zero) PostThreadMessage(threadId, WM_TOGGLE, openWindow, IntPtr.Zero);
        openMenu = openPopup = openWindow = IntPtr.Zero;
    }

    private IntPtr KeyHook(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && openMenu != IntPtr.Zero && (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((k.vkCode == VK_RETURN && ItemLit()) || k.vkCode == VK_T) Pick();
            }
        }
        catch (Exception ex) { log.Warn("Always on top: key", ex); }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                int m = (int)wParam;
                if (m == WM_RBUTTONDOWN)
                {
                    // the window right-clicked gets the item before its menu shows (done off the hook)
                    var e = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    PostThreadMessage(threadId, WM_PREPARE, IntPtr.Zero, Pack(e.pt));
                }
                else if (m is WM_LBUTTONUP or WM_RBUTTONUP && openMenu != IntPtr.Zero)
                {
                    var e = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (ItemLit() && GetWindowRect(openPopup, out var r) && e.pt.X >= r.Left && e.pt.X < r.Right && e.pt.Y >= r.Top && e.pt.Y < r.Bottom) Pick();
                }
            }
        }
        catch (Exception ex) { log.Warn("Always on top: mouse", ex); }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static IntPtr Pack(POINT pt) => (IntPtr)(((long)pt.Y << 32) | (uint)pt.X);

    private static IntPtr RootAt(IntPtr packed)
    {
        long v = packed.ToInt64();
        var w = WindowFromPoint(new POINT { X = (int)(v & 0xFFFFFFFF), Y = (int)(v >> 32) });
        return w == IntPtr.Zero ? IntPtr.Zero : GetAncestor(w, GA_ROOT);
    }

    private static bool Usable(IntPtr w)
    {
        if (w == IntPtr.Zero || !IsWindowVisible(w)) return false;
        var cls = new StringBuilder(64);
        GetClassName(w, cls, cls.Capacity);
        return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or "#32768");
    }

    private static bool IsOnTop(IntPtr w) => (GetWindowLong(w, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    // Windows' calls

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
    private const uint WM_QUIT = 0x12, WM_PREPARE = 0x8001, WM_TOGGLE = 0x8002, MN_GETHMENU = 0x1E1;
    private const uint VK_RETURN = 0x0D, VK_T = 0x54;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MENUPOPUPSTART = 0x6, EVENT_SYSTEM_MENUPOPUPEND = 0x7, WINEVENT_OUTOFCONTEXT = 0;
    private const uint SMTO_ABORTIFHUNG = 0x2;
    private const uint GA_ROOT = 2;
    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const int WS_SYSMENU = 0x80000, WS_EX_TOPMOST = 0x8;
    private const uint SC_CLOSE = 0xF060;
    private const uint MF_BYCOMMAND = 0x0, MF_BYPOSITION = 0x400, MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_CHECKED = 0x8, MF_UNCHECKED = 0x0, MF_HILITE = 0x80;
    private static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_ASYNCWINDOWPOS = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc fn, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int pos);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool InsertMenu(IntPtr menu, uint pos, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern bool DeleteMenu(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern uint CheckMenuItem(IntPtr menu, uint item, uint flags);
}
