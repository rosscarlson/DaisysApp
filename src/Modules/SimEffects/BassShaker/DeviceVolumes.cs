using System.Windows;
using DaisysApp.Shared.Audio;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// A device's volume and mute are Windows' own for that device (as on the Audio tab): read from it when the device is
/// found, sent to it as the slider, mute or a shortcut changes them, and taken back when they're changed elsewhere
/// (Windows' mixer, the Audio tab). Two entries for one device (Left and Right) share it. Nothing is polled (since
/// 4.0.0; it was checked every 100 ms): a change to an entry sends it (<see cref="ShakerDevice.Edited"/>), Windows'
/// changes arrive by its notification (Shared's <see cref="EndpointVolume"/>), and a device plugged in is found when
/// Windows says so (Shared's <see cref="DeviceService"/>).
/// </summary>
internal sealed class DeviceVolumes : IDisposable
{
    private readonly ShakerSettings settings;
    private readonly Dictionary<string, EndpointVolume> endpoints = new();
    private readonly HashSet<string> missing = new();
    private MMDeviceEnumerator? enumerator;
    private DeviceService? devices;
    private bool started, syncing, queued;

    /// <summary>Windows changed a device's volume or mute (on the UI thread).</summary>
    public event Action? Changed;

    public DeviceVolumes(ShakerSettings settings) => this.settings = settings;

    public void Start()
    {
        started = true;
        ShakerDevice.Edited += SyncSoon;
        try
        {
            devices = new DeviceService();
            // (a device plugged in, or back: looked for again)
            devices.DevicesChanged += () => Application.Current?.Dispatcher.BeginInvoke(() => { missing.Clear(); Sync(); });
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => "Shaker: device notifications: " + ex.Message); }
        Sync();
    }

    // (several changes at once, a slider dragged: one sync after them, on the UI thread)
    private void SyncSoon()
    {
        if (!started || queued || syncing) return;
        queued = true;
        Application.Current?.Dispatcher.BeginInvoke(() => { queued = false; Sync(); });
    }

    /// <summary>Finds new devices, lets go of removed ones, and sends the app's changes to Windows.</summary>
    public void Sync()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            var ids = settings.Devices.Select(d => d.DeviceId).Distinct().ToList();
            foreach (var id in endpoints.Keys.Except(ids).ToList()) Drop(id);
            foreach (var id in ids)
            {
                if (!endpoints.TryGetValue(id, out var ep))
                {
                    if (missing.Contains(id)) continue;
                    ep = Open(id);
                    if (ep == null) { missing.Add(id); continue; }
                }
                // the app's slider, mute or shortcut moved: the first entry that differs from what Windows has
                var changed = settings.Devices.FirstOrDefault(d => d.DeviceId == id && (Math.Abs(d.Volume - ep.Level) > 0.004 || d.Muted != ep.Muted));
                if (changed == null) continue;
                if (!ep.Set(Math.Abs(changed.Volume - ep.Level) > 0.004 ? changed.Volume : null, changed.Muted))
                {
                    Logging.Log.Here.Warn($"Shaker: {changed.Name}'s volume couldn't be set");
                    Drop(id);
                    continue;
                }
                // (the other entry of the same device follows)
                foreach (var d in settings.Devices.Where(d => d.DeviceId == id)) { d.Volume = Math.Round(ep.Level, 2); d.Muted = ep.Muted; }
                Logging.Log.Here.Debug(() => $"Shaker: {changed.Name} set to {ep.Level * 100:0}%{(ep.Muted ? ", muted" : "")} in Windows");
            }
        }
        finally { syncing = false; }
    }

    private EndpointVolume? Open(string id)
    {
        enumerator ??= new MMDeviceEnumerator();
        var ep = EndpointVolume.Open(enumerator, id);
        if (ep == null)
        {
            Logging.Log.Here.Debug(() => $"Shaker: device volume not found: {id}");
            return null;
        }
        ep.Changed += (level, muted) => Take(id, Math.Round(level, 2), muted);
        endpoints[id] = ep;
        // Windows' volume is the one that counts: the entries show it
        syncing = true;
        try { foreach (var d in settings.Devices.Where(d => d.DeviceId == id)) { d.Volume = Math.Round(ep.Level, 2); d.Muted = ep.Muted; } }
        finally { syncing = false; }
        Logging.Log.Here.Info($"Shaker: {ep.Device.FriendlyName} is at {ep.Level * 100:0}%{(ep.Muted ? ", muted" : "")} in Windows");
        Changed?.Invoke();
        return ep;
    }

    /// <summary>Windows says a device's volume or mute changed (maybe by our own setting it).</summary>
    private void Take(string id, double level, bool muted)
    {
        if (!endpoints.ContainsKey(id)) return;
        var entries = settings.Devices.Where(d => d.DeviceId == id).ToList();
        if (entries.All(d => Math.Abs(d.Volume - level) <= 0.004 && d.Muted == muted)) return;
        syncing = true;
        try { foreach (var d in entries) { d.Volume = level; d.Muted = muted; } }
        finally { syncing = false; }
        Changed?.Invoke();
    }

    private void Drop(string id)
    {
        if (endpoints.Remove(id, out var ep)) ep.Dispose();
    }

    public void Dispose()
    {
        started = false;
        ShakerDevice.Edited -= SyncSoon;
        devices?.Dispose();
        foreach (var id in endpoints.Keys.ToList()) Drop(id);
        enumerator?.Dispose();
    }
}
