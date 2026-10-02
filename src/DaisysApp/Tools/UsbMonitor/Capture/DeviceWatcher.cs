using System.Windows.Threading;
using DaisysApp.Logging;
using DaisysApp.Tools.UsbMonitor.Models;

namespace DaisysApp.Tools.UsbMonitor.Capture;

/// <summary>
/// Combines interface arrival/removal notifications, broadcast-driven device-node
/// diffing, and a periodic poll (belt-and-suspenders in case either notification
/// path misses something) so both well-behaved devices and devices that fail to
/// enumerate are captured. Raises <see cref="DeviceEventCaptured"/> on the
/// dispatcher it was constructed on.
/// </summary>
internal sealed class DeviceWatcher : IDisposable
{
    private readonly DeviceNotificationWindow _window;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly Dispatcher _dispatcher;
    private readonly object _cacheLock = new();
    private Dictionary<string, DeviceInfo> _deviceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, DeviceEventType), DateTime> _recentEvents = new();
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(1.5);
    private bool _diffInFlight;

    public event Action<DeviceRecord>? DeviceEventCaptured;

    public DeviceWatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        _window = new DeviceNotificationWindow();
        _window.DeviceInterfaceChanged += OnDeviceInterfaceChanged;
        _window.DeviceNodesChanged += OnDeviceNodesChanged;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RunDiff();
        };

        // Fallback safety net: re-diff every few seconds regardless of whether any
        // notification fired, so a missed/undelivered Windows message never means
        // a plug/unplug goes completely unnoticed.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += (_, _) => RunDiff();
        _pollTimer.Start();
    }

    public void Start()
    {
        // Baseline snapshot: never fire events for what was already plugged in.
        Task.Run(() =>
        {
            try
            {
                var snapshot = DeviceSnapshot.TakeUsbSnapshot();
                lock (_cacheLock)
                {
                    _deviceCache = snapshot;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("DeviceWatcher.Start baseline snapshot", ex);
            }
        });
    }

    private void OnDeviceNodesChanged()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void RunDiff()
    {
        if (_diffInFlight) return;
        _diffInFlight = true;

        Task.Run(() =>
        {
            try
            {
                var fresh = DeviceSnapshot.TakeUsbSnapshot();
                Dictionary<string, DeviceInfo> previous;
                lock (_cacheLock)
                {
                    previous = _deviceCache;
                    _deviceCache = fresh;
                }

                foreach (var (id, info) in fresh)
                {
                    if (!previous.ContainsKey(id))
                    {
                        Emit(DeviceEventType.Arrived, info);
                    }
                }

                foreach (var (id, info) in previous)
                {
                    if (!fresh.ContainsKey(id))
                    {
                        Emit(DeviceEventType.Removed, info);
                    }
                }

                foreach (var (id, info) in fresh)
                {
                    if (previous.TryGetValue(id, out var old) &&
                        (old.ProblemCode != info.ProblemCode || old.Status != info.Status))
                    {
                        Emit(DeviceEventType.StatusChanged, info);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("DeviceWatcher.RunDiff", ex);
            }
            finally
            {
                _diffInFlight = false;
            }
        });
    }

    private void OnDeviceInterfaceChanged(string devicePath, bool isArrival)
    {
        Task.Run(() =>
        {
            try
            {
                string candidateId = ParseInstanceIdFromPath(devicePath);

                if (isArrival)
                {
                    DeviceInfo? info = null;
                    try { info = DeviceSnapshot.TryGetByInstanceId(candidateId); }
                    catch (Exception ex) { ErrorLog.Write("TryGetByInstanceId (arrival)", ex); }

                    info ??= new DeviceInfo { InstanceId = string.IsNullOrEmpty(candidateId) ? devicePath : candidateId };
                    info.DevicePath ??= devicePath;

                    lock (_cacheLock)
                    {
                        _deviceCache[info.InstanceId] = info;
                    }
                    Emit(DeviceEventType.Arrived, info);
                }
                else
                {
                    DeviceInfo? info;
                    lock (_cacheLock)
                    {
                        _deviceCache.TryGetValue(candidateId, out info);
                    }
                    if (info == null)
                    {
                        try { info = DeviceSnapshot.TryGetByInstanceId(candidateId); }
                        catch (Exception ex) { ErrorLog.Write("TryGetByInstanceId (removal)", ex); }
                    }
                    info ??= new DeviceInfo
                    {
                        InstanceId = string.IsNullOrEmpty(candidateId) ? devicePath : candidateId,
                        DevicePath = devicePath,
                        Status = "Unavailable"
                    };
                    Emit(DeviceEventType.Removed, info);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("DeviceWatcher.OnDeviceInterfaceChanged", ex);
            }
        });
    }

    /// <summary>
    /// Turns a device interface symbolic link path such as
    /// "\\?\USB#VID_0781&amp;PID_5567#4C531001331122115172#{a5dcbf10-...}"
    /// into an approximate instance id "USB\VID_0781&amp;PID_5567\4C531001331122115172".
    /// </summary>
    private static string ParseInstanceIdFromPath(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return "";

        string trimmed = devicePath.TrimStart('\\', '?');
        var segments = trimmed.Split('#', StringSplitOptions.RemoveEmptyEntries);
        var idSegments = segments.Where(s => !s.StartsWith('{')).ToArray();
        if (idSegments.Length == 0) return "";
        return string.Join('\\', idSegments).ToUpperInvariant();
    }

    private void Emit(DeviceEventType type, DeviceInfo info)
    {
        var key = (info.InstanceId, type);
        lock (_recentEvents)
        {
            DateTime now = DateTime.Now;
            if (_recentEvents.TryGetValue(key, out var last) && now - last < DedupWindow)
            {
                return;
            }
            _recentEvents[key] = now;

            if (_recentEvents.Count > 512)
            {
                foreach (var staleKey in _recentEvents.Where(kv => now - kv.Value > TimeSpan.FromMinutes(1)).Select(kv => kv.Key).ToList())
                {
                    _recentEvents.Remove(staleKey);
                }
            }
        }

        var record = new DeviceRecord
        {
            Timestamp = DateTime.Now,
            EventType = type,
            Info = info.Clone()
        };

        _dispatcher.BeginInvoke(() => DeviceEventCaptured?.Invoke(record));
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
        _pollTimer.Stop();
        _window.Dispose();
    }
}
