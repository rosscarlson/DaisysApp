using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Recordings;

/// <summary>
/// (since 4.20.0) A recording played as if its game were running: every tool reads it in place of the game
/// (<see cref="SimData.Play"/>), with the profile it was recorded with (or the game's) in use and playing; stopped, the
/// profile is as it was before. The clock is here (the video's time), so it goes on whichever tab shows; the Recordings
/// tab's video follows it. Analysis records nothing of it, nor are redlines learned (<see cref="TelemetryFrame.Playback"/>).
/// On the UI thread.
/// </summary>
internal static class SimPlayback
{
    private static PlaybackClock clock = new();
    private static SimRecording? loading;
    private static readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    /// <summary>The profile before, and whether it played; the one used for the recording.</summary>
    private static (SimProfile Profile, bool Playing)? before;
    private static SimProfile? used;
    private static bool switching;

    static SimPlayback()
    {
        timer.Tick += (_, _) => Tick();
        SimProfiles.Changed += ProfilesChanged;
    }

    /// <summary>The recording being played (or paused); null: none, the game is read.</summary>
    public static SimRecording? Current { get; private set; }
    public static bool Playing { get; private set; }
    public static double Length { get; private set; }
    public static double Position => Math.Min(clock.Now.At, Length);

    /// <summary>A recording began or stopped playing (the game is read again).</summary>
    public static event Action? Changed;
    /// <summary>Played, paused, moved along or stopped.</summary>
    public static event Action? Moved;

    /// <summary>Plays a recording from a moment of it (the video's time); the one paused goes on from there.</summary>
    public static async void Play(SimRecording r, double from)
    {
        if (Current == r)
        {
            if (from >= Length - 0.1) from = 0;
            if (Math.Abs(from - Position) > 0.001) clock.Seek(from);
            Run(true);
            return;
        }
        if (SimRecorder.Recording || loading == r) return;
        End("another one");
        loading = r;
        try
        {
            var data = await Task.Run(() => FrameData.Load(r.Frames));
            if (loading != r) return;
            loading = null;
            if (SimRecorder.Recording) return;
            if (data.Count == 0) { Notifications.Show(T("SimEffects"), T("No telemetry in this recording")); return; }
            Begin(r, data, from);
        }
        catch (Exception ex)
        {
            if (loading == r) loading = null;
            Logging.Log.Here.Warn($"Recordings: {r.Id} couldn't be played", ex);
            Notifications.Show(T("SimEffects"), F("Can't play this recording: {0}", ex.Message));
        }
    }

    private static void Begin(SimRecording r, FrameData data, double from)
    {
        // (one the app ended while recording: how long it is)
        if (Math.Abs(r.Seconds - data.Seconds) > 0.01) { r.Seconds = data.Seconds; r.Save(); }
        Length = r.Length;
        var c = clock = new PlaybackClock();
        c.SetShift(r.Shift);
        c.Seek(from >= Length - 0.1 ? 0 : from);
        Current = r;
        Logging.Log.Here.Info($"Recordings: playing {r.Id} ({r.Name}, {data.Count} frames, {data.Seconds:0.0} s)");
        before = (SimProfiles.Active, SimProfiles.Playing);
        used = SimProfiles.All.FirstOrDefault(p => p.Name == r.Profile && p.Game == r.Game)
            ?? (SimProfiles.Active.Game == r.Game ? SimProfiles.Active : SimProfiles.All.FirstOrDefault(p => p.Game == r.Game)) ?? SimProfiles.Active;
        SimData.Play(new SimData.Recorded(r.Game, r.Edition, () => new PlaybackSource(data, c)));
        SimBadges.Set("Recordings", T("Playback"), F("Playing {0}", r.Name));
        Run(true);
        Changed?.Invoke();
    }

    public static void Pause()
    {
        loading = null;
        if (Current != null) Run(false);
    }

    private static void Run(bool on)
    {
        // (its tools are dark while the profile is stopped)
        if (on && used is { } p && (SimProfiles.Active != p || !SimProfiles.Playing)) Switch(() => SimProfiles.Use(p, "a recording plays"));
        clock.Run(on);
        Playing = on;
        if (on) timer.Start();
        else timer.Stop();
        Moved?.Invoke();
    }

    public static void Seek(double to)
    {
        if (Current == null) return;
        clock.Seek(Math.Clamp(to, 0, Length));
        Moved?.Invoke();
    }

    /// <summary>The telemetry moved in the video (<see cref="SimRecording.Offset"/>).</summary>
    public static void Shifted(SimRecording r)
    {
        if (Current != r) return;
        clock.SetShift(r.Shift);
        Length = r.Length;
    }

    /// <summary>Stops: the game is read again, and the profile is as it was (unless <paramref name="restore"/> is false,
    /// or another was picked meanwhile).</summary>
    public static void End(string why, bool restore = true)
    {
        loading = null;
        if (Current is not { } r) return;
        timer.Stop();
        clock.Run(false);
        Playing = false;
        Current = null;
        Logging.Log.Here.Info($"Recordings: {r.Id} stopped ({why})");
        SimBadges.Set("Recordings", null);
        var (was, mine) = (before, used);
        before = null;
        used = null;
        // (stopped meanwhile by its Stop: it stays stopped)
        if (restore && was is { } b && SimProfiles.Active == mine && SimProfiles.All.Contains(b.Profile))
            Switch(() => SimProfiles.Use(b.Profile, "the recording stopped", b.Playing && (SimProfiles.Playing || b.Profile != mine)));
        SimData.Play(null);
        Moved?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>A game ended (Auto Start) while a recording plays: its profile isn't played again after it; true when the
    /// recording goes on with it meanwhile.</summary>
    public static bool GameEnded(SimProfile p)
    {
        if (Current == null) return false;
        if (before is { } b && b.Profile == p) before = (p, false);
        return true;
    }

    private static void Switch(Action change)
    {
        switching = true;
        try { change(); }
        finally { switching = false; }
    }

    /// <summary>Another profile picked: of the same game, the recording goes on with it; of another, it stops. The profile
    /// stopped: it pauses.</summary>
    private static void ProfilesChanged()
    {
        if (switching || Current is not { } r) return;
        if (SimProfiles.Active.Game != r.Game) { End("another game's profile", restore: false); return; }
        used = SimProfiles.Active;
        if (!SimProfiles.Playing && Playing) Run(false);
    }

    private static void Tick()
    {
        if (!Playing || clock.Now.At < Length) return;
        if (RecordingsSettings.All.Loop && Length > 0.5) { Seek(0); return; }
        clock.Seek(Length);
        Run(false);
    }
}
