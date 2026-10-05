using DaisysApp.Applets.UsbMonitor.Models;

namespace DaisysApp.Applets.UsbMonitor.ViewModels;

/// <summary>Pre-formatted, bindable view of a <see cref="DeviceRecord"/> for the event grid.</summary>
internal sealed class DeviceRecordRow
{
    public DeviceRecord Record { get; }

    public DeviceRecordRow(DeviceRecord record) => Record = record;

    public string Timestamp => Record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string Event => Record.EventLabel;
    public string Description => Record.Info.DisplayName;
    public string Manufacturer => Record.Info.Manufacturer ?? "";
    public string VendorId => Record.Info.VendorId ?? "";
    public string ProductId => Record.Info.ProductId ?? "";
    public string Revision => Record.Info.Revision ?? "";
    public string SerialNumber => Record.Info.SerialNumber ?? "";
    public string DeviceClass => Record.Info.DeviceClass ?? "";
    public string Service => Record.Info.Service ?? "";
    public string Location => Record.Info.LocationInformation ?? "";
    public string Status => Record.Info.Status;
    public string InstanceId => Record.Info.InstanceId;

    public bool IsRemoved => Record.EventType == DeviceEventType.Removed;
}
