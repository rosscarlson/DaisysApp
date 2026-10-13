using System.Text.Json.Serialization;

namespace DaisysApp.Applets.Hardware.UsbMonitor.Models;

/// <summary>
/// A USB device seen at some time: what it was last seen as, what it was when it last worked, and when it came and
/// went. Kept in %APPDATA%\DaisysApp\UsbDevices.json (<see cref="KnownDeviceStore"/>).
/// </summary>
public sealed class KnownDevice
{
    public string InstanceId { get; set; } = "";
    /// <summary>Everything read about it the last time it was seen.</summary>
    public DeviceInfo Info { get; set; } = new();
    /// <summary>Everything read about it the last time it was working (connected, no problem), if it ever was.</summary>
    public DeviceInfo? LastGood { get; set; }

    public bool Connected { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime? LastConnected { get; set; }
    public DateTime? LastDisconnected { get; set; }
    public DateTime? LastWorking { get; set; }
    public DateTime? LastProblem { get; set; }
    public int LastProblemCode { get; set; }
    public int Events { get; set; }
    /// <summary>Only known from Windows' own records (an unplugged device it remembers), not seen by Daisy's App yet.</summary>
    public bool FromWindows { get; set; }

    [JsonIgnore]
    public string Name => (LastGood is { } g && !string.IsNullOrWhiteSpace(g.FriendlyName ?? g.DeviceDescription) ? g : Info).DisplayName;

    /// <summary>Connected with a problem now.</summary>
    [JsonIgnore]
    public bool HasProblem => Connected && Info.ProblemCode != 0;

    /// <summary>Ever seen working (by Daisy's App, or installed with a driver as Windows remembers it).</summary>
    [JsonIgnore]
    public bool KnownGood => LastGood != null;
}
