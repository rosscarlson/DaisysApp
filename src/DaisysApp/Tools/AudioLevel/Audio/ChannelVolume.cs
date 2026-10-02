using NAudio.CoreAudioApi;

namespace DaisysApp.Tools.AudioLevel.Audio;

/// <summary>
/// Per-channel Windows endpoint volume (the Sound control panel "Levels → Balance" values) for one output device.
/// Windows keeps the master volume equal to the loudest channel, and moving the master shifts every channel by the
/// same number of dB, so a balance set here survives later volume changes. Values are system-wide and persistent.
/// </summary>
public sealed class ChannelVolume : ILevelControl
{
    private readonly MMDevice device;
    private readonly AudioEndpointVolume volume;
    private readonly int[] map; // speaker channel (mix format) -> endpoint volume channel, or -1
    private readonly AudioEndpointVolumeNotificationDelegate handler;

    /// <summary>Raised on a COM thread whenever any volume on the endpoint changes (including our own changes).</summary>
    public event Action? Changed;

    public ChannelVolume(MMDevice device, int mixChannels, IReadOnlyList<SpeakerDef> speakers)
    {
        this.device = device;
        volume = device.AudioEndpointVolume;
        MinDb = Math.Max(volume.VolumeRange.MinDecibels, -60);
        MaxDb = volume.VolumeRange.MaxDecibels;

        map = SpeakerChannelMap.Build(device, mixChannels, speakers, volume.Channels.Count);

        handler = _ => Changed?.Invoke();
        volume.OnVolumeNotification += handler;
    }

    public string Description => "Windows channel volume";
    public double MinDb { get; }
    public double MaxDb { get; }

    /// <summary>Master volume in dB; Windows keeps this equal to the loudest channel.</summary>
    public double MasterDb => volume.MasterVolumeLevel;

    public bool CanControl(int channel) => channel >= 0 && channel < map.Length && map[channel] >= 0;

    public double Get(int channel) => volume.Channels[map[channel]].VolumeLevel;

    public void Set(int channel, double db) =>
        volume.Channels[map[channel]].VolumeLevel = (float)Math.Clamp(db, MinDb, MaxDb);

    public void Dispose()
    {
        volume.OnVolumeNotification -= handler;
        device.Dispose();
    }
}
