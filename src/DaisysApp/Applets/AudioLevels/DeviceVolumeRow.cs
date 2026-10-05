using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioLevels;

/// <summary>
/// One device's Windows volume and mute, kept in step both ways: moving the slider sets the device, and a change made
/// anywhere else (Windows, the device itself, another app) moves the slider.
/// </summary>
public sealed class DeviceVolumeRow : INotifyPropertyChanged, IDisposable
{
    private readonly MMDevice device;
    private readonly AudioEndpointVolume endpoint;
    private double volume;
    private bool muted;
    private bool applying;

    public DeviceVolumeRow(MMDevice device, string name, bool isDefault)
    {
        this.device = device;
        Id = device.ID;
        Name = name;
        IsDefault = isDefault;
        endpoint = device.AudioEndpointVolume;
        volume = Math.Round(endpoint.MasterVolumeLevelScalar * 100);
        muted = endpoint.Mute;
        endpoint.OnVolumeNotification += OnNotification;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsDefault { get; }
    public string Display => IsDefault ? $"Default · {Name}" : Name;

    /// <summary>0–100, as in Windows' volume slider.</summary>
    public double Volume
    {
        get => volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (volume == value) return;
            volume = value;
            if (!applying)
            {
                try { endpoint.MasterVolumeLevelScalar = (float)(value / 100); }
                catch { /* device just went away; the list refreshes */ }
            }
            OnChanged();
            OnChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{volume:0}";

    public bool Muted
    {
        get => muted;
        set
        {
            if (muted == value) return;
            muted = value;
            if (!applying)
            {
                try { endpoint.Mute = value; }
                catch { /* device just went away */ }
            }
            OnChanged();
            OnChanged(nameof(MuteGlyph));
            OnChanged(nameof(MuteTip));
        }
    }

    public string MuteGlyph => muted ? "" : "";
    public string MuteTip => muted ? "Muted — click to unmute" : "Click to mute";

    private void OnNotification(AudioVolumeNotificationData data)
    {
        // raised on a Windows audio thread
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            applying = true;
            Volume = data.MasterVolume * 100;
            Muted = data.Muted;
            applying = false;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        try { endpoint.OnVolumeNotification -= OnNotification; } catch { }
        device.Dispose();
    }
}
