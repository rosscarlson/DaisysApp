using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.AI;

/// <summary>
/// The Claude Code CLI on this PC: where claude.exe is, who's signed in (claude auth status), signing in (claude auth
/// login, in a console window: it opens the browser), and the sessions it has saved (~/.claude/projects/&lt;folder&gt;/&lt;id&gt;.jsonl),
/// for a tab's history and for resuming one.
/// </summary>
internal static class ClaudeCli
{
    public const string InstallUrl = "https://docs.claude.com/en/docs/claude-code/setup";

    /// <summary>claude.exe (or claude.cmd from npm): the one set in settings, ~/.local/bin, then PATH.</summary>
    public static string? Find(string? custom)
    {
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var p in new[] { Path.Combine(home, ".local", "bin", "claude.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd") })
            if (File.Exists(p)) return p;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                try { string p = Path.Combine(dir.Trim(), name); if (File.Exists(p)) return p; } catch { }
            }
        return null;
    }

    /// <summary>A ProcessStartInfo for claude with these arguments (a .cmd through cmd.exe).</summary>
    public static ProcessStartInfo StartInfo(string exe, IEnumerable<string> args, string? folder = null)
    {
        var info = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", exe } }
            : new ProcessStartInfo(exe);
        foreach (var a in args) info.ArgumentList.Add(a);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        if (folder != null) info.WorkingDirectory = folder;
        return info;
    }

    public sealed record Account(bool LoggedIn, string? Email, string? Method, string? Org);

    /// <summary>"claude auth status": signed in, as whom. Null if it couldn't be asked.</summary>
    public static async Task<Account?> StatusAsync(string exe)
    {
        try
        {
            var info = StartInfo(exe, new[] { "auth", "status" });
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(15000)) { try { p.Kill(true); } catch { } return null; }
            string text = await output;
            int start = text.IndexOf('{');
            if (start < 0) return new Account(false, null, null, null);
            using var doc = JsonDocument.Parse(text[start..]);
            var r = doc.RootElement;
            string? S(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new Account(r.TryGetProperty("loggedIn", out var l) && l.ValueKind == JsonValueKind.True, S("email"), S("authMethod"), S("orgName"));
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("claude auth status: " + ex.Message);
            return null;
        }
    }

    /// <summary>"claude auth login" (or logout) in a console window of its own: it opens the browser to sign in.</summary>
    public static void Auth(string exe, bool login)
    {
        string verb = login ? "login" : "logout";
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{exe}\" auth {verb}\"") { UseShellExecute = true });
    }

    /// <summary>"claude auth login" for another account: a Claude subscription or the Anthropic Console (--console),
    /// the email filled in on the sign-in page if it's given.</summary>
    public static void Login(string exe, bool console, string? email)
    {
        string args = "auth login" + (console ? " --console" : "") + (email is { Length: > 0 } e && Regex.IsMatch(e, @"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+$") ? $" --email {e}" : "");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{exe}\" {args}\"") { UseShellExecute = true });
    }

    /// <summary>~/.claude (or CLAUDE_CONFIG_DIR)/projects</summary>
    public static string ProjectsFolder =>
        Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "projects");

    /// <summary>The folder Claude Code keeps a working folder's sessions in (every character but letters and digits as -).</summary>
    public static string ProjectFolderFor(string folder) =>
        Path.Combine(ProjectsFolder, Regex.Replace(Path.GetFullPath(folder).TrimEnd('\\'), "[^A-Za-z0-9]", "-"));

    /// <summary>A session's transcript, wherever it is.</summary>
    public static string? TranscriptOf(SessionInfo s)
    {
        try
        {
            string direct = Path.Combine(ProjectFolderFor(s.Folder), s.Id + ".jsonl");
            if (File.Exists(direct)) return direct;
            if (!Directory.Exists(ProjectsFolder)) return null;
            return Directory.EnumerateFiles(ProjectsFolder, s.Id + ".jsonl", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>A transcript's conversation: the user's and Claude's messages, in order (not the bookkeeping lines).</summary>
    public static List<JsonElement> ReadTranscript(string path)
    {
        var list = new List<JsonElement>();
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length < 2) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                string type = r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (type is not ("user" or "assistant")) continue;
                if (r.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) continue;
                if (r.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) continue;
                list.Add(r.Clone());
            }
            catch { }
        }
        return list;
    }

    /// <summary>What a pasted session reference is: a session id here ("claude --resume &lt;id&gt;", or the id), or one on
    /// claude.ai/code to bring here (a claude.ai/code link, a session_… id, or "claude --teleport &lt;x&gt;").</summary>
    public static (string? Local, string? Remote) ParseReference(string text)
    {
        text = text.Trim().Trim('"', '\'');
        var url = Regex.Match(text, @"https?://\S*claude\.ai/code\S*");
        if (url.Success) return (null, url.Value.TrimEnd('.', ',', ')'));
        var tele = Regex.Match(text, @"--teleport\s+(\S+)");
        if (tele.Success) return (null, tele.Groups[1].Value);
        var remote = Regex.Match(text, @"\bsession_[A-Za-z0-9]+\b");
        if (remote.Success) return (null, remote.Value);
        var id = Regex.Match(text, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");
        return id.Success ? (id.Value.ToLowerInvariant(), null) : (null, null);
    }

    /// <summary>A session's transcript on this PC, by its id, in whichever folder's sessions it is.</summary>
    public static string? FindTranscript(string id)
    {
        try
        {
            return Directory.Exists(ProjectsFolder) ? Directory.EnumerateFiles(ProjectsFolder, id + ".jsonl", SearchOption.AllDirectories).FirstOrDefault() : null;
        }
        catch { return null; }
    }

    /// <summary>The folder a transcript's session worked in (its "cwd"), and its name, from its first lines.</summary>
    public static (string? Folder, string? Title) Describe(string transcript)
    {
        string? folder = null, title = null;
        try
        {
            using var reader = new StreamReader(new FileStream(transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (int n = 0; n < 2000 && reader.ReadLine() is { } line; n++)
            {
                if (folder == null && line.Contains("\"cwd\""))
                    try { using var d = JsonDocument.Parse(line); if (d.RootElement.TryGetProperty("cwd", out var c)) folder = c.GetString(); } catch { }
                if (line.Contains("\"custom-title\""))
                    try { using var d = JsonDocument.Parse(line); if (d.RootElement.TryGetProperty("customTitle", out var t)) title = t.GetString(); } catch { }
            }
        }
        catch { }
        return (folder, title);
    }

    /// <summary>
    /// A session as a file to take to another PC: its transcript (and its agents' transcripts, if any) and what the tab
    /// knows of it (name, folder, model, permissions), zipped.
    /// </summary>
    public static void Export(SessionInfo s, string transcript, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
        zip.CreateEntryFromFile(transcript, s.Id + ".jsonl");
        string extra = Path.Combine(Path.GetDirectoryName(transcript)!, s.Id);
        if (Directory.Exists(extra))
            foreach (var f in Directory.EnumerateFiles(extra, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(f, s.Id + "/" + Path.GetRelativePath(extra, f).Replace('\\', '/'));
        var entry = zip.CreateEntry("session.json");
        using var w = new StreamWriter(entry.Open());
        w.Write(JsonSerializer.Serialize(new { s.Id, s.Name, s.Folder, s.Model, s.Mode, Exported = DateTime.Now, From = Environment.MachineName }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>What an exported session file holds: its id, name, the folder it worked in there, model and permissions.</summary>
    public sealed record Exported(string Id, string Name, string Folder, string Model, string Mode, string? From);

    public static Exported ReadExport(string zipPath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var meta = zip.GetEntry("session.json") ?? throw new InvalidDataException(T("That isn't a session file exported from here."));
        using var r = new StreamReader(meta.Open());
        using var doc = JsonDocument.Parse(r.ReadToEnd());
        var e = doc.RootElement;
        string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        if (zip.GetEntry(S("Id") + ".jsonl") == null) throw new InvalidDataException(T("The session file has no conversation in it."));
        return new Exported(S("Id"), S("Name"), S("Folder"), S("Model"), S("Mode"), S("From"));
    }

    /// <summary>Puts an exported session where Claude Code looks for <paramref name="folder"/>'s sessions (the one there
    /// already, if any, kept as .bak), so it can be resumed in that folder.</summary>
    public static void Import(string zipPath, string folder)
    {
        var x = ReadExport(zipPath);
        string dir = ProjectFolderFor(folder);
        Directory.CreateDirectory(dir);
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName == "session.json" || entry.FullName.EndsWith("/")) continue;
            string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue; // nothing outside the folder
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) File.Copy(target, target + ".bak", true);
            entry.ExtractToFile(target, true);
        }
        Logging.Log.Here.Info($"Claude Code session {x.Id} imported from {zipPath} (from {x.From}) into {dir}");
    }

    /// <summary>A session saved for this folder, to resume: its id, name (or its first message) and when it was last used.</summary>
    public sealed record Saved(string Id, string Title, DateTime When);

    public static List<Saved> SavedSessions(string folder, int max = 30)
    {
        var result = new List<Saved>();
        try
        {
            string dir = ProjectFolderFor(folder);
            if (!Directory.Exists(dir)) return result;
            foreach (var file in new DirectoryInfo(dir).GetFiles("*.jsonl").OrderByDescending(f => f.LastWriteTime).Take(max))
            {
                string? title = null, first = null;
                using var reader = new StreamReader(new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                for (int n = 0; n < 400 && reader.ReadLine() is { } line; n++)
                {
                    bool isTitle = line.Contains("\"custom-title\""), isUser = first == null && line.Contains("\"type\":\"user\"");
                    if (!isTitle && !isUser) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var r = doc.RootElement;
                        if (isTitle && r.TryGetProperty("customTitle", out var ct)) title = ct.GetString();
                        else if (isUser && r.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c)
                            && !(r.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True))
                        {
                            // (a message with images: its text)
                            if (c.ValueKind == JsonValueKind.String) first = c.GetString();
                            else if (c.ValueKind == JsonValueKind.Array)
                                first = c.EnumerateArray().Where(b => b.TryGetProperty("type", out var bt) && bt.GetString() == "text" && b.TryGetProperty("text", out _))
                                    .Select(b => b.GetProperty("text").GetString()).FirstOrDefault(t => t is { Length: > 0 } && SessionView.Visible(t));
                            if (first != null) first = SessionView.WithoutReadFirst(first);
                        }
                    }
                    catch { }
                }
                string label = title ?? first ?? Path.GetFileNameWithoutExtension(file.Name);
                if (label.Length > 80) label = label[..80] + "…";
                result.Add(new Saved(Path.GetFileNameWithoutExtension(file.Name), label.Replace('\n', ' '), file.LastWriteTime));
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Listing Claude Code sessions: " + ex.Message); }
        return result;
    }

    /// <summary>Deletes a saved session of a folder: its transcript, and its agents' (the folder beside it).</summary>
    public static void DeleteSession(string folder, string id)
    {
        string dir = ProjectFolderFor(folder);
        string file = Path.Combine(dir, id + ".jsonl");
        if (File.Exists(file)) File.Delete(file);
        string side = Path.Combine(dir, id);
        if (Directory.Exists(side)) Directory.Delete(side, true);
        Logging.Log.Here.Info($"Claude Code session {id} deleted ({folder})");
    }
}
