using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>A place on the map: a label (the hops there) at a latitude and longitude.</summary>
internal sealed record MapPoint(string Label, double Lat, double Lon, string Tip);

/// <summary>
/// A map of OpenStreetMap's tiles with a route drawn on it: numbered markers joined in order. It fits itself to the
/// route; the wheel zooms (where the pointer is) and dragging moves it. A click on a marker picks it (<see cref="Picked"/>);
/// <see cref="Selected"/> draws one in orange, on top. Tiles are downloaded as they're needed (with the
/// app's name, as OpenStreetMap asks) and kept in <c>%LOCALAPPDATA%\DaisysApp\MapTiles</c> for 30 days.
/// </summary>
internal sealed class MapView : FrameworkElement
{
    private const int TileSize = 256, MinZoom = 1, MaxZoom = 16;
    private static readonly HttpClient http = MakeClient();
    private static readonly ConcurrentDictionary<string, BitmapSource?> tiles = new();
    private static readonly ConcurrentDictionary<string, byte> loading = new();
    private IReadOnlyList<MapPoint> points = Array.Empty<MapPoint>();
    private int zoom = 2;
    private Point center = new(TileSize * 2, TileSize * 2); // in world pixels at the zoom
    private Point? dragFrom, pressedAt;
    private bool fitted;
    private int maxZoom = 10;
    private int? selected;

    /// <summary>A marker was clicked: its index in the route.</summary>
    public event Action<int>? Picked;

    /// <summary>The marker drawn in orange (its index in the route), or null.</summary>
    public int? Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            if (value is int i && i < points.Count) KeepInView(points[i]);
            InvalidateVisual();
        }
    }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "MapTiles");

    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.ShortName}/{typeof(MapView).Assembly.GetName().Version?.ToString(3)} (+https://github.com/rosscarlson/DaisysApp)");
        return h;
    }

    public MapView()
    {
        ClipToBounds = true;
        Cursor = Cursors.SizeAll;
        Focusable = false;
        SizeChanged += (_, _) => { if (!fitted && ActualWidth > 0) Fit(); };
        MouseLeftButtonDown += (_, e) => { dragFrom = pressedAt = e.GetPosition(this); CaptureMouse(); };
        MouseLeftButtonUp += (_, e) =>
        {
            var p = e.GetPosition(this);
            // a click (not a drag) on a marker picks it
            if (pressedAt is Point from && (p - from).Length < 4 && MarkerAt(p) is int i) Picked?.Invoke(i);
            dragFrom = pressedAt = null;
            ReleaseMouseCapture();
        };
        MouseMove += (_, e) =>
        {
            if (dragFrom is not Point from) return;
            var p = e.GetPosition(this);
            center = new Point(center.X - (p.X - from.X), center.Y - (p.Y - from.Y));
            dragFrom = p;
            InvalidateVisual();
        };
        MouseWheel += (_, e) =>
        {
            int z = Math.Clamp(zoom + (e.Delta > 0 ? 1 : -1), MinZoom, MaxZoom);
            if (z == zoom) return;
            var p = e.GetPosition(this);
            // the point under the pointer stays where it is
            var world = new Point(center.X + p.X - ActualWidth / 2, center.Y + p.Y - ActualHeight / 2);
            double f = Math.Pow(2, z - zoom);
            center = new Point(world.X * f - (p.X - ActualWidth / 2), world.Y * f - (p.Y - ActualHeight / 2));
            zoom = z;
            e.Handled = true;
            InvalidateVisual();
        };
    }

    /// <summary>Shows a route (refitting only when <paramref name="fit"/>, so a hand-moved map stays put; fitted no closer than <paramref name="zoom"/>).</summary>
    public void Show(IReadOnlyList<MapPoint> route, bool fit, int zoom = 10)
    {
        points = route;
        maxZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (selected >= route.Count) selected = null;
        if (fit) Fit();
        else InvalidateVisual();
    }

    /// <summary>Zooms and centres so every point shows (the world when there are none).</summary>
    public void Fit()
    {
        if (ActualWidth <= 0) { InvalidateVisual(); return; }
        fitted = true;
        if (points.Count == 0)
        {
            zoom = ActualWidth > 700 ? 2 : 1;
            center = new Point(TileSize * Math.Pow(2, zoom) / 2, TileSize * Math.Pow(2, zoom) / 2 * 0.8);
            InvalidateVisual();
            return;
        }
        for (zoom = maxZoom; zoom > MinZoom; zoom--)
        {
            var px = points.Select(p => World(p.Lat, p.Lon, zoom)).ToList();
            double w = px.Max(p => p.X) - px.Min(p => p.X), h = px.Max(p => p.Y) - px.Min(p => p.Y);
            if (w < ActualWidth - 80 && h < ActualHeight - 80) break;
        }
        var all = points.Select(p => World(p.Lat, p.Lon, zoom)).ToList();
        center = new Point((all.Max(p => p.X) + all.Min(p => p.X)) / 2, (all.Max(p => p.Y) + all.Min(p => p.Y)) / 2);
        InvalidateVisual();
    }

    /// <summary>Moves the map so a point shows (if it's off the edge).</summary>
    private void KeepInView(MapPoint p)
    {
        if (ActualWidth <= 0) return;
        var q = World(p.Lat, p.Lon, zoom);
        double x = q.X - (center.X - ActualWidth / 2), y = q.Y - (center.Y - ActualHeight / 2);
        if (x < 30 || y < 20 || x > ActualWidth - 30 || y > ActualHeight - 20) center = q;
    }

    /// <summary>The marker under a point of the control (the top one), or null.</summary>
    private int? MarkerAt(Point at)
    {
        double left = center.X - ActualWidth / 2, top = center.Y - ActualHeight / 2;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            var q = World(points[i].Lat, points[i].Lon, zoom);
            double r = Math.Max(12, points[i].Label.Length * 3.5 + 6);
            if (Math.Abs(q.X - left - at.X) <= r && Math.Abs(q.Y - top - at.Y) <= 12) return i;
        }
        return null;
    }

    /// <summary>Web Mercator: a latitude and longitude in world pixels at a zoom.</summary>
    private static Point World(double lat, double lon, int z)
    {
        double size = TileSize * Math.Pow(2, z);
        lat = Math.Clamp(lat, -85, 85) * Math.PI / 180;
        return new Point((lon + 180) / 360 * size, (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * size);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(TryFindResource("ControlBrush") as Brush ?? Brushes.Gray, null, new Rect(0, 0, w, h));
        int count = 1 << zoom;
        double left = center.X - w / 2, top = center.Y - h / 2;
        for (int ty = (int)Math.Floor(top / TileSize); ty <= (int)Math.Floor((top + h) / TileSize); ty++)
        {
            if (ty < 0 || ty >= count) continue;
            for (int tx = (int)Math.Floor(left / TileSize); tx <= (int)Math.Floor((left + w) / TileSize); tx++)
            {
                int wrapped = ((tx % count) + count) % count;
                if (Tile(zoom, wrapped, ty) is { } img)
                    dc.DrawImage(img, new Rect(tx * TileSize - left, ty * TileSize - top, TileSize, TileSize));
            }
        }

        // the route, then the markers on top
        var screen = points.Select(p => { var q = World(p.Lat, p.Lon, zoom); return new Point(q.X - left, q.Y - top); }).ToList();
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var line = new Pen(accent, 2.5) { DashStyle = new DashStyle(new double[] { 3, 1.5 }, 0) };
        for (int i = 1; i < screen.Count; i++) dc.DrawLine(line, screen[i - 1], screen[i]);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var white = new Pen(Brushes.White, 2);
        var picked = new SolidColorBrush((Color)ColorConverter.ConvertFromString(NetUi.Orange));
        // the selected marker last, so it's on top
        foreach (int i in Enumerable.Range(0, screen.Count).OrderBy(i => i == selected))
        {
            bool on = i == selected;
            var label = new FormattedText(points[i].Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), on ? 13 : 11, Brushes.White, dpi);
            double half = on ? 13 : 10, r = Math.Max(half, label.Width / 2 + 5);
            dc.DrawRoundedRectangle(on ? picked : accent, on ? new Pen(Brushes.White, 3) : white, new Rect(screen[i].X - r, screen[i].Y - half, r * 2, half * 2), half, half);
            dc.DrawText(label, new Point(screen[i].X - label.Width / 2, screen[i].Y - label.Height / 2));
        }

        // OpenStreetMap's data is used on the condition that it's credited
        var credit = new FormattedText("© OpenStreetMap contributors", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.Black, dpi);
        var box = new Rect(w - credit.Width - 8, h - credit.Height - 2, credit.Width + 8, credit.Height + 2);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), null, box);
        dc.DrawText(credit, new Point(box.X + 4, box.Y + 1));
    }

    /// <summary>A tile, if it's loaded; otherwise starts loading it (from the disk, else OpenStreetMap) and redraws once it is.</summary>
    private BitmapSource? Tile(int z, int x, int y)
    {
        string key = $"{z}/{x}/{y}";
        if (tiles.TryGetValue(key, out var img)) return img;
        if (loading.TryAdd(key, 0)) _ = LoadAsync(key);
        return null;
    }

    private async Task LoadAsync(string key)
    {
        BitmapSource? img = null;
        try
        {
            string file = Path.Combine(Folder, key.Replace('/', Path.DirectorySeparatorChar) + ".png");
            byte[]? bytes = null;
            if (File.Exists(file) && File.GetLastWriteTime(file) > DateTime.Now.AddDays(-30)) bytes = await File.ReadAllBytesAsync(file);
            if (bytes == null)
            {
                bytes = await http.GetByteArrayAsync($"https://tile.openstreetmap.org/{key}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, bytes);
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            img = bmp;
        }
        catch (Exception ex) { Logging.Log.Here.Debug(() => $"Map tile {key}: {ex.Message}"); }
        tiles[key] = img;
        loading.TryRemove(key, out _);
        _ = Dispatcher.BeginInvoke(InvalidateVisual);
    }
}
