using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Lights;

/// <summary>What's known about one car: its name, the highest RPM it's held (its limiter, once it's reached it), and
/// your own redline for it if you set one.</summary>
public sealed class CarRedline
{
    public string Name { get; set; } = "";
    public float Learned { get; set; }
    public float? Yours { get; set; }
    public DateTime Seen { get; set; }
}

/// <summary>
/// The shift lights' settings, shared by every tool that has them (Notify's LEDs, the Fanatec wheel): which game to
/// follow, where the lights start, where to shift and where they flash (% of the car's redline), and each car's
/// redline. Saved in %APPDATA%\DaisysApp\SimLights.json.
/// </summary>
public sealed class LightsSettings
{
    private const string FileName = "SimLights";

    /// <summary>What each SimEffects profile keeps of these (the game is the profile's since 3.18.0).</summary>
    public static readonly string[] Scoped = { nameof(StartAt), nameof(ShiftAt), nameof(FlashAt), nameof(FlashRate), nameof(UseGameLights) };

    /// <summary>% of the redline where the first light comes on.</summary>
    public int StartAt { get; set; } = 75;
    /// <summary>% of the redline where they're all on: shift.</summary>
    public int ShiftAt { get; set; } = 96;
    /// <summary>% of the redline where they flash (over-rev).</summary>
    public int FlashAt { get; set; } = 99;
    /// <summary>Flashes a second.</summary>
    public int FlashRate { get; set; } = 6;
    /// <summary>iRacing's own shift lights for each car (its first light, shift and flash RPM) instead of the % above.</summary>
    public bool UseGameLights { get; set; } = true;
    /// <summary>Each car by the game's id for it ("FH:2352", "iRacing:mx5 mx52016", "ACC:porsche_991ii_gt3_r").</summary>
    public Dictionary<string, CarRedline> Cars { get; set; } = new();

    [JsonIgnore] public readonly object Gate = new();

    public static LightsSettings Load() => JsonStore.Load<LightsSettings>(FileName);

    public void Save()
    {
        lock (Gate) JsonStore.Save(FileName, this);
        Profiles.SimProfiles.Saved(this);
    }
}
