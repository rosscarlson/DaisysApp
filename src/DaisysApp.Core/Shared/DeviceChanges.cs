using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DaisysApp.Shared;

/// <summary>
/// Windows saying a device came or went, heard once for the whole app (since Core 0.55: before, USB Monitor and
/// Devices each had their own window for it, and others looked on a timer). One hidden top-level window (not a
/// message-only one: DBT_DEVNODES_CHANGED is a broadcast, which those don't get) registered for every device
/// interface. Everything is raised on the UI thread. <see cref="OnSettled"/> is what most want: a call once things
/// have been quiet for a moment after a change (a hub plugged in sends dozens).
/// </summary>
public static class DeviceChanges
{
    /// <summary>The HID interface class (controllers, wheels, pedals, button boxes, Fanatec rims…).</summary>
    public static readonly Guid Hid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    /// <summary>The COM port interface class (Arduinos, serial adapters).</summary>
    public static readonly Guid ComPort = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

    /// <summary>A device interface arrived (true) or was removed (false): its path and class.</summary>
    public static event Action<string, Guid, bool>? InterfaceChanged
    {
        add { Ensure(); interfaceChanged += value; }
        remove => interfaceChanged -= value;
    }

    /// <summary>Windows' device tree changed (a broadcast; also for devices that fail to start, which have no interface).</summary>
    public static event Action? NodesChanged
    {
        add { Ensure(); nodesChanged += value; }
        remove => nodesChanged -= value;
    }

    private static Action<string, Guid, bool>? interfaceChanged;
    private static Action? nodesChanged;

    /// <summary>
    /// Calls <paramref name="changed"/> (on the UI thread) once Windows has been quiet for <paramref name="quiet"/>
    /// after a device of <paramref name="classGuid"/> came or went (any class when null; with
    /// <paramref name="nodes"/>, the device tree changing counts too). Dispose to stop.
    /// </summary>
    public static IDisposable OnSettled(TimeSpan quiet, Action changed, Guid? classGuid = null, bool nodes = false)
    {
        Ensure();
        var ui = window!.Dispatcher;
        var d = ui.CheckAccess() ? new Debounce(quiet, changed) : ui.Invoke(() => new Debounce(quiet, changed));
        void Interface(string path, Guid c, bool arrived) { if (classGuid == null || c == classGuid) d.Poke(); }
        void Nodes() => d.Poke();
        interfaceChanged += Interface;
        if (nodes) nodesChanged += Nodes;
        return new Off(() =>
        {
            interfaceChanged -= Interface;
            if (nodes) nodesChanged -= Nodes;
            d.Dispose();
        });
    }

    private sealed class Off(Action off) : IDisposable
    {
        private Action? off = off;
        public void Dispose() => Interlocked.Exchange(ref off, null)?.Invoke();
    }

    private static HwndSource? window;
    private static IntPtr registration;

    // made on the UI thread the first time someone listens
    private static void Ensure()
    {
        if (window != null) return;
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(Ensure); return; }
        if (window != null) return;
        window = new HwndSource(new HwndSourceParameters("DaisysApp.DeviceChanges") { WindowStyle = 0, Width = 0, Height = 0 });
        window.AddHook(Hook);
        var filter = new Filter { Size = Marshal.SizeOf<Filter>(), DeviceType = DBT_DEVTYP_DEVICEINTERFACE };
        registration = RegisterDeviceNotificationW(window.Handle, ref filter, DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);
        if (registration == IntPtr.Zero) Logging.Log.App.Warn($"Couldn't ask Windows about devices coming and going ({Marshal.GetLastWin32Error()})");
        dispatcher.ShutdownStarted += (_, _) =>
        {
            if (registration != IntPtr.Zero) UnregisterDeviceNotification(registration);
            registration = IntPtr.Zero;
        };
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE) return IntPtr.Zero;
        try
        {
            int code = wParam.ToInt32();
            if (code == DBT_DEVNODES_CHANGED) nodesChanged?.Invoke();
            else if (code is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE && lParam != IntPtr.Zero
                && Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_DEVICEINTERFACE)
            {
                var guid = Marshal.PtrToStructure<Guid>(lParam + 12);
                string path = Marshal.PtrToStringUni(lParam + 28) ?? "";
                interfaceChanged?.Invoke(path, guid, code == DBT_DEVICEARRIVAL);
            }
        }
        catch (Exception ex) { Logging.Log.App.Warn("A device change", ex); }
        return IntPtr.Zero;
    }

    private const int WM_DEVICECHANGE = 0x219, DBT_DEVNODES_CHANGED = 0x7, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004,
        DBT_DEVTYP_DEVICEINTERFACE = 5, DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Filter
    {
        public int Size, DeviceType, Reserved;
        public Guid ClassGuid;
        public short Name;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr RegisterDeviceNotificationW(IntPtr recipient, ref Filter filter, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
