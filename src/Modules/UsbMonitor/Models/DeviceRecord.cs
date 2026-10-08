namespace DaisysApp.Applets.UsbMonitor.Models;

/// <summary>
/// One row in the event list / log file: an event that happened to a device, plus
/// the fullest snapshot of that device's properties available at the time.
/// </summary>
public sealed class DeviceRecord
{
    public required DateTime Timestamp { get; init; }
    public required DeviceEventType EventType { get; init; }
    public required DeviceInfo Info { get; init; }

    public string EventLabel => EventType switch
    {
        DeviceEventType.Arrived => T("Connected"),
        DeviceEventType.Removed => T("Disconnected"),
        DeviceEventType.StatusChanged => T("Status changed"),
        _ => EventType.ToString()
    };
}
