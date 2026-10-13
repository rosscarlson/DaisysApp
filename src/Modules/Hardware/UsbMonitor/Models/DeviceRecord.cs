namespace DaisysApp.Applets.Hardware.UsbMonitor.Models;

/// <summary>
/// One row in the event list / log file: an event that happened to a device, plus
/// the fullest snapshot of that device's properties available at the time.
/// </summary>
public sealed class DeviceRecord
{
    public required DateTime Timestamp { get; init; }
    public required DeviceEventType EventType { get; init; }
    public required DeviceInfo Info { get; init; }

    /// <summary>
    /// Something wrong: Windows reports a problem with it, or it appeared but couldn't be read. Shown bold red, and
    /// matched against the devices known to have worked (<see cref="Analysis"/>).
    /// </summary>
    public bool IsProblem => Info.ProblemCode != 0 || (EventType == DeviceEventType.Arrived && !Info.StatusKnown);

    /// <summary>For a problem: what it means and which known, working device it most likely is.</summary>
    public string? Analysis { get; set; }
    /// <summary>For a problem: the name of the known device it most likely is.</summary>
    public string? LikelyDevice { get; set; }

    public string EventLabel => EventType switch
    {
        DeviceEventType.Arrived => T("Connected"),
        DeviceEventType.Removed => T("Disconnected"),
        DeviceEventType.StatusChanged => T("Status changed"),
        _ => EventType.ToString()
    };
}
