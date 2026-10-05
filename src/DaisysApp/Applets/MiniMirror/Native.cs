using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DaisysApp.Applets.MiniMirror;

/// <summary>
/// Places WPF windows in physical-pixel desktop coordinates with SetWindowPos instead of WPF's DIP-based Left/Top, so
/// placement stays exact on mixed-DPI monitor setups (the app is Per-Monitor-V2 DPI aware).
/// </summary>
internal static class WindowPlacement
{
    public static IntPtr EnsureHandle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    public static void SetPhysicalBounds(Window window, PixelRect rect) =>
        Native.SetWindowPos(EnsureHandle(window), IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOSENDCHANGING);

    public static PixelRect GetPhysicalBounds(Window window)
    {
        Native.GetWindowRect(EnsureHandle(window), out var r);
        return PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static PixelPoint GetCursorPosition()
    {
        Native.GetCursorPos(out var p);
        return new PixelPoint(p.X, p.Y);
    }

    /// <summary>Mouse clicks pass through the window to whatever is underneath.</summary>
    public static void SetClickThrough(Window window, bool enabled)
    {
        var hwnd = EnsureHandle(window);
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_LAYERED;
        ex = enabled ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Keeps the window out of Alt+Tab and the taskbar.</summary>
    public static void MakeToolWindow(Window window)
    {
        var hwnd = EnsureHandle(window);
        long ex = (Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>
    /// Leaves the window out of screen capture, so a mirror sitting over the area it mirrors shows what's beneath it
    /// instead of an endless tunnel of itself. Needs Windows 10 2004 or later; does nothing on older versions.
    /// </summary>
    public static void ExcludeFromCapture(Window window) =>
        Native.SetWindowDisplayAffinity(EnsureHandle(window), Native.WDA_EXCLUDEFROMCAPTURE);
}

/// <summary>Displays in physical-pixel desktop coordinates, with per-monitor DPI. Never cached: displays come and go.</summary>
internal static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var b = screen.Bounds;
            var rect = new Native.RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
            double scale = 1.0;
            var monitor = Native.MonitorFromRect(ref rect, Native.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero && Native.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpi, out _) == 0)
                scale = dpi / 96.0;
            result.Add(new MonitorInfo
            {
                DeviceName = screen.DeviceName,
                Bounds = new PixelRect(b.X, b.Y, b.Width, b.Height),
                IsPrimary = screen.Primary,
                DpiScale = scale,
            });
        }
        return result;
    }

    public static IEnumerable<MonitorInfo> GetMonitorsIntersecting(PixelRect rect) => GetMonitors().Where(m => m.Bounds.IntersectsWith(rect));

    /// <summary>The monitor's current refresh rate in Hz, or 60 if it can't be read.</summary>
    public static int GetRefreshRateHz(string deviceName)
    {
        var mode = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
        return Native.EnumDisplaySettings(deviceName, Native.ENUM_CURRENT_SETTINGS, ref mode) && mode.dmDisplayFrequency > 1
            ? mode.dmDisplayFrequency
            : 60;
    }
}

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x00040000;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOSENDCHANGING = 0x0400;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE mode);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
