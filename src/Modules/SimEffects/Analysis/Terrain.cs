using System.IO;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// Which of a game's maps the car is on (since 4.10.8; <see cref="AnalysisSettings.Worlds"/>): Forza Horizon doesn't say,
/// and its maps share their coordinates (Mexico's, Sierra Nueva's and Hot Wheels Park's tracks can start at the same
/// place), so each map's ground is learnt as it's driven (the lowest the car's been in each <see cref="Cell"/> m square)
/// and the map whose ground the car's driving on is the one it's on. The squares of the last <see cref="Window"/> it went
/// through are the evidence: another map's ground that fits them all (where the map set has none that fits) is switched
/// to. Ground is learnt only while the map set fits (or nothing's known there), so a map driven unpicked doesn't spoil
/// it; a map picked by hand takes the squares learnt since the last switch. Kept in AnalysisTerrain.bin. Fed on the UI
/// thread.
/// </summary>
internal sealed class Terrain
{
    private const float Cell = 25;
    /// <summary>How far under and over a square's lowest the car can be and still be on that ground, metres (slopes,
    /// bridges and jumps over it).</summary>
    private const float Under = 12, Over = 60;
    private const int Window = 16;
    private const int MaxRecent = 20000;
    private static readonly string FilePath = Path.Combine(AppPaths.SettingsFolder, "AnalysisTerrain.bin");

    private readonly AnalysisSettings settings;
    /// <summary>Each map's ground, by <see cref="AnalysisSettings.WorldKey"/>: a square's lowest, metres.</summary>
    private readonly Dictionary<string, Dictionary<int, short>> grounds = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The last squares gone through: per map (the game's, in order) 1 fits, -1 doesn't, 0 not known.</summary>
    private readonly Queue<sbyte[]> seen = new();
    /// <summary>The squares first learnt since the last switch, and on which map: moved by a map picked by hand.</summary>
    private readonly List<int> recent = new();
    private string? recentKey;
    private int lastCell = int.MinValue;
    private string? lastGame;
    private bool dirty;
    private long savedAt = Environment.TickCount64;

    public Terrain(AnalysisSettings settings)
    {
        this.settings = settings;
        Load();
    }

    /// <summary>The car's place in <paramref name="game"/>: the map it's found on, when that isn't the one set (null: no change).</summary>
    public string? Feed(string game, TelemetryFrame f)
    {
        if (!AnalysisSettings.Worlds.TryGetValue(game, out var maps) || !f.Live || !f.HasRace || (f.PosX == 0 && f.PosY == 0 && f.PosZ == 0)) return null;
        if (game != lastGame) { Reset(); lastGame = game; }
        if (dirty && Environment.TickCount64 - savedAt > 60_000) Save();
        int cell = Key(f.PosX, f.PosZ);
        if (cell == lastCell) { Lower(game, cell, f.PosY); return null; }
        lastCell = cell;
        string? map = settings.MapOf(game);
        int now = map == null ? 0 : Array.IndexOf(maps, map);
        var row = new sbyte[maps.Length];
        for (int i = 0; i < maps.Length; i++)
            row[i] = Ground(AnalysisSettings.WorldKey(game, i == 0 ? null : maps[i]), false) is { } g && g.TryGetValue(cell, out var lo)
                ? (sbyte)(f.PosY >= lo - Under && f.PosY <= lo + Over ? 1 : -1) : (sbyte)0;
        seen.Enqueue(row);
        while (seen.Count > Window) seen.Dequeue();
        int Fits(int i) => seen.Count(r => r[i] > 0);
        int Misses(int i) => seen.Count(r => r[i] < 0);
        int fits = Fits(now), misses = Misses(now);
        if (fits == 0)
        {
            // (another map's ground here, all of it, where the one set has none: that map)
            for (int i = 0; i < maps.Length; i++)
            {
                if (i == now || Misses(i) > 0) continue;
                int other = Fits(i);
                if ((other >= 8 && misses >= 3) || other >= Window)
                {
                    Logging.Log.Here.Info($"Analysis: on {maps[i]}, not {maps[now]} ({other} squares fit its ground, {misses} miss {maps[now]}'s)");
                    Reset();
                    return maps[i];
                }
            }
        }
        // (learnt where the map set fits, or nothing's known; not where another's ground does and its doesn't)
        bool elsewhere = fits == 0 && Enumerable.Range(0, maps.Length).Any(i => i != now && Fits(i) >= 4 && Misses(i) == 0);
        if (misses == 0 && !elsewhere) Learn(AnalysisSettings.WorldKey(game, map), cell, f.PosY);
        return null;
    }

    /// <summary>A map picked by hand: the squares learnt since the last switch were its.</summary>
    public void Picked(string game, string? map)
    {
        string key = AnalysisSettings.WorldKey(game, map);
        if (recentKey != null && recentKey != key && recent.Count > 0 && Ground(recentKey, false) is { } from)
        {
            var to = Ground(key, true)!;
            int moved = 0;
            foreach (int c in recent)
                if (from.Remove(c, out var lo)) { to.TryAdd(c, lo); moved++; }
            Logging.Log.Here.Info($"Analysis: {moved} squares of ground moved from {AnalysisSettings.WorldName(recentKey)} to {AnalysisSettings.WorldName(key)}");
            dirty = true;
        }
        Reset();
        recentKey = key;
        Save();
    }

    private void Reset()
    {
        seen.Clear();
        recent.Clear();
        recentKey = null;
        lastCell = int.MinValue;
    }

    private void Learn(string key, int cell, float y)
    {
        var g = Ground(key, true)!;
        short h = (short)Math.Clamp(MathF.Round(y), short.MinValue, short.MaxValue);
        if (g.TryGetValue(cell, out var lo)) { if (h < lo) { g[cell] = h; dirty = true; } return; }
        g[cell] = h;
        dirty = true;
        if (recentKey != key) { recent.Clear(); recentKey = key; }
        if (recent.Count < MaxRecent) recent.Add(cell);
    }

    /// <summary>Still in the same square: its lowest kept, where it fits.</summary>
    private void Lower(string game, int cell, float y)
    {
        string key = AnalysisSettings.WorldKey(game, settings.MapOf(game));
        if (Ground(key, false) is { } g && g.TryGetValue(cell, out var lo) && y < lo && y >= lo - Under)
        {
            g[cell] = (short)MathF.Round(y);
            dirty = true;
        }
    }

    private Dictionary<int, short>? Ground(string key, bool make) =>
        grounds.TryGetValue(key, out var g) ? g : make ? grounds[key] = new Dictionary<int, short>() : null;

    private static int Key(float x, float z)
    {
        int cx = (int)MathF.Floor(x / Cell), cz = (int)MathF.Floor(z / Cell);
        return ((cx + 32768) & 0xFFFF) << 16 | ((cz + 32768) & 0xFFFF);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            using var r = new BinaryReader(File.OpenRead(FilePath));
            if (r.ReadInt32() != 1) return;
            int maps = r.ReadInt32();
            for (int m = 0; m < maps; m++)
            {
                var g = Ground(r.ReadString(), true)!;
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) g[r.ReadInt32()] = r.ReadInt16();
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Analysis: reading the maps' ground", ex); }
    }

    public void Save()
    {
        savedAt = Environment.TickCount64;
        if (!dirty) return;
        dirty = false;
        try
        {
            string temp = FilePath + ".tmp";
            using (var w = new BinaryWriter(File.Create(temp)))
            {
                w.Write(1);
                w.Write(grounds.Count);
                foreach (var (key, g) in grounds)
                {
                    w.Write(key);
                    w.Write(g.Count);
                    foreach (var (c, lo) in g) { w.Write(c); w.Write(lo); }
                }
            }
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Analysis: saving the maps' ground", ex); }
    }
}
