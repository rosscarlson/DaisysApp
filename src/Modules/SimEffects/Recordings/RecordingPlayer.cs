using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Analysis.Sessions;
using DaisysApp.Shared;

namespace DaisysApp.Applets.SimEffects.Recordings;

/// <summary>
/// (since 4.20.0) A recording's video and its controls: play, along it, stop, loop, the sync and the volume. The video
/// (<see cref="VideoFrames"/>) and its sound (<see cref="VideoSound"/>) follow <see cref="SimPlayback"/>'s clock; hidden
/// (another tab), the recording and its sound go on and the video waits.
/// </summary>
internal sealed class RecordingPlayer : DockPanel
{
    /// <summary>How far off the clock the video may get before it's put right, and no sooner than this after the last time.</summary>
    private const double MaxOff = 0.25;
    private const long PutRightMs = 2000;

    private readonly Image picture = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock note = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly Slider along = new() { Minimum = 0, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock clock = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MinWidth = 100 };
    private readonly TextBlock offset = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Slider level = new() { Minimum = 0, Maximum = 100, Width = 110, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
    private readonly Button play, stop, mute;
    private readonly StackPanel controls = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer soundSoon = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private SimRecording? shown;
    private VideoFrames? frames;
    private VideoSound? sound;
    private WriteableBitmap? bitmap;
    private bool setting;
    private string? error;
    private double shownAt = double.NaN;
    private long putRight;
    /// <summary>Where it'll play from, while it isn't the one playing.</summary>
    private double from;

    public RecordingPlayer()
    {
        LastChildFill = true;
        SetResourceReference(BackgroundProperty, "RoomBrush");
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        offset.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        play = IconButton("", T("Play"), PlayPause);
        stop = IconButton("", T("Stop playback"), () => SimPlayback.End("Stop"));
        var first = new DockPanel();
        foreach (var b in new[] { play, stop }) { SetDock(b, Dock.Left); first.Children.Add(b); }
        SetDock(clock, Dock.Right);
        first.Children.Add(clock);
        AutomationProperties.SetName(along, T("Along the recording"));
        along.ValueChanged += (_, e) => { if (!setting) SeekTo(e.NewValue); };
        first.Children.Add(along);
        controls.Children.Add(first);

        var second = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
        var loop = Ui.Chip(T("Loop"), RecordingsSettings.All.Loop);
        Ui.OnToggle(loop, on => { RecordingsSettings.All.Loop = on; RecordingsSettings.All.Save(); });
        second.Children.Add(loop);
        offset.Margin = new Thickness(10, 0, 8, 0);
        second.Children.Add(offset);
        foreach (var (text, by) in new[] { ("−0.1 s", -0.1), ("−0.02 s", -0.02), ("+0.02 s", 0.02), ("+0.1 s", 0.1) })
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 2, 8, 2), ToolTip = by < 0 ? T("The telemetry earlier in the video") : T("The telemetry later in the video") };
            b.Click += (_, _) => Shift(by);
            second.Children.Add(b);
        }
        var auto = new Button { Content = T("Auto"), Padding = new Thickness(8, 2, 8, 2), ToolTip = T("As recorded") };
        auto.Click += (_, _) => { if (shown != null) Shift(-shown.Offset); };
        second.Children.Add(auto);
        var volume = new StackPanel { Orientation = Orientation.Horizontal };
        mute = IconButton("", T("Mute"), () => { RecordingsSettings.All.Muted = !RecordingsSettings.All.Muted; LevelChanged(); });
        AutomationProperties.SetName(level, T("Video volume"));
        level.ToolTip = T("Volume");
        level.ValueChanged += (_, e) =>
        {
            if (setting) return;
            RecordingsSettings.All.Volume = Math.Round(e.NewValue / 100, 2);
            RecordingsSettings.All.Muted = false;
            LevelChanged();
        };
        volume.Children.Add(mute);
        volume.Children.Add(level);
        SetDock(volume, Dock.Right);
        second.Children.Add(volume);
        controls.Children.Add(second);
        SetDock(controls, Dock.Bottom);
        Children.Add(controls);

        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var screen = new Grid { ClipToBounds = true, Background = Brushes.Transparent };
        screen.Children.Add(picture);
        screen.Children.Add(note);
        screen.MouseLeftButtonUp += (_, _) => PlayPause();
        Children.Add(screen);

        ShowLevel();
        SimPlayback.Moved += Follow;
        SimPlayback.Changed += ShowButtons;
        SimRecorder.Changed += ShowButtons;
        soundSoon.Tick += (_, _) => { soundSoon.Stop(); StartSound(); };
        Ui.Every(this, 30, Tick, shown: visible =>
        {
            if (visible) FollowVideo();
            else frames?.Pause();
        });
        Show(null);
    }

    private static Button IconButton(string glyph, string name, Action click)
    {
        var b = BassShaker.VolumeControl.IconButton(glyph);
        Face(b, glyph, name);
        b.Click += (_, _) => click();
        return b;
    }

    private static void Face(Button b, string glyph, string name)
    {
        ((TextBlock)b.Content).Text = glyph;
        b.ToolTip = name;
        AutomationProperties.SetName(b, name);
    }

    /// <summary>The recording picked (null: none), at its start, or where it plays.</summary>
    public void Show(SimRecording? r)
    {
        if (r != null && r == shown) return;
        soundSoon.Stop();
        frames?.Dispose();
        frames = null;
        sound?.Dispose();
        sound = null;
        picture.Source = bitmap = null;
        shownAt = double.NaN;
        error = null;
        from = 0;
        shown = r;
        controls.IsEnabled = r != null;
        if (r == null) ShowNote(T("Pick a recording"));
        else if (!r.HasVideo) ShowNote(F("Video not found: {0}", Path.GetFileName(r.Video)));
        else
        {
            ShowNote(T("Opening the video…"));
            frames = new VideoFrames(r.Video);
            frames.Want(0);
        }
        ShowOffset();
        ShowButtons();
        ShowPlace();
        Follow();
    }

    /// <summary>It's the one being played (or paused).</summary>
    private bool Mine => shown != null && SimPlayback.Current == shown;

    private double Position => Mine ? SimPlayback.Position : from;

    public void Play(SimRecording r)
    {
        if (r == shown) SimPlayback.Play(r, Position);
    }

    private void PlayPause()
    {
        if (shown == null) return;
        if (Mine && SimPlayback.Playing) SimPlayback.Pause();
        else SimPlayback.Play(shown, Position);
    }

    private void SeekTo(double to)
    {
        if (Mine) { SimPlayback.Seek(to); return; }
        from = to;
        frames?.Want(to);
        ShowPlace();
    }

    private void Shift(double by)
    {
        if (shown == null) return;
        shown.Offset = Math.Round(shown.Offset + by, 3);
        shown.Save();
        SimPlayback.Shifted(shown);
        ShowOffset();
    }

    /// <summary>Played, paused, moved along or stopped: the video and its sound with it.</summary>
    private void Follow()
    {
        soundSoon.Stop();
        sound?.Stop();
        FollowVideo();
        // (a moment later: the slider being dragged along moves it many times)
        if (Mine && SimPlayback.Playing && shown!.HasVideo) soundSoon.Start();
        ShowButtons();
        ShowPlace();
    }

    private void FollowVideo()
    {
        if (frames == null) return;
        putRight = Environment.TickCount64;
        if (!IsVisible) frames.Pause();
        else if (Mine && SimPlayback.Playing) frames.Play(SimPlayback.Position);
        else frames.Want(Position);
    }

    private void StartSound()
    {
        if (!Mine || !SimPlayback.Playing) return;
        (sound ??= new VideoSound(shown!.Video) { Level = SoundLevel }).Play(SimPlayback.Position);
    }

    private void Tick()
    {
        ShowPlace();
        if (frames == null) return;
        if (frames.Error is { } e && e != error) { error = e; ShowNote(F("Can't play this video: {0}", e)); return; }
        if (!frames.Ready) return;
        if (bitmap == null)
        {
            picture.Source = bitmap = new WriteableBitmap(frames.Width, frames.Height, 96, 96, PixelFormats.Bgr32, null);
            ShowNote(null);
        }
        var bmp = bitmap;
        frames.Take((pixels, t) =>
        {
            if (pixels.Length != bmp.PixelWidth * bmp.PixelHeight * 4) return;
            bmp.WritePixels(new Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight), pixels, bmp.PixelWidth * 4, 0);
            shownAt = t;
        });
        // (fallen behind, or opened after it began)
        if (Mine && SimPlayback.Playing && !frames.Ended && Math.Abs(shownAt - SimPlayback.Position) > MaxOff && Environment.TickCount64 - putRight > PutRightMs) FollowVideo();
    }

    private void ShowPlace()
    {
        double length = shown?.Length ?? 0, at = Position;
        setting = true;
        along.Maximum = Math.Max(0.1, length);
        along.Value = Math.Clamp(at, 0, along.Maximum);
        setting = false;
        clock.Text = shown == null ? "" : $"{Clock(at)} / {Clock(length)}";
    }

    private static string Clock(double s) => $"{(int)(s / 60)}:{s % 60:00.0}";

    private void ShowButtons()
    {
        bool on = Mine && SimPlayback.Playing;
        Face(play, on ? "" : "", on ? T("Pause") : T("Play"));
        play.IsEnabled = !SimRecorder.Recording;
        stop.IsEnabled = Mine;
        // (stopped: from where it was)
        if (!Mine) from = Math.Min(from, shown?.Length ?? 0);
    }

    private void ShowOffset() =>
        offset.Text = shown == null ? "" : Math.Abs(shown.Offset) < 1e-4 ? T("Sync: as recorded") : F("Sync: {0:+0.000;-0.000} s", shown.Offset);

    private void ShowNote(string? text)
    {
        note.Text = text ?? "";
        note.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- the volume

    private static float SoundLevel => RecordingsSettings.All.Muted ? 0 : (float)RecordingsSettings.All.Volume;

    private void LevelChanged()
    {
        RecordingsSettings.All.Save();
        ShowLevel();
    }

    private void ShowLevel()
    {
        var s = RecordingsSettings.All;
        setting = true;
        level.Value = s.Volume * 100;
        setting = false;
        level.Opacity = s.Muted ? 0.45 : 1;
        Face(mute, s.Muted ? "" : "", s.Muted ? T("Unmute") : T("Mute"));
        if (sound != null) sound.Level = SoundLevel;
    }
}
