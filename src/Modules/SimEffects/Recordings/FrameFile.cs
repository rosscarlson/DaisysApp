using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaisysApp.Applets.SimEffects.Recordings;

/// <summary>
/// (since 4.20.0) How a <see cref="TelemetryFrame"/> is written to a recording: every public field of it, found by
/// reflection (so a field added later is recorded without anything here changing): numbers, flags and arrays as 4-byte
/// columns in each frame, texts and lists (the field, the cars near) only when they change, as JSON. A recording's file
/// names its columns, so one from another version plays too (what it doesn't have stays as a new frame has it).
/// </summary>
internal static class FrameColumns
{
    public sealed record Column(string Name, Func<TelemetryFrame, int> Get, Action<TelemetryFrame, int> Set);
    public sealed record Side(string Name, Type Type, FieldInfo Field)
    {
        /// <summary>A list made new with each frame (the cars near): written at most 20 times a second.</summary>
        public bool Often => Type == typeof(IReadOnlyList<NearCar>);
    }

    public static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static readonly IReadOnlyList<Column> All;
    public static readonly IReadOnlyList<Side> Sides;

    static FrameColumns()
    {
        var columns = new List<Column>();
        var sides = new List<Side>();
        foreach (var f in typeof(TelemetryFrame).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.Name == nameof(TelemetryFrame.Playback)) continue;
            var type = f.FieldType;
            if (type == typeof(float))
                columns.Add(new(f.Name, o => BitConverter.SingleToInt32Bits((float)f.GetValue(o)!), (o, v) => f.SetValue(o, BitConverter.Int32BitsToSingle(v))));
            else if (type == typeof(int)) columns.Add(new(f.Name, o => (int)f.GetValue(o)!, (o, v) => f.SetValue(o, v)));
            else if (type == typeof(uint)) columns.Add(new(f.Name, o => unchecked((int)(uint)f.GetValue(o)!), (o, v) => f.SetValue(o, unchecked((uint)v))));
            else if (type == typeof(bool)) columns.Add(new(f.Name, o => (bool)f.GetValue(o)! ? 1 : 0, (o, v) => f.SetValue(o, v != 0)));
            else if (type.IsEnum) columns.Add(new(f.Name, o => Convert.ToInt32(f.GetValue(o)), (o, v) => f.SetValue(o, Enum.ToObject(type, v))));
            else if (type == typeof(float[]))
            {
                int n = ((float[])f.GetValue(new TelemetryFrame())!).Length;
                for (int i = 0; i < n; i++)
                {
                    int at = i;
                    columns.Add(new($"{f.Name}[{at}]", o => BitConverter.SingleToInt32Bits(((float[])f.GetValue(o)!)[at]), (o, v) => ((float[])f.GetValue(o)!)[at] = BitConverter.Int32BitsToSingle(v)));
                }
            }
            else sides.Add(new(f.Name, type, f));
        }
        All = columns;
        Sides = sides;
    }
}

/// <summary>Writes a recording's frames (gzip): a line naming the columns, then each frame's time and columns, and the
/// texts and lists as they change.</summary>
internal sealed class FrameWriter : IDisposable
{
    public const string Magic = "DSIM1";
    private readonly BinaryWriter writer;
    private readonly object?[] last = new object?[FrameColumns.Sides.Count];
    private readonly double[] lastAt = new double[FrameColumns.Sides.Count];
    private double flushed;

    public int Frames { get; private set; }
    /// <summary>The last frame's time.</summary>
    public double Seconds { get; private set; }

    public FrameWriter(string path)
    {
        writer = new BinaryWriter(new GZipStream(File.Create(path), CompressionLevel.Fastest));
        writer.Write(Magic);
        writer.Write(string.Join(',', FrameColumns.All.Select(c => c.Name)));
        writer.Write(string.Join(',', FrameColumns.Sides.Select(s => s.Name)));
        Array.Fill(lastAt, double.NegativeInfinity);
    }

    public void Write(double seconds, TelemetryFrame f)
    {
        for (int i = 0; i < FrameColumns.Sides.Count; i++)
        {
            var side = FrameColumns.Sides[i];
            var now = side.Field.GetValue(f);
            if (Equals(now, last[i]) || (now is string { Length: 0 } && last[i] == null)) continue;
            if (side.Often && now != null && last[i] != null && seconds - lastAt[i] < 0.05) continue;
            writer.Write((byte)2);
            writer.Write((byte)i);
            writer.Write(JsonSerializer.Serialize(now, side.Type, FrameColumns.Json));
            last[i] = now;
            lastAt[i] = seconds;
        }
        writer.Write((byte)1);
        writer.Write(seconds);
        foreach (var c in FrameColumns.All) writer.Write(c.Get(f));
        Frames++;
        Seconds = seconds;
        // (a recording cut short by a crash keeps all but its last seconds)
        if (seconds - flushed > 5) { flushed = seconds; writer.Flush(); }
    }

    public void Dispose() => writer.Dispose();
}

/// <summary>A recording's frames, read to be played.</summary>
internal sealed class FrameData
{
    public readonly List<double> Times = new();
    private readonly List<int[]> rows = new();
    /// <summary>The texts and lists as they change, each from its row on.</summary>
    private readonly List<(int Row, FrameColumns.Side Side, object? Value)> changes = new();
    private FrameColumns.Column?[] columns = Array.Empty<FrameColumns.Column?>();

    public int Count => rows.Count;
    public double Seconds => Times.Count == 0 ? 0 : Times[^1];

    public static FrameData Load(string path)
    {
        var d = new FrameData();
        using var reader = new BinaryReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
        try
        {
            if (reader.ReadString() != FrameWriter.Magic) throw new InvalidDataException("not a recording");
            d.columns = reader.ReadString().Split(',').Select(n => FrameColumns.All.FirstOrDefault(c => c.Name == n)).ToArray();
            var sides = reader.ReadString().Split(',').Select(n => FrameColumns.Sides.FirstOrDefault(s => s.Name == n)).ToArray();
            var bytes = new byte[d.columns.Length * 4];
            while (true)
            {
                byte kind = reader.ReadByte();
                if (kind == 1)
                {
                    double t = reader.ReadDouble();
                    reader.BaseStream.ReadExactly(bytes);
                    var row = new int[d.columns.Length];
                    Buffer.BlockCopy(bytes, 0, row, 0, bytes.Length);
                    d.rows.Add(row);
                    d.Times.Add(t);
                }
                else if (kind == 2)
                {
                    int i = reader.ReadByte();
                    string json = reader.ReadString();
                    if (i < sides.Length && sides[i] is { } side)
                        d.changes.Add((d.rows.Count, side, JsonSerializer.Deserialize(json, side.Type, FrameColumns.Json)));
                }
                else throw new InvalidDataException("a damaged recording");
            }
        }
        catch (EndOfStreamException) { }
        // (cut short: what's there plays)
        catch (Exception ex) when (d.Count > 0) { Logging.Log.Here.Warn($"Recordings: {Path.GetFileName(path)} read up to {d.Seconds:0.0} s: {ex.Message}"); }
        return d;
    }

    /// <summary>The first frame at <paramref name="t"/> or after (<see cref="Count"/>: none).</summary>
    public int At(double t)
    {
        int i = Times.BinarySearch(t);
        return i >= 0 ? i : ~i;
    }

    /// <summary>Fills frames in turn: it keeps where it is in the texts and lists.</summary>
    public sealed class Cursor(FrameData data)
    {
        private TelemetryFrame holder = new();
        private int row = -1, change;

        public void Fill(TelemetryFrame f, int at)
        {
            if (at < row) { holder = new(); change = 0; }
            row = at;
            for (; change < data.changes.Count && data.changes[change].Row <= at; change++)
            {
                var (_, side, value) = data.changes[change];
                if (value != null || side.Type != typeof(string)) side.Field.SetValue(holder, value);
            }
            var values = data.rows[at];
            for (int i = 0; i < values.Length; i++) data.columns[i]?.Set(holder, values[i]);
            holder.Playback = true;
            holder.CopyTo(f);
        }
    }
}
