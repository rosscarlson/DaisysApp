using System.IO;
using DaisysApp.Shell;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// (since 4.15.0) Race videos going to YouTube (Core's <see cref="YouTube"/>), one at a time: each one marked
/// <see cref="RaceVideoLink.Upload"/> until it's up (so a restart picks it up again), then its link kept in
/// <see cref="RaceVideoLink.YouTube"/> and (with <see cref="RaceVideos.YouTubeDeleteLocal"/>) its file deleted. Named
/// from its session (<see cref="Describe"/>). While one goes, the title bar shows it (since 4.15.1): "YT: 24%,
/// 4.5Mb/s, 1:24" (done, speed, time left). Everything here is on the UI thread.
/// </summary>
internal static class RaceUploads
{
    private static readonly Dictionary<string, double> going = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> failed = new(StringComparer.OrdinalIgnoreCase);
    private static bool pumping;
    private static CancellationTokenSource? cancel;
    /// <summary>The upload going's last few seconds (when, bytes sent), for its speed.</summary>
    private static readonly Queue<(long Ms, double Bytes)> recent = new();
    private const string BadgeId = "YouTubeUpload";

    static RaceUploads()
    {
        YouTube.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (YouTube.SignedIn) Resume(); });
    }

    /// <summary>A session's video's title and description (by its log's file name), or null when it isn't listed.</summary>
    public static Func<string, (string Title, string Description)?>? Describe { get; set; }

    /// <summary>An upload started, moved on, finished or failed.</summary>
    public static event Action? Changed;

    /// <summary>How far a video's upload is (0 to 1; NaN waiting its turn), or null when it isn't going.</summary>
    public static double? Progress(string log) => going.TryGetValue(log, out double p) ? p : null;

    /// <summary>Why its last upload failed, or null.</summary>
    public static string? Failure(string log) => failed.TryGetValue(log, out var f) ? f : null;

    /// <summary>Uploads these sessions' videos (those with a file and no YouTube copy).</summary>
    public static void Queue(IEnumerable<string> logs)
    {
        bool any = false;
        foreach (var log in logs)
            if (RaceVideos.Of(log) is { HasFile: true, YouTube: null } link)
            {
                link.Upload = true;
                failed.Remove(log);
                if (!going.ContainsKey(log)) going[log] = double.NaN;
                any = true;
            }
        if (!any) return;
        RaceVideos.All.Save();
        Changed?.Invoke();
        _ = Pump();
    }

    /// <summary>Those marked to go (a restart, or waiting for a sign-in), queued again.</summary>
    public static void Resume()
    {
        if (!YouTube.SignedIn) return;
        var marked = RaceVideos.All.Videos.Where(v => v.Value.Upload && v.Value.YouTube == null).Select(v => v.Key).ToList();
        if (marked.Count > 0) Queue(marked);
    }

    /// <summary>Stops the uploads (those waiting stay marked, for a later Resume).</summary>
    public static void StopAll()
    {
        cancel?.Cancel();
        foreach (var k in going.Keys.ToList()) if (RaceVideos.Of(k) is { } l) l.Upload = false;
        going.Clear();
        RaceVideos.All.Save();
        Changed?.Invoke();
    }

    private static async Task Pump()
    {
        if (pumping) return;
        pumping = true;
        try
        {
            while (going.Keys.FirstOrDefault() is { } log)
            {
                if (RaceVideos.Of(log) is not { HasFile: true, YouTube: null } link) { going.Remove(log); continue; }
                if (!YouTube.SignedIn) { failed[log] = T("Not signed in to YouTube"); going.Clear(); break; }
                var (title, description) = Describe?.Invoke(log) ?? (F("Race {0}", Path.GetFileNameWithoutExtension(log)), "");
                going[log] = 0;
                Changed?.Invoke();
                cancel = new CancellationTokenSource();
                long size = 0;
                try { size = new FileInfo(link.File).Length; } catch (IOException) { }
                recent.Clear();
                Badge(title, 0, size);
                var progress = new Progress<double>(p =>
                {
                    if (!going.ContainsKey(log) || p < going[log]) return;
                    going[log] = p;
                    Badge(title, p, size);
                    Changed?.Invoke();
                });
                try
                {
                    var up = await YouTube.Upload(link.File, title, description + (description.Length > 0 ? "\n\n" : "") + T("Recorded with Daisy's App"),
                        RaceVideos.All.YouTubePrivacy, progress, cancel.Token, new[] { "Daisy's App", "sim racing" });
                    link.YouTube = up.Url;
                    link.Upload = false;
                    RaceVideos.All.Save();
                    Logging.Log.Here.Info($"Analysis: {log}'s video is on YouTube: {up.Url}");
                    if (RaceVideos.All.YouTubeDeleteLocal) DeleteFiles(new[] { log });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    failed[log] = ex.Message;
                    Logging.Log.Here.Warn($"Analysis: {log}'s video couldn't go to YouTube: {ex.Message}");
                }
                finally
                {
                    cancel.Dispose();
                    cancel = null;
                    going.Remove(log);
                    TitleBadges.Set(BadgeId, null);
                    Changed?.Invoke();
                }
            }
        }
        finally { pumping = false; TitleBadges.Set(BadgeId, null); }
    }

    /// <summary>The title bar's bubble for the upload going: how far, how fast (bits a second, over the last 8 s) and
    /// how long left; pointing at it names the video (and how many wait after it).</summary>
    private static void Badge(string title, double done, long size)
    {
        long now = Environment.TickCount64;
        recent.Enqueue((now, done * size));
        while (recent.Count > 2 && now - recent.Peek().Ms > 8000) recent.Dequeue();
        var first = recent.Peek();
        double seconds = (now - first.Ms) / 1000.0, bytesPerSecond = seconds >= 1 ? (done * size - first.Bytes) / seconds : 0;
        string text = $"YT: {(int)(done * 100)}%";
        if (bytesPerSecond > 0)
        {
            double bits = bytesPerSecond * 8;
            string rate = bits >= 1e9 ? $"{bits / 1e9:0.0}Gb/s" : bits >= 1e6 ? $"{bits / 1e6:0.0}Mb/s" : $"{bits / 1e3:0.0}kb/s";
            var left = TimeSpan.FromSeconds(Math.Min((1 - done) * size / bytesPerSecond, 359999));
            text += $", {rate}, " + (left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{(int)left.TotalMinutes}:{left:ss}");
        }
        int waiting = going.Count - 1;
        string tip = waiting > 0 ? F("Uploading to YouTube: {0} ({1} more waiting)", title, waiting) : F("Uploading to YouTube: {0}", title);
        TitleBadges.Set(BadgeId, text, tip, TitleBadges.Green, "SimEffects/" + SimEffectsApplet.Analysis);
    }

    /// <summary>Deletes these sessions' video files where they're on YouTube (the link kept: watched from there).</summary>
    public static int DeleteFiles(IEnumerable<string> logs)
    {
        int n = 0;
        foreach (var log in logs)
        {
            if (RaceVideos.Of(log) is not { YouTube: not null, HasFile: true } link) continue;
            string file = link.File;
            link.File = "";
            n++;
            Logging.Log.Here.Info($"Analysis: {log}'s video file deleted (on YouTube: {link.YouTube})");
            // (once the video shown has let go of it)
            Task.Run(async () =>
            {
                for (int i = 0; i < 20 && File.Exists(file); i++)
                {
                    try { File.Delete(file); return; }
                    catch { await Task.Delay(1000); }
                }
                if (File.Exists(file)) Logging.Log.Here.Warn($"Analysis: the video file couldn't be deleted: {file}");
            });
        }
        if (n > 0) RaceVideos.All.Save();
        return n;
    }
}
