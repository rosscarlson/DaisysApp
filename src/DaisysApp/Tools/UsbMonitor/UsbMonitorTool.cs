using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Shell;
using DaisysApp.Tools.UsbMonitor.Capture;
using DaisysApp.Tools.UsbMonitor.Logging;
using DaisysApp.Tools.UsbMonitor.Models;

namespace DaisysApp.Tools.UsbMonitor;

/// <summary>USB Monitor tab: logs device connect/disconnect activity for as long as the app runs.</summary>
public sealed class UsbMonitorTool : ITool
{
    private readonly UsbMonitorSettings settings = UsbMonitorSettings.Load();
    private readonly UsbMonitorView view;
    private readonly UsbMonitorSettingsView settingsView;
    private DeviceWatcher? watcher;
    private EventLogger? logger;

    public UsbMonitorTool()
    {
        view = new UsbMonitorView(settings);
        settingsView = new UsbMonitorSettingsView(this, settings);
    }

    public string Id => "UsbMonitor";
    public string Title => "USB Monitor";
    public string Icon => "";
    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    /// <summary>The log file being written, or null when logging is off (or couldn't start).</summary>
    public string? LogFilePath => logger?.LogFilePath;

    /// <summary>Raised when logging starts or stops.</summary>
    public event Action? LoggingChanged;

    public void Start()
    {
        SetLogging(settings.LogToFile);

        watcher = new DeviceWatcher();
        watcher.DeviceEventCaptured += OnDeviceEventCaptured;
        watcher.Start();
    }

    private void OnDeviceEventCaptured(DeviceRecord record)
    {
        view.AddRecord(record);
        logger?.Log(record);
    }

    /// <summary>Turns the log file on or off. Turning it on starts a new file.</summary>
    public void SetLogging(bool on)
    {
        settings.LogToFile = on;
        string? error = null;
        if (on && logger == null)
        {
            try { logger = new EventLogger(); }
            catch (Exception ex)
            {
                ErrorLog.Write("UsbMonitorTool.SetLogging", ex);
                error = "Couldn't start the log file: " + ex.Message;
            }
        }
        else if (!on && logger != null)
        {
            logger.Dispose();
            logger = null;
        }
        view.SetLogStatus(logger?.LogFilePath, error);
        LoggingChanged?.Invoke();
    }

    public void SaveSettings()
    {
        view.StoreLayout(settings);
        settings.Save();
    }

    public void Dispose()
    {
        watcher?.Dispose();
        logger?.Dispose();
        logger = null;
    }
}
