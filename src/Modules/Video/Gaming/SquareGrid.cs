using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Lays its children out as squares, <see cref="Columns"/> to a row, filling the width: each is as tall as it is wide,
/// or taller if its content needs it (then the whole row is). Collapsed children take no place.
/// </summary>
internal sealed class SquareGrid : Panel
{
    public int Columns { get; set; } = 2;
    public double Gap { get; set; } = 12;

    private readonly List<double> rows = new();
    private double cell;

    private List<UIElement> Shown() => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

    protected override Size MeasureOverride(Size available)
    {
        int cols = Math.Max(1, Columns);
        double width = double.IsInfinity(available.Width) ? 900 : available.Width;
        cell = Math.Max(0, (width - Gap * (cols - 1)) / cols);
        rows.Clear();
        var shown = Shown();
        for (int r = 0; r * cols < shown.Count; r++)
        {
            double h = cell;
            foreach (var c in shown.Skip(r * cols).Take(cols))
            {
                c.Measure(new Size(cell, double.PositiveInfinity));
                h = Math.Max(h, c.DesiredSize.Height);
            }
            rows.Add(h);
        }
        foreach (UIElement c in InternalChildren) if (c.Visibility == Visibility.Collapsed) c.Measure(new Size(0, 0));
        return new Size(width, rows.Sum() + Gap * Math.Max(0, rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size final)
    {
        int cols = Math.Max(1, Columns);
        var shown = Shown();
        double y = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            int i = 0;
            foreach (var c in shown.Skip(r * cols).Take(cols))
                c.Arrange(new Rect((cell + Gap) * i++, y, cell, rows[r]));
            y += rows[r] + Gap;
        }
        return final;
    }
}
