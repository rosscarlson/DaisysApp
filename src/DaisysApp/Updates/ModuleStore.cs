using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Updates;

/// <summary>One module in a release's list (Modules-1.4.0.json): what the wrapper shows and downloads.</summary>
public sealed class ModuleListing
{
    /// <summary>The module's folder name (modules\&lt;Name&gt;\).</summary>
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    /// <summary>The tools in it, when there are several (or it shares a tab: the tab's name).</summary>
    public List<string> Tools { get; set; } = new();
    public string? Tab { get; set; }
    /// <summary>Its zip on the release (Module-Joy2Key-1.4.0.zip), its size and SHA-256.</summary>
    public string Asset { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    /// <summary>Where to download it from (filled in from the release, not in the file).</summary>
    public string? Url { get; set; }
}

/// <summary>A release's list of modules for the wrapper.</summary>
public sealed class ModuleCatalogFile
{
    public int Format { get; set; } = 1;
    public string Version { get; set; } = "";
    public List<ModuleListing> Modules { get; set; } = new();
}

/// <summary>
/// The wrapper's modules: it starts with none and downloads the ones picked (ModulePicker) from the release of its own
/// version: each a zip (Module-&lt;Name&gt;-x.y.z.zip) listed with its SHA-256 in Modules-x.y.z.json, both built from the
/// same source as Daisy's App (build.ps1, src/Modules/modules.json). They're unpacked into
/// %LOCALAPPDATA%\Wrapper\modules\ (<see cref="AppPaths.UserModulesFolder"/>). A module in use can't be replaced, so
/// changes go to a pending folder and are put in place at the next start (<see cref="ApplyPending"/>), before anything
/// is loaded. Each module records the version it came with; after the wrapper updates, the modules from its new version
/// are fetched the same way, so the program and its modules always match.
/// </summary>
public static class ModuleStore
{
    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.ShortName}/{UpdateService.Display(UpdateService.CurrentVersion)}");
        return http;
    }

    private static string Root => Path.GetDirectoryName(AppPaths.UserModulesFolder)!;
    private static string PendingFolder => Path.Combine(Root, "pending");
    private const string VersionFile = "module-version.txt";
    private const string RemoveSuffix = ".remove";

    /// <summary>The program's version, as releases are tagged (1.4.0).</summary>
    public static string AppVersion => UpdateService.Display(UpdateService.CurrentVersion);

    /// <summary>The modules downloaded (folder names), with the version each came with ("" if unknown).</summary>
    public static Dictionary<string, string> Installed()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(AppPaths.UserModulesFolder)) return result;
        foreach (var dir in Directory.GetDirectories(AppPaths.UserModulesFolder))
        {
            string file = Path.Combine(dir, VersionFile);
            result[Path.GetFileName(dir)] = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        }
        return result;
    }

    /// <summary>Downloaded modules from another version than the program's: fetched again for this one.</summary>
    public static List<string> OutOfDate() => Installed().Where(p => p.Value != AppVersion).Select(p => p.Key).ToList();

    /// <summary>Whether there are changes waiting for the next start.</summary>
    public static bool HasPending => Directory.Exists(PendingFolder) && Directory.EnumerateFileSystemEntries(PendingFolder).Any();

    /// <summary>At start, before the modules load: puts downloaded modules in place and removes the ones taken off.</summary>
    public static void ApplyPending()
    {
        if (!Directory.Exists(PendingFolder)) return;
        Directory.CreateDirectory(AppPaths.UserModulesFolder);
        foreach (var marker in Directory.GetFiles(PendingFolder, "*" + RemoveSuffix))
        {
            string name = Path.GetFileName(marker)[..^RemoveSuffix.Length];
            try
            {
                string target = Path.Combine(AppPaths.UserModulesFolder, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                File.Delete(marker);
                Log.App.Info($"Module {name} removed");
            }
            catch (Exception ex) { ErrorLog.Write($"Removing module {name}", ex); }
        }
        foreach (var dir in Directory.GetDirectories(PendingFolder))
        {
            string name = Path.GetFileName(dir);
            try
            {
                string target = Path.Combine(AppPaths.UserModulesFolder, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(dir, target);
                Log.App.Info($"Module {name} installed ({File.ReadAllText(Path.Combine(target, VersionFile)).Trim()})");
            }
            catch (Exception ex) { ErrorLog.Write($"Installing module {name}", ex); }
        }
    }

    /// <summary>For trying a build before it's released: a folder with Modules-x.y.z.json and the zips (build.ps1's artifacts).</summary>
    private static string? LocalSource => Environment.GetEnvironmentVariable("DAISYSAPP_MODULES_FROM") is { Length: > 0 } dir && Directory.Exists(dir) ? dir : null;

    /// <summary>The list of modules for this version of the program, from its release.</summary>
    public static async Task<List<ModuleListing>> FetchAsync(CancellationToken ct = default)
    {
        if (LocalSource is { } local)
        {
            var listed = JsonSerializer.Deserialize<ModuleCatalogFile>(await File.ReadAllTextAsync(Path.Combine(local, $"Modules-{AppVersion}.json"), ct)) ?? new();
            foreach (var m in listed.Modules) m.Url = Path.Combine(local, m.Asset);
            Log.App.Info($"Modules listed from {local} (DAISYSAPP_MODULES_FROM)");
            return listed.Modules.Where(m => File.Exists(m.Url)).ToList();
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{UpdateService.Owner}/{UpdateService.Repo}/releases/tags/v{AppVersion}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(F("There's no release {0} to download modules from.", AppVersion));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString() ?? "", a => a.GetProperty("browser_download_url").GetString() ?? "", StringComparer.OrdinalIgnoreCase);
        if (!assets.TryGetValue($"Modules-{AppVersion}.json", out var listUrl))
            throw new InvalidOperationException(F("Release {0} has no list of modules.", AppVersion));
        var file = JsonSerializer.Deserialize<ModuleCatalogFile>(await Http.GetStringAsync(listUrl, ct)) ?? new();
        foreach (var m in file.Modules) m.Url = assets.GetValueOrDefault(m.Asset);
        return file.Modules.Where(m => m.Url != null).ToList();
    }

    /// <summary>
    /// Downloads <paramref name="install"/> (checking each one's SHA-256) and marks <paramref name="remove"/> to be taken
    /// off; both take effect at the next start. <paramref name="progress"/>: what's being done, and how far (0–1).
    /// </summary>
    public static async Task StageAsync(IReadOnlyList<ModuleListing> install, IEnumerable<string> remove, IProgress<(string Text, double Done)>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(PendingFolder);
        foreach (var name in remove)
        {
            string staged = Path.Combine(PendingFolder, name);
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            await File.WriteAllTextAsync(Path.Combine(PendingFolder, name + RemoveSuffix), "", ct);
        }
        long total = Math.Max(1, install.Sum(m => m.Size)), before = 0;
        foreach (var m in install)
        {
            string zip = Path.Combine(PendingFolder, m.Asset);
            string title = Any(m.Title);
            progress?.Report((F("Downloading {0}…", title), (double)before / total));
            if (LocalSource != null) File.Copy(m.Url!, zip, true);
            else using (var response = await Http.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report((F("Downloading {0}…", title), (double)(before + Math.Min(done, m.Size)) / total));
                }
            }
            await using (var check = File.OpenRead(zip))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
                if (!actual.Equals(m.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(F("{0} failed its integrity check: try again.", title));
            }
            string dir = Path.Combine(PendingFolder, m.Name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            ZipFile.ExtractToDirectory(zip, dir);
            File.Delete(zip);
            await File.WriteAllTextAsync(Path.Combine(dir, VersionFile), AppVersion, ct);
            File.Delete(Path.Combine(PendingFolder, m.Name + RemoveSuffix));
            Log.App.Info($"Module {m.Name} {AppVersion} downloaded ({m.Size} bytes): in place at the next start");
            before += m.Size;
        }
        progress?.Report((T("Done"), 1));
    }

    /// <summary>"1.2 MB"</summary>
    public static string Size(long bytes) => bytes >= 1 << 20 ? F("{0:0.0} MB", bytes / 1048576.0) : F("{0:0} KB", Math.Max(1, bytes / 1024.0));

    /// <summary>"DaisysApp.exe --write-catalog &lt;modules&gt; &lt;zips&gt; &lt;version&gt; &lt;out.json&gt; [names…]" (build.ps1): the list of
    /// modules for a release, from the built modules (what each is, from its DLL) and their zips (size, SHA-256).</summary>
    public static void WriteCatalog(string modulesRoot, string zipFolder, string version, string output, IReadOnlyCollection<string> only)
    {
        var file = new ModuleCatalogFile { Version = version };
        foreach (var (module, applets, tabs) in AppletCatalog.Describe(modulesRoot))
        {
            if (only.Count > 0 && !only.Contains(module, StringComparer.OrdinalIgnoreCase)) continue;
            string asset = $"Module-{module}-{version}.zip";
            string zip = Path.Combine(zipFolder, asset);
            if (!File.Exists(zip) || applets.Count == 0) continue;
            var main = applets.OrderBy(a => a.Order).First();
            file.Modules.Add(new ModuleListing
            {
                Name = module,
                Title = applets.Count == 1 ? main.Title : module,
                Description = string.Join(" ", applets.Select(a => a.Description).Where(d => d.Length > 0).Distinct()),
                Icon = main.Icon,
                Tools = applets.Count > 1 ? applets.Select(a => a.Title).ToList() : new(),
                Tab = tabs.FirstOrDefault(t => applets.Any(a => a.Group == t.Id))?.Title,
                Asset = asset,
                Size = new FileInfo(zip).Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))),
            });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
