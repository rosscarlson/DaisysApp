using System.IO;
using System.Windows;
using System.Windows.Media;
using DaisysApp.Applets.SimEffects.Analysis.Sessions;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// (since 4.6.2) The track's shape, from where the game says the car was: the line of your best lap on it (read from
/// that race's log, <see cref="RaceLog"/>), a lap's worth of places (X and Z, in metres) found once per track and best lap.
/// </summary>
internal static class TrackLines
{
    private static readonly Dictionary<string, (string Key, Point[] Line)> lines = new();
    private static readonly HashSet<string> reading = new();

    /// <summary>A line read (on the UI thread): the maps showing it draw again.</summary>
    public static event Action? Read;

    /// <summary>The best lap's line on this track (null: none, or still being read).</summary>
    public static Point[]? For(string? trackId)
    {
        if (trackId == null || AnalysisApplet.Current is not { } a) return null;
        var best = a.Store.All.Where(l => l.Track == trackId && l.Session != null).MinBy(l => l.Time);
        if (best == null) return null;
        string key = best.Session + "#" + best.Lap;
        if (lines.TryGetValue(trackId, out var known) && known.Key == key) return known.Line;
        if (reading.Add(key))
        {
            var ui = Application.Current?.Dispatcher;
            Task.Run(() =>
            {
                Point[] line = Array.Empty<Point>();
                try { line = Load(Path.Combine(SessionLog.Folder, best.Session!), best.Lap); }
                catch (Exception ex) { Logging.Log.Here.Warn($"Analysis: the track map's line from {best.Session}", ex); }
                ui?.BeginInvoke(() =>
                {
                    reading.Remove(key);
                    lines[trackId] = (key, line);
                    Read?.Invoke();
                });
            });
        }
        return known.Line;
    }

    /// <summary>A lap's places (its number in the race, 1 the first; not there: the whole log), 20 a second.</summary>
    private static Point[] Load(string path, int lap)
    {
        if (!File.Exists(path)) return Array.Empty<Point>();
        var d = SessionData.Load(path);
        var l = d.Laps.FirstOrDefault(x => x.Number == lap - 1);
        int from = l?.Start ?? 0, to = l?.End ?? d.Count - 1;
        var points = new List<Point>();
        for (int i = from; i <= to; i += 5)
            if (!double.IsNaN(d.X[i]) && !double.IsNaN(d.Z[i]) && !(d.X[i] == 0 && d.Z[i] == 0)) points.Add(new Point(d.X[i], d.Z[i]));
        return points.ToArray();
    }
}

/// <summary>
/// (since 4.6.2) The Track Map block (and its overlay): the track as your best lap on it went (grey), the line you're
/// driving now (the last few minutes, in the accent colour) and the car, north up, fitted to the track (or, on a track
/// not driven yet, to the line so far). <see cref="ShowBest"/> and <see cref="ShowTrail"/> are its widget's rows.
/// </summary>
internal sealed class TrackMapView : FrameworkElement
{
    private const int MostTrail = 3000;
    private readonly List<Point> trail = new();
    private Point? car;
    private Point[]? best;
    private string? trackId;

    public bool ShowBest { get; set; } = true;
    public bool ShowTrail { get; set; } = true;

    public TrackMapView()
    {
        ClipToBounds = true;
        Loaded += (_, _) => TrackLines.Read += Again;
        Unloaded += (_, _) => TrackLines.Read -= Again;
    }

    private void Again()
    {
        best = TrackLines.For(trackId);
        InvalidateVisual();
    }

    public void Update(Reading r)
    {
        if (r.TrackId != trackId)
        {
            trackId = r.TrackId;
            best = TrackLines.For(trackId);
        }
        else if (best == null && trackId != null) best = TrackLines.For(trackId);
        if (r.Frame is { HasRace: true } f && !(f.PosX == 0 && f.PosZ == 0))
        {
            var p = new Point(f.PosX, f.PosZ);
            if (trail.Count > 0)
            {
                var last = trail[^1];
                double m = Math.Sqrt((p.X - last.X) * (p.X - last.X) + (p.Y - last.Y) * (p.Y - last.Y));
                // (a jump: a restart, or the next race elsewhere)
                if (m > 250) trail.Clear();
                else if (m < 1) p = last;
            }
            if (trail.Count == 0 || trail[^1] != p) trail.Add(p);
            if (trail.Count > MostTrail) trail.RemoveRange(0, trail.Count - MostTrail);
            car = p;
        }
        else car = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var dim = PedalColours.Theme(this, "TextSecondaryBrush");
        var accent = PedalColours.Theme(this, "AccentBrush");
        var line = ShowBest && best is { Length: > 1 } ? best : null;
        // (fitted to the track; else to the line driven so far)
        IReadOnlyList<Point> fit = line ?? (IReadOnlyList<Point>)trail;
        if (fit.Count < 2)
        {
            var t = PedalColours.Text(this, car == null ? T("Not driving") : T("Drive to draw the track"), 12, dim);
            dc.DrawText(t, new Point(Math.Max(4, (w - t.Width) / 2), Math.Max(4, (h - t.Height) / 2)));
            return;
        }
        double minX = fit.Min(p => p.X), maxX = fit.Max(p => p.X), minZ = fit.Min(p => p.Y), maxZ = fit.Max(p => p.Y);
        double scale = Math.Max(Math.Max(50, maxX - minX) / Math.Max(10, w - 16), Math.Max(50, maxZ - minZ) / Math.Max(10, h - 16));
        double cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;
        Point S(Point p) => new(w / 2 + (p.X - cx) / scale, h / 2 - (p.Y - cz) / scale);
        void Draw(IReadOnlyList<Point> points, Pen pen)
        {
            if (points.Count < 2) return;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(S(points[0]), false, false);
                for (int i = 1; i < points.Count; i++) ctx.LineTo(S(points[i]), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        if (line != null)
        {
            Draw(line, PedalColours.Pen(dim, 6, 0.45));
            var start = S(line[0]);
            dc.DrawRectangle(PedalColours.Theme(this, "TextBrush"), null, new Rect(start.X - 4, start.Y - 1.5, 8, 3));
        }
        if (ShowTrail) Draw(trail, PedalColours.Pen(accent, 2));
        if (car is { } c)
            dc.DrawEllipse(Brushes.White, PedalColours.Pen(accent, 2.5), S(c), 5, 5);
    }
}
