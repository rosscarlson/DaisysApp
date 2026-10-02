using System.IO;

namespace DaisysApp.Logging;

/// <summary>Best-effort crash/exception log so failures are diagnosable without a debugger attached.</summary>
public static class ErrorLog
{
    private static readonly string LogDir =
        AppPaths.LogFolder;

    private static readonly string LogPath = Path.Combine(LogDir, "errors.log");
    private static readonly object Lock = new();

    public static void Write(string context, Exception ex)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {context}: {ex}{Environment.NewLine}");
            }
        }
        catch
        {
            // If we can't even log the error, there's nothing more to do.
        }
    }
}
