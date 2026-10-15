using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// Runs commands as administrator (vJoyConfig, and reg.exe writing HKLM for Hush and the racing wheel, need it): all of them in one batch file, so
/// there's one Windows permission prompt, with what each printed and its exit code collected in a log.
/// </summary>
internal static class Elevated
{
    public sealed record Outcome(bool Ok, string Output);

    public static Task<Outcome> RunAsync(IReadOnlyList<(string Exe, string Args)> commands) => Task.Run(() =>
    {
        string dir = Path.Combine(Path.GetTempPath(), "DaisysApp");
        Directory.CreateDirectory(dir);
        string script = Path.Combine(dir, "forzawheel-admin.cmd"), log = Path.Combine(dir, "forzawheel-admin.log");
        var sb = new StringBuilder("@echo off\r\nchcp 65001 >nul\r\n");
        sb.Append($"type nul > \"{log}\"\r\nset failed=0\r\n");
        foreach (var (exe, args) in commands)
        {
            // (the arguments' & are inside quotes, where cmd takes them literally)
            sb.Append($"echo ^> {Path.GetFileName(exe)} {args.Replace("%", "%%")} >> \"{log}\"\r\n");
            sb.Append($"\"{exe}\" {args.Replace("%", "%%")} >> \"{log}\" 2>&1\r\n");
            sb.Append($"if errorlevel 1 echo   ^(failed^) >> \"{log}\"\r\n");
            sb.Append("if errorlevel 1 set failed=1\r\n");
        }
        sb.Append("exit /b %failed%\r\n");
        File.WriteAllText(script, sb.ToString(), new UTF8Encoding(false));
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p == null) return new Outcome(false, T("Couldn't start it."));
            if (!p.WaitForExit(60000)) return new Outcome(false, T("It didn't finish within a minute."));
            string output = File.Exists(log) ? File.ReadAllText(log).Trim() : "";
            Logging.Log.Here.Info($"Ran as administrator (exit {p.ExitCode}):\n{output}");
            return new Outcome(p.ExitCode == 0, output);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new Outcome(false, T("Cancelled: Windows asked for permission and it wasn't given."));
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("ForzaWheel run as administrator", ex);
            return new Outcome(false, ex.Message);
        }
    });
}
