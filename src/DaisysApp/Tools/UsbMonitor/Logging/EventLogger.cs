using System.IO;
using DaisysApp.Tools.UsbMonitor.Models;

namespace DaisysApp.Tools.UsbMonitor.Logging;

/// <summary>Writes one tab-delimited row per event to a fresh log file for this launch, flushing every write.</summary>
public sealed class EventLogger : IDisposable
{
    private static readonly string LogDir =
        AppPaths.LogFolder;

    private static readonly string[] Header =
    {
        "Timestamp", "Event", "DeviceInstanceId", "DevicePath", "FriendlyName", "DeviceDescription",
        "Manufacturer", "VID", "PID", "Revision", "SerialNumber", "DeviceClass", "ClassGuid",
        "Service", "DriverKey", "Location", "PhysicalDeviceObjectName", "Enumerator", "ContainerId",
        "HardwareIds", "CompatibleIds", "Status", "ProblemCode"
    };

    private readonly StreamWriter _writer;

    public string LogFilePath { get; }

    public EventLogger()
    {
        Directory.CreateDirectory(LogDir);
        string fileName = $"usbmon_{DateTime.Now:yyyy-MM-dd_HHmmss}.log";
        LogFilePath = Path.Combine(LogDir, fileName);

        _writer = new StreamWriter(new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
        _writer.WriteLine(string.Join('\t', Header));
    }

    public void Log(DeviceRecord record)
    {
        var info = record.Info;
        string[] fields =
        {
            record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            record.EventLabel,
            info.InstanceId,
            info.DevicePath ?? "",
            info.FriendlyName ?? "",
            info.DeviceDescription ?? "",
            info.Manufacturer ?? "",
            info.VendorId ?? "",
            info.ProductId ?? "",
            info.Revision ?? "",
            info.SerialNumber ?? "",
            info.DeviceClass ?? "",
            info.ClassGuid ?? "",
            info.Service ?? "",
            info.DriverKey ?? "",
            info.LocationInformation ?? "",
            info.PhysicalDeviceObjectName ?? "",
            info.Enumerator ?? "",
            info.ContainerId ?? "",
            info.HardwareIds ?? "",
            info.CompatibleIds ?? "",
            info.Status,
            info.ProblemCode.ToString()
        };

        try
        {
            _writer.WriteLine(string.Join('\t', fields.Select(Sanitize)));
        }
        catch
        {
            // Never let a logging failure take down capture/UI.
        }
    }

    private static string Sanitize(string field) => field.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public void Dispose()
    {
        _writer.Dispose();
    }
}
