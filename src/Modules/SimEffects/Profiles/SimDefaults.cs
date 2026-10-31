using System.IO;
using System.Text.Json;
using DaisysApp.Applets.SimEffects.BassShaker;
using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// What a new install starts with (since 3.32.0; <c>Defaults.json</c>, embedded): the rig's profiles as they were
/// tuned there (games, ports, programs, Auto Start, every tool's settings), and the Shaker's effects for each game
/// (without their devices, which are each PC's own), which are also each game's defaults (a new profile's, Reset
/// Effects).
/// </summary>
public sealed class SimDefaults
{
    /// <summary>The profile in use at first.</summary>
    public string? Active { get; set; }
    public List<SimProfile> Profiles { get; set; } = new();
    /// <summary>The Shaker's effects, by game (<see cref="Game"/>'s name).</summary>
    public Dictionary<string, List<ShakerEffect>> Effects { get; set; } = new();

    /// <summary>Read afresh each time (rarely: a new install, a new profile, Reset Effects), so nothing changes them.</summary>
    public static SimDefaults Read()
    {
        try
        {
            using var stream = typeof(SimDefaults).Assembly.GetManifestResourceStream("DaisysApp.SimEffects.Defaults.json");
            if (stream != null) return JsonSerializer.Deserialize<SimDefaults>(stream, JsonStore.Options) ?? new();
        }
        catch (Exception ex) { Logging.Log.Here.Warn("SimEffects: the default profiles", ex); }
        return new();
    }

    /// <summary>A game's tuned effects (none: the built-in ones, <see cref="DefaultProfiles"/>).</summary>
    public static List<ShakerEffect> EffectsFor(Game game) =>
        Read().Effects.TryGetValue(game.ToString(), out var e) ? e : new();
}
