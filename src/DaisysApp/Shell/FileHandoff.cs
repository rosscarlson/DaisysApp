using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using DaisysApp.Logging;

namespace DaisysApp.Shell;

/// <summary>
/// "--open file" given to a second copy (Windows starts one for each file opened with the app): the file's path goes
/// to the running copy over a named pipe of this user's (Core's OpenFiles opens it there), and the second copy exits.
/// </summary>
internal static class FileHandoff
{
    private static string PipeName =>
        $"{AppPaths.ShortName}.Open.{WindowsIdentity.GetCurrent().User?.Value}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    /// <summary>The path after --open, if there is one.</summary>
    public static string? FileIn(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals(OpenFiles.Argument, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && args[i + 1].Trim().Length > 0 ? Path.GetFullPath(args[i + 1].Trim()) : null;
    }

    /// <summary>Sends the path to the running copy (waiting while it starts); false if it can't be reached.</summary>
    public static bool Send(string path)
    {
        try
        {
            // (started by Windows for the file, this copy may bring a window forward: so may the running one)
            AllowSetForegroundWindow(-1);
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(30000);
            using var w = new StreamWriter(pipe, new UTF8Encoding(false));
            w.WriteLine(path);
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>The running copy: each path sent is given to <paramref name="open"/> (on a background thread).</summary>
    public static void Listen(Action<string> open)
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    pipe.WaitForConnection();
                    using var r = new StreamReader(pipe, Encoding.UTF8);
                    if (r.ReadLine() is { Length: > 0 } path) open(path);
                }
                catch (Exception ex)
                {
                    Log.App.Warn("Files opened with the app: the pipe failed", ex);
                    Thread.Sleep(2000);
                }
            }
        }) { IsBackground = true, Name = "DaisysApp file opener" }.Start();
    }
}
