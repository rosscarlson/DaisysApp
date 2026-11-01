using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DaisysApp.Applets.GameDevices;

/// <summary>
/// Windows saying a HID device (a controller, among others) came or went, once things have been quiet for a moment:
/// listing controllers opens each, which a game feels, so it's done only then, never on a timer.
/// </summary>
internal sealed class HotPlug : IDisposable
{
    private const int WM_DEVICECHANGE = 0x219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_DEVICEINTERFACE = 5;
    private static readonly Guid Hid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    private struct Filter
    {
        public int Size, DeviceType, Reserved;
        public Guid ClassGuid;
        public short Name;
    }

    private readonly HwndSource window;
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private IntPtr handle;

    public HotPlug(HwndSource window, Action changed)
    {
        this.window = window;
        settle.Tick += (_, _) => { settle.Stop(); changed(); };
        var filter = new Filter { Size = Marshal.SizeOf<Filter>(), DeviceType = DBT_DEVTYP_DEVICEINTERFACE, ClassGuid = Hid };
        handle = RegisterDeviceNotificationW(window.Handle, ref filter, 0);
        if (handle == IntPtr.Zero) Logging.Log.Here.Warn($"Couldn't ask Windows about devices coming and going ({Marshal.GetLastWin32Error()})");
        window.AddHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE && wParam.ToInt64() is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
        {
            settle.Stop();
            settle.Start();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        settle.Stop();
        window.RemoveHook(Hook);
        if (handle != IntPtr.Zero) UnregisterDeviceNotification(handle);
        handle = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr RegisterDeviceNotificationW(IntPtr recipient, ref Filter filter, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
