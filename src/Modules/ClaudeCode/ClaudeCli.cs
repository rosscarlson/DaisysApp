using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.ClaudeCode;

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
                        else if (isUser && r.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                            && !(r.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True))
                            first = c.GetString();
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
}
