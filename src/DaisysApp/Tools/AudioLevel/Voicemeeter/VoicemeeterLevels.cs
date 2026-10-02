using DaisysApp.Tools.AudioLevel.Audio;

namespace DaisysApp.Tools.AudioLevel.Voicemeeter;

/// <summary>
/// Speaker levels applied by the app inside one Voicemeeter output bus (via the bus output insert).
/// Gains are kept per bus channel and saved in settings; they only take effect while the app is running.
/// </summary>
public sealed class VoicemeeterLevels : ILevelControl
{
    private readonly int busIndex;
    private readonly int[] map;       // speaker channel -> bus channel (0..7) or -1
    private readonly double[] gains;  // dB per bus channel (shared with settings, so changes persist)

    public VoicemeeterLevels(string busName, int busIndex, int[] map, double[] gains)
    {
        BusName = busName;
        this.busIndex = busIndex;
        this.map = map;
        this.gains = gains;
    }

    public string BusName { get; }
    public string Description => $"Voicemeeter bus {BusName}";
    public double MinDb => -40;
    public double MaxDb => 12;

    public bool CanControl(int channel) => channel >= 0 && channel < map.Length && map[channel] is >= 0 and < 8;

    public double Get(int channel) => gains[map[channel]];

    public void Set(int channel, double db)
    {
        int busChannel = map[channel];
        gains[busChannel] = Math.Round(Math.Clamp(db, MinDb, MaxDb), 1);
        VoicemeeterRemote.SetGain(busIndex * 8 + busChannel, gains[busChannel]);
        Changed?.Invoke();
    }

    /// <summary>Pushes all saved gains for this bus to the audio callback.</summary>
    public void ApplyAll()
    {
        for (int c = 0; c < 8; c++) VoicemeeterRemote.SetGain(busIndex * 8 + c, gains[c]);
    }

    public event Action? Changed;

    public void Dispose() { /* gains stay applied app-wide; see MainWindow for bus switching */ }
}
