using System.Runtime.InteropServices;

namespace DaisysApp.Tools.UsbMonitor.Capture;

internal static class NativeMethods
{
    // ---- Window messages ----
    public const int WM_DEVICECHANGE = 0x0219;

    // ---- DBT event codes (wParam of WM_DEVICECHANGE) ----
    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const int DBT_DEVNODES_CHANGED = 0x0007;

    // ---- DEV_BROADCAST types (dbch_devicetype) ----
    public const int DBT_DEVTYP_DEVICEINTERFACE = 5;

    // ---- RegisterDeviceNotification flags ----
    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
    public const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEV_BROADCAST_HDR
    {
        public int dbch_size;
        public int dbch_devicetype;
        public int dbch_reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;
        public Guid dbcc_classguid;
        public short dbcc_name; // first char of the variable-length name; read the rest manually
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterDeviceNotification(IntPtr handle);

    // ---- SetupAPI ----
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_ALLCLASSES = 0x00000004;

    public const int SPDRP_DEVICEDESC = 0x00000000;
    public const int SPDRP_HARDWAREID = 0x00000001;
    public const int SPDRP_COMPATIBLEIDS = 0x00000002;
    public const int SPDRP_SERVICE = 0x00000004;
    public const int SPDRP_CLASS = 0x00000007;
    public const int SPDRP_CLASSGUID = 0x00000008;
    public const int SPDRP_DRIVER = 0x00000009;
    public const int SPDRP_MFG = 0x0000000B;
    public const int SPDRP_FRIENDLYNAME = 0x0000000C;
    public const int SPDRP_LOCATION_INFORMATION = 0x0000000D;
    public const int SPDRP_PHYSICAL_DEVICE_OBJECT_NAME = 0x0000000E;
    public const int SPDRP_ENUMERATOR_NAME = 0x00000016;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA did, System.Text.StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiOpenDeviceInfo(IntPtr deviceInfoSet, string deviceInstanceId, IntPtr hwndParent, uint openFlags, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    // ---- CfgMgr32 ----
    [DllImport("cfgmgr32.dll", SetLastError = false)]
    public static extern int CM_Get_DevNode_Status(out int status, out int problemNumber, uint devInst, int flags);

    public const int CR_SUCCESS = 0;
}
