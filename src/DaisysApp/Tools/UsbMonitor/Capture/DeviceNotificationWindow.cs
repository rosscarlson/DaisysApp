using System.Runtime.InteropServices;
using System.Windows.Interop;
using static DaisysApp.Tools.UsbMonitor.Capture.NativeMethods;

namespace DaisysApp.Tools.UsbMonitor.Capture;

/// <summary>
/// A hidden top-level window that exists purely to receive WM_DEVICECHANGE.
/// This must be a real top-level window, not one parented to HWND_MESSAGE:
/// DBT_DEVNODES_CHANGED is delivered as a broadcast (like WM_SETTINGCHANGE),
/// and Windows does not deliver broadcasts to message-only windows. Device
/// interface arrival/removal (registered via RegisterDeviceNotification) would
/// work with a message-only window too, but using one real hidden window for
/// both keeps this simple and reliable.
/// </summary>
internal sealed class DeviceNotificationWindow : IDisposable
{
    private readonly HwndSource _source;
    private IntPtr _notificationHandle;

    public event Action<string, bool>? DeviceInterfaceChanged; // (devicePath, isArrival)
    public event Action? DeviceNodesChanged;

    public DeviceNotificationWindow()
    {
        var parameters = new HwndSourceParameters("DaisysApp.DeviceNotificationWindow")
        {
            WindowStyle = 0, // no WS_VISIBLE: a real, ownerless, top-level window that just never shows
            Width = 0,
            Height = 0,
            ParentWindow = IntPtr.Zero,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _notificationHandle = RegisterDeviceNotification(
            _source.Handle,
            IntPtr.Zero,
            DEVICE_NOTIFY_WINDOW_HANDLE | DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
        {
            HandleDeviceChange(wParam, lParam);
        }
        return IntPtr.Zero;
    }

    private void HandleDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        int eventCode = wParam.ToInt32();

        if (eventCode == DBT_DEVNODES_CHANGED)
        {
            DeviceNodesChanged?.Invoke();
            return;
        }

        if ((eventCode == DBT_DEVICEARRIVAL || eventCode == DBT_DEVICEREMOVECOMPLETE) && lParam != IntPtr.Zero)
        {
            var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
            if (hdr.dbch_devicetype == DBT_DEVTYP_DEVICEINTERFACE)
            {
                string path = ReadDeviceInterfaceName(lParam);
                DeviceInterfaceChanged?.Invoke(path, eventCode == DBT_DEVICEARRIVAL);
            }
        }
    }

    private static string ReadDeviceInterfaceName(IntPtr lParam)
    {
        // dbcc_name starts right after the fixed part of DEV_BROADCAST_DEVICEINTERFACE:
        // int size, int devicetype, int reserved, Guid classguid  => 4+4+4+16 = 28 bytes
        int nameOffset = sizeof(int) * 3 + 16;
        IntPtr namePtr = IntPtr.Add(lParam, nameOffset);
        return Marshal.PtrToStringAuto(namePtr) ?? "";
    }

    public void Dispose()
    {
        if (_notificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
