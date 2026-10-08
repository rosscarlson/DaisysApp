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

        using (var response = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
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
        return path;
    }

    /// <summary>Starts the installer silently (Windows asks for admin approval). The caller should then exit.</summary>
    public static void LaunchInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });

    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
