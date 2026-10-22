using System.Runtime.InteropServices;
using DaisysApp.Logging;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>A Bluetooth device paired with this PC.</summary>
internal sealed record BtDevice(string Address, string Name, bool Connected, uint ClassOfDevice)
{
    /// <summary>Headsets, speakers and other audio devices (the major class in the class of device).</summary>
    public bool IsAudio => ((ClassOfDevice >> 8) & 0x1F) == 0x04;
}

/// <summary>
/// The paired (classic) Bluetooth devices, and connecting or disconnecting them. Audio devices are connected the way
/// Sound settings' Connect does it: the Bluetooth audio driver's one-shot reconnect / disconnect, through the device's
/// audio endpoints. Other devices have their Bluetooth services switched off and on again, which drops the connection
/// and lets the device connect again.
/// </summary>
internal static class Bluetooth
{
    private static readonly Logger log = Log.Here;

    /// <summary>Every paired device (empty when there's no Bluetooth, or it's off).</summary>
    public static List<BtDevice> Paired()
    {
        var list = new List<BtDevice>();
        var search = new SearchParams
        {
            dwSize = Marshal.SizeOf<SearchParams>(),
            fReturnAuthenticated = true, fReturnRemembered = true, fReturnConnected = true, fReturnUnknown = false,
            fIssueInquiry = false, cTimeoutMultiplier = 0, hRadio = IntPtr.Zero,
        };
        var info = new DeviceInfo { dwSize = Marshal.SizeOf<DeviceInfo>() };
        IntPtr find;
        try { find = BluetoothFindFirstDevice(ref search, ref info); }
        catch (DllNotFoundException) { return list; }
        if (find == IntPtr.Zero) return list;
        try
        {
            do
            {
                string address = info.Address.ToString("X12");
                if (!list.Any(d => d.Address == address))
                    list.Add(new BtDevice(address, string.IsNullOrWhiteSpace(info.szName) ? address : info.szName, info.fConnected, info.ulClassofDevice));
                info = new DeviceInfo { dwSize = Marshal.SizeOf<DeviceInfo>() };
            } while (BluetoothFindNextDevice(find, ref info));
        }
        finally { BluetoothFindDeviceClose(find); }
        return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Connects or disconnects a device; false if Windows wouldn't. Slow (seconds): call it off the UI thread.</summary>
    public static bool Set(BtDevice device, bool connect)
    {
        log.Info($"{(connect ? "Connecting" : "Disconnecting")} {device.Name} ({device.Address})");
        if (AudioOneShot(device, connect)) return true;
        return ToggleServices(device);
    }

    // ---- audio: KSPROPERTY_ONESHOT_RECONNECT / DISCONNECT on the Bluetooth audio driver's filter ----

    private static readonly Guid KsPropSetBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private static readonly Guid IidKsControl = new("28F54685-06FD-11D2-B27A-00A0C9223196");

    private static bool AudioOneShot(BtDevice device, bool connect)
    {
        // the filters behind the device's endpoints (its A2DP and hands-free ones), found by the address in their path
        var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var enumerator = new MMDeviceEnumerator())
        {
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active | DeviceState.Unplugged))
            {
                try
                {
                    var topology = endpoint.DeviceTopology;
                    if (topology.ConnectorCount == 0) continue;
                    string filter = topology.GetConnector(0).ConnectedToDeviceId;
                    if (filter.Contains("bth", StringComparison.OrdinalIgnoreCase) && filter.Contains(device.Address, StringComparison.OrdinalIgnoreCase))
                        filters.Add(filter);
                }
                catch { } // an endpoint without a topology
                finally { endpoint.Dispose(); }
            }
        }
        if (filters.Count == 0) return false;

        bool done = false;
        var mm = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        try
        {
            foreach (var id in filters)
            {
                try
                {
                    if (mm.GetDevice(id, out var dev) != 0 || dev == null) continue;
                    var iid = IidKsControl;
                    if (dev.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var obj) != 0 || obj is not IKsControl ks) continue;
                    var prop = new KsProperty { Set = KsPropSetBtAudio, Id = connect ? 0 : 1, Flags = 1 /* GET */ };
                    int hr = ks.KsProperty(ref prop, Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                    log.Debug($"One-shot {(connect ? "reconnect" : "disconnect")} on {id}: 0x{hr:X8}");
                    if (hr >= 0) done = true;
                    Marshal.ReleaseComObject(ks);
                    Marshal.ReleaseComObject(dev);
                }
                catch (Exception ex) { log.Debug($"One-shot on {id} failed: {ex.Message}"); }
            }
        }
        finally { Marshal.ReleaseComObject(mm); }
        return done;
    }

    // ---- anything else: its services off and on again ----

    private static bool ToggleServices(BtDevice device)
    {
        var info = new DeviceInfo { dwSize = Marshal.SizeOf<DeviceInfo>(), Address = Convert.ToUInt64(device.Address, 16) };
        uint count = 16;
        var services = new Guid[count];
        if (BluetoothEnumerateInstalledServices(IntPtr.Zero, ref info, ref count, services) != 0 || count == 0)
        {
            log.Warn($"{device.Name} has no Bluetooth services to switch");
            return false;
        }
        bool ok = true;
        for (int i = 0; i < count; i++)
        {
            var s = services[i];
            ok &= BluetoothSetServiceState(IntPtr.Zero, ref info, ref s, 0) == 0;
        }
        for (int i = 0; i < count; i++)
        {
            var s = services[i];
            ok &= BluetoothSetServiceState(IntPtr.Zero, ref info, ref s, 1) == 0;
        }
        if (!ok) log.Warn($"Couldn't switch every Bluetooth service of {device.Name}");
        return ok;
    }

    // ---- interop ----

    [StructLayout(LayoutKind.Sequential)]
    private struct SearchParams
    {
        public int dwSize;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceInfo
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SystemTime stLastSeen;
        public SystemTime stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty { public Guid Set; public int Id; public int Flags; }

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref SearchParams search, ref DeviceInfo info);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref DeviceInfo info);

    [DllImport("BluetoothApis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

    [DllImport("BluetoothApis.dll")]
    private static extern uint BluetoothEnumerateInstalledServices(IntPtr radio, ref DeviceInfo info, ref uint count, [Out] Guid[] services);

    [DllImport("BluetoothApis.dll")]
    private static extern uint BluetoothSetServiceState(IntPtr radio, ref DeviceInfo info, ref Guid service, uint flags);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, int propertyLength, IntPtr data, int dataLength, out int bytesReturned);
    }
}
