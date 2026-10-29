using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaisysApp.Logging;

namespace DaisysApp.Shell;

/// <summary>
/// Settings → General → Settings: Save writes every settings file of the app and its modules (everything in
/// <see cref="AppPaths.SettingsFolder"/>, which every module saves to through Core's JsonStore) into one JSON file;
/// Load puts one back. Loading only stages the file: the app restarts and <see cref="ApplyPending"/> replaces the
/// settings before anything reads them (so modules saving as they close can't write over them), keeping a copy of the
/// settings it replaced.
/// </summary>
internal static class SettingsBackup
{
    private const string Marker = "DaisysApp settings";
    private const long MaxFileSize = 2 * 1024 * 1024;
    // data the tools keep beside the settings, not settings: EQ wizard runs, preview music, Analysis's laps, Mini Mirror's crash marker
    private static readonly string[] SkippedFolders = { "Logs", "Preview music", "PluginData", "Plugins" };
    private static readonly string[] SkippedExtensions = { ".jsonl", ".capturing", ".tmp" };

    private static string LocalFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName);
    /// <summary>A file chosen with Load, put in place at the next start.</summary>
    private static string PendingPath => Path.Combine(LocalFolder, "settings-restore.json");
    /// <summary>The settings a Load replaced, in case it wasn't the file meant.</summary>
    public static string BeforeRestorePath => Path.Combine(LocalFolder, "settings-before-restore.json");

    /// <summary>Set by <see cref="ApplyPending"/>: what was loaded, for the notification bar.</summary>
    public static string? Restored { get; private set; }
    public static bool RestoreFailed { get; private set; }

    public static string DefaultFileName => $"{AppPaths.ShortName}-settings-{DateTime.Now:yyyy-MM-dd-HHmm}.json";

    /// <summary>The settings files, by their path in the settings folder ("Joy2Key/Profiles/Default.json").</summary>
    private static IEnumerable<string> SettingsFiles()
    {
        string root = AppPaths.SettingsFolder;
        if (!Directory.Exists(root)) yield break;
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            string first = rel.Contains('/') ? rel[..rel.IndexOf('/')] : "";
            if (SkippedFolders.Contains(first, StringComparer.OrdinalIgnoreCase)) continue;
            if (SkippedExtensions.Contains(Path.GetExtension(rel), StringComparer.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(rel).Contains("-GAMER11", StringComparison.OrdinalIgnoreCase)) continue; // OneDrive conflict copies
            if (new FileInfo(path).Length > MaxFileSize) continue;
            yield return rel;
        }
    }

    /// <summary>Every settings file in one JSON object: JSON files as they are, others (a key, a calibration) in base64.</summary>
    public static int Save(string target)
    {
        var files = new JsonObject();
        var other = new JsonObject();
        foreach (string rel in SettingsFiles().OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes;
            using (var stream = new FileStream(Path.Combine(AppPaths.SettingsFolder, rel), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var copy = new MemoryStream()) { stream.CopyTo(copy); bytes = copy.ToArray(); }
            JsonNode? node = null;
            if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                try { node = JsonNode.Parse(bytes); } catch (JsonException) { } // (a broken one is kept byte for byte)
            if (node != null) files[rel] = node;
            else other[rel] = Convert.ToBase64String(bytes);
        }
        var backup = new JsonObject
        {
            ["format"] = Marker,
            ["app"] = AppPaths.DisplayName,
            ["version"] = Updates.UpdateService.Display(Updates.UpdateService.CurrentVersion),
            ["computer"] = Environment.MachineName,
            ["saved"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["files"] = files,
            ["otherFiles"] = other,
        };
        string temp = target + ".tmp";
        File.WriteAllText(temp, backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, target, overwrite: true);
        int count = files.Count + other.Count;
        Log.App.Info($"Settings saved to {target} ({count} files)");
        return count;
    }

    /// <summary>Checks a saved file and stages it for the next start. Throws, with a message to show, if it isn't one.</summary>
    public static void Stage(string source)
    {
        var backup = Read(File.ReadAllText(source));
        Directory.CreateDirectory(LocalFolder);
        File.Copy(source, PendingPath, overwrite: true);
        Log.App.Info($"Settings from {source} ({backup.Files.Count} files, saved {backup.Saved} on {backup.Computer}) staged for the next start");
    }

    private sealed record Backup(Dictionary<string, byte[]> Files, string Saved, string Computer, string Version);

    private static Backup Read(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidDataException(T("That isn't a settings file saved by the app.")); }
        if ((string?)root["format"] != Marker || root["files"] is not JsonObject files)
            throw new InvalidDataException(T("That isn't a settings file saved by the app."));
        var all = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, node) in files)
            if (node != null) all[Checked(rel)] = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (root["otherFiles"] is JsonObject other)
            foreach (var (rel, node) in other)
                if (node != null) all[Checked(rel)] = Convert.FromBase64String((string)node!);
        if (all.Count == 0) throw new InvalidDataException(T("That settings file is empty."));
        return new Backup(all, (string?)root["saved"] ?? "?", (string?)root["computer"] ?? "?", (string?)root["version"] ?? "?");
    }

    /// <summary>A path inside the settings folder, never outside it.</summary>
    private static string Checked(string rel)
    {
        string full = Path.GetFullPath(Path.Combine(AppPaths.SettingsFolder, rel));
        string root = Path.GetFullPath(AppPaths.SettingsFolder).TrimEnd('\\') + "\\";
        if (Path.IsPathRooted(rel) || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(T("That isn't a settings file saved by the app."));
        return rel.Replace('\\', '/');
    }

    /// <summary>At the start, before any settings are read: a file chosen with Load replaces the settings (the ones
    /// it replaces kept in <see cref="BeforeRestorePath"/>).</summary>
    public static void ApplyPending()
    {
        if (!File.Exists(PendingPath)) return;
        try
        {
            var backup = Read(File.ReadAllText(PendingPath));
            try { Save(BeforeRestorePath); }
            catch (Exception ex) { Log.App.Warn("Couldn't keep a copy of the settings before loading the saved ones", ex); }
            // the settings folder becomes the saved one: files it didn't have go (they'd mix two sets of settings)
            foreach (string rel in SettingsFiles().ToList())
                if (!backup.Files.ContainsKey(rel))
                    try { File.Delete(Path.Combine(AppPaths.SettingsFolder, rel)); }
                    catch (Exception ex) { Log.App.Warn($"Couldn't delete {rel} while loading settings", ex); }
            foreach (var (rel, bytes) in backup.Files)
            {
                string path = Path.Combine(AppPaths.SettingsFolder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Restored = F("Settings loaded: saved {0} on {1} (version {2}).", backup.Saved, backup.Computer, backup.Version);
            Log.App.Info($"Settings loaded: {backup.Files.Count} files, saved {backup.Saved} on {backup.Computer} with {backup.Version}; the ones replaced are in {BeforeRestorePath}");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Loading saved settings", ex);
            RestoreFailed = true;
            Restored = T("Couldn't load the saved settings: ") + ex.Message;
        }
        finally
        {
            try { File.Delete(PendingPath); } catch { }
        }
    }
}
