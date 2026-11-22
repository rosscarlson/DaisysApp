using System.Diagnostics;
using System.Windows;
using DaisysApp.Applets.SimEffects.Analysis;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Recordings;

/// <summary>
/// (since 4.20.0) SimEffects' Record: the telemetry of the profile in use's game and a video by Video → Recording (as its
/// Record card is set), both from when the video starts. Not while a race's video is being recorded (Analysis'
/// <see cref="RaceVideo"/>), and one beginning stops it (<see cref="MakeWay"/>): Recording makes one video at a time. On
/// the UI thread.
/// </summary>
internal static class SimRecorder
{
    private static SimRecording? recording;
    private static bool starting, stopWanted, following;
    private static List<Action> waiting = new();
    private static Thread? thread;
    private static volatile bool quit;
    private static FrameWriter? writer;
    private static volatile string car = "";

    public static bool Recording => recording != null || starting;

    /// <summary>Started, stopped, or whether it can start changed.</summary>
    public static event Action? Changed;
    /// <summary>A recording was made (its video's file finished).</summary>
    public static event Action<SimRecording>? Saved;

    /// <summary>Follows what decides whether it can record (once).</summary>
    public static void Follow()
    {
        if (following) return;
        following = true;
        void Later(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
        void Again() => Later(() => Changed?.Invoke());
        RaceVideo.RecordingChanged += Again;
        ScreenRecordings.Changed += Again;
        SimPlayback.Changed += Again;
        SimProfiles.Changed += Again;
        // the profile's game is another one: what was recorded ends
        SimData.Changed += () => Later(() => { if (recording is { } r && SimData.Game?.Game != r.Game) Stop("another game's profile"); });
        // the video stopped on its own (Video's Stop, or an error)
        ScreenRecordings.RecordingChanged += () => Later(() => { if (recording != null && !ScreenRecordings.Recording) Stop("the video stopped"); });
    }

    /// <summary>Why it can't record now, or null.</summary>
    public static string? CantRecord =>
        SimPlayback.Current != null ? T("A recording is playing")
        : SimProfiles.Active.Game == null ? T("No game in this profile")
        : ScreenRecordings.Recorder == null ? T("Video → Recording is off")
        : RaceVideo.Recording ? T("A race is being recorded")
        : null;

    public static void Toggle(string why)
    {
        if (Recording) Stop(why);
        else Start(why);
    }

    private static void Start(string why)
    {
        if (CantRecord is { } reason) { Refuse(reason); return; }
        if (ScreenRecordings.Recorder is not { } r || SimProfiles.Active.Game is not { } game) return;
        starting = true;
        stopWanted = false;
        Logging.Log.Here.Info($"Recordings: starting ({why})");
        Changed?.Invoke();
        r.Start((started, error) =>
        {
            starting = false;
            if (started == null) Refuse(error ?? T("Couldn't start recording"));
            else if (stopWanted) r.Stop();
            else Begin(game, started);
            if (recording == null) Done();
            Changed?.Invoke();
        });
    }

    private static void Refuse(string reason)
    {
        Logging.Log.Here.Info($"Recordings: not recording: {reason}");
        Notifications.Show(T("SimEffects"), F("Not recording: {0}", reason));
    }

    private static void Begin(Game game, ScreenRecordings.Started video)
    {
        var now = DateTime.Now;
        try
        {
            var r = new SimRecording
            {
                Id = SimRecording.NewId(now), Name = Games.Title(game), Game = game, Edition = CarNames.Edition, Profile = SimProfiles.Active.Name,
                Started = now, Video = video.File, VideoStarted = video.At,
            };
            var w = writer = new FrameWriter(r.Frames);
            var clock = Stopwatch.StartNew();
            r.Started = DateTime.Now;
            r.Save();
            recording = r;
            car = "";
            quit = false;
            thread = new Thread(() => Loop(w, clock)) { IsBackground = true, Name = "DaisysApp-SimRecorder", Priority = ThreadPriority.AboveNormal };
            thread.Start();
            Logging.Log.Here.Info($"Recordings: recording {r.Id}, its video {video.File}");
            SimBadges.Set("Recordings", T("Recording"), T("The session is being recorded"));
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Recordings: couldn't start", ex);
            writer?.Dispose();
            writer = null;
            recording = null;
            ScreenRecordings.Recorder?.Stop();
            Refuse(ex.Message);
        }
    }

    /// <summary>The game's frames to the file, until it's stopped.</summary>
    private static void Loop(FrameWriter w, Stopwatch clock)
    {
        var frame = new TelemetryFrame();
        ITelemetrySource? source = null;
        try
        {
            source = SimData.Open();
            while (!quit)
            {
                if (!source.Read(frame, 100) || frame.Playback) continue;
                w.Write(clock.Elapsed.TotalSeconds, frame);
                if (frame.Live && frame.Car.Length > 0) car = frame.Car;
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("SimEffects recording", ex); }
        finally
        {
            source?.Dispose();
            try { w.Dispose(); } catch (Exception ex) { Logging.Log.Here.Warn("Recordings: closing the file", ex); }
        }
    }

    /// <summary>Stops it; <paramref name="then"/> once the recorder can start another video.</summary>
    public static void Stop(string why, Action? then = null)
    {
        if (then != null) waiting.Add(then);
        if (starting) { stopWanted = true; return; }
        if (recording is not { } r) { Done(); return; }
        recording = null;
        quit = true;
        thread?.Join(3000);
        thread = null;
        int frames = writer?.Frames ?? 0;
        r.Seconds = writer?.Seconds ?? 0;
        r.Edition = CarNames.Edition ?? r.Edition;
        r.Car = car;
        writer = null;
        r.Save();
        Logging.Log.Here.Info($"Recordings: {r.Id} stopped ({why}): {frames} frames, {r.Seconds:0.0} s");
        SimBadges.Set("Recordings", null);
        if (frames == 0) Notifications.Show(T("SimEffects"), T("No telemetry came: only the video was recorded"));
        if (ScreenRecordings.Recorder is { } recorder) recorder.Stop(() => Saved?.Invoke(r));
        else Saved?.Invoke(r);
        Done();
        Changed?.Invoke();
    }

    /// <summary>A race's video is about to start: the session's recording ends first.</summary>
    public static void MakeWay(Action then)
    {
        if (!Recording) { then(); return; }
        Notifications.Show(T("SimEffects"), T("Session recording stopped: a race began"));
        Stop("a race began", then);
    }

    private static void Done()
    {
        var all = waiting;
        waiting = new();
        foreach (var a in all) a();
    }
}
