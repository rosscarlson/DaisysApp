using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DaisysApp.Updates;

public sealed record UpdateInfo(Version Version, string Tag, string ReleaseUrl, string AssetUrl, string AssetName, long Size, string? Sha256);

/// <summary>
/// Checks GitHub Releases for a newer version, downloads its installer (verifying the SHA-256 GitHub publishes for
/// the asset) and runs it silently. The installer closes this app, upgrades in place and relaunches it.
/// </summary>
public static class UpdateService
{
    public const string Owner = "rosscarlson";
    public const string Repo = "DaisysApp";
    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";

    // Static initializers run in declaration order: CurrentVersion must come before Http (its User-Agent uses it).
    public static Version CurrentVersion { get; } = ParseVersion(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    public static string Display(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DaisysApp/{Display(CurrentVersion)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>"v1.2.3", "1.2.3-beta" or "1.2.3+sha" → 1.2.3; null if unparseable.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) text = text[..cut];
        if (!Version.TryParse(text, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>Returns the latest release if it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // no releases published yet
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version == null || version <= CurrentVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("DaisysApp-Setup", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            string? sha = asset.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : null;
            return new UpdateInfo(
                version, tag,
                root.GetProperty("html_url").GetString() ?? RepoUrl,
                asset.GetProperty("browser_download_url").GetString()!,
                name,
                asset.GetProperty("size").GetInt64(),
                sha);
        }
        return null; // newer release without an installer attached (yet)
    }

    /// <summary>Downloads the installer to %TEMP%\DaisysApp-Update and returns its path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "DaisysApp-Update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, update.AssetName);
        await DownloadToAsync(update, path, progress == null ? null : progress.Report, ct);
        return path;
    }

    /// <summary>How long a download may go without receiving anything before it's tried again from where it got to.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private const int Attempts = 4;

    /// <summary>
    /// Downloads the installer to <paramref name="path"/> and checks its SHA-256 (when GitHub gives one). A connection that
    /// stops sending for 30 seconds (or drops) is opened again and the download carries on from where it got to (an
    /// HTTP range request), up to four tries. <paramref name="progress"/> (0–1) is called on the downloading thread.
    /// </summary>
    private static async Task DownloadToAsync(UpdateInfo update, string path, Action<double>? progress, CancellationToken ct = default)
    {
        long done = 0, total = update.Size;
        if (File.Exists(path)) File.Delete(path);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, update.AssetUrl);
                if (done > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(done, null);
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                response.EnsureSuccessStatusCode();
                // a server that ignores the range starts again from the beginning
                bool resumed = done > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!resumed) done = 0;
                if (!resumed && response.Content.Headers.ContentLength is { } length) total = length;
                await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
                await using var file = new FileStream(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                int read;
                while (true)
                {
                    stall.CancelAfter(StallTimeout); // the clock starts again with each piece that arrives
                    read = await source.ReadAsync(buffer, stall.Token);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Invoke(Math.Min(1, (double)done / total));
                }
                if (total > 0 && done < total) throw new IOException($"The download ended early ({done} of {total} bytes).");
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < Attempts && ex is HttpRequestException or IOException or OperationCanceledException)
            {
                Logging.Log.App.Warn($"Update download: try {attempt} stopped at {done} of {total} bytes ({ex.Message}); trying again");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(T("The download stopped (nothing arrived for 30 seconds, four times)."));
            }
        }

        if (update.Sha256 != null)
        {
            await using var check = File.OpenRead(path);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException(T("The downloaded installer failed its integrity check."));
            }
        }
    }

    /// <summary>
    /// Starts the installer silently (Windows asks for admin approval). <paramref name="quiet"/>: no window at all
    /// (/VERYSILENT), as for a quick update; else its progress shows (/SILENT). The caller should then exit.
    /// </summary>
    public static void LaunchInstaller(string path, bool quiet = false) =>
        Process.Start(new ProcessStartInfo(path, (quiet ? "/VERYSILENT" : "/SILENT") + " /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });

    // ---------------------------------------------------------------- updating without Windows' question

    /// <summary>
    /// The installer registers two tasks for the user who installed (Task Scheduler, folder DaisysApp): "Update" runs
    /// DaisysApp.exe --install-update with the highest rights the user has, so a quick update needs no admin prompt;
    /// "Start" runs the app with ordinary rights, which the installer uses to start it again afterwards. The elevated
    /// copy only ever installs the newest release it finds on GitHub itself, so starting the task can't make it run
    /// anything else.
    /// </summary>
    public const string UpdateTaskName = @"DaisysApp\Update";

    /// <summary>What the elevated updater is doing, in {app}\updates\status.json (readable by the app, writable only by admins).</summary>
    public sealed record Status(string Stage, double Progress, string? Version, string? Error, DateTime At);

    private static string UpdatesFolder => Path.Combine(AppContext.BaseDirectory, "updates");
    private static string StatusFile => Path.Combine(UpdatesFolder, "status.json");

    /// <summary>The elevated updater's last status, or null if there's none (or it can't be read yet).</summary>
    public static Status? ReadStatus()
    {
        try
        {
            if (!File.Exists(StatusFile)) return null;
            // shared for writing and deleting, so the updater can replace it while it's being read
            using var stream = new FileStream(StatusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Status>(reader.ReadToEnd());
        }
        catch { return null; }
    }

    /// <summary>Starts the Update task; false if it isn't there (an install from before 0.44, or another user's).</summary>
    public static bool StartUpdateTask()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), $"/run /tn \"{UpdateTaskName}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (p == null) return false;
            if (!p.WaitForExit(10000)) return false;
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// DaisysApp.exe --install-update (run by the Update task, elevated): finds the newest release on GitHub, downloads
    /// its installer into {app}\updates (where only admins can write), checks its SHA-256 and starts it with no window
    /// (/VERYSILENT); the installer closes the app, upgrades it and starts it again with the Start task. Each step goes
    /// to the status file for the app to show. Returns the exit code.
    /// </summary>
    public static int RunElevatedUpdate()
    {
        try
        {
            Directory.CreateDirectory(UpdatesFolder);
            WriteStatus("checking", 0, null, null);
            var update = CheckAsync().GetAwaiter().GetResult();
            if (update == null) { WriteStatus("uptodate", 1, Display(CurrentVersion), null); return 0; }
            // only GitHub's own download links for this repository
            if (!Uri.TryCreate(update.AssetUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
                !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !url.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected download address: " + update.AssetUrl);
            foreach (var old in Directory.EnumerateFiles(UpdatesFolder, "*.exe")) { try { File.Delete(old); } catch { } }

            string version = Display(update.Version);
            string path = Path.Combine(UpdatesFolder, Path.GetFileName(update.AssetName));
            // written on the downloading thread itself, at most about three times a second: a Progress<T> ran each report
            // on its own thread pool thread, and two at once (or the app reading the file) crashed the updater (0.54)
            var last = DateTime.MinValue;
            WriteStatus("downloading", 0, version, null);
            DownloadToAsync(update, path, f =>
            {
                if ((DateTime.UtcNow - last).TotalMilliseconds < 300) return;
                last = DateTime.UtcNow;
                WriteStatus("downloading", f, version, null);
            }).GetAwaiter().GetResult();
            WriteStatus("installing", 1, version, null);
            Logging.Log.App.Info($"Quick update: installing {version} from {path}");
            Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FROMTASK") { UseShellExecute = false, WorkingDirectory = UpdatesFolder });
            return 0;
        }
        catch (Exception ex)
        {
            Logging.Log.App.Error("Quick update failed", ex);
            try { WriteStatus("failed", 0, null, ex.Message); } catch { }
            return 1;
        }
    }

    /// <summary>
    /// Writes the status file (a temporary file moved over it, so the app never reads half of one). The app may have it
    /// open just then, so it's tried a few times; a status that can't be written is skipped, never fatal: the next one
    /// gets through.
    /// </summary>
    private static readonly object statusGate = new();

    private static void WriteStatus(string stage, double progress, string? version, string? error)
    {
        lock (statusGate)
        {
            string temp = StatusFile + ".tmp";
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    File.WriteAllText(temp, JsonSerializer.Serialize(new Status(stage, progress, version, error, DateTime.UtcNow)));
                    File.Move(temp, StatusFile, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(40);
                }
            }
        }
    }

    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
