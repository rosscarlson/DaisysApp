using System.IO;
using System.Windows;
using System.Windows.Threading;
using DaisysApp.Settings;
using DaisysApp.Shared.Network;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>(since 4.8.0) A race's video and how it lines up with the race's log.</summary>
public sealed class RaceVideoLink
{
    /// <summary>The video file (Video → Recording's folder).</summary>
    public string File { get; set; } = "";

    /// <summary>When the video's first frame was: with the log's own start, where the race is in the video.</summary>
    public DateTime Started { get; set; }

    /// <summary>Seconds it's moved by hand to line up (+: the race comes later in the video).</summary>
    public double Offset { get; set; }

    /// <summary>Where a moment of the race (seconds from its log's start) is in the video, in seconds.</summary>
    public double VideoAt(DateTime logStarted, double seconds) => seconds + (logStarted - Started).TotalSeconds + Offset;

    /// <summary>(since 4.15.0) Its copy on YouTube (the link), or null.</summary>
    public string? YouTube { get; set; }

    /// <summary>(since 4.15.0) Waiting to go to YouTube (<see cref="RaceUploads"/>; picked up again after a restart).</summary>
    public bool Upload { get; set; }

    /// <summary>(since 4.15.0) The video file is there (it may have been deleted, its YouTube copy kept).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasFile => File.Length > 0 && System.IO.File.Exists(File);

    /// <summary>(since 4.15.0) There's a video to watch: the file, or its YouTube copy.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Watchable => HasFile || YouTube != null;
}

/// <summary>(since 4.8.0) Each race's video, by its log's file name: %APPDATA%\DaisysApp\AnalysisVideos.json.</summary>
public sealed class RaceVideos
{
    private const string FileName = "AnalysisVideos";

    public Dictionary<string, RaceVideoLink> Videos { get; set; } = new();

    /// <summary>(since 4.15.0) Each race's video uploaded to YouTube when it's saved (the account: Core's YouTube).</summary>
    public bool YouTubeAuto { get; set; }

    /// <summary>(since 4.15.0) Who can watch what's uploaded.</summary>
    public YouTubePrivacy YouTubePrivacy { get; set; } = YouTubePrivacy.Unlisted;

    /// <summary>(since 4.15.0) A video uploaded has its file deleted (watched from YouTube).</summary>
    public bool YouTubeDeleteLocal { get; set; }

    private static RaceVideos? all;
    public static RaceVideos All => all ??= JsonStore.Load<RaceVideos>(FileName);

    /// <summary>Raised (on the UI thread) when a video is added, moved or dropped.</summary>
    public static event Action? Changed;

    public static RaceVideoLink? Of(string? log) => log != null && All.Videos.TryGetValue(log, out var v) ? v : null;

    public void Save(bool soon = false)
    {
        if (soon) JsonStore.SaveSoon(FileName, this);
        else JsonStore.Save(FileName, this);
        Changed?.Invoke();
    }
}

/// <summary>
/// (since 4.8.0) Records each race with Video → Recording (Core's <see cref="ScreenRecordings"/>: what its Record card
/// has chosen) while <see cref="AnalysisSettings.RecordVideo"/> is on: started when the race's log begins, stopped when
/// it ends, and deleted with it when the race isn't kept. The video is linked to the log with when its first frame was,
/// so they line up by themselves; <see cref="RaceVideoLink.Offset"/> is the hand's correction.
/// </summary>
internal sealed class RaceVideo
{
    private readonly AnalysisSettings settings;
    private readonly Dispatcher ui = Application.Current.Dispatcher;
    /// <summary>The race (its log's file name) being recorded, or starting to be.</summary>
    private string? recording;
    private bool starting;
    /// <summary>Races dropped while their video was starting.</summary>
    private readonly HashSet<string> dropped = new(StringComparer.OrdinalIgnoreCase);

    public RaceVideo(RaceLog log, AnalysisSettings settings)
    {
        this.settings = settings;
        log.Began += p => ui.BeginInvoke(() => Began(Path.GetFileName(p)));
        log.Ended += p => ui.BeginInvoke(() => Ended(Path.GetFileName(p)));
        log.Dropped += p => ui.BeginInvoke(() => Dropped(Path.GetFileName(p)));
        // links to logs gone (deleted with their track, or by hand) are forgotten; their videos stay
        var gone = RaceVideos.All.Videos.Keys.Where(k => !File.Exists(Path.Combine(SessionLog.Folder, k))).ToList();
        if (gone.Count > 0)
        {
            foreach (var k in gone) RaceVideos.All.Videos.Remove(k);
            RaceVideos.All.Save();
        }
        // (since 4.15.0) uploads a restart cut short, and those waiting for a sign-in
        RaceUploads.Resume();
    }

    /// <summary>Switched off: a race being recorded stops.</summary>
    public void Switched(bool on)
    {
        if (!on && recording != null) Ended(recording);
    }

    private void Began(string log)
    {
        if (recording != null) Ended(recording);
        if (!settings.RecordVideo) return;
        if (ScreenRecordings.Recorder is not { } r) { Logging.Log.Here.Info("Analysis: race not recorded: Video → Recording is off"); return; }
        // (not r.Busy: the last race's video is still being saved when the next race begins, and Recording starts this
        // one after it; it says when it's busy with the user's own)
        recording = log;
        starting = true;
        r.Start((started, error) =>
        {
            starting = false;
            if (started == null)
            {
                Logging.Log.Here.Warn($"Analysis: the race's video couldn't start: {error}");
                if (recording == log) recording = null;
                return;
            }
            Logging.Log.Here.Info($"Analysis: recording the race {log} to {started.File}");
            RaceVideos.All.Videos[log] = new RaceVideoLink { File = started.File, Started = started.At };
            RaceVideos.All.Save();
            // (ended, or dropped, while it started)
            if (recording != log) r.Stop(() => { if (dropped.Remove(log)) Dropped(log); });
        });
    }

    private void Ended(string log)
    {
        if (recording != log) return;
        recording = null;
        if (!starting) ScreenRecordings.Recorder?.Stop(() => Saved(log));
    }

    /// <summary>(since 4.15.0) A race's video finished: uploaded to YouTube when that's on, once the race is kept (a
    /// race not kept has its video dropped a moment after it ends).</summary>
    private void Saved(string log)
    {
        if (!RaceVideos.All.YouTubeAuto || RaceVideos.Of(log) is not { YouTube: null } link) return;
        // (marked now, so a restart in the meantime still uploads it)
        link.Upload = true;
        RaceVideos.All.Save();
        _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ =>
        {
            if (RaceVideos.Of(log) is { HasFile: true, YouTube: null }) RaceUploads.Queue(new[] { log });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Dropped(string log)
    {
        if (starting && recording == log) { dropped.Add(log); return; }
        if (!RaceVideos.All.Videos.Remove(log, out var link)) return;
        RaceVideos.All.Save();
        string file = link.File;
        // (once the recorder has let go of it)
        Task.Run(async () =>
        {
            for (int i = 0; i < 20 && File.Exists(file); i++)
            {
                try { File.Delete(file); Logging.Log.Here.Info($"Analysis: race video dropped (no lap or race kept): {Path.GetFileName(file)}"); return; }
                catch { await Task.Delay(1000); }
            }
        });
    }

    /// <summary>The app closing: a race being recorded stops.</summary>
    public void Dispose()
    {
        if (recording != null && !starting)
        {
            ScreenRecordings.Recorder?.Stop();
            // (uploaded at the next start, when it's kept)
            if (RaceVideos.All.YouTubeAuto && RaceVideos.Of(recording) is { YouTube: null } link) { link.Upload = true; RaceVideos.All.Save(); }
        }
        recording = null;
    }
}
