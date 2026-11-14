using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DaisysApp.Applets.SimEffects.Analysis;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// (since 4.17.9) What each overlay looks like, for its box on the tab: the overlay's own drawing (the Dash's
/// <see cref="DashView"/>, Inputs' <see cref="OverlayWindow.Trace"/>, a block's <see cref="BlockView"/> in its
/// <see cref="OverlayFrame"/>) with made-up readings, drawn once, scaled into the box over a dark backdrop (a game).
/// Nothing reads or ticks for them.
/// </summary>
internal static class OverlayPreviews
{
    public const double Height = 150;

    private static readonly Brush Backdrop = Freeze(new LinearGradientBrush(Color.FromRgb(0x34, 0x40, 0x4E), Color.FromRgb(0x10, 0x14, 0x19), 90));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    /// <summary>The backdrop, with <paramref name="content"/> (its size its own) scaled to fit.</summary>
    public static Border Frame(FrameworkElement content) => new()
    {
        Height = Height, CornerRadius = new CornerRadius(6), Background = Backdrop, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10),
        ClipToBounds = true, Child = new Viewbox { Stretch = Stretch.Uniform, Child = content },
    };

    public static FrameworkElement Dash(OverlaySettings settings)
    {
        var space = DashView.SpaceOf(settings.Dash);
        var view = new DashView(settings, () => Math.Max(settings.Dash.Opacity, 0.6)) { Still = true, Has = true, Width = space.Width, Height = space.Height };
        var f = view.Frame;
        f.Rpm = 6400;
        f.MaxRpm = 8000;
        f.GearNo = 4;
        f.SpeedMs = 38;
        f.Throttle = 1;
        return view;
    }

    public static FrameworkElement Inputs(OverlaySettings settings)
    {
        var trace = new OverlayWindow.Trace(settings) { Width = 480, Height = 140 };
        // a few corners: flat out, brake and turn in, coast, back on the throttle (a shift's clutch on the straights)
        var now = DateTime.UtcNow;
        double span = OverlayWindow.Shown(settings.Seconds);
        for (double t = span; t >= 0; t -= 1 / 30.0)
        {
            double u = (span - t) % 6;
            bool left = (int)((span - t) / 6) % 2 == 0;
            double throttle = u < 2.5 ? 1 : u < 2.6 ? 1 - (u - 2.5) * 10 : u < 3.3 ? 0 : u < 4 ? 0.25 : u < 5 ? 0.25 + (u - 4) * 0.75 : 1;
            double brake = u is >= 2.55 and < 3.3 ? 0.95 * Math.Pow(1 - (u - 2.55) / 0.75, 1.5) : 0;
            double steer = u is >= 2.9 and < 4.9 ? 0.5 + 0.3 * Math.Sin((u - 2.9) / 2 * Math.PI) * (left ? 1 : -1) : 0.5;
            double clutch = u is >= 1.2 and < 1.35 ? 1 : 0;
            trace.Add(now - TimeSpan.FromSeconds(t), steer, throttle, brake, clutch, 0);
        }
        trace.Speeds = (82, 97, 58);
        return trace;
    }

    /// <summary>A block as its overlay draws it, with a lap in a race on made-up numbers.</summary>
    public static FrameworkElement Block(Block block, AnalysisSettings analysis)
    {
        var place = analysis.Widget(block.Id);
        var body = new BlockView(block, new HashSet<string>(place.Hidden));
        var title = Ui.Header(block.Title);
        title.Margin = new Thickness(0, 0, 0, 2);
        var frame = new OverlayFrame(place, body, title, titleAtFirst: block.Kind is not (BlockKind.Timing or BlockKind.Big))
        {
            Width = block.WidgetSize.Width, Height = block.WidgetSize.Height,
        };
        var f = new TelemetryFrame
        {
            Live = true, HasRace = true, CarOrdinal = 1, Power = 320_000, Torque = 520, Boost = 14,
            CurrentLap = 41.237f, LastLap = 83.912f, RaceTime = 210, RacePosition = 3, LapNumber = 1,
            Field = new Standings(new[]
            {
                new Racer(1, "7", "A. Driver", float.NaN, 0, false, false),
                new Racer(2, "22", "B. Driver", 1.4f, 0, false, false),
                new Racer(3, "44", T("You"), 0.8f, 0, true, false),
                new Racer(4, "9", "C. Driver", 2.1f, 0, false, false),
                new Racer(5, "31", "D. Driver", 0.6f, 0, false, false),
                new Racer(6, "5", "E. Driver", 3.9f, 0, false, true),
            }, true),
        };
        float[] temps = { 185, 190, 198, 195 };
        temps.CopyTo(f.TireTemp, 0);
        // the Performance graphs' minute, and the Track Map's line: a lap round a made-up track
        var history = new PerfHistory();
        var map = new List<(float X, float Z)>();
        for (int i = 0; i < 300; i++)
        {
            double a = i / 300.0 * 2 * Math.PI;
            f.Power = (float)(250_000 + 90_000 * Math.Sin(a * 3));
            f.Torque = (float)(450 + 90 * Math.Sin(a * 3 + 0.4));
            f.Boost = (float)(10 + 6 * Math.Sin(a * 3 + 0.8));
            history.Add(f);
            if (i < 280) map.Add(((float)(420 * Math.Cos(a) + 120 * Math.Cos(2 * a)), (float)(260 * Math.Sin(a) - 90 * Math.Sin(3 * a))));
        }
        Reading At(float x, float z)
        {
            f.PosX = x;
            f.PosZ = z;
            return new Reading(f, "FH5", analysis, T("Track"), 82.604f, 83.412f, 8, 3, history, 81.950f);
        }
        if (block.Kind == BlockKind.Map)
            foreach (var (x, z) in map) body.Update(At(x, z));
        else body.Update(At(0, 0));
        return frame;
    }

    /// <summary>A box whose overlay can't be shown (Analysis switched off): its note in the preview's place.</summary>
    public static Border Note(string text) => new()
    {
        Height = Height, CornerRadius = new CornerRadius(6), Background = Backdrop, Margin = new Thickness(0, 0, 0, 10),
        Child = new TextBlock { Text = text, Foreground = Brushes.White, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) },
    };
}
