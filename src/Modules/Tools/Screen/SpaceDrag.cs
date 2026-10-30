using System.Runtime.InteropServices;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Tools.Screen;

/// <summary>
/// Spacebar Drag: while the spacebar is held, a left click anywhere on a window grabs it as its title bar would, and
/// it follows the mouse until the button is let go (as some X window managers did). Low-level keyboard and mouse
/// hooks on a thread of their own (so a busy UI thread never holds them up). A space held alone is kept back and typed
/// when it's let go if no window was grabbed; another key pressed meanwhile types it at once (fast typing), and a space
/// with Ctrl, Alt, Shift or Win goes through as it is. A held space counts only while its key repeats come in, so one
/// whose release was never seen (a locked screen) can't grab. Windows move by posted SetWindowPos calls, so a hung one
/// can't block the hooks; a maximized one is restored under the pointer first, as Windows does.
/// </summary>
internal sealed class SpaceDrag : IDisposable
{
    private readonly Logger log;
    // the delegates, kept alive for as long as the hooks are in
    private readonly HookProc keyProc, mouseProc;
    private Thread? thread;
    private uint threadId;

    private enum Space { Up, Held, Passed, Used }
    private Space space;
    private uint spaceTime;
    private IntPtr grabbed;
    private POINT grabPoint;
    private RECT grabRect;
    private bool maximized;

    public SpaceDrag(Logger log)
    {
        this.log = log;
        keyProc = KeyHook;
        mouseProc = MouseHook;
    }

    public void Start()
    {
        if (thread != null) return;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Spacebar Drag" };
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

    private void Loop(ManualResetEventSlim ready)
    {
        threadId = GetCurrentThreadId();
        SetThreadDpiAwarenessContext(new IntPtr(-4)); // real pixels on every monitor, as the hooks' points are
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // the thread's message queue, before anyone posts to it
        var module = GetModuleHandle(null);
        var keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, module, 0);
        var mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
        if (keyHook == IntPtr.Zero || mouseHook == IntPtr.Zero) log.Warn($"Spacebar Drag: hooks not set (error {Marshal.GetLastWin32Error()})");
        space = Space.Up;
        grabbed = IntPtr.Zero;
        ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        grabbed = IntPtr.Zero;
    }

    private IntPtr KeyHook(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && Key(wParam, Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam))) return (IntPtr)1;
        }
        catch (Exception ex) { log.Warn("Spacebar Drag: key", ex); }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>True: kept back.</summary>
    private bool Key(IntPtr wParam, KBDLLHOOKSTRUCT k)
    {
        if ((k.flags & LLKHF_INJECTED) != 0) return false;
        int m = (int)wParam;
        bool down = m is WM_KEYDOWN or WM_SYSKEYDOWN;
        if (k.vkCode == VK_SPACE)
        {
            if (down)
            {
                if (space == Space.Up) space = Modifiers() ? Space.Passed : Space.Held;
                spaceTime = k.time;
                return space is Space.Held or Space.Used; // (and its repeats)
            }
            var was = space;
            space = Space.Up;
            if (was == Space.Held) Send(Input(VK_SPACE, 0x39, false, false), Input(VK_SPACE, 0x39, false, true)); // no window grabbed: typed now
            return was is Space.Held or Space.Used;
        }
        if (down && space == Space.Held)
        {
            // typed fast: the space goes first, then this key, and the space is let through from here on
            space = Space.Passed;
            bool ext = (k.flags & LLKHF_EXTENDED) != 0;
            Send(Input(VK_SPACE, 0x39, false, false), Input((ushort)k.vkCode, (ushort)k.scanCode, ext, false));
            return true;
        }
        return false;
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && Mouse((int)wParam, Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam))) return (IntPtr)1;
        }
        catch (Exception ex) { log.Warn("Spacebar Drag: mouse", ex); }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>True: kept back.</summary>
    private bool Mouse(int m, MSLLHOOKSTRUCT e)
    {
        if ((e.flags & LLMHF_INJECTED) != 0) return false;
        switch (m)
        {
            case WM_LBUTTONDOWN:
                grabbed = IntPtr.Zero; // (a release not seen)
                if (space is not (Space.Held or Space.Used)) return false;
                if (unchecked(e.time - spaceTime) > RepeatWait())
                {
                    space = Space.Up; // its release was never seen
                    return false;
                }
                var target = Target(e.pt);
                if (target == IntPtr.Zero || !GetWindowRect(target, out grabRect)) return false;
                space = Space.Used;
                grabbed = target;
                grabPoint = e.pt;
                maximized = IsZoomed(target);
                SetForegroundWindow(target);
                return true;
            case WM_MOUSEMOVE:
                if (grabbed != IntPtr.Zero) Move(e.pt);
                return false;
            case WM_LBUTTONUP:
                if (grabbed == IntPtr.Zero) return false;
                grabbed = IntPtr.Zero;
                return true;
        }
        return false;
    }

    private void Move(POINT pt)
    {
        int dx = pt.X - grabPoint.X, dy = pt.Y - grabPoint.Y;
        if (maximized)
        {
            if (Math.Abs(dx) + Math.Abs(dy) < 6) return;
            // restored to its normal size, under the pointer at the same share of its width
            var place = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(grabbed, ref place)) return;
            int w = place.rcNormalPosition.Right - place.rcNormalPosition.Left, h = place.rcNormalPosition.Bottom - place.rcNormalPosition.Top;
            double share = (grabPoint.X - grabRect.Left) / (double)Math.Max(1, grabRect.Right - grabRect.Left);
            int left = pt.X - (int)(w * share), top = pt.Y - Math.Min(grabPoint.Y - grabRect.Top, h / 2);
            ShowWindowAsync(grabbed, SW_RESTORE);
            grabRect = new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
            grabPoint = pt;
            maximized = false;
            dx = dy = 0;
        }
        SetWindowPos(grabbed, IntPtr.Zero, grabRect.Left + dx, grabRect.Top + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }

    /// <summary>The top-level window under the point, if it's one that can be moved (not the desktop or the taskbar,
    /// nor a window of a program with more rights than ours, which Windows doesn't let us move).</summary>
    private static IntPtr Target(POINT pt)
    {
        var w = WindowFromPoint(pt);
        if (w == IntPtr.Zero) return IntPtr.Zero;
        w = GetAncestor(w, GA_ROOT);
        if (w == IntPtr.Zero || !IsWindowVisible(w)) return IntPtr.Zero;
        var cls = new StringBuilder(64);
        GetClassName(w, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return IntPtr.Zero;
        // fails for a window Windows won't let us move (UIPI)
        if (!SetWindowPos(w, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS)) return IntPtr.Zero;
        return w;
    }

    /// <summary>How long a held key's repeats can take to come (Windows' repeat delay, and some to spare).</summary>
    private static uint RepeatWait()
    {
        int delay = 1;
        SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, ref delay, 0);
        return (uint)((Math.Clamp(delay, 0, 3) + 1) * 250 + 400);
    }

    private static bool Modifiers() =>
        Down(VK_SHIFT) || Down(VK_CONTROL) || Down(VK_MENU) || Down(VK_LWIN) || Down(VK_RWIN);

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static INPUT Input(ushort vk, ushort scan, bool extended, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0) } },
    };

    private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    // Windows' calls

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    private const uint WM_QUIT = 0x12;
    private const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x01;
    private const ushort VK_SPACE = 0x20;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const uint GA_ROOT = 2;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_ASYNCWINDOWPOS = 0x4000;
    private const uint SPI_GETKEYBOARDDELAY = 0x16;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi; // (the union's size)
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT place);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
