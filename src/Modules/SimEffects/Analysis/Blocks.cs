using System.Globalization;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>One row of a block: its key (kept in a widget's hidden rows), label and how it's shown.</summary>
public sealed record BlockRow(string Key, string Label, Func<Reading, string> Show);

/// <summary>A row in a cell, after the one before it with <see cref="Sep"/> between (in the label too).</summary>
public sealed record CellPart(string Key, string Sep = " ");

/// <summary>A label and a value made of one or more rows ("Class Index": "A 800"); a part with nothing to show is
/// left out, with its separator.</summary>
public sealed record BlockCell(params CellPart[] Parts);

/// <summary>How a block is drawn: cells (label and value, <see cref="Block.Lines"/>), the car with its tyres, graphs
/// with their history, lap times in large type, large "3 / 8"s side by side, or the field in order (a column a row).</summary>
public enum BlockKind { Cells, Tires, Graphs, Timing, Big, Board }

/// <summary>A block of the Analysis tab (and its widget): Vehicle, Tires, Laps, Performance, Timing, Position, Leaderboard.
/// <see cref="Lines"/>: a <see cref="BlockKind.Cells"/> block's cells, line by line, a column each.</summary>
public sealed record Block(string Id, string Title, BlockRow[] Rows, BlockCell[][]? Lines, BlockKind Kind = BlockKind.Cells)
{
    /// <summary>A widget's first size.</summary>
    public System.Windows.Size WidgetSize => Kind switch
    {
        BlockKind.Tires => new(300, 200),
        BlockKind.Graphs => new(360, 330),
        BlockKind.Timing => new(300, 270),
        BlockKind.Big => new(360, 110),
        BlockKind.Board => new(320, 330),
        _ => new(420, 60 + (Lines?.Length ?? 1) * 22),
    };
}

/// <summary>What the blocks show from: the last frame driving (null: none yet; held while the game is paused), the
/// game ("FH6"…), the units, the names, the lap tracker's track, the bests (personal: this car on this track; the
/// session's; the class's: any car of this class on this track), how many cars and laps the race has (0: unknown), and
/// the power, torque and boost of the last minute, and how much of a sprint is done (0 – 1; -1: unknown or none).</summary>
public sealed record Reading(TelemetryFrame? Frame, string? Game, AnalysisSettings Settings, string? Track, float PersonalBest,
    float SessionBest = 0, int Racers = 0, int TotalLaps = 0, PerfHistory? History = null, float ClassBest = 0, float Progress = -1);

/// <summary>The blocks and their rows, from Forza's Data Out (Horizon 6's names in the comments).</summary>
public static class Blocks
{
    public const string None = "—";

    private static BlockCell Cell(params string[] keys) => new(keys.Select(k => new CellPart(k)).ToArray());

    public static readonly Block Vehicle = new("Vehicle", T("Vehicle"), new BlockRow[]
    {
        new("Car", T("Make"), r => Car(r, f => r.Settings.CarName(r.Game, f.CarOrdinal))),              // CarOrdinal
        new("Class", T("Class"), r => Car(r, f => ClassName(f.CarClass, r.Game))),                     // CarClass
        new("PI", T("Index"), r => Car(r, f => f.CarPi.ToString(CultureInfo.CurrentCulture))),          // CarPerformanceIndex
        new("Group", T("Group"), r => Car(r, f => f.CarGroup.ToString(CultureInfo.CurrentCulture))),   // CarGroup
        new("Drive", T("Drive"), r => Car(r, f => DriveName(f.Drivetrain))),                           // DrivetrainType
        new("HP", T("HP"), r => Value(r, f => $"{Hp(f.Power):0}HP")),                                  // Power (W)
        new("TQ", T("TQ"), r => Value(r, f => r.Settings.Units == AnalysisUnits.Metric ? $"{f.Torque:0}N·m" : $"{f.Torque * 0.7376f:0}LB/FT")), // Torque (N·m)
        new("Motor", T("Motor"), r => Car(r, f => Engine(f.Cylinders))),                               // NumCylinders
        new("Boost", T("Boost"), r => Value(r, f => BoostText(r.Settings, f.Boost))),                  // Boost (psi)
    }, new[]
    {
        new[] { Cell("Car"), Cell("Drive") },
        new[] { Cell("Class", "PI"), Cell("HP", "TQ", "Motor") },
        new[] { Cell("Group"), Cell("Boost") },
    });

    public static readonly Block Tires = new("Tires", T("Tires"), new BlockRow[]
    {
        new("FL", T("FL"), r => Temp(r, 0)),   // TireTempFrontLeft
        new("FR", T("FR"), r => Temp(r, 1)),   // TireTempFrontRight
        new("RL", T("RL"), r => Temp(r, 2)),   // TireTempRearLeft
        new("RR", T("RR"), r => Temp(r, 3)),   // TireTempRearRight
    }, null, BlockKind.Tires);

    public static readonly Block Laps = new("Laps", T("Laps"), new BlockRow[]
    {
        new("Last", T("Last"), r => Value(r, f => LapTime(f.LastLap))),           // LastLap
        new("Best", T("Best"), r => Value(r, f => LapTime(f.BestLap))),           // BestLap
        new("Curr", T("Curr"), r => Value(r, f => LapTime(f.CurrentLap))),       // CurrentLap
        new("PB", T("PB"), r => LapTime(r.PersonalBest)),
        new("Position", T("Pos"), r => Value(r, f => f.RacePosition > 0 ? f.RacePosition.ToString(CultureInfo.CurrentCulture) : None)), // RacePosition
        new("Lap", T("Lap"), r => Value(r, f => HasLaps(r) ? Of(f.LapNumber + 1, r.TotalLaps) : None)), // LapNumber (0 the first); since 3.27.1 in a race with laps only
        new("Done", T("Done"), DoneText), // a sprint's progress (since 3.27.1: Forza sends none, it's the distance over the track's)
        new("Race", T("Race"), r => Value(r, f => LapTime(f.RaceTime))),         // CurrentRaceTime
        new("Track", T("Track"), r => r.Track ?? None),
        new("Car", T("Make"), r => Car(r, f => r.Settings.CarName(r.Game, f.CarOrdinal))),
        new("Class", T("Class"), r => Car(r, f => ClassName(f.CarClass, r.Game))),
        new("PI", T("Index"), r => Car(r, f => f.CarPi.ToString(CultureInfo.CurrentCulture))),
    }, new[]
    {
        new[] { Cell("Last"), Cell("Best") },
        new[] { Cell("Curr"), Cell("PB") },
        new[] { new BlockCell(new CellPart("Position"), new CellPart("Lap", " - "), new CellPart("Done", " - ")), Cell("Race") },
        new[] { Cell("Track"), new BlockCell(new CellPart("Car"), new CellPart("Class", " - "), new CellPart("PI")) },
    });

    /// <summary>Power, torque and boost, each a graph of the last minute with its value now and its peak.</summary>
    public static readonly Block Performance = new("Performance", T("Performance"), new BlockRow[]
    {
        new("PerfHP", T("Power"), r => Value(r, f => $"{Hp(f.Power):0} hp")),
        new("PerfTQ", T("Torque"), r => Value(r, f => TorqueText(r.Settings, f.Torque))),
        new("PerfBoost", T("Boost"), r => Value(r, f => BoostText(r.Settings, f.Boost))),
    }, null, BlockKind.Graphs);

    /// <summary>Lap times to read at a glance: this lap, the last, the session's best, your best in this car (PB) and
    /// in any car of its class (PBC), on this track.</summary>
    public static readonly Block Timing = new("Timing", T("Timing"), new BlockRow[]
    {
        new("TimeCurr", T("Current"), r => Value(r, f => LapTime(f.CurrentLap))),
        new("TimeLast", T("Last"), r => Value(r, f => LapTime(f.LastLap))),
        new("TimeBest", T("Best"), r => LapTime(r.SessionBest)),
        new("TimePB", T("PB"), r => LapTime(r.PersonalBest)),
        new("TimePBC", T("PBC"), r => LapTime(r.ClassBest)),
    }, null, BlockKind.Timing);

    /// <summary>"3 / 8": the place in the race, of how many (the most seen, as Forza doesn't send it); beside it (since
    /// 3.24.2, the Lap block before) "2 / 3", the lap, of how many (learnt from the track's races), in a race with laps
    /// (<see cref="HasLaps"/>), or (since 3.27.1) a sprint's "45%" once its length is known; else Position alone.</summary>
    public static readonly Block Position = new("Position", T("Position"), new BlockRow[]
    {
        new("Place", T("Position"), r => Value(r, f => f.RaceTime > 0 && f.RacePosition > 0 ? Of(f.RacePosition, r.Racers) : None)),
        new("LapOf", T("Lap"), r => Value(r, f => f.RaceTime > 0 ? Of(f.LapNumber + 1, r.TotalLaps) : None)),
        new("DoneOf", T("Done"), DoneText),
    }, null, BlockKind.Big);

    /// <summary>The field around you (since 3.28.2; iRacing's, as Forza sends only your place): the places, car numbers,
    /// drivers, and each car's gap to the one ahead of it, so the gaps are between the cars, not to you. A column a row;
    /// how many cars it shows is in Settings (<see cref="AnalysisSettings.BoardRows"/>).</summary>
    public static readonly Block Leaderboard = new("Leaderboard", T("Leaderboard"), new BlockRow[]
    {
        new("BoardPos", T("Pos"), r => ""),
        new("BoardNo", "#", r => ""),
        new("BoardName", T("Driver"), r => ""),
        new("BoardGap", T("Gap"), r => ""),
    }, null, BlockKind.Board);

    /// <summary>The cars the leaderboard shows: <paramref name="rows"/> of them, yours in the middle (as near as the
    /// field's ends let it be), or all when there are no more.</summary>
    public static IReadOnlyList<Racer> Around(Standings field, int rows)
    {
        var all = field.Racers;
        if (all.Count <= rows) return all;
        int me = all.ToList().FindIndex(r => r.Me);
        int start = me < 0 ? 0 : Math.Clamp(me - (rows - 1) / 2, 0, all.Count - rows);
        return all.Skip(start).Take(rows).ToList();
    }

    /// <summary>A car's gap to the one ahead: "+1.4" in a race ("+1 L" a lap or more), "+0.352" between best laps
    /// outside one, "" for the leader or when it's not known.</summary>
    public static string GapText(Racer r, bool race) =>
        r.LapsDown >= 1 ? F("+{0} L", r.LapsDown)
        : float.IsNaN(r.Interval) ? ""
        : "+" + r.Interval.ToString(race ? "0.0" : "0.000", CultureInfo.CurrentCulture);

    /// <summary>A sprint's progress, "45%" (— until its track's length is known, and in a race with laps).</summary>
    public static string DoneText(Reading r) =>
        r.Progress >= 0 && !HasLaps(r) ? $"{Math.Floor(r.Progress * 100).ToString(CultureInfo.CurrentCulture)}%" : None;

    /// <summary>The race goes round laps: its track has had more than one, or a lap has ended in it (a sprint has none).</summary>
    public static bool HasLaps(Reading r) => r.Frame is { HasRace: true, RaceTime: > 0 } f && (r.TotalLaps > 1 || f.LapNumber > 0);

    public static readonly Block[] All = { Vehicle, Tires, Laps, Performance, Timing, Position, Leaderboard };

    public static Block? ById(string id) => All.FirstOrDefault(b => b.Id == id);

    /// <summary>A time as Forza shows it, 1:23.456 (— for none).</summary>
    public static string LapTime(float s)
    {
        if (!(s > 0)) return None;
        var t = TimeSpan.FromSeconds(s);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture) : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    /// <summary>"3 / 8", or "3" while the "of" isn't known (or is passed).</summary>
    public static string Of(int n, int of) => of >= n ? $"{n} / {of}" : n.ToString(CultureInfo.CurrentCulture);

    public static float Hp(float watts) => watts / 745.7f;

    public static float TorqueIn(AnalysisSettings s, float nm) => s.Units == AnalysisUnits.Metric ? nm : nm * 0.7376f;

    public static string TorqueText(AnalysisSettings s, float nm) => s.Units == AnalysisUnits.Metric ? $"{nm:0} N·m" : $"{nm * 0.7376f:0} lb·ft";

    public static float BoostIn(AnalysisSettings s, float psi) => s.Units == AnalysisUnits.Metric ? psi * 0.0689f : psi;

    public static string BoostText(AnalysisSettings s, float psi) => s.Units == AnalysisUnits.Metric ? $"{psi * 0.0689f:0.00} bar" : $"{psi:0.0} psi";

    /// <summary>A class's letter in a game ("FH6": R above S2).</summary>
    public static string ClassName(int c, string? game) => ForzaSource.ClassName(c, game) is { Length: > 0 } n ? n : "?";

    /// <summary>The engine as "V8": Forza sends only how many cylinders, so up to 5 are counted inline, 6 and more a V.</summary>
    public static string Engine(int cylinders) => cylinders switch { <= 0 => T("Electric"), <= 5 => $"I{cylinders}", _ => $"V{cylinders}" };

    public static string DriveName(int d) => d switch { 0 => "FWD", 1 => "RWD", 2 => "AWD", _ => "?" };

    private static string Car(Reading r, Func<TelemetryFrame, string> show) =>
        r.Frame is { HasRace: true, CarOrdinal: > 0 } f ? show(f) : None;

    private static string Value(Reading r, Func<TelemetryFrame, string> show) =>
        r.Frame is { HasRace: true } f ? show(f) : None;

    private static string Temp(Reading r, int wheel) => Value(r, f =>
        r.Settings.Celsius ? $"{(f.TireTemp[wheel] - 32) / 1.8f:0}°C" : $"{f.TireTemp[wheel]:0}°F");
}

/// <summary>
/// Power, torque and boost of the last minute while driving (10 a second, from the frames the blocks show), with each
/// one's peak since the car changed: the Performance block's graphs. Held while the game is paused.
/// </summary>
public sealed class PerfHistory
{
    public const int Size = 600;
    private readonly float[][] values = { new float[Size], new float[Size], new float[Size] };
    private int car = -1;

    /// <summary>How many samples there are (up to <see cref="Size"/>).</summary>
    public int Count { get; private set; }
    private int next;
    /// <summary>Each one's highest (watts, N·m, psi) since the car changed.</summary>
    public float[] Peak { get; } = new float[3];
    /// <summary>Each one's lowest (boost below 0 is vacuum).</summary>
    public float[] Low { get; } = new float[3];

    public void Add(TelemetryFrame f)
    {
        if (f.CarOrdinal != car)
        {
            car = f.CarOrdinal;
            Count = next = 0;
            Array.Clear(Peak);
            Array.Clear(Low);
        }
        Span<float> now = stackalloc float[] { Math.Max(0, f.Power), Math.Max(0, f.Torque), f.Boost };
        for (int i = 0; i < 3; i++)
        {
            values[i][next] = now[i];
            Peak[i] = Math.Max(Peak[i], now[i]);
            Low[i] = Math.Min(Low[i], now[i]);
        }
        next = (next + 1) % Size;
        Count = Math.Min(Size, Count + 1);
    }

    /// <summary>The <paramref name="i"/>th measure's <paramref name="k"/>th sample, oldest first.</summary>
    public float At(int i, int k) => values[i][(next - Count + k + Size) % Size];
}
