using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.8.0) Compare's Video: Session 1's race as recorded (<see cref="RaceVideo"/>), at the moment the mouse is
/// at on Compare's chart (or a zone, corner or move picked). Read by <see cref="VideoFrames"/>; its sound while it plays by <see cref="VideoSound"/> (since 4.8.1). Popped out, it has its tools: play, a slider along the
/// video, and the sync: the race's own clock at the frame shown, the video moved earlier or later by a second, a tenth
/// or a frame, **Race starts here** (the frame the race clock starts at), and **Auto** (as recorded); and (since 4.9.0)
/// **Export**: the video with the race's telemetry drawn on as it's synced (<see cref="VideoExport"/>).
/// </summary>
internal sealed class RaceVideoView : DockPanel
{
    private const double Frame = 1.0 / 60;

    private readonly Image picture = new() { Stretch = Stretch.Uniform };
    private VideoFrames? frames;
    private VideoSound? sound;
    private WriteableBitmap? bitmap;
    /// <summary>The time in the video of the frame shown.</summary>
    private double shownAt = double.NaN;
    private readonly TextBlock note = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(8) };
    private readonly StackPanel tools = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button play, export;
    private readonly TextBlock exporting = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private CancellationTokenSource? exportCancel;
    private readonly Slider along = new() { Minimum = 0, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
    private readonly TextBlock clock = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MinWidth = 110 };
    private readonly TextBlock race = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, MinWidth = 170 };
    private readonly TextBlock offset = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), MinWidth = 120 };
    // (since 4.10.9) its volume: under the picture in Compare, on the tools' first line popped out
    private readonly StackPanel volume = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider level = new() { Minimum = 0, Maximum = 100, Width = 110, SmallChange = 1, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button mute;
    private readonly DockPanel under = new() { Margin = new Thickness(0, 4, 0, 0) };
    private DockPanel first = null!;
    private double soundLevel = 1;
    private bool muted, showingLevel;

    /// <summary>(since 4.10.9) The volume or mute changed (to be kept).</summary>
    public event Action? VolumeChanged;

    private SessionData? data;
    private string? log;
    private RaceVideoLink? link;
    private bool opened, playing, settingSlider;
    /// <summary>Its sound playing for Compare's playback.</summary>
    private bool sounding;
    private string? error;
    /// <summary>The race's moment shown (a sample of its log, fractional), and the one to show next.</summary>
    private double sample = double.NaN, wanted = double.NaN;

    /// <summary>The race's moment the video shows when it moves by itself (playing, the slider, the sync): a sample of
    /// Session 1's log, NaN when it stops.</summary>
    public event Action<double>? Shown;

    public RaceVideoView()
    {
        LastChildFill = true;
        SetResourceReference(BackgroundProperty, "RoomBrush");
        System.Windows.Automation.AutomationProperties.SetName(this, T("Video"));
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        // the tools, popped out
        play = Button("", T("Play"), () => { if (playing) Pause(); else Play(); });
        first = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        export = Button("", T("Export the video with the telemetry on it"), Export);
        export.Margin = new Thickness(4, 0, 0, 0);
        exporting.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        DockPanel.SetDock(play, Dock.Left);
        DockPanel.SetDock(export, Dock.Left);
        DockPanel.SetDock(exporting, Dock.Right);
        DockPanel.SetDock(clock, Dock.Right);
        first.Children.Add(play);
        first.Children.Add(export);
        first.Children.Add(exporting);
        first.Children.Add(clock);
        along.Margin = new Thickness(8, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(along, T("Along the video"));
        along.ValueChanged += (_, e) =>
        {
            if (settingSlider || !opened || frames == null) return;
            Pause();
            frames.Want(e.NewValue);
            FromVideo(e.NewValue);
        };
        first.Children.Add(along);
        tools.Children.Add(first);

        var second = new WrapPanel();
        second.Children.Add(race);
        offset.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        second.Children.Add(offset);
        foreach (var (text, by) in new[] { ("−1 s", -1.0), ("−0.1 s", -0.1), ("−1 frame", -Frame), ("+1 frame", Frame), ("+0.1 s", 0.1), ("+1 s", 1.0) })
        {
            var b = new Button { Content = text.Contains("frame") ? T(text) : text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2),
                ToolTip = by < 0 ? T("The race earlier in the video") : T("The race later in the video") };
            b.Click += (_, _) => Move(by);
            second.Children.Add(b);
        }
        var start = new Button { Content = T("Race starts here"), Margin = new Thickness(8, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2), ToolTip = T("This frame is where the race clock starts") };
        start.Click += (_, _) => StartsHere();
        second.Children.Add(start);
        var auto = new Button { Content = T("Auto"), Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2), ToolTip = T("As recorded") };
        auto.Click += (_, _) => { if (link != null) Move(-link.Offset); };
        second.Children.Add(auto);
        tools.Children.Add(second);
        SetDock(tools, Dock.Bottom);
        Children.Add(tools);

        mute = Button("", T("Mute"), () => { muted = !muted; ShowLevel(); VolumeChanged?.Invoke(); });
        System.Windows.Automation.AutomationProperties.SetName(level, T("Video volume"));
        level.ToolTip = T("Volume");
        level.Margin = new Thickness(2, 0, 0, 0);
        level.ValueChanged += (_, e) =>
        {
            if (showingLevel) return;
            soundLevel = Math.Round(e.NewValue / 100, 2);
            muted = false;
            ShowLevel();
            VolumeChanged?.Invoke();
        };
        volume.Children.Add(mute);
        volume.Children.Add(level);
        volume.HorizontalAlignment = HorizontalAlignment.Right;
        SetDock(under, Dock.Bottom);
        under.Children.Add(volume);
        Children.Add(under);
        ShowLevel();

        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        Children.Add(new Grid { Children = { picture, note }, ClipToBounds = true });
        ShowNote(T("No video of this race"));
        Ui.Every(this, 30, Tick);
    }

    /// <summary>Popped out: the tools show.</summary>
    public bool Tools
    {
        set
        {
            tools.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (!value) Pause();
            // (the volume on the tools' first line, right of the clock; back under the picture)
            ((Panel)volume.Parent).Children.Remove(volume);
            if (value)
            {
                volume.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(volume, Dock.Right);
                first.Children.Insert(first.Children.IndexOf(clock), volume);
            }
            else
            {
                volume.Margin = new Thickness(0);
                under.Children.Add(volume);
            }
            under.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>(since 4.10.9) Its volume (0 to 1) and mute, as kept.</summary>
    public void SetVolume(double v, bool off)
    {
        soundLevel = Math.Clamp(v, 0, 1);
        muted = off;
        ShowLevel();
    }

    public double Volume => soundLevel;
    public bool Muted => muted;

    private void ShowLevel()
    {
        showingLevel = true;
        level.Value = soundLevel * 100;
        showingLevel = false;
        level.Opacity = muted ? 0.45 : 1;
        ((TextBlock)mute.Content).Text = muted ? "" : "";
        string name = muted ? T("Unmute") : T("Mute");
        mute.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(mute, name);
        if (sound != null) sound.Level = SoundLevel;
    }

    private float SoundLevel => muted ? 0 : (float)soundLevel;

    private static Button Button(string glyph, string name, Action click)
    {
        var b = SimEffects.BassShaker.VolumeControl.IconButton(glyph);
        b.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Session 1: its log (null: none, or imported) and its file name, the video's key.</summary>
    public void Show(SessionData? session, string? key)
    {
        var l = session == null ? null : RaceVideos.Of(key);
        if (ReferenceEquals(session, data) && key == log && ReferenceEquals(l, link)) return;
        Pause();
        data = session;
        log = key;
        link = l;
        Close();
        sample = wanted = shownAt = double.NaN;
        picture.Source = bitmap = null;
        if (link == null) { ShowNote(T("No video of this race")); ShowSync(); return; }
        if (!File.Exists(link.File)) { ShowNote(F("Video not found: {0}", Path.GetFileName(link.File))); ShowSync(); return; }
        ShowNote(T("Opening the video…"));
        frames = new VideoFrames(link.File);
        wanted = 0;
        ShowSync();
    }

    /// <summary>(since 4.10.3) Compare's playback: the race's moment (a sample of Session 1's log) as it plays at a rate
    /// (0: still), its own play stopped; the sound only as it was (1×), started again when it jumped.</summary>
    public void Follow(double at, double rate, bool jumped)
    {
        if (double.IsNaN(at) || data == null) return;
        if (playing) Pause();
        wanted = at;
        if (rate != 1) { if (sounding) sound?.Stop(); sounding = false; return; }
        if ((sounding && !jumped) || link == null || !opened) return;
        sounding = true;
        (sound ??= new VideoSound(link.File) { Level = SoundLevel }).Play(Math.Max(0, VideoOf(at)));
    }

    /// <summary>Shows the race's moment (a sample of Session 1's log, fractional; NaN: none). Not while it plays.</summary>
    public void ShowSample(double at)
    {
        if (double.IsNaN(at) || playing || data == null) return;
        wanted = at;
    }

    private double VideoOf(double s) => link == null || data == null ? double.NaN : link.VideoAt(data.Started, s * SessionData.Dt);
    private double SampleOf(double video) => link == null || data == null ? double.NaN : (video - link.VideoAt(data.Started, 0)) / SessionData.Dt;

    private void Close()
    {
        frames?.Dispose();
        frames = null;
        sound?.Dispose();
        sound = null;
        sounding = false;
        opened = false;
        error = null;
    }

    private void Tick()
    {
        if (frames == null) return;
        if (frames.Error is { } e && e != error) { error = e; ShowNote(F("Can't play this video: {0}", e)); return; }
        if (!frames.Ready) return;
        if (!opened)
        {
            opened = true;
            along.Maximum = frames.Duration;
            bitmap = new WriteableBitmap(frames.Width, frames.Height, 96, 96, PixelFormats.Bgr32, null);
            picture.Source = bitmap;
            ShowNote(null);
        }
        if (frames.Take(out var pixels, out double at) && pixels.Length == frames.Width * frames.Height * 4 && bitmap != null)
        {
            bitmap.WritePixels(new Int32Rect(0, 0, frames.Width, frames.Height), pixels, frames.Width * 4, 0);
            shownAt = at;
            if (playing) FromVideo(at);
            else ShowPlace(at);
        }
        if (playing && frames.Ended) Pause();
        if (playing || double.IsNaN(wanted)) return;
        double s = wanted;
        wanted = double.NaN;
        sample = s;
        frames.Want(Math.Max(0, VideoOf(s)));
    }

    /// <summary>The video moved by itself: where the race is, for the chart and the map.</summary>
    private void FromVideo(double v)
    {
        sample = SampleOf(v);
        ShowPlace(v);
        Shown?.Invoke(data != null && sample >= 0 && sample < data.Count ? sample : double.NaN);
    }

    private void ShowPlace(double video)
    {
        settingSlider = true;
        along.Value = Math.Clamp(video, 0, along.Maximum);
        settingSlider = false;
        clock.Text = $"{Clock(video)} / {Clock(along.Maximum)}";
        int i = (int)Math.Round(sample);
        if (data == null || double.IsNaN(sample) || i < 0 || i >= data.Count) { race.Text = T("Before or after the race"); return; }
        double lap = data.LapNo[i], lapTime = data.LapTime[i];
        race.Text = F("Race {0}", Clock(i * SessionData.Dt))
            + (!double.IsNaN(lap) && lap >= 0 && !double.IsNaN(lapTime) ? " · " + F("Lap {0}  {1}", (int)lap + 1, Lap.LapClock(lapTime)) : "");
    }

    private static string Clock(double s) => double.IsNaN(s) || s < 0 ? "–" : $"{(int)(s / 60)}:{s % 60:00.0}";

    private void Play()
    {
        if (!opened || frames == null) return;
        playing = true;
        sounding = false;
        double from = double.IsNaN(shownAt) || shownAt >= along.Maximum - 0.1 ? 0 : shownAt;
        frames.Play(from);
        if (link != null) (sound ??= new VideoSound(link.File) { Level = SoundLevel }).Play(from);
        ((TextBlock)play.Content).Text = "";
        play.ToolTip = T("Pause");
        System.Windows.Automation.AutomationProperties.SetName(play, T("Pause"));
    }

    private void Pause()
    {
        if (!playing) return;
        playing = false;
        frames?.Pause();
        sound?.Stop();
        ((TextBlock)play.Content).Text = "";
        play.ToolTip = T("Play");
        System.Windows.Automation.AutomationProperties.SetName(play, T("Play"));
        Shown?.Invoke(double.NaN);
    }

    /// <summary>The race moved in the video (+: later), the same moment of it kept on screen.</summary>
    private void Move(double by)
    {
        if (link == null) return;
        link.Offset = Math.Round(link.Offset + by, 4);
        RaceVideos.All.Save(soon: true);
        ShowSync();
        if (!opened || playing || double.IsNaN(sample) || frames == null) return;
        frames.Want(Math.Max(0, VideoOf(sample)));
    }

    /// <summary>The frame shown is where the race clock starts (the log's first sample).</summary>
    private void StartsHere()
    {
        if (link == null || data == null || !opened || double.IsNaN(shownAt)) return;
        Pause();
        double v = shownAt;
        sample = 0;
        Move(v - VideoOf(0));
        FromVideo(v);
    }

    /// <summary>Exports the video with the telemetry drawn on (or stops the export going).</summary>
    private void Export()
    {
        if (exportCancel != null) { exportCancel.Cancel(); return; }
        if (link == null || data == null || !File.Exists(link.File)) { exporting.Text = T("No video of this race"); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("Export the video with the telemetry on it"),
            Filter = T("MP4 video") + " (*.mp4)|*.mp4",
            InitialDirectory = Path.GetDirectoryName(link.File),
            FileName = Path.GetFileNameWithoutExtension(link.File) + " " + T("telemetry") + ".mp4",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string target = dialog.FileName;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(link.File), StringComparison.OrdinalIgnoreCase)) { exporting.Text = T("Pick another name"); return; }
        Pause();
        var cancel = exportCancel = new CancellationTokenSource();
        // (the sync as it is now: moving it meanwhile doesn't change this export)
        var synced = new RaceVideoLink { File = link.File, Started = link.Started, Offset = link.Offset };
        double done = 0;
        ExportButton(T("Stop exporting"), "");
        exporting.Text = F("Exporting {0:0}%", 0);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => exporting.Text = F("Exporting {0:0}%", Volatile.Read(ref done) * 100);
        timer.Start();
        Logging.Log.Here.Info($"Analysis: exporting the race video with its telemetry to {target}");
        VideoExport.Run(link.File, target, data, synced, p => Volatile.Write(ref done, p), cancel.Token).ContinueWith(t =>
        {
            timer.Stop();
            exportCancel = null;
            cancel.Dispose();
            ExportButton(T("Export the video with the telemetry on it"), "");
            if (t.IsCanceled) exporting.Text = T("Export stopped");
            else if (t.Exception?.GetBaseException() is { } ex)
            {
                exporting.Text = F("Couldn't export: {0}", ex.Message);
                Logging.Log.Here.Warn($"Analysis: the race video couldn't be exported: {ex}");
            }
            else
            {
                exporting.Text = F("Exported: {0}", Path.GetFileName(target));
                Logging.Log.Here.Info($"Analysis: race video exported: {target}");
                Ui.Open("explorer.exe", $"/select,\"{target}\"");
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ExportButton(string name, string glyph)
    {
        ((TextBlock)export.Content).Text = glyph;
        export.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(export, name);
    }

    private void ShowSync() =>
        offset.Text = link == null ? "" : Math.Abs(link.Offset) < 1e-4 ? T("Sync: as recorded") : F("Sync: {0:+0.000;-0.000} s", link.Offset);

    private void ShowNote(string? text)
    {
        note.Text = text ?? "";
        note.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }
}
