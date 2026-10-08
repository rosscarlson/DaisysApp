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
        yield return (T("Device Instance ID"), InstanceId);
        yield return (T("Device Path"), DevicePath ?? T("(unavailable)"));
        yield return (T("Friendly Name"), FriendlyName ?? T("(unavailable)"));
        yield return (T("Device Description"), DeviceDescription ?? T("(unavailable)"));
        yield return (T("Manufacturer"), Manufacturer ?? T("(unavailable)"));
        yield return (T("Vendor ID (VID)"), VendorId ?? T("(unavailable)"));
        yield return (T("Product ID (PID)"), ProductId ?? T("(unavailable)"));
        yield return (T("Revision"), Revision ?? T("(unavailable)"));
        yield return (T("Serial Number"), SerialNumber ?? T("(unavailable)"));
        yield return (T("Device Class"), DeviceClass ?? T("(unavailable)"));
        yield return (T("Class GUID"), ClassGuid ?? T("(unavailable)"));
        yield return (T("Driver Service"), Service ?? T("(unavailable)"));
        yield return (T("Driver Key"), DriverKey ?? T("(unavailable)"));
        yield return (T("Location"), LocationInformation ?? T("(unavailable)"));
        yield return (T("Physical Device Object"), PhysicalDeviceObjectName ?? T("(unavailable)"));
        yield return (T("Enumerator"), Enumerator ?? T("(unavailable)"));
        yield return (T("Container ID"), ContainerId ?? T("(unavailable)"));
        yield return (T("Hardware IDs"), HardwareIds ?? T("(unavailable)"));
        yield return (T("Compatible IDs"), CompatibleIds ?? T("(unavailable)"));
        yield return (T("Status"), Status);
        yield return (T("Problem Code"), ProblemCode == 0 ? T("0 (none)") : ProblemCode.ToString());
    }
}
