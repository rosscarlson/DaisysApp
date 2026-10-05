namespace DaisysApp.Applets.UsbMonitor.Models;

/// <summary>
/// Snapshot of everything readable about a device node at a point in time.
/// Any field that could not be read is left null/empty rather than dropping the record.
/// </summary>
public sealed class DeviceInfo
{
    public string InstanceId { get; set; } = "";
    public string? DevicePath { get; set; }
    public string? DeviceDescription { get; set; }
    public string? FriendlyName { get; set; }
    public string? Manufacturer { get; set; }
    public string? VendorId { get; set; }
    public string? ProductId { get; set; }
    public string? Revision { get; set; }
    public string? SerialNumber { get; set; }
    public string? DeviceClass { get; set; }
    public string? ClassGuid { get; set; }
    public string? Service { get; set; }
    public string? DriverKey { get; set; }
    public string? LocationInformation { get; set; }
    public string? PhysicalDeviceObjectName { get; set; }
    public string? Enumerator { get; set; }
    public string? HardwareIds { get; set; }
    public string? CompatibleIds { get; set; }
    public string? ContainerId { get; set; }

    public bool StatusKnown { get; set; }
    public string Status { get; set; } = "Unavailable";
    public int ProblemCode { get; set; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName! :
        !string.IsNullOrWhiteSpace(DeviceDescription) ? DeviceDescription! :
        InstanceId;

    public DeviceInfo Clone() => (DeviceInfo)MemberwiseClone();

    public IEnumerable<(string Key, string Value)> AllProperties()
    {
        yield return ("Device Instance ID", InstanceId);
        yield return ("Device Path", DevicePath ?? "(unavailable)");
        yield return ("Friendly Name", FriendlyName ?? "(unavailable)");
        yield return ("Device Description", DeviceDescription ?? "(unavailable)");
        yield return ("Manufacturer", Manufacturer ?? "(unavailable)");
        yield return ("Vendor ID (VID)", VendorId ?? "(unavailable)");
        yield return ("Product ID (PID)", ProductId ?? "(unavailable)");
        yield return ("Revision", Revision ?? "(unavailable)");
        yield return ("Serial Number", SerialNumber ?? "(unavailable)");
        yield return ("Device Class", DeviceClass ?? "(unavailable)");
        yield return ("Class GUID", ClassGuid ?? "(unavailable)");
        yield return ("Driver Service", Service ?? "(unavailable)");
        yield return ("Driver Key", DriverKey ?? "(unavailable)");
        yield return ("Location", LocationInformation ?? "(unavailable)");
        yield return ("Physical Device Object", PhysicalDeviceObjectName ?? "(unavailable)");
        yield return ("Enumerator", Enumerator ?? "(unavailable)");
        yield return ("Container ID", ContainerId ?? "(unavailable)");
        yield return ("Hardware IDs", HardwareIds ?? "(unavailable)");
        yield return ("Compatible IDs", CompatibleIds ?? "(unavailable)");
        yield return ("Status", Status);
        yield return ("Problem Code", ProblemCode == 0 ? "0 (none)" : ProblemCode.ToString());
    }
}
