using System.IO;
using System.Text.Json;

namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>
/// Forza's cars by their number (CarOrdinal, the only thing Data Out says about the car), per game: Horizon 4, 5 and 6,
/// Motorsport 7 and Motorsport (2023), from <c>Cars.json</c> (built from the community's lists of car ordinals; names
/// the lists only had as file names, such as "TOY_2000GT_69", are spelt out: "1969 Toyota 2000GT"). The game being
/// read is <see cref="Edition"/>: the profile's version, or the one running.
/// </summary>
public static class CarNames
{
    private static Dictionary<string, Dictionary<int, string>>? cars;

    /// <summary>The Forza being read: "FH6", "FH5", "FH4", "FM8" (2023) or "FM7"; null: not a Forza.</summary>
    public static volatile string? Edition;

    /// <summary>The versions a profile can pick for a game (none: it has only one).</summary>
    public static IReadOnlyList<(int Version, string Title)> Versions(Game g) => g switch
    {
        Game.ForzaHorizon => new[] { (6, "Forza Horizon 6"), (5, "Forza Horizon 5"), (4, "Forza Horizon 4") },
        Game.ForzaMotorsport => new[] { (8, "Forza Motorsport (2023)"), (7, "Forza Motorsport 7") },
        _ => Array.Empty<(int, string)>(),
    };

    /// <summary>The version a running program is (ForzaHorizon5 → 5), or null.</summary>
    public static int? VersionOf(Game g, string bareProgram)
    {
        string p = bareProgram.ToLowerInvariant();
        if (g == Game.ForzaHorizon && p.StartsWith("forzahorizon") && int.TryParse(p["forzahorizon".Length..], out int v)) return v;
        if (g == Game.ForzaMotorsport) return p == "forzamotorsport7" ? 7 : p.StartsWith("forza_") ? 8 : null;
        return null;
    }

    /// <summary>A game's key: "FH" or "FM" and its version (the newest when it isn't known).</summary>
    public static string? Key(Game g, int? version) => g switch
    {
        Game.ForzaHorizon => $"FH{version ?? 6}",
        Game.ForzaMotorsport => $"FM{version ?? 8}",
        _ => null,
    };

    /// <summary>The car's name in this game (null: not in the list).</summary>
    public static string? Name(string? edition, int ordinal)
    {
        if (edition == null || ordinal <= 0) return null;
        cars ??= Load();
        if (cars.TryGetValue(edition, out var list) && list.TryGetValue(ordinal, out var n)) return n;
        // a car newer than the list, or a game it doesn't have: the same number is the same car in every Forza
        foreach (var (game, other) in cars.OrderByDescending(c => c.Key[..2] == edition[..2]).ThenByDescending(c => c.Key))
            if (other.TryGetValue(ordinal, out n)) return n;
        return null;
    }

    private static Dictionary<string, Dictionary<int, string>> Load()
    {
        var all = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = typeof(CarNames).Assembly.GetManifestResourceStream("DaisysApp.SimEffects.Cars.json");
            if (s == null) return all;
            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(s) ?? new();
            foreach (var (game, list) in raw)
                all[game] = list.Where(e => int.TryParse(e.Key, out _)).ToDictionary(e => int.Parse(e.Key), e => e.Value);
        }
        catch (Exception ex) { Logging.Log.Here.Warn("SimEffects: reading the car list", ex); }
        return all;
    }
}
