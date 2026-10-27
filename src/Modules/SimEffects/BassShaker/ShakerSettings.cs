using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>Something with a volume: the master, a device, an effect. Each has its own up / down / mute shortcuts.</summary>
public interface IVolume
{
    double Volume { get; set; }
    bool Muted { get; set; }
    VolumeKeys Keys { get; set; }
}

/// <summary>Shortcuts (keys or controller buttons, as Core's ShortcutBox writes them) for a volume.</summary>
public sealed class VolumeKeys
{
    public string? Up { get; set; }
    public string? Down { get; set; }
    public string? Mute { get; set; }

    [JsonIgnore] public bool Any => Up != null || Down != null || Mute != null;
}

public sealed class MasterVolume : IVolume
{
    public double Volume { get; set; } = 0.8;
    public bool Muted { get; set; }
    public VolumeKeys Keys { get; set; } = new();
}

/// <summary>Which of a device's channels it plays on: two shakers on one stereo amplifier are a device each, Left
/// and Right; Split plays the left wheels of its <see cref="Position"/> on the left channel and its right wheels on
/// the right one (front left / front right from one amplifier, say).</summary>
public enum Channels { All, Left, Right, Lfe, Split }

/// <summary>Which wheels a device stands for: the whole car, an axle, a side, or a corner.</summary>
public enum Position { All, Front, Rear, Left, Right, FrontLeft, FrontRight, RearLeft, RearRight }

/// <summary>The wheels, in telemetry's order: 0 front left, 1 front right, 2 rear left, 3 rear right.</summary>
public static class Wheels
{
    public static int[] Of(Position p) => p switch
    {
        Position.Front => new[] { 0, 1 },
        Position.Rear => new[] { 2, 3 },
        Position.Left => new[] { 0, 2 },
        Position.Right => new[] { 1, 3 },
        Position.FrontLeft => new[] { 0 },
        Position.FrontRight => new[] { 1 },
        Position.RearLeft => new[] { 2 },
        Position.RearRight => new[] { 3 },
        _ => new[] { 0, 1, 2, 3 },
    };

    public static bool IsLeft(int wheel) => wheel is 0 or 2;
    public static bool IsFront(int wheel) => wheel is 0 or 1;

    /// <summary>For the log: "all wheels", "FL+FR"…</summary>
    public static string Describe(int[] wheels) =>
        wheels.Length == 4 ? "all wheels" : string.Join("+", wheels.Select(w => new[] { "FL", "FR", "RL", "RR" }[w]));
}

/// <summary>An audio device with a transducer on it.</summary>
public sealed class ShakerDevice : IVolume
{
    /// <summary>This entry's own id (one Windows device can be two entries, Left and Right).</summary>
    public string Key { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>Windows' id of the playback device.</summary>
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "generic";
    public int MinHz { get; set; } = 20;
    public int MaxHz { get; set; } = 100;
    public Channels Channels { get; set; }
    public Position Position { get; set; }
    public double Volume { get; set; } = 0.8;
    public bool Muted { get; set; }
    public bool Expanded { get; set; }
    public VolumeKeys Keys { get; set; } = new();
}

/// <summary>One effect of a profile, with its own volume and settings (which of them a kind uses: <see cref="EffectKinds"/>).</summary>
public sealed class ShakerEffect : IVolume
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public EffectKind Kind { get; set; }
    public double Volume { get; set; } = 0.7;
    public bool Muted { get; set; }
    public bool Expanded { get; set; }
    /// <summary>Hz (Engine: at idle).</summary>
    public double Frequency { get; set; } = 40;
    /// <summary>Engine: Hz at the redline.</summary>
    public double FrequencyHigh { get; set; } = 70;
    public double Sensitivity { get; set; } = 1;
    /// <summary>Where it starts, in the kind's own unit (m/s, g, % of grip or of the redline).</summary>
    public double Threshold { get; set; }
    /// <summary>How long one knock lasts (bumps, crashes, gear shifts), ms.</summary>
    public double LengthMs { get; set; } = 100;
    /// <summary>Engine: how much it shakes at idle with no throttle, 0..1.</summary>
    public double IdleLevel { get; set; } = 0.3;
    /// <summary>Devices (their <see cref="ShakerDevice.Key"/>) it doesn't play on; every other one, new ones too, it does.</summary>
    public List<string> Off { get; set; } = new();
    /// <summary>How much of it goes to each device it plays on (by its key), 0..1; 1 when not listed.</summary>
    public Dictionary<string, double> Levels { get; set; } = new();
    public VolumeKeys Keys { get; set; } = new();
}

/// <summary>A game's set of effects, with the port it sends to (Forza) and the programs that pick it.</summary>
public sealed class ShakerProfile
{
    public string Name { get; set; } = "";
    public Game Game { get; set; }
    public int Port { get; set; } = 8000;
    /// <summary>Program names (ForzaHorizon5.exe…): with Game Switching on, one running picks this profile.</summary>
    public List<string> Programs { get; set; } = new();
    public List<ShakerEffect> Effects { get; set; } = new();
}

/// <summary>Saved in %APPDATA%\DaisysApp\BassShaker.json.</summary>
public sealed class ShakerSettings
{
    private const string FileName = "BassShaker";

    public bool Enabled { get; set; }
    public MasterVolume Master { get; set; } = new();
    public List<ShakerDevice> Devices { get; set; } = new();
    public List<ShakerProfile> Profiles { get; set; } = new();
    /// <summary>The profile in use, by name.</summary>
    public string? Active { get; set; }
    /// <summary>A profile's program running switches to that profile.</summary>
    public bool GameSwitching { get; set; } = true;
    /// <summary>The shortcut that switches it on and off (keys or a controller button).</summary>
    public string? OnOffKey { get; set; }
    /// <summary>A title bar bubble while it's on.</summary>
    public bool TitleBadge { get; set; } = true;
    /// <summary>The Level Wizard's shortcuts.</summary>
    public WizardKeys Wizard { get; set; } = new();
    /// <summary>How strong the Test buttons play, 0.1..1 (Settings → SimEffects → Shaker).</summary>
    public double TestLevel { get; set; } = 1;
    /// <summary>The meters popped out as a widget.</summary>
    public MetersWidget MetersWidget { get; set; } = new();
    /// <summary>Forza Horizon's default port moved from 5300 to 8000 (3.5.1): profiles still on 5300 were moved once.</summary>
    public bool HorizonOn8000 { get; set; }

    public static ShakerSettings Load()
    {
        var s = JsonStore.Load<ShakerSettings>(FileName);
        if (s.Profiles.Count == 0) s.Profiles.AddRange(DefaultProfiles.All());
        if (!s.HorizonOn8000)
        {
            foreach (var p in s.Profiles.Where(p => p.Game == Game.ForzaHorizon && p.Port == 5300)) p.Port = 8000;
            s.HorizonOn8000 = true;
        }
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>The meters' widget: open or not, and where (Core's widget keeps it).</summary>
public sealed class MetersWidget : DaisysApp.Shared.Widgets.IWidgetPlace
{
    public bool Show { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Opacity { get; set; } = 0.9;
    public bool Topmost { get; set; } = true;
}

/// <summary>The Level Wizard's shortcuts (keys or controller buttons): steps, level, the four settings, and Solo / All.</summary>
public sealed class WizardKeys
{
    public string? Previous { get; set; }
    public string? Next { get; set; }
    public string? LevelUp { get; set; }
    public string? LevelDown { get; set; }
    public string? FrequencyUp { get; set; }
    public string? FrequencyDown { get; set; }
    public string? SensitivityUp { get; set; }
    public string? SensitivityDown { get; set; }
    public string? ThresholdUp { get; set; }
    public string? ThresholdDown { get; set; }
    public string? LengthUp { get; set; }
    public string? LengthDown { get; set; }
    public string? Solo { get; set; }
}
