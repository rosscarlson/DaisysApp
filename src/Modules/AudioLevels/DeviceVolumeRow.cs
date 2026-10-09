using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioLevels;

/// <summary>
/// One row: a playback or recording device's Windows volume, or an app's own volume. Kept in step both ways: moving
/// the slider sets it, and a change made anywhere else (Windows, the device, the app) moves the slider.
/// </summary>
public abstract class VolumeRow : INotifyPropertyChanged, IDisposable
{
    private double volume;
    private bool muted;
    private string hotkeyText = "";
    protected bool applying;

    protected VolumeRow(string key, string name)
    {
        Key = key;
        Name = name;
    }

    /// <summary>"out:&lt;id&gt;", "in:&lt;id&gt;" or "app:&lt;program&gt;": what its shortcuts are saved under.</summary>
    public string Key { get; }
    public string Name { get; }
    public virtual string Display => Name;

    /// <summary>0–100, as in Windows' volume slider.</summary>
    public double Volume
    {
        get => volume;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, 100);
            if (volume == value) return;
            volume = value;
            if (!applying) ApplyVolume(value);
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
            if (!applying) ApplyMute(value);
            OnChanged();
            OnChanged(nameof(MuteGlyph));
            OnChanged(nameof(MuteTip));
        }
    }

    public string MuteGlyph => muted ? "" : "";
    public string MuteTip => muted ? T("Muted — click to unmute") : T("Click to mute");

    /// <summary>Its shortcuts in short ("Ctrl+Alt+Up · Controller 1 Button 3…"), or empty.</summary>
    public string HotkeyText
    {
        get => hotkeyText;
        set { hotkeyText = value; OnChanged(); OnChanged(nameof(HasHotkeys)); OnChanged(nameof(NameTip)); }
    }

    public bool HasHotkeys => hotkeyText.Length > 0;
    public string NameTip => Display + "\n" + (HasHotkeys ? hotkeyText + "\n" : "") + T("Click to set its shortcuts (volume up, down, mute)");

    /// <summary>Shows a value read from Windows without writing it back.</summary>
    protected void Show(double newVolume, bool newMuted)
    {
        applying = true;
        Volume = newVolume;
        Muted = newMuted;
        applying = false;
    }

    protected abstract void ApplyVolume(double percent);
    protected abstract void ApplyMute(bool mute);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public virtual void Dispose() { }
}

/// <summary>A device's Windows volume and mute (Sound settings), told of changes by Windows.</summary>
public sealed class DeviceVolumeRow : VolumeRow
{
    private readonly MMDevice device;
    private readonly AudioEndpointVolume endpoint;

    public DeviceVolumeRow(MMDevice device, string name, bool isDefault, bool recording)
        : base((recording ? "in:" : "out:") + device.ID, name)
    {
        this.device = device;
        IsDefault = isDefault;
        endpoint = device.AudioEndpointVolume;
        Show(Math.Round(endpoint.MasterVolumeLevelScalar * 100), endpoint.Mute);
        endpoint.OnVolumeNotification += OnNotification;
    }

    public bool IsDefault { get; }
    public override string Display => IsDefault ? F("Default · {0}", Name) : Name;

    protected override void ApplyVolume(double percent)
    {
        try { endpoint.MasterVolumeLevelScalar = (float)(percent / 100); }
        catch { /* device just went away; the list refreshes */ }
    }

    protected override void ApplyMute(bool mute)
    {
        try { endpoint.Mute = mute; }
        catch { /* device just went away */ }
    }

    // raised on a Windows audio thread
    private void OnNotification(AudioVolumeNotificationData data) =>
        Application.Current?.Dispatcher.BeginInvoke(() => Show(data.MasterVolume * 100, data.Muted));

    public override void Dispose()
    {
        try { endpoint.OnVolumeNotification -= OnNotification; } catch { }
        device.Dispose();
    }
}

/// <summary>An app's own volume (the volume mixer's), refreshed while the tab is showing.</summary>
internal sealed class AppVolumeRow : VolumeRow
{
    private AppAudio app;

    public AppVolumeRow(AppAudio app) : base(app.Key, app.Name)
    {
        this.app = app;
        Refresh(app);
    }

    /// <summary>Takes the app's latest sessions and shows their volume (unless it's being dragged).</summary>
    public void Refresh(AppAudio latest)
    {
        app = latest;
        try { Show(AppVolumes.Volume(app), AppVolumes.Muted(app)); } catch { }
    }

    protected override void ApplyVolume(double percent) => AppVolumes.SetVolume(app, percent);
    protected override void ApplyMute(bool mute) => AppVolumes.SetMuted(app, mute);
}
