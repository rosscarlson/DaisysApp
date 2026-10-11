using System.IO;
using System.Text.Json;
using System.Windows;
using DaisysApp.Settings;
using DaisysApp.Shared.Hardware;

namespace DaisysApp.Applets.Sensors;

/// <summary>Saved in %APPDATA%\DaisysApp\Sensors.json.</summary>
public sealed class SensorsSettings
{
    private const string FileName = "Sensors";

    /// <summary>Where LibreHardwareMonitor's Remote Web Server is.</summary>
    public string HardwareAddress { get; set; } = HardwareMonitor.DefaultAddress;
    public bool LogEnabled { get; set; } = true;
    public int KeepDays { get; set; } = 365;
    /// <summary>1: the log's default went from 30 days to a year (0.13), and 30 was moved up with it.</summary>
    public int SettingsVersion { get; set; }

    /// <summary>
    /// Loads the settings; the first time, takes the address and the log settings from Performance.json, where they
    /// were before sensors had their own tab.
    /// </summary>
    public static SensorsSettings Load()
    {
        if (JsonStore.Exists(FileName))
        {
            var loaded = JsonStore.Load<SensorsSettings>(FileName);
            if (loaded.SettingsVersion < 1)
            {
                if (loaded.KeepDays == 30) loaded.KeepDays = 365;
                loaded.SettingsVersion = 1;
                loaded.Save();
            }
            return loaded;
        }
        var s = new SensorsSettings { SettingsVersion = 1 };
        try
        {
            string path = JsonStore.PathFor("Performance");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.TryGetProperty("HardwareAddress", out var a) && a.ValueKind == JsonValueKind.String && a.GetString() is { Length: > 0 } address)
                    s.HardwareAddress = address;
                if (root.TryGetProperty("LogEnabled", out var l) && l.ValueKind is JsonValueKind.True or JsonValueKind.False) s.LogEnabled = l.GetBoolean();
                if (root.TryGetProperty("KeepDays", out var k) && k.TryGetInt32(out int days)) s.KeepDays = days == 30 ? 365 : days;
            }
        }
        catch { /* defaults */ }
        s.Save();
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>
/// The Sensors applet's engine: uses the app's one LibreHardwareMonitor connection (<see cref="SensorHub"/>), keeps the
/// last 10 minutes of every sensor for the live graphs, and logs temperatures, fans and power every 10 seconds.
/// </summary>
internal sealed class SensorsService : IDisposable
{
    public const int LiveSeconds = 600;

    public SensorsSettings Settings { get; } = SensorsSettings.Load();
    public SensorLog Log { get; }

    private readonly object gate = new();
    private readonly Dictionary<string, LinkedList<(DateTime Time, double Value)>> live = new();
    private readonly SensorAggregator aggregate = new();
    private IDisposable? use;
    private DateTime lastCleanup;

    public SensorsService() => Log = new SensorLog(Settings);

    /// <summary>Raised on the UI thread with each new reading, or null when LibreHardwareMonitor stops answering.</summary>
    public event Action<HwSnapshot?>? Updated;

    public HwSnapshot? Latest => SensorHub.Latest;

    public void Start()
    {
        Logging.Log.Here.Info($"LibreHardwareMonitor address {Settings.HardwareAddress}; history in {SensorLog.Folder}, {SensorLog.Days().Count} days");
        SensorHub.Address = Settings.HardwareAddress;
        SensorHub.Updated += OnReading;
        use = SensorHub.Use();
        Log.Cleanup();
        lastCleanup = DateTime.Now;
    }

    /// <summary>After the address was changed in Settings.</summary>
    public void SetAddress(string address)
    {
        Logging.Log.Here.Info($"LibreHardwareMonitor address set to {address}");
        Settings.HardwareAddress = address;
        Settings.Save();
        SensorHub.Address = address;
    }

    /// <summary>A sensor's readings of the last 10 minutes.</summary>
    public List<(DateTime Time, double Value)> Live(string key)
    {
        lock (gate) return live.TryGetValue(key, out var h) ? h.ToList() : new();
    }

    private void OnReading(HwSnapshot? snapshot)
    {
        if (snapshot != null)
        {
            lock (gate)
                foreach (var x in snapshot.Sensors)
                {
                    var h = live.TryGetValue(x.Key, out var l) ? l : live[x.Key] = new();
                    h.AddLast((snapshot.Time, x.Value));
                    while (h.Count > 0 && (snapshot.Time - h.First!.Value.Time).TotalSeconds > LiveSeconds) h.RemoveFirst();
                }
            if (aggregate.Due(snapshot.Time))
            {
                var (time, values, sensors) = aggregate.Take();
                Log.Append(time, values, sensors);
            }
            aggregate.Add(snapshot);
            if ((DateTime.Now - lastCleanup).TotalHours >= 6)
            {
                lastCleanup = DateTime.Now;
                Log.Cleanup();
            }
        }
        Application.Current?.Dispatcher.BeginInvoke(() => Updated?.Invoke(snapshot));
    }

    public void Dispose()
    {
        SensorHub.Updated -= OnReading;
        use?.Dispose();
    }
}
