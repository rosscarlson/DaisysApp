using System.Text;
using System.Text.RegularExpressions;
using DaisysApp.Tools.UsbMonitor.Models;
using static DaisysApp.Tools.UsbMonitor.Capture.NativeMethods;

namespace DaisysApp.Tools.UsbMonitor.Capture;

/// <summary>
/// Reads device node properties via SetupAPI/CfgMgr32 and takes point-in-time
/// snapshots of everything enumerated under the USB enumerator branch, so
/// devices that never register an interface (failed enumeration, Code 43, etc.)
/// are still caught by diffing snapshots against each other.
/// </summary>
internal static class DeviceSnapshot
{
    private static readonly Regex VidPidRevRegex = new(
        @"VID_(?<vid>[0-9A-Fa-f]{4})(&PID_(?<pid>[0-9A-Fa-f]{4}))?(&REV_(?<rev>[0-9A-Fa-f]{4}))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Enumerates every device node currently registered under the "USB" enumerator branch.</summary>
    public static Dictionary<string, DeviceInfo> TakeUsbSnapshot()
    {
        var result = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);

        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_ALLCLASSES);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return result;
        }

        try
        {
            uint index = 0;
            var did = new SP_DEVINFO_DATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<SP_DEVINFO_DATA>() };
            while (SetupDiEnumDeviceInfo(deviceInfoSet, index, ref did))
            {
                var info = ReadDeviceInfo(deviceInfoSet, ref did);
                if (info != null)
                {
                    result[info.InstanceId] = info;
                }
                index++;
                did = new SP_DEVINFO_DATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<SP_DEVINFO_DATA>() };
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return result;
    }

    /// <summary>Looks up a single device by instance id, for enriching an interface arrival/removal event.</summary>
    public static DeviceInfo? TryGetByInstanceId(string instanceId)
    {
        IntPtr deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            var did = new SP_DEVINFO_DATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<SP_DEVINFO_DATA>() };
            if (!SetupDiOpenDeviceInfo(deviceInfoSet, instanceId, IntPtr.Zero, 0, ref did))
            {
                return null;
            }
            return ReadDeviceInfo(deviceInfoSet, ref did);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static DeviceInfo? ReadDeviceInfo(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA did)
    {
        var instanceIdSb = new StringBuilder(1024);
        if (!SetupDiGetDeviceInstanceId(deviceInfoSet, ref did, instanceIdSb, instanceIdSb.Capacity, out _))
        {
            return null; // can't identify the device at all; nothing useful to log
        }

        var info = new DeviceInfo { InstanceId = instanceIdSb.ToString() };

        info.DeviceDescription = GetStringProperty(deviceInfoSet, ref did, SPDRP_DEVICEDESC);
        info.FriendlyName = GetStringProperty(deviceInfoSet, ref did, SPDRP_FRIENDLYNAME);
        info.Manufacturer = GetStringProperty(deviceInfoSet, ref did, SPDRP_MFG);
        info.DeviceClass = GetStringProperty(deviceInfoSet, ref did, SPDRP_CLASS);
        info.Service = GetStringProperty(deviceInfoSet, ref did, SPDRP_SERVICE);
        info.DriverKey = GetStringProperty(deviceInfoSet, ref did, SPDRP_DRIVER);
        info.LocationInformation = GetStringProperty(deviceInfoSet, ref did, SPDRP_LOCATION_INFORMATION);
        info.PhysicalDeviceObjectName = GetStringProperty(deviceInfoSet, ref did, SPDRP_PHYSICAL_DEVICE_OBJECT_NAME);
        info.Enumerator = GetStringProperty(deviceInfoSet, ref did, SPDRP_ENUMERATOR_NAME);

        var classGuidStr = GetStringProperty(deviceInfoSet, ref did, SPDRP_CLASSGUID);
        info.ClassGuid = string.IsNullOrEmpty(classGuidStr) ? did.ClassGuid.ToString("B") : classGuidStr;

        var hardwareIds = GetMultiStringProperty(deviceInfoSet, ref did, SPDRP_HARDWAREID);
        info.HardwareIds = hardwareIds.Count > 0 ? string.Join(" ; ", hardwareIds) : null;

        var compatibleIds = GetMultiStringProperty(deviceInfoSet, ref did, SPDRP_COMPATIBLEIDS);
        info.CompatibleIds = compatibleIds.Count > 0 ? string.Join(" ; ", compatibleIds) : null;

        foreach (var hwid in hardwareIds)
        {
            var m = VidPidRevRegex.Match(hwid);
            if (m.Success)
            {
                info.VendorId ??= m.Groups["vid"].Success ? m.Groups["vid"].Value.ToUpperInvariant() : null;
                info.ProductId ??= m.Groups["pid"].Success ? m.Groups["pid"].Value.ToUpperInvariant() : null;
                info.Revision ??= m.Groups["rev"].Success ? m.Groups["rev"].Value.ToUpperInvariant() : null;
            }
        }

        info.SerialNumber = TryExtractSerial(info.InstanceId);

        try
        {
            int cr = CM_Get_DevNode_Status(out int status, out int problemNumber, did.DevInst, 0);
            if (cr == CR_SUCCESS)
            {
                info.StatusKnown = true;
                info.ProblemCode = problemNumber;
                info.Status = problemNumber == 0 ? "OK" : $"Problem (Code {problemNumber})";
            }
            else
            {
                info.StatusKnown = false;
                info.Status = "Unavailable";
            }
        }
        catch
        {
            info.StatusKnown = false;
            info.Status = "Unavailable";
        }

        return info;
    }

    /// <summary>
    /// The third segment of a USB instance id (USB\VID_x&PID_y\THIRD) is a real
    /// serial number only if the device actually reported one; Windows otherwise
    /// synthesizes a location-based id there that contains '&amp;' characters.
    /// </summary>
    private static string? TryExtractSerial(string instanceId)
    {
        var parts = instanceId.Split('\\');
        if (parts.Length < 3) return null;
        var candidate = parts[2];
        if (string.IsNullOrEmpty(candidate)) return null;
        return candidate.Contains('&') ? null : candidate;
    }

    private static string? GetStringProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA did, int property)
    {
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref did, (uint)property, out _, null, 0, out uint requiredSize) && requiredSize == 0)
        {
            return null;
        }

        var buffer = new byte[requiredSize];
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref did, (uint)property, out _, buffer, (uint)buffer.Length, out _))
        {
            return null;
        }

        string value = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<string> GetMultiStringProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA did, int property)
    {
        var list = new List<string>();
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref did, (uint)property, out _, null, 0, out uint requiredSize) && requiredSize == 0)
        {
            return list;
        }

        var buffer = new byte[requiredSize];
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref did, (uint)property, out _, buffer, (uint)buffer.Length, out _))
        {
            return list;
        }

        string all = Encoding.Unicode.GetString(buffer);
        foreach (var s in all.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            list.Add(s);
        }
        return list;
    }
}
