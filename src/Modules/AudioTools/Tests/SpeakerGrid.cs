using System.Windows;
using DaisysApp.Shared.Audio;

namespace DaisysApp.Applets.AudioTools.Tests;

/// <summary>A cell of the speaker map's grid (column and row from the top-left, front of the room at the top).</summary>
public sealed record GridCell(int Col, int Row);

/// <summary>
/// The speaker map is an N × N grid (Settings → Audio); each speaker tile sits in a cell and can be dragged to
/// another. The middle of the grid is kept free of speakers (<see cref="Reserved"/>: the inner 3 × 3 cells of the usual
/// 5 × 5): the listener is drawn there, and the microphone's live analyser while the test signal plays. Geometry is in
/// the map's own units (it's scaled to fit).
/// </summary>
public static class SpeakerGrid
{
    public const int DefaultSize = 5, MinSize = 3, MaxSize = 9;
    private const double Gap = 14, Pad = 16, LabelBand = 22;
    private const double TileWidth = SpeakerLayout.TileWidth, TileHeight = SpeakerLayout.TileHeight;

    public static double Width(int n) => Pad * 2 + n * TileWidth + (n - 1) * Gap;
    public static double Height(int n) => LabelBand + Pad * 2 + n * TileHeight + (n - 1) * Gap;

    /// <summary>Top-left of a cell (where a tile in it is drawn).</summary>
    public static Point CellOrigin(GridCell c) => new(Pad + c.Col * (TileWidth + Gap), LabelBand + Pad + c.Row * (TileHeight + Gap));

    /// <summary>The cell whose area is nearest to a tile drawn with its top-left at <paramref name="tileOrigin"/>.</summary>
    public static GridCell CellAt(int n, Point tileOrigin) => new(
        Math.Clamp((int)Math.Round((tileOrigin.X - Pad) / (TileWidth + Gap)), 0, n - 1),
        Math.Clamp((int)Math.Round((tileOrigin.Y - LabelBand - Pad) / (TileHeight + Gap)), 0, n - 1));

    /// <summary>Centre of the map, where the listener is drawn.</summary>
    public static Point Centre(int n) => new(Width(n) / 2, LabelBand + Pad + (n * TileHeight + (n - 1) * Gap) / 2);

    /// <summary>How many cells across the middle block is: everything inside the outer ring up to 5 × 5 (3 × 3 there, the
    /// listener's cell alone on 3 × 3), then 3 × 3 (odd sizes) or 4 × 4 (even) in the middle of bigger grids.</summary>
    public static int ReservedSize(int n) => n <= 5 ? Math.Max(1, n - 2) : n % 2 == 1 ? 3 : 4;

    /// <summary>The middle block's cells, where speakers can't go (the listener's and the analyser's place).</summary>
    public static HashSet<GridCell> Reserved(int n)
    {
        int k = ReservedSize(n), start = (n - k) / 2;
        var set = new HashSet<GridCell>();
        for (int r = start; r < start + k; r++)
            for (int c = start; c < start + k; c++) set.Add(new GridCell(c, r));
        return set;
    }

    /// <summary>The middle block's area on the map (from its first cell's corner to its last cell's).</summary>
    public static Rect ReservedArea(int n)
    {
        int k = ReservedSize(n), start = (n - k) / 2;
        var a = CellOrigin(new GridCell(start, start));
        var b = CellOrigin(new GridCell(start + k - 1, start + k - 1));
        return new Rect(a, new Point(b.X + TileWidth, b.Y + TileHeight));
    }

    /// <summary>Where each channel goes by default: from its usual room position, or in reading order for unknown layouts.</summary>
    public static Dictionary<int, GridCell> Defaults(SpeakerLayoutInfo layout, int n)
    {
        var wanted = new List<(int Channel, GridCell Cell)>();
        int i = 0;
        foreach (var s in layout.Speakers)
        {
            GridCell cell;
            if (layout.ShowListener)
            {
                // the standard positions span x 0.10–0.90 and y 0.095–0.87 of the room
                double x = Math.Clamp((s.X - 0.10) / 0.80, 0, 1), y = Math.Clamp((s.Y - 0.095) / 0.775, 0, 1);
                cell = new GridCell((int)Math.Round(x * (n - 1)), (int)Math.Round(y * (n - 1)));
            }
            else cell = new GridCell(i % n, Math.Min(i / n, n - 1));
            wanted.Add((s.Channel, cell));
            i++;
        }
        return Resolve(wanted, n, Reserved(n));
    }

    /// <summary>Moves saved positions onto a grid of a different size, keeping their place in the room.</summary>
    public static Dictionary<int, GridCell> Rescale(Dictionary<int, GridCell> cells, int from, int to)
    {
        double f = from <= 1 ? 0 : (to - 1.0) / (from - 1.0);
        return Resolve(cells.Select(p => (p.Key, new GridCell((int)Math.Round(p.Value.Col * f), (int)Math.Round(p.Value.Row * f)))).ToList(),
            to, Reserved(to));
    }

    /// <summary>Gives every channel its wanted cell, or the nearest free one if it's taken (or in the middle block).</summary>
    public static Dictionary<int, GridCell> Resolve(IList<(int Channel, GridCell Cell)> wanted, int n, IEnumerable<GridCell>? reserved)
    {
        var taken = new HashSet<GridCell>(reserved ?? []);
        var result = new Dictionary<int, GridCell>();
        foreach (var (channel, w) in wanted)
        {
            var want = new GridCell(Math.Clamp(w.Col, 0, n - 1), Math.Clamp(w.Row, 0, n - 1));
            var cell = taken.Contains(want) ? Nearest(want, n, taken) : want;
            if (cell == null) continue; // more speakers than cells: left for the caller to place
            taken.Add(cell);
            result[channel] = cell;
        }
        return result;
    }

    private static GridCell? Nearest(GridCell from, int n, HashSet<GridCell> taken)
    {
        GridCell? best = null;
        double bestDistance = double.MaxValue;
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                var cell = new GridCell(c, r);
                if (taken.Contains(cell)) continue;
                double d = Math.Pow(c - from.Col, 2) + Math.Pow(r - from.Row, 2);
                if (d < bestDistance) { bestDistance = d; best = cell; }
            }
        return best;
    }
}
