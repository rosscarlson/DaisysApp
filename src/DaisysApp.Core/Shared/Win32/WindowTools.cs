using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DaisysApp.Shared.Win32;

/// <summary>
/// Windows in physical pixels with SetWindowPos instead of WPF's DIP-based Left/Top, so placement stays exact on
/// mixed-DPI monitor setups (the app is Per-Monitor-V2 DPI aware); and the styles floating windows need.
/// </summary>
public static class WindowTools
{
    public const long WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    /// <summary><paramref name="silent"/>: without telling the window first (WM_WINDOWPOSCHANGING), for one moved every frame.</summary>
    public static void SetBounds(Window w, PixelRect r, bool silent = false) =>
        SetWindowPos(Handle(w), IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE | (silent ? SWP_NOSENDCHANGING : 0));

    public static PixelRect GetBounds(Window w) => GetBounds(Handle(w));

    public static PixelRect GetBounds(IntPtr hwnd)
    {
        TryGetBounds(hwnd, out var r);
        return r;
    }

    public static bool TryGetBounds(IntPtr hwnd, out PixelRect bounds)
    {
        bool ok = GetWindowRect(hwnd, out var r);
        bounds = PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return ok;
    }

    public static PixelPoint Cursor()
    {
        GetCursorPos(out var p);
        return new PixelPoint(p.X, p.Y);
    }

    /// <summary>Sets and clears extended style bits.</summary>
    public static void ChangeExStyle(IntPtr hwnd, long set, long clear = 0)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr((ex | set) & ~clear));
    }

    /// <summary>Mouse clicks pass through the window to whatever is underneath.</summary>
    public static void SetClickThrough(Window w, bool on) =>
        ChangeExStyle(Handle(w), on ? WS_EX_LAYERED | WS_EX_TRANSPARENT : WS_EX_LAYERED, on ? 0 : WS_EX_TRANSPARENT);

    /// <summary>Clicking the window doesn't make it the active window, so whatever had the focus (a game) keeps it.</summary>
    public static void MakeNoActivate(Window w) => ChangeExStyle(Handle(w), WS_EX_NOACTIVATE);

    /// <summary>Keeps the window out of Alt+Tab and the taskbar.</summary>
    public static void MakeToolWindow(Window w) => ChangeExStyle(Handle(w), WS_EX_TOOLWINDOW, WS_EX_APPWINDOW);

    /// <summary>Leaves the window out of screen capture (recordings, screenshots); Windows 10 2004 or later.</summary>
    public static void ExcludeFromCapture(Window w, bool exclude = true) =>
        SetWindowDisplayAffinity(Handle(w), exclude ? WDA_EXCLUDEFROMCAPTURE : 0);

    /// <summary>Back on top of everything (a game going full screen can push it down).</summary>
    public static void BringToTop(Window w) =>
        SetWindowPos(Handle(w), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Brings another program's window forward, restored if it was minimized.</summary>
    public static void SwitchTo(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
    }

    /// <summary>Windows' Properties window for a file.</summary>
    public static void ShowProperties(string path)
    {
        var info = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), lpVerb = "properties", lpFile = path, nShow = 1, fMask = 0x0000000C }; // SEE_MASK_INVOKEIDLIST
        ShellExecuteEx(ref info);
    }

    private const int GWL_EXSTYLE = -20;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOSENDCHANGING = 0x400;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
}
