using System.IO;
using System.Windows;
using System.Windows.Threading;
using DaisysApp.Settings;
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
}

/// <summary>(since 4.8.0) Each race's video, by its log's file name: %APPDATA%\DaisysApp\AnalysisVideos.json.</summary>
public sealed class RaceVideos
{
    private const string FileName = "AnalysisVideos";

    public Dictionary<string, RaceVideoLink> Videos { get; set; } = new();

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
        if (r.Busy) { Logging.Log.Here.Info("Analysis: race not recorded: Video → Recording is busy"); return; }
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
        if (!starting) ScreenRecordings.Recorder?.Stop();
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
        if (recording != null && !starting) ScreenRecordings.Recorder?.Stop();
        recording = null;
    }
}
