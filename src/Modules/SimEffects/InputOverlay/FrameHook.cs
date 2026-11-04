using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>
/// Draws a widget once per screen frame only while there's something new to draw: <see cref="CompositionTarget.Rendering"/>
/// (which keeps WPF redrawing at the monitor's rate, 144–240 Hz) is hooked only while the widget shows and the reader
/// has readings coming (not with no game, nor while it's paused), looked at four times a second; and each frame calls
/// <c>draw</c> only when the reader has a new reading. (Since 4.0.0: before, both widgets drew every frame they showed.)
/// </summary>
internal sealed class FrameHook
{
    private readonly OverlayReader reader;
    private readonly bool dash;
    private readonly Action draw;
    private readonly Action idle;
    private readonly DispatcherTimer look = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool hooked;
    private long seen = -1;

    /// <param name="draw">A new reading: take it and draw.</param>
    /// <param name="idle">Readings stopped: drawn once as it is now.</param>
    public FrameHook(Window window, OverlayReader reader, bool dash, Action draw, Action idle)
    {
        this.reader = reader;
        this.dash = dash;
        this.draw = draw;
        this.idle = idle;
        look.Tick += (_, _) => Hook(reader.Live(dash));
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
        if (on) CompositionTarget.Rendering += OnFrame;
        else
        {
            CompositionTarget.Rendering -= OnFrame;
            idle();
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        long v = reader.Version;
        if (v == seen) return;
        seen = v;
        draw();
    }
}
