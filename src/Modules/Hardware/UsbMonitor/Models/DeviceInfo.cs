using System.Text.Json.Serialization;

namespace DaisysApp.Applets.Hardware.UsbMonitor.Models;

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
    /// <summary>The physical path to the port, e.g. PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3): the same for whatever is plugged in there.</summary>
    public string? LocationPaths { get; set; }
    /// <summary>The hub (or controller) it hangs off.</summary>
    public string? ParentInstanceId { get; set; }
    /// <summary>When Windows last saw it connect and disconnect (Windows keeps these for devices that are unplugged too).</summary>
    public DateTime? LastArrival { get; set; }
    public DateTime? LastRemoval { get; set; }

    public bool StatusKnown { get; set; }
    public string Status { get; set; } = "Unavailable";
    public int ProblemCode { get; set; }

    /// <summary>What Windows' problem code means, or null for none.</summary>
    [JsonIgnore]
    public string? ProblemText => ProblemCode == 0 ? null : DescribeProblem(ProblemCode);

    public static string DescribeProblem(int code) => code switch
    {
        1 => T("not set up properly"),
        3 => T("its driver may be damaged, or Windows is short of memory"),
        10 => T("the device can't start"),
        12 => T("not enough free resources"),
        14 => T("needs a restart"),
        18 => T("its drivers need reinstalling"),
        19 => T("its configuration in the registry is damaged"),
        21 => T("Windows is removing it"),
        22 => T("it's disabled"),
        24 => T("not present, not working, or missing drivers"),
        28 => T("no driver installed"),
        29 => T("disabled by the firmware"),
        31 => T("its driver couldn't load"),
        32 => T("its driver's service is disabled"),
        37 => T("its driver failed to start"),
        39 => T("its driver is missing or damaged"),
        41 => T("its driver loaded but can't find the device"),
        43 => T("Windows stopped it because it reported problems (on USB usually a failed handshake: the cable, the port, its power or the device itself)"),
        45 => T("not connected"),
        47 => T("prepared for safe removal"),
        48 => T("its driver is blocked"),
        52 => T("its driver's signature can't be checked"),
        _ => F("problem code {0}", code),
    };

    [JsonIgnore]
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
        yield return (T("Location Paths"), LocationPaths ?? T("(unavailable)"));
        yield return (T("Parent (hub)"), ParentInstanceId ?? T("(unavailable)"));
        yield return (T("Windows: last connected"), LastArrival?.ToString("yyyy-MM-dd HH:mm:ss") ?? T("(unavailable)"));
        yield return (T("Windows: last disconnected"), LastRemoval?.ToString("yyyy-MM-dd HH:mm:ss") ?? T("(unavailable)"));
        yield return (T("Hardware IDs"), HardwareIds ?? T("(unavailable)"));
        yield return (T("Compatible IDs"), CompatibleIds ?? T("(unavailable)"));
        yield return (T("Status"), Status);
        yield return (T("Problem Code"), ProblemCode == 0 ? T("0 (none)") : $"{ProblemCode}: {ProblemText}");
    }
}
