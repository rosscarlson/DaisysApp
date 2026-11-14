using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// Draws a widget once per screen frame only while there's something new to draw: <see cref="CompositionTarget.Rendering"/>
/// (which keeps WPF redrawing at the monitor's rate, 144–240 Hz) is hooked only while the widget shows and the reader
/// has readings coming (not with no game, nor while it's paused), looked at four times a second; and each frame calls
/// <c>draw</c> only when the reader has a new reading. (Since 4.0.0: before, both widgets drew every frame they showed.)
/// Since 4.17.7 the SimEffects log has how fast WPF really drew while it's hooked (15 s in, then each minute: Info when
/// under 30 frames a second, "WPF behind", else Debug), with the app's see-through windows showing (Mini Mirror's).
/// </summary>
internal sealed class FrameHook
{
    private readonly OverlayReader reader;
    private readonly Window window;
    private readonly bool dash;
    private readonly Action draw;
    private readonly Action idle;
    private readonly DispatcherTimer look = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool hooked;
    private long seen = -1;
    // for the log (since 4.17.7)
    private readonly RenderPace pace = new();
    private long hookedAt, reportAt, readingsAt;
    private int drawn, slowReports;
    private static readonly Logging.Logger log = Logging.Log.Here;

    /// <param name="draw">A new reading: take it and draw.</param>
    /// <param name="idle">Readings stopped: drawn once as it is now.</param>
    public FrameHook(Window window, OverlayReader reader, bool dash, Action draw, Action idle)
    {
        this.reader = reader;
        this.window = window;
        this.dash = dash;
        this.draw = draw;
        this.idle = idle;
        look.Tick += (_, _) =>
        {
            Hook(reader.Live(dash));
            if (hooked && Environment.TickCount64 >= reportAt) Report();
        };
        window.IsVisibleChanged += (_, _) =>
        {
            if (window.IsVisible) { look.Start(); Hook(reader.Live(dash)); }
            else { look.Stop(); Hook(false); }
        };
        window.Closed += (_, _) => { look.Stop(); Hook(false); };
    }

    private void Hook(bool on)
    {
        if (on == hooked) return;
        hooked = on;
        if (on)
        {
            CompositionTarget.Rendering += OnFrame;
            pace.Take(out _);
            hookedAt = Environment.TickCount64;
            reportAt = hookedAt + 15000;
            readingsAt = reader.Version;
            drawn = 0;
        }
        else
        {
            CompositionTarget.Rendering -= OnFrame;
            idle();
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        pace.Frame();
        long v = reader.Version;
        if (v == seen) return;
        seen = v;
        draw();
        drawn++;
    }

    /// <summary>
    /// The log: readings a second, how many drawn, and how many frames WPF really drew (it should draw every screen
    /// refresh while hooked; one render thread draws all the app's windows, so a busy see-through one, like a mirror,
    /// can hold it up). The first at Info, then Info when it's slow.
    /// </summary>
    private void Report()
    {
        long now = Environment.TickCount64;
        double seconds = Math.Max(0.001, (now - hookedAt) / 1000.0);
        long version = reader.Version;
        string text = $"Overlays: {window.Title} in {seconds:0} s: {(version - readingsAt) / seconds:0.#} readings/s, drawn {drawn / seconds:0.#}/s; " +
                      $"{pace.Take(out double perSecond)}{(perSecond < 30 ? " (WPF behind)" : "")}; {RenderPace.SeeThrough()}";
        bool first = reportAt == hookedAt + 15000;
        if (first || perSecond < 30 && (++slowReports <= 10 || slowReports % 30 == 0)) log.Info(text);
        else log.Debug(text);
        hookedAt = now;
        reportAt = now + 60000;
        readingsAt = version;
        drawn = 0;
    }
}
