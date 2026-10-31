using System.Runtime.InteropServices;

namespace DaisysApp.Applets.Video.MiniMirror;

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
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

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
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
