using System.Globalization;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>One row of a block: its key (kept in a widget's hidden rows), label and how it's shown.</summary>
public sealed record BlockRow(string Key, string Label, Func<Reading, string> Show);

/// <summary>A row in a cell, after the one before it with <see cref="Sep"/> between (in the label too).</summary>
public sealed record CellPart(string Key, string Sep = " ");

/// <summary>A label and a value made of one or more rows ("Class Index": "A 800"); a part with nothing to show is
/// left out, with its separator.</summary>
public sealed record BlockCell(params CellPart[] Parts);

/// <summary>A block of the Analysis tab (and its widget): Vehicle, Tires, Laps. <see cref="Lines"/>: its cells, line
/// by line, a column each; null: the car drawing, a row at each wheel (Tires).</summary>
public sealed record Block(string Id, string Title, BlockRow[] Rows, BlockCell[][]? Lines);

/// <summary>What the blocks show from: the newest frame (null: none for a while), the units, the names and the lap
/// tracker's track and personal best.</summary>
public sealed record Reading(TelemetryFrame? Frame, AnalysisSettings Settings, string? Track, float PersonalBest);

/// <summary>The blocks and their rows, from Forza's Data Out (Horizon 6's names in the comments).</summary>
public static class Blocks
{
    public const string None = "—";

    private static BlockCell Cell(params string[] keys) => new(keys.Select(k => new CellPart(k)).ToArray());

    public static readonly Block Vehicle = new("Vehicle", T("Vehicle"), new BlockRow[]
    {
        new("Car", T("Make"), r => Car(r, f => r.Settings.CarName("FH", f.CarOrdinal))),                // CarOrdinal
        new("Class", T("Class"), r => Car(r, f => ClassName(f.CarClass))),                             // CarClass
        new("PI", T("Index"), r => Car(r, f => f.CarPi.ToString(CultureInfo.CurrentCulture))),          // CarPerformanceIndex
        new("Group", T("Group"), r => Car(r, f => f.CarGroup.ToString(CultureInfo.CurrentCulture))),   // CarGroup
        new("Drive", T("Drive"), r => Car(r, f => DriveName(f.Drivetrain))),                           // DrivetrainType
        new("HP", T("HP"), r => Value(r, f => $"{f.Power / 745.7f:0}HP")),                             // Power (W)
        new("TQ", T("TQ"), r => Value(r, f => r.Settings.Units == AnalysisUnits.Metric ? $"{f.Torque:0}N·m" : $"{f.Torque * 0.7376f:0}LB/FT")), // Torque (N·m)
        new("Motor", T("Motor"), r => Car(r, f => Engine(f.Cylinders))),                               // NumCylinders
        new("Boost", T("Boost"), r => Value(r, f => r.Settings.Units == AnalysisUnits.Metric ? $"{f.Boost * 0.0689f:0.00} bar" : $"{f.Boost:0.0} psi")), // Boost (psi)
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
    }, null);

    public static readonly Block Laps = new("Laps", T("Laps"), new BlockRow[]
    {
        new("Last", T("Last"), r => Value(r, f => LapTime(f.LastLap))),           // LastLap
        new("Best", T("Best"), r => Value(r, f => LapTime(f.BestLap))),           // BestLap
        new("Curr", T("Curr"), r => Value(r, f => LapTime(f.CurrentLap))),       // CurrentLap
        new("PB", T("PB"), r => LapTime(r.PersonalBest)),
        new("Position", T("Pos"), r => Value(r, f => f.RacePosition > 0 ? f.RacePosition.ToString(CultureInfo.CurrentCulture) : None)), // RacePosition
        new("Lap", T("Lap"), r => Value(r, f => f.RaceTime > 0 ? (f.LapNumber + 1).ToString(CultureInfo.CurrentCulture) : None)), // LapNumber (0 the first)
        new("Race", T("Race"), r => Value(r, f => LapTime(f.RaceTime))),         // CurrentRaceTime
        new("Track", T("Track"), r => r.Track ?? None),
        new("Car", T("Make"), r => Car(r, f => r.Settings.CarName("FH", f.CarOrdinal))),
        new("Class", T("Class"), r => Car(r, f => ClassName(f.CarClass))),
        new("PI", T("Index"), r => Car(r, f => f.CarPi.ToString(CultureInfo.CurrentCulture))),
    }, new[]
    {
        new[] { Cell("Last"), Cell("Best") },
        new[] { Cell("Curr"), Cell("PB") },
        new[] { new BlockCell(new CellPart("Position"), new CellPart("Lap", " - ")), Cell("Race") },
        new[] { Cell("Track"), new BlockCell(new CellPart("Car"), new CellPart("Class", " - "), new CellPart("PI")) },
    });

    public static readonly Block[] All = { Vehicle, Tires, Laps };

    public static Block? ById(string id) => All.FirstOrDefault(b => b.Id == id);

    /// <summary>A time as Forza shows it, 1:23.456 (— for none).</summary>
    public static string LapTime(float s)
    {
        if (!(s > 0)) return None;
        var t = TimeSpan.FromSeconds(s);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture) : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    public static string ClassName(int c) => ForzaSource.ClassName(c) is { Length: > 0 } n ? n : "?";

    /// <summary>The engine as "V8": Forza sends only how many cylinders, so up to 5 are counted inline, 6 and more a V.</summary>
    public static string Engine(int cylinders) => cylinders switch { <= 0 => T("Electric"), <= 5 => $"I{cylinders}", _ => $"V{cylinders}" };

    public static string DriveName(int d) => d switch { 0 => "FWD", 1 => "RWD", 2 => "AWD", _ => "?" };

    private static string Car(Reading r, Func<TelemetryFrame, string> show) =>
        r.Frame is { HasRace: true, CarOrdinal: > 0 } f ? show(f) : None;

    private static string Value(Reading r, Func<TelemetryFrame, string> show) =>
        r.Frame is { HasRace: true } f ? show(f) : None;

    private static string Temp(Reading r, int wheel) => Value(r, f =>
        r.Settings.Units == AnalysisUnits.Metric ? $"{(f.TireTemp[wheel] - 32) / 1.8f:0} °C" : $"{f.TireTemp[wheel]:0} °F");
}
