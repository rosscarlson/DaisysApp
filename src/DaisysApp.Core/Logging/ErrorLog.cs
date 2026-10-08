using System.IO;

namespace DaisysApp.Logging;

/// <summary>
/// Best-effort error log (%LOCALAPPDATA%\DaisysApp\logs\errors.log; Settings → General → Files → Error log) so failures
/// are diagnosable without a debugger attached. A crash inside native code (a driver, Windows) kills the process
/// before anything can be written; <see cref="StartSession"/> notices that afterwards and says so in the log.
/// </summary>
public static class ErrorLog
{
    private static readonly string LogDir = AppPaths.LogFolder;
    private static readonly string MarkerPath = Path.Combine(LogDir, "running.marker");
    private static readonly object Lock = new();

    public static string LogPath { get; } = Path.Combine(LogDir, "errors.log");

    public static void Write(string context, Exception ex) => Append($"{context}: {ex}");

    /// <summary>A line that isn't an exception (e.g. "closed unexpectedly last time").</summary>
    public static void Note(string context, string message) => Append($"{context}: {message}");

    private static void Append(string text)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}{Environment.NewLine}");
            }
        }
        catch
        {
            // If we can't even log the error, there's nothing more to do.
        }
    }

    /// <summary>
    /// Call at startup: if the last run didn't end cleanly (its marker is still there), notes it in the log. Returns
    /// true in that case.
    /// </summary>
    public static bool StartSession(string version)
    {
        bool crashed = false;
        try
        {
            if (File.Exists(MarkerPath))
            {
                crashed = true;
                Note("Startup", $"Daisy's App closed unexpectedly last time (version and start time: {File.ReadAllText(MarkerPath).Trim()}). " +
                                "If nothing is logged just before this, it was a crash in native code (a driver or Windows), which can't be logged.");
            }
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(MarkerPath, $"{version}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch { }
        return crashed;
    }

    /// <summary>Call on a clean exit.</summary>
    public static void EndSession()
    {
        try { File.Delete(MarkerPath); } catch { }
    }
}
