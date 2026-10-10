using DaisysApp.Applets.AudioTools.Tests.Voicemeeter;
using DaisysApp.Shared.Voicemeeter;

namespace DaisysApp.Applets.AudioTools.Tests.Eq;

/// <summary>
/// Speaker EQ in Voicemeeter's bus EQ: cells 1–4 of each bus channel as peaking filters (cells 5 and 6 hold the
/// speaker's level, see <see cref="VoicemeeterEqLevels"/>). Voicemeeter saves them with its own settings, so they stay
/// applied without Daisy's App running.
/// </summary>
public sealed class VoicemeeterEq(string busName, int busIndex, int[] map) : IEqControl
{
    public const int Cells = 4;
    private const int PeakType = 0;
    private static readonly string[] Fields = ["on", "type", "f", "gain", "q"];

    public string BusName { get; } = busName;
    public string Description => F("Voicemeeter, bus {0} EQ (cells 1–4 of each channel; stays applied without Daisy's App running)", BusName);

    public EqLimits Limits(double maxBoostDb) => new(Cells, MinQ: 1, MaxQ: 20, MinHz: 20, MaxHz: 20000, MaxCutDb: 12, MaxBoostDb: maxBoostDb);

    public bool CanControl(int channel) => channel >= 0 && channel < map.Length && map[channel] is >= 0 and < 8;

    private string Cell(int channel, int cell, string field) => $"Bus[{busIndex}].EQ.channel[{channel}].cell[{cell}].{field}";

    public IReadOnlyList<EqBand> Get(int channel)
    {
        int c = map[channel];
        var bands = new List<EqBand>();
        for (int cell = 0; cell < Cells; cell++)
        {
            if (VoicemeeterRemote.Get(Cell(c, cell, "on")) != 1 || VoicemeeterRemote.Get(Cell(c, cell, "type")) != PeakType) continue;
            float gain = VoicemeeterRemote.Get(Cell(c, cell, "gain")) ?? 0;
            if (Math.Abs(gain) < 0.05) continue;
            bands.Add(new EqBand(VoicemeeterRemote.Get(Cell(c, cell, "f")) ?? 1000, gain, VoicemeeterRemote.Get(Cell(c, cell, "q")) ?? 1));
        }
        return bands;
    }

    public void Set(int channel, IReadOnlyList<EqBand> bands)
    {
        int c = map[channel];
        var script = new List<(string, double)>();
        if (bands.Count > 0) script.Add(($"Bus[{busIndex}].EQ.on", 1));
        for (int cell = 0; cell < Cells; cell++)
        {
            if (cell < bands.Count)
            {
                var b = bands[cell];
                script.Add((Cell(c, cell, "type"), PeakType));
                script.Add((Cell(c, cell, "f"), Math.Clamp(b.Hz, 20, 20000)));
                script.Add((Cell(c, cell, "q"), Math.Clamp(b.Q, 1, 100)));
                script.Add((Cell(c, cell, "gain"), Math.Clamp(b.GainDb, -12, 12)));
                script.Add((Cell(c, cell, "on"), 1));
            }
            else
            {
                script.Add((Cell(c, cell, "gain"), 0));
                script.Add((Cell(c, cell, "on"), 0));
            }
        }
        VoicemeeterRemote.Set(script);
    }

    public object Snapshot()
    {
        var values = new List<(string, double)>();
        foreach (var c in map.Where(m => m is >= 0 and < 8).Distinct())
            for (int cell = 0; cell < Cells; cell++)
                foreach (var field in Fields)
                    if (VoicemeeterRemote.Get(Cell(c, cell, field)) is float v) values.Add((Cell(c, cell, field), v));
        if (VoicemeeterRemote.Get($"Bus[{busIndex}].EQ.on") is float on) values.Add(($"Bus[{busIndex}].EQ.on", on));
        return values;
    }

    public void Restore(object snapshot)
    {
        if (snapshot is List<(string, double)> values && values.Count > 0) VoicemeeterRemote.Set(values);
    }

    public string? Warning
    {
        get
        {
            // cells 1–4 already used for something this app didn't put there (another filter type)
            foreach (var c in map.Where(m => m is >= 0 and < 8).Distinct())
                for (int cell = 0; cell < Cells; cell++)
                    if (VoicemeeterRemote.Get(Cell(c, cell, "on")) == 1 && VoicemeeterRemote.Get(Cell(c, cell, "type")) is float t && t != PeakType)
                        return F("Bus {0}'s EQ already has other filters in cells 1–4. The EQ Wizard replaces them (Cancel puts them back).", BusName);
            return null;
        }
    }

    /// <summary>The bus's EQ switches and every cell of every channel (all six, including the level cells).</summary>
    public string Dump()
    {
        var sb = new System.Text.StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.AppendLine($"Voicemeeter bus {BusName} (index {busIndex}); speaker channel -> bus channel: {string.Join(", ", map.Select((m, i) => $"{i}->{m}"))}");
        sb.AppendLine($"Bus[{busIndex}].EQ.on = {VoicemeeterRemote.Get($"Bus[{busIndex}].EQ.on")?.ToString(inv) ?? "?"}, EQ.AB = {VoicemeeterRemote.Get($"Bus[{busIndex}].EQ.AB")?.ToString(inv) ?? "?"}");
        for (int c = 0; c < 8; c++)
            for (int cell = 0; cell < 6; cell++)
                sb.AppendLine(string.Format(inv, "channel[{0}].cell[{1}]  {2}", c, cell,
                    string.Join("  ", Fields.Select(f => $"{f}={VoicemeeterRemote.Get(Cell(c, cell, f))?.ToString(inv) ?? "?"}"))));
        return sb.ToString();
    }

    public void Dispose() { /* the EQ lives in Voicemeeter */ }
}
