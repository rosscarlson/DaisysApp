using System.Windows;
using NAudio.CoreAudioApi;

namespace DaisysApp.Shared.Audio;

/// <summary>
/// A device's own volume and mute (Windows' Sound settings), read when it's opened, set by <see cref="Set"/>, and kept
/// up to date by Windows' notification (<see cref="Changed"/>, on the UI thread). The Audio tab's device rows and the
/// Shaker's devices use it (since 4.0.0: each wrapped the endpoint itself, and the Shaker checked it every 100 ms).
/// </summary>
public sealed class EndpointVolume : IDisposable
{
    private readonly MMDevice device;
    private readonly AudioEndpointVolume endpoint;
    private readonly bool ownsDevice;
    private volatile bool disposed;

    /// <summary>0–1, as Windows last said or was told.</summary>
    public double Level { get; private set; }
    public bool Muted { get; private set; }
    public MMDevice Device => device;

    /// <summary>Windows says the volume or mute changed, by anyone, this app too (level 0–1, muted).</summary>
    public event Action<double, bool>? Changed;

    /// <param name="ownsDevice">Disposing this disposes the device too.</param>
    public EndpointVolume(MMDevice device, bool ownsDevice = true)
    {
        this.device = device;
        this.ownsDevice = ownsDevice;
        endpoint = device.AudioEndpointVolume;
        Level = endpoint.MasterVolumeLevelScalar;
        Muted = endpoint.Mute;
        endpoint.OnVolumeNotification += OnNotification;
    }

    /// <summary>The active device with this id, or null (unplugged, disabled, no such device).</summary>
    public static EndpointVolume? Open(MMDeviceEnumerator enumerator, string id)
    {
        try
        {
            var d = enumerator.GetDevice(id);
            if (d.State == DeviceState.Active) return new EndpointVolume(d);
            d.Dispose();
        }
        catch { }
        return null;
    }

    /// <summary>Sets the volume (0–1) and mute, whichever changed; false if the device has gone.</summary>
    public bool Set(double? level = null, bool? muted = null)
    {
        try
        {
            if (level is double l)
            {
                l = Math.Clamp(l, 0, 1);
                endpoint.MasterVolumeLevelScalar = (float)l;
                Level = l;
            }
            if (muted is bool m && m != Muted)
            {
                endpoint.Mute = m;
                Muted = m;
            }
            return true;
        }
        catch { return false; }
    }

    // raised on a Windows audio thread
    private void OnNotification(AudioVolumeNotificationData data)
    {
        double level = data.MasterVolume;
        bool muted = data.Muted;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            Level = level;
            Muted = muted;
            Changed?.Invoke(level, muted);
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { endpoint.OnVolumeNotification -= OnNotification; } catch { }
        try { endpoint.Dispose(); } catch { }
        if (ownsDevice) device.Dispose();
    }
}
