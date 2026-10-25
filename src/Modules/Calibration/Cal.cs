using System.Text.Json.Serialization;

namespace DaisysApp.Applets.Calibration;

/// <summary>
/// One axis's calibration as Windows keeps it (DirectInput's DIPROPCAL, in the device's raw units): the reading that
/// is all the way one way, the one that is the middle, and the one that is all the way the other way.
/// </summary>
public readonly record struct Cal(int Min, int Center, int Max)
{
    /// <summary>The centre halfway between <paramref name="min"/> and <paramref name="max"/>: a straight line.</summary>
    public static Cal Straight(int min, int max) => new(min, min + (max - min + 1) / 2, max);

    /// <summary>Usable: min below centre below max (DirectInput takes nothing else).</summary>
    [JsonIgnore]
    public bool Valid => Min < Center && Center < Max;

    /// <summary>
    /// What a game gets for <paramref name="raw"/>, 0 to 1, as DirectInput works it out: min to centre is the first
    /// half, centre to max the second, each a straight line, and beyond min or max it stays at the end.
    /// </summary>
    public double Cook(double raw)
    {
        if (!Valid) return 0;
        if (raw <= Center) return Math.Clamp(0.5 * (raw - Min) / (Center - Min), 0, 0.5);
        return Math.Clamp(0.5 + 0.5 * (raw - Center) / (Max - Center), 0.5, 1);
    }

    public override string ToString() => $"{Min} · {Center} · {Max}";
}

/// <summary>
/// The wizard's finding for one axis: the lowest and highest readings seen since it started (or Reset), and the
/// calibration they make, with the centre halfway (a straight line).
/// </summary>
public sealed class RangeFinder
{
    private readonly Axis axis;

    public int? Low { get; private set; }
    public int? High { get; private set; }

    public RangeFinder(Axis axis) => this.axis = axis;

    public void Reset() { Low = null; High = null; }

    public void See(int raw)
    {
        if (Low == null || raw < Low) Low = raw;
        if (High == null || raw > High) High = raw;
    }

    /// <summary>The part of the axis's whole range it has moved through, 0 to 1.</summary>
    public double Span => Low == null || High == null || axis.High <= axis.Low ? 0 : (double)(High.Value - Low.Value) / (axis.High - axis.Low);

    /// <summary>Moved far enough to be a calibration (a tenth of its range): end to end is expected.</summary>
    public bool Found => Span >= 0.1 && Result.Valid;

    public Cal Result => Low == null || High == null ? default : Cal.Straight(Low.Value, High.Value);
}
