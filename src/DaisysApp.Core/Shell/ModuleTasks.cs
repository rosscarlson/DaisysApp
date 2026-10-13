using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace DaisysApp.Shell;

/// <summary>
/// A job a module runs in a second copy of DaisysApp.exe, usually with administrator rights the app itself doesn't
/// have (e.g. Tools → Disk Usage reading a drive's file table). The class needs a public parameterless constructor; the
/// second copy creates it, calls <see cref="Run"/> with the arguments and exits with what it returns, showing no
/// window. Results go back through a file the arguments name.
/// </summary>
public interface IModuleTask
{
    int Run(string[] args);
}

/// <summary>Runs an <see cref="IModuleTask"/> in a second copy of the app (see <see cref="RunAsync"/>).</summary>
public static class ModuleTasks
{
    /// <summary>The first argument of a copy started for a task: DaisysApp.exe --module-task &lt;module folder&gt; &lt;class&gt; args…</summary>
    public const string Argument = "--module-task";

    /// <summary>Whether this copy of the app runs with administrator rights.</summary>
    public static bool IsElevated { get; } = CheckElevated();

    private static bool CheckElevated()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    /// <summary>
    /// Starts DaisysApp.exe to run <typeparamref name="T"/> (a class in a module) with these arguments, as administrator
    /// when <paramref name="elevated"/> (Windows asks first, unless the app already is), and waits for it. Returns its
    /// exit code, or null when the user said no to Windows' question. Cancelling stops waiting (not the task).
    /// </summary>
    public static async Task<int?> RunAsync<T>(IEnumerable<string> args, bool elevated, CancellationToken cancel = default) where T : IModuleTask
    {
        var type = typeof(T);
        string folder = Path.GetFileName(Path.GetDirectoryName(type.Assembly.Location)) ?? "";
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("No program path"))
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = elevated && !IsElevated ? "runas" : "",
        };
        string line = string.Join(" ", new[] { Argument, folder, type.FullName! }.Concat(args).Select(Quote));
        psi.Arguments = line;
        Process? p;
        try { p = Process.Start(psi); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; } // ERROR_CANCELLED: the user said no
        if (p == null) return null;
        using (p)
        {
            await p.WaitForExitAsync(cancel);
            return p.ExitCode;
        }
    }

    private static string Quote(string a)
    {
        if (a.Length > 0 && !a.Any(c => c is ' ' or '\t' or '"')) return a;
        string t = a.Replace("\"", "\\\"");
        if (t.EndsWith('\\')) t += "\\";
        return "\"" + t + "\"";
    }

    /// <summary>
    /// For the app's startup: runs the task named in <paramref name="args"/> (a copy started by <see cref="RunAsync"/>) and
    /// returns its exit code. Only a module installed next to the app can be named.
    /// </summary>
    public static int RunHere(string[] args)
    {
        if (args.Length < 3) return 90;
        string folder = args[1], typeName = args[2];
        if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder.Contains("..")) return 91;
        string dll = Path.Combine(AppContext.BaseDirectory, "modules", folder, $"DaisysApp.{folder}.dll");
        if (!File.Exists(dll)) return 92;
        try
        {
            var asm = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
            var type = asm.GetType(typeName, throwOnError: false);
            if (type == null || !typeof(IModuleTask).IsAssignableFrom(type)) return 93;
            var task = (IModuleTask)Activator.CreateInstance(type)!;
            return task.Run(args.Skip(3).ToArray());
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write($"Module task {typeName}", ex is TargetInvocationException { InnerException: { } inner } ? inner : ex);
            return 99;
        }
    }
}
