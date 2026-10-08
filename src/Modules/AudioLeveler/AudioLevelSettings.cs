using System.IO;
using System.Text.Json;
using DaisysApp.Settings;
using DaisysApp.Applets.AudioLevel.Audio;
using DaisysApp.Applets.AudioLevel.Eq;

namespace DaisysApp.Applets.AudioLevel;

public sealed class AudioLevelSettings
{
    private const string FileName = "AudioLevel";

    public string? DeviceId { get; set; }
    public SignalType Signal { get; set; } = SignalType.PinkNoise;
    public double SineFrequency { get; set; } = 1000;
    public bool LfeLowPass { get; set; } = true;
    public double LfeCutoffHz { get; set; } = TestSignalProvider.DefaultLfeCutoff;

    /// <summary>For Voicemeeter devices, set the speaker levels in Voicemeeter's bus EQ (Settings → Audio Leveler).</summary>
    public bool VoicemeeterIntegration { get; set; } = true;
    /// <summary>The Voicemeeter output bus the speakers are connected to.</summary>
    public string VoicemeeterBus { get; set; } = "A1";
    public bool CycleEnabled { get; set; }
    public int CycleSeconds { get; set; } = 4;

    public string? MicDeviceId { get; set; }

    /// <summary>Each microphone's calibration file (a copy in the settings folder), by capture device id.</summary>
    public Dictionary<string, string> MicCalibrationByMic { get; set; } = new();

    // EQ Wizard options
    public EqTarget EqTarget { get; set; } = EqTarget.Flat;
    public double EqUpToHz { get; set; } = 1000;
    public double EqMaxBoostDb { get; set; } = 3;

    /// <summary>The speaker map is a grid this many cells square (Settings → Audio Leveler).</summary>
    public int SpeakerGridSize { get; set; } = SpeakerGrid.DefaultSize;

    /// <summary>Where the user dragged each speaker, per device: channel index → grid cell.</summary>
    public Dictionary<string, Dictionary<int, GridCell>> SpeakerCellsByDevice { get; set; } = new();

    /// <summary>Selected speakers per device, as a bitmask of channel indexes.</summary>
    public Dictionary<string, ulong> SelectionByDevice { get; set; } = new();

    public static AudioLevelSettings Load()
    {
        if (!JsonStore.Exists(FileName))
        {
            try
            {
                // one-time import from the standalone app this tool started as (MCAL): devices, signal, Voicemeeter bus
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
