using NAudio.CoreAudioApi;

namespace DaisysApp.Tools.AudioLevel.Audio;

/// <summary>
/// The Windows input volume of a microphone (Sound settings → Input → Volume), as 0–100 % like Windows shows it,
/// plus dB steps for lowering it after clipping. Windows keeps the value, so it survives restarts.
/// </summary>
public sealed class MicGain : IDisposable
{
    private readonly MMDevice device;
    private readonly AudioEndpointVolume volume;

    /// <summary>Raised (possibly on another thread) when the level changes, here or in Windows.</summary>
    public event Action? Changed;

    public MicGain(MMDevice device)
    {
        this.device = device;
        volume = device.AudioEndpointVolume;
        volume.OnVolumeNotification += OnNotification;
    }

    public double Percent
    {
        get => Math.Round(volume.MasterVolumeLevelScalar * 100, 0);
        set => volume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100, 0, 1);
    }

    /// <summary>Lowers the input volume by about <paramref name="db"/> dB. False if it was already at the minimum.</summary>
    public bool LowerBy(double db)
    {
        float min = volume.VolumeRange.MinDecibels;
        float now = volume.MasterVolumeLevel;
        if (now <= min + 0.5f && volume.MasterVolumeLevelScalar <= 0.02f) return false;
        float want = (float)Math.Max(now - db, min);
        if (want >= now - 0.01f)
        {
            // the dB range is used up but the scalar isn't at zero yet: step the scalar instead
            volume.MasterVolumeLevelScalar = Math.Max(0, volume.MasterVolumeLevelScalar * 0.5f);
            return true;
        }
        volume.MasterVolumeLevel = want;
        return true;
    }

    private void OnNotification(AudioVolumeNotificationData data) => Changed?.Invoke();

    public void Dispose()
    {
        volume.OnVolumeNotification -= OnNotification;
        device.Dispose();
    }
}
