using System.IO;
using System.Text.Json;
using DaisysApp.Settings;
using DaisysApp.Tools.AudioLevel.Audio;

namespace DaisysApp.Tools.AudioLevel;

public sealed class AudioLevelSettings
{
    private const string FileName = "AudioLevel";

    public string? DeviceId { get; set; }
    public SignalType Signal { get; set; } = SignalType.PinkNoise;
    public double LevelDb { get; set; } = -20;
    public double SineFrequency { get; set; } = 1000;
    public bool LfeLowPass { get; set; } = true;
    public double LfeCutoffHz { get; set; } = TestSignalProvider.DefaultLfeCutoff;

    /// <summary>Settings switch: allow level control through Voicemeeter's bus insert at all.</summary>
    public bool VoicemeeterIntegration { get; set; } = true;
    /// <summary>Speaker levels are being applied inside Voicemeeter (set once a Voicemeeter device has been used).</summary>
    public bool VoicemeeterEnabled { get; set; }
    public string VoicemeeterBus { get; set; } = "A1";
    /// <summary>dB per bus channel (8 per bus), keyed by bus name.</summary>
    public Dictionary<string, double[]> VoicemeeterGains { get; set; } = new();
    public bool CycleEnabled { get; set; }
    public int CycleSeconds { get; set; } = 4;

    public string? MicDeviceId { get; set; }

    /// <summary>Selected speakers per device, as a bitmask of channel indexes.</summary>
    public Dictionary<string, ulong> SelectionByDevice { get; set; } = new();

    public static AudioLevelSettings Load()
    {
        if (!JsonStore.Exists(FileName))
        {
            try
            {
                // one-time import from the standalone app this tool started as (MCAL): devices, levels, Voicemeeter gains
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCAL", "settings.json");
                if (File.Exists(legacy) &&
                    JsonSerializer.Deserialize<AudioLevelSettings>(File.ReadAllText(legacy), JsonStore.Options) is { } imported)
                {
                    imported.Save();
                    return imported;
                }
            }
            catch { /* unreadable legacy settings: start fresh */ }
        }
        return JsonStore.Load<AudioLevelSettings>(FileName);
    }

    public void Save() => JsonStore.Save(FileName, this);
}
