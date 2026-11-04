using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.AudioTools.Levels;

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
    public string NameTip => HasHotkeys ? Display + "\n" + hotkeyText : Display;

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

/// <summary>A device's Windows volume and mute (Sound settings), told of changes by Windows (Shared's EndpointVolume).</summary>
public sealed class DeviceVolumeRow : VolumeRow
{
    private readonly DaisysApp.Shared.Audio.EndpointVolume endpoint;

    public DeviceVolumeRow(MMDevice device, string name, bool isDefault, bool recording)
        : base((recording ? "in:" : "out:") + device.ID, name)
    {
        IsDefault = isDefault;
        endpoint = new DaisysApp.Shared.Audio.EndpointVolume(device);
        Show(Math.Round(endpoint.Level * 100), endpoint.Muted);
        endpoint.Changed += (level, muted) => Show(level * 100, muted);
    }

    public bool IsDefault { get; }
    public override string Display => IsDefault ? F("Default · {0}", Name) : Name;

    // (a device that just went away: the list refreshes)
    protected override void ApplyVolume(double percent) => endpoint.Set(level: percent / 100);

    protected override void ApplyMute(bool mute) => endpoint.Set(muted: mute);

    public override void Dispose() => endpoint.Dispose();
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
