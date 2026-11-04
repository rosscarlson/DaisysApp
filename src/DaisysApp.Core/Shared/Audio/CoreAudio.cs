using System.Runtime.InteropServices;

namespace DaisysApp.Shared.Audio;

/// <summary>
/// Windows' Core Audio devices by COM, without packages: the device enumerator, a device, its properties, and
/// activating one of its interfaces (its endpoint volume, its device topology, its kernel-streaming control). Core's
/// AppAudio and Audio's Bluetooth and mic gain use these (since Core 0.55: each declared its own). The enumerator is
/// made by its CLSID, not as a [ComImport] class: NAudio's class of the same CLSID would win the cast.
/// </summary>
public static class CoreAudio
{
    public const int CLSCTX_ALL = 0x17;
    public const int eRender = 0, eCapture = 1, eAll = 2;
    public const int DEVICE_STATE_ACTIVE = 1;

    private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>A new device enumerator.</summary>
    public static IMMDeviceEnumerator Enumerator() =>
        (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClsid)!)!;

    /// <summary>The device's interface <paramref name="iid"/> (IAudioEndpointVolume, IDeviceTopology, IKsControl…),
    /// or null if there's no such device or it hasn't one.</summary>
    public static object? Activate(string deviceId, Guid iid)
    {
        var e = Enumerator();
        try
        {
            if (e.GetDevice(deviceId, out var device) != 0 || device == null) return null;
            try { return device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var o) == 0 ? o : null; }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(e); }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey { public Guid Fmtid; public int Pid; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant { public ushort Vt, R1, R2, R3; public IntPtr Pointer, Pointer2; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant v);
}
