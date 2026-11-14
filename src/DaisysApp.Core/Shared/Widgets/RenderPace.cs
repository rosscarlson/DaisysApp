using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Shared.Widgets;

/// <summary>
/// (since 4.17.7) How fast WPF really puts the app's windows on screen, for the logs. One render thread draws every
/// window of the app, and a see-through window (Mini Mirror's mirrors, widgets, SimEffects' overlays) costs it a copy
/// back from the graphics card each time it changes, so two busy ones can slow each other while each is fine alone.
/// Counts <see cref="CompositionTarget.Rendering"/> (once per frame WPF draws; while it's hooked WPF tries for every
/// screen refresh). UI thread only.
/// </summary>
public sealed class RenderPace
{
    private long since = Stopwatch.GetTimestamp(), last, gapMax;
    private int frames;

    /// <summary>A frame WPF drew (from a Rendering handler).</summary>
    public void Frame()
    {
        long now = Stopwatch.GetTimestamp();
        if (last != 0) gapMax = Math.Max(gapMax, now - last);
        last = now;
        frames++;
    }

    /// <summary>The frames a second since the last time, as text, and starts counting again.</summary>
    public string Take(out double perSecond)
    {
        long now = Stopwatch.GetTimestamp();
        perSecond = frames / Math.Max(0.001, (now - since) / (double)Stopwatch.Frequency);
        string text = $"WPF drew {perSecond:0.#} frames/s, longest gap {gapMax * 1000.0 / Stopwatch.Frequency:0} ms";
        since = now;
        last = gapMax = 0;
        frames = 0;
        return text;
    }

    /// <summary>
    /// Hooks Rendering for <paramref name="ms"/> and hands back the pace and the see-through windows showing (and the
    /// frames a second: under 30 means WPF is behind, as it would draw every refresh).
    /// </summary>
    public static void Sample(int ms, Action<string, double> done)
    {
        var pace = new RenderPace();
        EventHandler frame = (_, _) => pace.Frame();
        CompositionTarget.Rendering += frame;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CompositionTarget.Rendering -= frame;
            string text = pace.Take(out double perSecond);
            done($"{text}; {SeeThrough()}", perSecond);
        };
        timer.Start();
    }

    /// <summary>The app's see-through windows showing, with their sizes in physical pixels.</summary>
    public static string SeeThrough()
    {
        var shown = new List<string>();
        if (Application.Current is { } app)
            foreach (Window w in app.Windows)
            {
                if (!w.IsVisible || !w.AllowsTransparency) continue;
                var scale = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                string name = string.IsNullOrEmpty(w.Title) ? w.GetType().Name : w.Title;
                shown.Add($"{name} {w.ActualWidth * scale.M11:0}x{w.ActualHeight * scale.M22:0}");
            }
        return $"see-through windows showing: {(shown.Count == 0 ? "none" : string.Join(", ", shown))}";
    }
}
