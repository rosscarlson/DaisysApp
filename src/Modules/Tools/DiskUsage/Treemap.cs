using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>
/// The scan as a treemap: every file a rectangle sized by its size, inside its folder's, coloured by its type and
/// shaded as a cushion (van Wijk and van de Wetering's, as WinDirStat draws them) so the folders stand out. Drawn into a
/// bitmap on a background thread, with which entry each pixel is, for pointing and clicking.
/// </summary>
internal sealed class Treemap : Grid
{
    private readonly Image image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle selection = new() { StrokeThickness = 2, Stroke = Brushes.White, Visibility = Visibility.Collapsed };
    private readonly Rectangle selectionShadow = new() { StrokeThickness = 4, Stroke = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private DiskTree? tree;
    private Func<int, uint>? colorOf; // an entry's colour (0xRRGGBB)
    private int[] at = []; // the entry at each pixel
    private Dictionary<int, Int32Rect> rects = new();
    private int width, height, version, selected = -1;
    private int highlight = -1; // a file type picked out (others dimmed), by its index
    private Func<int, int>? typeOf;

    /// <summary>The entry under the pointer (−1 for none), as it moves.</summary>
    public event Action<int>? Hovered;

    /// <summary>An entry clicked.</summary>
    public event Action<int>? Clicked;

    /// <summary>An entry right-clicked, for its menu.</summary>
    public event Action<int>? RightClicked;

    public Treemap()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Children.Add(image);
        overlay.Children.Add(selectionShadow);
        overlay.Children.Add(selection);
        Children.Add(overlay);
        SizeChanged += (_, _) => Redraw();
        MouseMove += (_, e) => Hovered?.Invoke(EntryAt(e.GetPosition(this)));
        MouseLeave += (_, _) => Hovered?.Invoke(-1);
        MouseLeftButtonDown += (_, e) => { if (EntryAt(e.GetPosition(this)) is >= 0 and var n) Clicked?.Invoke(n); };
        MouseRightButtonUp += (_, e) => { if (EntryAt(e.GetPosition(this)) is >= 0 and var n) RightClicked?.Invoke(n); };
    }

    private int EntryAt(Point p)
    {
        int x = (int)p.X, y = (int)p.Y;
        if (x < 0 || y < 0 || x >= width || y >= height || at.Length < width * height) return -1;
        return at[y * width + x];
    }

    /// <param name="color">An entry's colour as 0xRRGGBB (folders' small files: the folder's).</param>
    /// <param name="type">An entry's file type index (for <see cref="Highlight"/>), −1 for none.</param>
    public void Show(DiskTree? t, Func<int, uint> color, Func<int, int> type)
    {
        tree = t;
        colorOf = color;
        typeOf = type;
        Redraw();
    }

    /// <summary>Picks out one file type (the others dimmed), or −1 for none.</summary>
    public int Highlight
    {
        get => highlight;
        set { if (highlight == value) return; highlight = value; Redraw(); }
    }

    /// <summary>Outlines an entry (or the nearest folder above it that's big enough to show).</summary>
    public void Select(int node)
    {
        selected = node;
        Outline();
    }

    private void Outline()
    {
        int n = selected;
        while (n > 0 && tree != null && !rects.ContainsKey(n)) n = tree.Parent[n];
        if (n < 0 || tree == null || !rects.TryGetValue(n, out var r) || (n == 0 && selected != 0))
        {
            selection.Visibility = selectionShadow.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var s in new[] { selectionShadow, selection })
        {
            Canvas.SetLeft(s, r.X);
            Canvas.SetTop(s, r.Y);
            s.Width = Math.Max(3, r.Width);
            s.Height = Math.Max(3, r.Height);
            s.Visibility = Visibility.Visible;
        }
    }

    private void Redraw()
    {
        int w = (int)ActualWidth, h = (int)ActualHeight;
        var t = tree;
        int v = ++version;
        if (t == null || w < 4 || h < 4 || colorOf == null)
        {
            image.Source = null;
            at = [];
            rects = new();
            Outline();
            return;
        }
        var color = colorOf;
        var type = typeOf;
        int hi = highlight;
        Task.Run(() =>
        {
            var painter = new Painter(t, w, h, color, type, hi);
            painter.Paint();
            return painter;
        }).ContinueWith(task =>
        {
            if (v != version || task.Result is not { } p) return;
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), p.Pixels, w * 4, 0);
            bmp.Freeze();
            image.Source = bmp;
            width = w; height = h;
            at = p.At;
            rects = p.Rects;
            Outline();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Lays out and shades one treemap into pixels.</summary>
    private sealed class Painter(DiskTree t, int w, int h, Func<int, uint> color, Func<int, int>? type, int highlight)
    {
        public readonly int[] Pixels = new int[w * h];
        public readonly int[] At = Fill(new int[w * h], -1);
        public readonly Dictionary<int, Int32Rect> Rects = new();

        private const double RidgeHeight = 0.38, Falloff = 0.75, Ambient = 0.16, Diffuse = 0.84;
        private static readonly double Lx, Ly, Lz;

        static Painter()
        {
            double len = Math.Sqrt(1 + 1 + 100);
            Lx = -1 / len; Ly = -1 / len; Lz = 10 / len;
        }

        private static int[] Fill(int[] a, int v) { Array.Fill(a, v); return a; }

        public void Paint()
        {
            if (t.Size[0] <= 0) return;
            Node(0, 0, 0, w, h, new double[4], RidgeHeight);
        }

        // a rectangle for an entry: its ridge added to the surface, then its entries inside, or painted
        private void Node(int n, double x, double y, double rw, double rh, double[] surface, double ridge)
        {
            var s = (double[])surface.Clone();
            AddRidge(x, x + rw, ridge, ref s[0], ref s[2]);
            AddRidge(y, y + rh, ridge, ref s[1], ref s[3]);
            int x0 = (int)Math.Round(x), y0 = (int)Math.Round(y), x1 = (int)Math.Round(x + rw), y1 = (int)Math.Round(y + rh);
            if (x1 > x0 && y1 > y0) Rects[n] = new Int32Rect(x0, y0, x1 - x0, y1 - y0);
            if (!t.IsDir(n) || t.ChildCount[n] == 0) { Paint(n, x0, y0, x1, y1, s); return; }
            Children(n, x, y, rw, rh, s, ridge * Falloff);
        }

        // squarified layout (Bruls, Huizing and van Wijk): rows along the shorter side, each as square as can be
        private void Children(int n, double x, double y, double rw, double rh, double[] s, double ridge)
        {
            var items = new List<int>();
            for (int c = t.FirstChild[n], end = c + t.ChildCount[n]; c < end; c++)
                if (!t.IsDeleted(c) && t.Size[c] > 0) items.Add(c);
            double total = 0;
            foreach (var c in items) total += t.Size[c];
            if (total <= 0) { Paint(n, (int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(x + rw), (int)Math.Round(y + rh), s); return; }
            double scale = rw * rh / total; // pixels per byte
            int i = 0;
            while (i < items.Count)
            {
                if (rw < 1 || rh < 1) return;
                // what's left is too small to see one by one: the folder's colour
                if (t.Size[items[i]] * scale < 1.5)
                {
                    Paint(n, (int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(x + rw), (int)Math.Round(y + rh), s, small: true);
                    return;
                }
                bool horizontal = rw >= rh; // a row is a column along the left when wide
                double side = horizontal ? rh : rw;
                int j = i;
                double sum = 0, worst = double.MaxValue;
                while (j < items.Count)
                {
                    double a = t.Size[items[j]] * scale;
                    double newSum = sum + a;
                    double thick = newSum / side;
                    double maxA = t.Size[items[i]] * scale, minA = a;
                    double ratio = Math.Max(side * side * maxA / (newSum * newSum), newSum * newSum / (side * side * minA));
                    _ = thick;
                    if (ratio > worst) break;
                    worst = ratio;
                    sum = newSum;
                    j++;
                }
                double rowThick = sum / side, pos = 0;
                for (int k = i; k < j; k++)
                {
                    double len = t.Size[items[k]] * scale / rowThick;
                    if (horizontal) Node(items[k], x, y + pos, rowThick, len, s, ridge);
                    else Node(items[k], x + pos, y, len, rowThick, s, ridge);
                    pos += len;
                }
                if (horizontal) { x += rowThick; rw -= rowThick; }
                else { y += rowThick; rh -= rowThick; }
                i = j;
            }
        }

        private static void AddRidge(double a1, double a2, double h, ref double linear, ref double square)
        {
            if (a2 - a1 <= 0) return;
            linear += 4 * h * (a2 + a1) / (a2 - a1);
            square -= 4 * h / (a2 - a1);
        }

        private void Paint(int n, int x0, int y0, int x1, int y1, double[] s, bool small = false)
        {
            x0 = Math.Clamp(x0, 0, w); x1 = Math.Clamp(x1, 0, w); y0 = Math.Clamp(y0, 0, h); y1 = Math.Clamp(y1, 0, h);
            if (x1 <= x0 || y1 <= y0) return;
            uint c = small ? 0x8A8A8A : color(n);
            if (highlight >= 0 && (small || type?.Invoke(n) != highlight)) c = Dim(c);
            double r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            for (int py = y0; py < y1; py++)
            {
                double fy = py + 0.5;
                double ny = -(2 * s[3] * fy + s[1]);
                int row = py * w;
                for (int px = x0; px < x1; px++)
                {
                    double fx = px + 0.5;
                    double nx = -(2 * s[2] * fx + s[0]);
                    double cos = (nx * Lx + ny * Ly + Lz) / Math.Sqrt(nx * nx + ny * ny + 1);
                    double light = Ambient + Diffuse * Math.Max(0, cos);
                    int pr = Math.Min(255, (int)(r * light)), pg = Math.Min(255, (int)(g * light)), pb = Math.Min(255, (int)(b * light));
                    Pixels[row + px] = (pr << 16) | (pg << 8) | pb;
                    At[row + px] = n;
                }
            }
        }

        private static uint Dim(uint c)
        {
            uint r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            uint grey = (r + g + b) / 3 / 3;
            return (grey << 16) | (grey << 8) | grey;
        }
    }
}
