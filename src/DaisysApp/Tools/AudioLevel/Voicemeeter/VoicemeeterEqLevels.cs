using DaisysApp.Tools.AudioLevel.Audio;

namespace DaisysApp.Tools.AudioLevel.Voicemeeter;

/// <summary>
/// Speaker levels stored in Voicemeeter itself: each bus channel's level is set with two cells of that channel's bus EQ
/// (cells 5 and 6 in Voicemeeter's EQ dialog), a low shelf and a high shelf at the same frequency, Q and gain.
/// Together they change the level evenly across the whole spectrum (the two shelves multiply to a flat gain), so the
/// EQ acts as a per-channel volume. Voicemeeter saves the values with its own settings, so they stay applied without
/// this app running. Range: −24 to +12 dB (within the cell gain range of −36 to +18 dB).
/// </summary>
public sealed class VoicemeeterEqLevels : ILevelControl
{
    /// <summary>Zero-based EQ cells used for the level (shown as cells 5 and 6 in Voicemeeter).</summary>
    public const int LowShelfCell = 4, HighShelfCell = 5;
    private const int LowShelfType = 5, HighShelfType = 6;
    private const double ShelfHz = 1000, ShelfQ = 1;

    private readonly int busIndex;
    private readonly int[] map;                       // speaker channel -> bus channel (0..7) or -1
    private readonly double[] gains = new double[8];  // dB per bus channel, as last read or set
    private DateTime lastSet = DateTime.MinValue;

    public VoicemeeterEqLevels(string busName, int busIndex, int[] map)
    {
        BusName = busName;
        this.busIndex = busIndex;
        this.map = map;
        Reload();
    }

    public string BusName { get; }
    public string Description => $"Voicemeeter bus {BusName} EQ";
    public double MinDb => -24;
    public double MaxDb => 12;

    public bool CanControl(int channel) => channel >= 0 && channel < map.Length && map[channel] is >= 0 and < 8;

    public double Get(int channel) => gains[map[channel]];

    public void Set(int channel, double db)
    {
        int c = map[channel];
        double g = Math.Round(Math.Clamp(db, MinDb, MaxDb), 1);
        gains[c] = g;
        lastSet = DateTime.Now;
        VoicemeeterRemote.Set(Script(c, g));
    }

    private string Cell(int channel, int cell, string field) => $"Bus[{busIndex}].EQ.channel[{channel}].cell[{cell}].{field}";

    private IEnumerable<(string, double)> Script(int c, double g)
    {
        if (Math.Abs(g) < 0.05)
        {
            // 0 dB: switch our cells off so the channel's EQ is left as it was
            foreach (int cell in new[] { LowShelfCell, HighShelfCell })
            {
                yield return (Cell(c, cell, "gain"), 0);
                yield return (Cell(c, cell, "on"), 0);
            }
            yield break;
        }
        yield return ($"Bus[{busIndex}].EQ.on", 1);
        foreach (var (cell, type) in new[] { (LowShelfCell, LowShelfType), (HighShelfCell, HighShelfType) })
        {
            yield return (Cell(c, cell, "type"), type);
            yield return (Cell(c, cell, "f"), ShelfHz);
            yield return (Cell(c, cell, "q"), ShelfQ);
            yield return (Cell(c, cell, "gain"), g);
            yield return (Cell(c, cell, "on"), 1);
        }
    }

    /// <summary>Reads our cells back from Voicemeeter. Returns true if any level differs from what we had.</summary>
    private bool Reload()
    {
        bool changed = false;
        for (int c = 0; c < 8; c++)
        {
            double g = 0;
            if (VoicemeeterRemote.Get(Cell(c, LowShelfCell, "on")) == 1
                && VoicemeeterRemote.Get(Cell(c, LowShelfCell, "type")) == LowShelfType
                && VoicemeeterRemote.Get(Cell(c, LowShelfCell, "gain")) is float gain)
                g = Math.Round(gain, 1);
            if (g != gains[c]) changed = true;
            gains[c] = g;
        }
        return changed;
    }

    /// <summary>Picks up changes made in Voicemeeter itself (or after it restarted). Call periodically.</summary>
    public void Poll()
    {
        if ((DateTime.Now - lastSet).TotalSeconds < 1.5) return; // our own writes take a moment to read back
        if (VoicemeeterRemote.Refresh() == 1 && Reload()) Changed?.Invoke();
    }

    /// <summary>
    /// When the bus EQ is off but other bands are set up on it, turning it on (which setting a level does) would make
    /// those bands audible too. Returns a warning to show in that case.
    /// </summary>
    public string? OtherEqWarning()
    {
        if (VoicemeeterRemote.Get($"Bus[{busIndex}].EQ.on") != 0) return null;
        for (int c = 0; c < 8; c++)
            for (int cell = 0; cell < 6; cell++)
            {
                if (cell is LowShelfCell or HighShelfCell) continue;
                if (VoicemeeterRemote.Get(Cell(c, cell, "on")) != 1) continue;
                float type = VoicemeeterRemote.Get(Cell(c, cell, "type")) ?? 0;
                float gain = VoicemeeterRemote.Get(Cell(c, cell, "gain")) ?? 0;
                if (type is >= 1 and <= 4 || Math.Abs(gain) > 0.05)
                    return $"Bus {BusName}'s EQ is off but has other bands set up. Setting a level turns the bus EQ on, which also makes those bands active.";
            }
        return null;
    }

    public event Action? Changed;

    public void Dispose() { /* the levels live in Voicemeeter */ }
}
