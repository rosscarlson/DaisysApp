using DaisysApp.Settings;

namespace DaisysApp.Tools.SetDelay;

public sealed class SetDelaySettings
{
    private const string FileName = "SetDelay";

    /// <summary>The Windows output device the beeps are played through (normally a Voicemeeter input).</summary>
    public string? PlayDeviceId { get; set; }
    public string? MicDeviceId { get; set; }

    /// <summary>The two Voicemeeter hardware buses to bring into sync (0 = A1).</summary>
    public int BusA { get; set; } = 0;
    public int BusB { get; set; } = 1;

    public static SetDelaySettings Load() => JsonStore.Load<SetDelaySettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
