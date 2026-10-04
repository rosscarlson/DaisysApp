using DaisysApp.Settings;

namespace DaisysApp.Tools.AudioDelay;

public sealed class AudioDelaySettings
{
    private const string FileName = "AudioDelay";

    /// <summary>The Windows output device the beeps are played through (normally a Voicemeeter input).</summary>
    public string? PlayDeviceId { get; set; }
    public string? MicDeviceId { get; set; }

    /// <summary>The two Voicemeeter hardware buses to bring into sync (0 = A1).</summary>
    public int BusA { get; set; } = 0;
    public int BusB { get; set; } = 1;

    public static AudioDelaySettings Load() => JsonStore.Load<AudioDelaySettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
