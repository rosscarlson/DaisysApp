using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DaisysApp.Logging;

/// <summary>
/// Errors, written as ERROR lines to the log of whoever reports them (the app's, or the calling module's; see
/// <see cref="Log"/>). A crash inside native code (a driver, Windows) kills the process before anything can be written;
/// <see cref="StartSession"/> notices that afterwards, says so in the log, and copies in what Windows recorded about it.
/// </summary>
public static class ErrorLog
{
    private static readonly string MarkerPath = Path.Combine(AppPaths.LogFolder, "running.marker");

    /// <summary>Today's app log.</summary>
    public static string LogPath => Log.PathOf(Log.AppName, DateTime.Today);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(string context, Exception ex) => Log.For(Log.NameOf(Assembly.GetCallingAssembly())).Error(context, ex);

    /// <summary>A problem that isn't an exception (e.g. "closed unexpectedly last time").</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Note(string context, string message) => Log.For(Log.NameOf(Assembly.GetCallingAssembly())).Warn($"{context}: {message}");

    /// <summary>
    /// Call at startup: if the last run didn't end cleanly (its marker is still there), notes it in the log with any
    /// crash Windows recorded for the app since then. Returns true in that case.
    /// </summary>
    public static bool StartSession(string version)
    {
        bool crashed = false;
        try
        {
            if (File.Exists(MarkerPath))
            {
                crashed = true;
                string marker = File.ReadAllText(MarkerPath).Trim();
                Log.App.Error($"Daisy's App closed unexpectedly last time (version and start time: {marker}). " +
                              "If nothing is logged just before that, it was a crash in native code (a driver or Windows); what Windows recorded about it follows, if anything.");
                var since = DateTime.Now.AddDays(-1);
                int comma = marker.LastIndexOf(',');
                if (comma > 0 && DateTime.TryParseExact(marker[(comma + 1)..].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var started))
                    since = started.AddMinutes(-1);
                var events = WindowsEvents.AppCrashes(since);
                if (events.Count == 0) Log.App.Info("Windows recorded no crash of DaisysApp.exe since then.");
                foreach (var e in events)
                    Log.App.Error($"Windows {e.LogName} log, {e.Time:yyyy-MM-dd HH:mm:ss}, {e.Provider} (event {e.Id}):\n{e.Message}");
            }
            Directory.CreateDirectory(AppPaths.LogFolder);
            File.WriteAllText(MarkerPath, $"{version}, {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex) { Log.App.Warn("Couldn't check how the last run ended", ex); }
        return crashed;
    }

    /// <summary>Call on a clean exit.</summary>
    public static void EndSession()
    {
        try { File.Delete(MarkerPath); } catch { }
    }
}
