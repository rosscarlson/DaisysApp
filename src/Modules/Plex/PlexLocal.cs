using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using DaisysApp.Logging;
using Microsoft.Win32;

namespace DaisysApp.Applets.Plex;

/// <summary>
/// Plex Media Server on this PC, if it's here: its folders (from the registry: the data folder is
/// LocalAppDataPath\Plex Media Server, %LOCALAPPDATA% by default), its program (the running one's, else InstallFolder,
/// else Program Files), Plex SQLite beside it, its token, and stopping and starting it. Tautulli's config.ini too, to
/// fill in its address and API key.
/// </summary>
public static class PlexLocal
{
    private const string Key = @"Software\Plex, Inc.\Plex Media Server";
    public const string ServerExe = "Plex Media Server.exe";
    public const string LibraryDb = "com.plexapp.plugins.library.db";
    public const string BlobsDb = "com.plexapp.plugins.library.blobs.db";

    private static string? Reg(string name, bool machine = false)
    {
        try
        {
            using var k = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(Key);
            return k?.GetValue(name) as string;
        }
        catch { return null; }
    }

    /// <summary>Plex Media Server has been set up for this Windows account.</summary>
    public static bool Installed => DataFolder is { } d && Directory.Exists(d);

    /// <summary>The server's data folder ("Plex Media Server": Logs, Cache, Plug-in Support…).</summary>
    public static string? DataFolder
    {
        get
        {
            string? parent = Reg("LocalAppDataPath");
            if (string.IsNullOrWhiteSpace(parent)) parent = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string d = Path.Combine(parent, "Plex Media Server");
            return Directory.Exists(d) ? d : null;
        }
    }

    public static string? DatabaseFolder => DataFolder is { } d ? Path.Combine(d, "Plug-in Support", "Databases") : null;
    public static string? LogFolder => DataFolder is { } d ? Path.Combine(d, "Logs") : null;
    public static string? ServerLog => LogFolder is { } d ? Path.Combine(d, "Plex Media Server.log") : null;
    public static string? CacheFolder => DataFolder is { } d ? Path.Combine(d, "Cache") : null;
    public static string? LibraryPath => DatabaseFolder is { } d ? Path.Combine(d, LibraryDb) : null;
    public static string? BlobsPath => DatabaseFolder is { } d ? Path.Combine(d, BlobsDb) : null;

    /// <summary>The token Plex keeps for the account it's signed in to (its owner's).</summary>
    public static string? Token => Reg("PlexOnlineToken") is { Length: > 0 } t ? t : null;

    /// <summary>The server's identifier (what /identity answers), to know the server set up is this PC's.</summary>
    public static string? MachineIdentifier => Reg("ProcessedMachineIdentifier");

    /// <summary>The folder Plex Media Server.exe is in: the running one's, else the installer's, else Program Files'.</summary>
    public static string? ProgramFolder
    {
        get
        {
            try
            {
                foreach (int pid in ServerPids())
                    using (var p = Process.GetProcessById(pid))
                        if (p.MainModule?.FileName is { } f) return Path.GetDirectoryName(f);
            }
            catch { }
            foreach (var d in new[]
            {
                Reg("InstallFolder"), Reg("InstallFolder", machine: true),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Plex", "Plex Media Server"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Plex", "Plex Media Server"),
                DataFolder,
            })
                if (!string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d, ServerExe))) return d;
            return null;
        }
    }

    /// <summary>Plex's own SQLite (with its tokenizers and collations, which the database needs), or null.</summary>
    public static string? Sqlite => ProgramFolder is { } d && File.Exists(Path.Combine(d, "Plex SQLite.exe")) ? Path.Combine(d, "Plex SQLite.exe") : null;

    /// <summary>The server is running (not counting the Plex Media Server.exe that Plex SQLite runs to do its work).</summary>
    public static bool Running => ServerPids().Count > 0;

    /// <summary>The server's processes: every Plex Media Server.exe but those Plex SQLite started.</summary>
    public static List<int> ServerPids()
    {
        var all = new Dictionary<int, (int Parent, string Exe)>();
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == new IntPtr(-1)) return [];
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                all[(int)e.th32ProcessID] = ((int)e.th32ParentProcessID, e.szExeFile);
        }
        finally { CloseHandle(snap); }
        return all.Where(p => p.Value.Exe.Equals(ServerExe, StringComparison.OrdinalIgnoreCase)
                && !(all.TryGetValue(p.Value.Parent, out var parent) && parent.Exe.Equals("Plex SQLite.exe", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Key).ToList();
    }

    /// <summary>
    /// Closes Plex Media Server as its tray icon's Exit does (taskkill without /F sends it WM_CLOSE), waiting up to
    /// <paramref name="seconds"/>; then ends it and what it started. True once it's gone. (By its process ids: a
    /// scheduled backup's Plex SQLite runs a Plex Media Server.exe too.)
    /// </summary>
    public static async Task<bool> StopAsync(int seconds = 45)
    {
        var pids = ServerPids();
        if (pids.Count == 0) return true;
        Log.Here.Info("Closing Plex Media Server");
        foreach (int pid in pids) await Run("taskkill.exe", "/PID", pid.ToString());
        for (int i = 0; i < seconds * 2 && ServerPids().Count > 0; i++) await Task.Delay(500);
        if (ServerPids() is { Count: > 0 } left)
        {
            Log.Here.Warn("Plex Media Server didn't close: ending it");
            foreach (int pid in left) await Run("taskkill.exe", "/F", "/T", "/PID", pid.ToString());
            for (int i = 0; i < 20 && ServerPids().Count > 0; i++) await Task.Delay(500);
        }
        // its helpers can hold the database open a moment longer
        foreach (var helper in new[] { "Plex Media Scanner", "PlexScriptHost", "Plex Tuner Service" })
            try { foreach (var p in Process.GetProcessesByName(helper)) using (p) p.Kill(); } catch { }
        await Task.Delay(1500);
        return ServerPids().Count == 0;
    }

    private const uint TH32CS_SNAPPROCESS = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    /// <summary>Starts Plex Media Server (from <paramref name="folder"/>, as found before it was stopped).</summary>
    public static bool Start(string? folder = null)
    {
        folder ??= ProgramFolder;
        if (folder == null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(folder, ServerExe)) { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
            Log.Here.Info("Started Plex Media Server");
            return true;
        }
        catch (Exception ex) { Log.Here.Error("Couldn't start Plex Media Server", ex); return false; }
    }

    /// <summary>Runs a program with no window; its exit code and output (stdout, then stderr).</summary>
    public static async Task<(int Code, string Output)> Run(string exe, params string[] args) => await Run(exe, null, args);

    public static async Task<(int Code, string Output)> Run(string exe, CancellationToken? cancel, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start " + exe);
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync(cancel ?? CancellationToken.None); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        string text = (await output).Trim();
        string err = (await error).Trim();
        return (p.ExitCode, err.Length > 0 ? (text.Length > 0 ? text + "\n" : "") + err : text);
    }

    /// <summary>Runs Plex SQLite on <paramref name="db"/> with each of <paramref name="commands"/> in turn.</summary>
    public static async Task<(int Code, string Output)> Sql(string db, CancellationToken cancel, params string[] commands)
    {
        string exe = Sqlite ?? throw new PlexException(T("Plex SQLite wasn't found (it comes with Plex Media Server)."));
        return await Run(exe, cancel, [db, .. commands]);
    }

    /// <summary>A path for a dot-command (in single quotes, which Plex SQLite takes as they are).</summary>
    public static string Quote(string path)
    {
        if (path.Contains('\'')) throw new PlexException(F("A path with ' in it can't be used: {0}", path));
        return "'" + path.Replace('\\', '/') + "'";
    }

    /// <summary>The size of a folder's files (0 if it isn't there).</summary>
    public static long FolderSize(string? folder)
    {
        if (folder == null || !Directory.Exists(folder)) return 0;
        long n = 0;
        try
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                n += f.Length;
        }
        catch { }
        return n;
    }

    // ---------------------------------------------------------------- Tautulli

    /// <summary>Tautulli's address and API key from its config.ini on this PC, if it's here and its API is on.</summary>
    public static (string Address, string Key)? Tautulli()
    {
        foreach (var file in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tautulli", "config.ini"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tautulli", "config.ini"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tautulli", "config.ini"),
        })
        {
            if (!File.Exists(file)) continue;
            try
            {
                var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadLines(file))
                {
                    int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                    if (eq > 0) v.TryAdd(line[..eq].Trim(), line[(eq + 3)..].Trim().Trim('"'));
                }
                if (!v.TryGetValue("api_key", out var key) || key.Length == 0) continue;
                string scheme = v.GetValueOrDefault("enable_https") == "1" ? "https" : "http";
                string port = v.GetValueOrDefault("http_port") is { Length: > 0 } p ? p : "8181";
                string root = v.GetValueOrDefault("http_root") is { Length: > 0 } r ? "/" + r.Trim('/') : "";
                return ($"{scheme}://127.0.0.1:{port}{root}", key);
            }
            catch (Exception ex) { Log.Here.Debug(() => $"Couldn't read {file}: {ex.Message}"); }
        }
        return null;
    }
}
