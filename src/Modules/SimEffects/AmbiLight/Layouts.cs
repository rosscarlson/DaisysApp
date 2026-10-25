namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>
/// Zones laid out along the displays' edges (left to right, or clockwise from the bottom left), as the light runs
/// behind them; each zone the same length of the path. Moved and sized by hand after.
/// </summary>
internal static class Layouts
{
    /// <summary>A strip along one edge of a display, and where it starts on the path.</summary>
    private sealed record Band(double X, double Y, double W, double H, bool Horizontal, bool Backwards, double Start)
    {
        public double Length => Horizontal ? W : H;
    }

    public static List<AmbiZone> Make(AmbiLayout layout, int count, IReadOnlyList<ScreenRect> displays)
    {
        var shown = displays.Count > 0 ? displays.OrderBy(d => d.X).ToList() : new List<ScreenRect> { new(0, 0, 1920, 1080) };
        var bands = new List<Band>();
        double at = 0;
        void Add(double x, double y, double w, double h, bool horizontal, bool backwards)
        {
            // displays side by side at the same height make one strip, so zones run on across the gap
            if (bands.Count > 0 && bands[^1] is { Horizontal: true } p && horizontal && p.Backwards == backwards && p.Y == y && p.H == h
                && (backwards ? x + w == p.X : p.X + p.W == x))
            {
                bands[^1] = p with { X = Math.Min(p.X, x), W = p.W + w };
                at += w;
                return;
            }
            var b = new Band(x, y, w, h, horizontal, backwards, at);
            bands.Add(b);
            at += b.Length;
        }
        static double Depth(ScreenRect d) => Math.Round(d.H * 0.15);
        static double Side(ScreenRect d) => Math.Round(d.W * 0.1);
        var first = shown[0];
        var last = shown[^1];
        if (layout is AmbiLayout.Sides or AmbiLayout.Around) Add(first.X, first.Y, Side(first), first.H, false, true);
        if (layout != AmbiLayout.Bottom)
            foreach (var d in shown) Add(d.X, d.Y, d.W, Depth(d), true, false);
        if (layout is AmbiLayout.Sides or AmbiLayout.Around) Add(last.Right - Side(last), last.Y, Side(last), last.H, false, false);
        if (layout == AmbiLayout.Bottom)
            foreach (var d in shown) Add(d.X, d.Bottom - Depth(d), d.W, Depth(d), true, false);
        if (layout == AmbiLayout.Around)
            foreach (var d in Enumerable.Reverse(shown)) Add(d.X, d.Bottom - Depth(d), d.W, Depth(d), true, true);

        var zones = new List<AmbiZone>();
        for (int i = 0; i < count; i++)
        {
            double a = at * i / count, b = at * (i + 1) / count, c = (a + b) / 2;
            var band = bands.FirstOrDefault(x => c >= x.Start && c < x.Start + x.Length) ?? bands[^1];
            double from = Math.Max(a, band.Start) - band.Start, to = Math.Min(b, band.Start + band.Length) - band.Start;
            if (band.Backwards) (from, to) = (band.Length - to, band.Length - from);
            zones.Add(band.Horizontal
                ? new AmbiZone { X = band.X + from, Y = band.Y, W = Math.Max(1, to - from), H = band.H }
                : new AmbiZone { X = band.X, Y = band.Y + from, W = band.W, H = Math.Max(1, to - from) });
        }
        return zones;
    }
}
