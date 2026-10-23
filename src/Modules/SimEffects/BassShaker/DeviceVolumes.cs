using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// A device's volume and mute are Windows' own for that device (as on the Audio tab): read from it when the device is
/// found, sent to it as the slider, mute or a shortcut changes them, and taken back when they're changed elsewhere
/// (Windows' mixer, the Audio tab). Two entries for one device (Left and Right) share it. Checked every 100 ms on the
/// UI thread; Windows' changes arrive by its notification.
/// </summary>
internal sealed class DeviceVolumes : IDisposable
{
    private sealed class Endpoint(MMDevice device, AudioEndpointVolume volume)
    {
        public readonly MMDevice Device = device;
        public readonly AudioEndpointVolume Volume = volume;
        public AudioEndpointVolumeNotificationDelegate? Handler;
        /// <summary>What it was last set to or read as, to see what the app changed since.</summary>
        public double Level;
        public bool Muted;
    }

    private readonly ShakerSettings settings;
    private readonly Dictionary<string, Endpoint> endpoints = new();
    private readonly HashSet<string> missing = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private MMDeviceEnumerator? enumerator;
    private DateTime nextLook;

    /// <summary>Windows changed a device's volume or mute (on the UI thread).</summary>
    public event Action? Changed;

    public DeviceVolumes(ShakerSettings settings)
    {
        this.settings = settings;
        timer.Tick += (_, _) => Sync();
    }

    public void Start()
    {
        Sync();
        timer.Start();
    }

    /// <summary>Finds new devices (and ones plugged back in, every 3 s), lets go of removed ones, and sends the
    /// app's changes to Windows.</summary>
    public void Sync()
    {
        var ids = settings.Devices.Select(d => d.DeviceId).Distinct().ToList();
        foreach (var id in endpoints.Keys.Except(ids).ToList()) Drop(id);
        if (DateTime.UtcNow >= nextLook)
        {
            nextLook = DateTime.UtcNow.AddSeconds(3);
            missing.Clear();
        }
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
            try
            {
                if (Math.Abs(changed.Volume - ep.Level) > 0.004) ep.Volume.MasterVolumeLevelScalar = (float)Math.Clamp(changed.Volume, 0, 1);
                if (changed.Muted != ep.Muted) ep.Volume.Mute = changed.Muted;
                ep.Level = changed.Volume;
                ep.Muted = changed.Muted;
                // (the other entry of the same device follows)
                foreach (var d in settings.Devices.Where(d => d.DeviceId == id)) { d.Volume = ep.Level; d.Muted = ep.Muted; }
                Logging.Log.Here.Debug(() => $"Shaker: {changed.Name} set to {ep.Level * 100:0}%{(ep.Muted ? ", muted" : "")} in Windows");
            }
            catch (Exception ex)
            {
                Logging.Log.Here.Warn($"Shaker: {changed.Name}'s volume couldn't be set", ex);
                Drop(id);
            }
        }
    }

    private Endpoint? Open(string id)
    {
        try
        {
            enumerator ??= new MMDeviceEnumerator();
            var device = enumerator.GetDevice(id);
            if (device.State != DeviceState.Active) { device.Dispose(); return null; }
            var ep = new Endpoint(device, device.AudioEndpointVolume)
            {
                Level = Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar, 2),
                Muted = device.AudioEndpointVolume.Mute,
            };
            ep.Handler = data =>
            {
                double level = Math.Round(data.MasterVolume, 2);
                bool muted = data.Muted;
                Application.Current?.Dispatcher.BeginInvoke(() => Take(id, level, muted));
            };
            ep.Volume.OnVolumeNotification += ep.Handler;
            endpoints[id] = ep;
            // Windows' volume is the one that counts: the entries show it
            foreach (var d in settings.Devices.Where(d => d.DeviceId == id)) { d.Volume = ep.Level; d.Muted = ep.Muted; }
            Logging.Log.Here.Info($"Shaker: {device.FriendlyName} is at {ep.Level * 100:0}%{(ep.Muted ? ", muted" : "")} in Windows");
            Changed?.Invoke();
            return ep;
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Debug(() => "Shaker: device volume not found: " + ex.Message);
            return null;
        }
    }

    /// <summary>Windows says a device's volume or mute changed (maybe by our own setting it).</summary>
    private void Take(string id, double level, bool muted)
    {
        if (!endpoints.TryGetValue(id, out var ep)) return;
        if (Math.Abs(level - ep.Level) <= 0.004 && muted == ep.Muted) return;
        ep.Level = level;
        ep.Muted = muted;
        foreach (var d in settings.Devices.Where(d => d.DeviceId == id)) { d.Volume = level; d.Muted = muted; }
        Changed?.Invoke();
    }

    private void Drop(string id)
    {
        if (!endpoints.Remove(id, out var ep)) return;
        try { if (ep.Handler != null) ep.Volume.OnVolumeNotification -= ep.Handler; } catch { }
        try { ep.Volume.Dispose(); } catch { }
        ep.Device.Dispose();
    }

    public void Dispose()
    {
        timer.Stop();
        foreach (var id in endpoints.Keys.ToList()) Drop(id);
        enumerator?.Dispose();
    }
}
