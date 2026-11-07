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

    /// <summary>The game being read: "FH6", "FH5", "FH4", "FM8" (2023) or "FM7", and since 4.10.0 "iRacing", "ACC",
    /// "AC" or "ACEvo" (Analysis keeps each game's tracks and laps under it); null: no game.</summary>
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

    /// <summary>A game's key: "FH" or "FM" and its version (the newest when it isn't known), or the other games' names.</summary>
    public static string? Key(Game g, int? version) => g switch
    {
        Game.ForzaHorizon => $"FH{version ?? 6}",
        Game.ForzaMotorsport => $"FM{version ?? 8}",
        Game.IRacing => "iRacing",
        Game.Acc => "ACC",
        Game.AssettoCorsa => "AC",
        Game.AcEvo => "ACEvo",
        _ => null,
    };

    /// <summary>A Forza's key ("FH6", "FM8"…): the games whose cars are numbered in <c>Cars.json</c>.</summary>
    public static bool IsForza([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? edition) => edition != null && (edition.StartsWith("FH") || edition.StartsWith("FM"));

    /// <summary>(since 4.10.0) A number for a car or class the game names (iRacing's, the Assetto Corsas'), the same each
    /// time (FNV-1a), so Analysis keeps their laps as it does Forza's numbered cars; 0 for none.</summary>
    public static int Number(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        uint h = 2166136261;
        foreach (char c in id) { h ^= c; h *= 16777619; }
        int n = (int)(h & 0x7FFFFFFF);
        return n == 0 ? 1 : n;
    }

    /// <summary>A game's id for a car or track as a name: "ks_mazda_mx5_cup" → "Mazda MX5 Cup", "bmw_m4_gt3" → "BMW M4 GT3".</summary>
    public static string Pretty(string id)
    {
        string s = id.Trim();
        if (s.StartsWith("ks_", StringComparison.OrdinalIgnoreCase)) s = s[3..];
        var words = s.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Any(char.IsDigit) || !w.Any(c => "aeiouAEIOU".Contains(c)) ? w.ToUpperInvariant()
                : char.IsLower(w[0]) ? char.ToUpperInvariant(w[0]) + w[1..] : w);
        return string.Join(' ', words);
    }

    /// <summary>The car's name in this game (null: not in the list).</summary>
    public static string? Name(string? edition, int ordinal)
    {
        if (!IsForza(edition) || ordinal <= 0) return null;
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
