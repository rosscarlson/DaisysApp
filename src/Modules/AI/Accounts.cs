using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DaisysApp.Applets.AI;

/// <summary>The kinds of account (Settings → AI → Accounts): each a way of signing in that more than one account can be
/// kept for, picked for each session. Only Claude so far; the API keys of other AIs are Providers.</summary>
internal static class AccountKinds
{
    /// <summary>A Claude account for Claude Code: a subscription (Pro, Max, Team) or an Anthropic Console sign-in.</summary>
    public const string Claude = "claude";

    /// <summary>Every kind, in the order Add an account… lists them: its id, name and what it is.</summary>
    public static (string Id, string Name, string About)[] All => new[]
    {
        (Claude, T("Claude (for Claude Code)"), T("The account Claude Code signs in with: a Claude subscription (Pro, Max, Team) or the Anthropic Console. Each Claude Code session picks one when it starts.")),
    };

    public static string NameOf(string kind) => All.FirstOrDefault(k => k.Id == kind).Name ?? kind;
}

/// <summary>An account kept here: its kind, its name here, who it is, and its sign-in (kept encrypted for this Windows
/// user) to put back when it's switched to.</summary>
public sealed class AiAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>AccountKinds (only "claude" so far).</summary>
    public string Kind { get; set; } = AccountKinds.Claude;
    /// <summary>What it's called here (empty: its email).</summary>
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Org { get; set; } = "";
    /// <summary>Its plan ("max", "pro"…), as the sign-in says.</summary>
    public string Plan { get; set; } = "";
    /// <summary>The account's own id (Claude's accountUuid), to know it again.</summary>
    public string Uuid { get; set; } = "";
    /// <summary>Claude: .credentials.json, and the oauthAccount and primaryApiKey from .claude.json, as JSON; encrypted.</summary>
    public string SignInProtected { get; set; } = "";
    /// <summary>When its sign-in was last kept (it's kept again each time it's checked, and before switching away).</summary>
    public DateTime? Kept { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => Name.Trim() is { Length: > 0 } n ? n : Email.Length > 0 ? Email : T("Claude account");

    /// <summary>"Name (email · plan)", or less when there's less to say.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Description
    {
        get
        {
            var more = new List<string>();
            if (Name.Trim().Length > 0 && Email.Length > 0) more.Add(Email);
            if (Plan.Length > 0) more.Add(char.ToUpperInvariant(Plan[0]) + Plan[1..]);
            if (Org.Length > 0 && !Org.Contains(Email, StringComparison.OrdinalIgnoreCase)) more.Add(Org);
            return more.Count == 0 ? Label : $"{Label} ({string.Join(" · ", more)})";
        }
    }
}

/// <summary>
/// Claude accounts for Claude Code. Claude Code keeps one sign-in, in its folder (~/.claude, or CLAUDE_CONFIG_DIR):
/// .credentials.json (the tokens) and, in .claude.json, oauthAccount (who it is) and primaryApiKey (a Console
/// sign-in). Each account's copy of those is kept here, encrypted. A session on the account Claude Code is signed in
/// to runs as a terminal's would; a session on another runs with the account's own folder as CLAUDE_CONFIG_DIR
/// (<see cref="FolderFor"/>), so sessions on different accounts run side by side and Claude Code's own sign-in is never
/// changed. That folder has the account's sign-in and .claude.json, copies of Claude Code's other files (settings.json,
/// CLAUDE.md…) and junctions to its folders (projects, skills, plugins…), so the sessions, their history and the rest
/// stay where they are, shared by every account.
/// </summary>
internal static class ClaudeAccounts
{
    private static string ConfigDir => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    private static string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");
    private static string ProfilePath => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? Path.Combine(c, ".claude.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    /// <summary>The accounts' own folders (%LOCALAPPDATA%\DaisysApp\Claude accounts\&lt;id&gt;).</summary>
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "Claude accounts");

    /// <summary>The sign-in Claude Code has now: its parts as one JSON object, and who it is. Null if it has none.</summary>
    public sealed record SignIn(string Json, string Uuid, string Email, string Org, string Plan);

    /// <summary>The sign-in in Claude Code's own folder, or (given) in an account's folder.</summary>
    public static SignIn? Current(string? folder = null)
    {
        string credentials = folder == null ? CredentialsPath : Path.Combine(folder, ".credentials.json");
        string profilePath = folder == null ? ProfilePath : Path.Combine(folder, ".claude.json");
        try
        {
            var o = new JsonObject();
            string plan = "";
            if (File.Exists(credentials))
            {
                var creds = JsonNode.Parse(ReadShared(credentials));
                // (signed out, it's empty)
                if (creds?["claudeAiOauth"] != null) o["credentials"] = creds;
                plan = creds?["claudeAiOauth"]?["subscriptionType"]?.GetValue<string>() ?? "";
            }
            JsonNode? account = null;
            if (File.Exists(profilePath) && JsonNode.Parse(ReadShared(profilePath)) is JsonObject profile)
            {
                if (profile["oauthAccount"] is { } a) { account = a.DeepClone(); o["oauthAccount"] = account; }
                if (profile["primaryApiKey"] is { } k) o["primaryApiKey"] = k.DeepClone();
            }
            if (o["credentials"] == null && o["primaryApiKey"] == null) return null;
            string S(string name) => account?[name] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
            return new SignIn(o.ToJsonString(), S("accountUuid"), S("emailAddress"), S("organizationName"), plan);
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("Claude accounts: reading Claude Code's sign-in: " + ex.Message);
            return null;
        }
    }

    private static AiAccount? Find(AiSettings settings, SignIn now) =>
        settings.Accounts.FirstOrDefault(x => x.Kind == AccountKinds.Claude && now.Uuid.Length > 0 && x.Uuid == now.Uuid)
        ?? settings.Accounts.FirstOrDefault(x => x.Kind == AccountKinds.Claude && x.Uuid.Length == 0 && now.Email.Length > 0 && x.Email.Equals(now.Email, StringComparison.OrdinalIgnoreCase));

    /// <summary>A sign-in kept as its account (added to the list if it's a new one, with this name).</summary>
    private static (AiAccount Account, bool Added) Store(AiSettings settings, SignIn now, string? name)
    {
        var a = Find(settings, now);
        bool added = a == null;
        if (a == null)
        {
            a = new AiAccount();
            settings.Accounts.Add(a);
        }
        if (name is { Length: > 0 }) a.Name = name;
        a.Uuid = now.Uuid;
        a.Email = now.Email;
        a.Org = now.Org;
        a.Plan = now.Plan;
        a.SignInProtected = Secret.Protect(now.Json);
        a.Kept = DateTime.Now;
        if (added) Logging.Log.Here.Info($"Claude accounts: {a.Email} added");
        return (a, added);
    }

    /// <summary>
    /// The sign-in Claude Code has kept as its account (added to the list if it's a new one), which becomes the one it's
    /// signed in to here (ActiveAccount). True in Switched if that was a different account before (signed in some other way).
    /// </summary>
    public static (AiAccount? Account, bool Added, bool Switched) Keep(AiSettings settings, string? name = null)
    {
        if (Current() is not { } now) return (null, false, false);
        var (a, added) = Store(settings, now, name);
        bool switched = settings.ActiveAccount is { } was && was != a.Id;
        settings.ActiveAccount = a.Id;
        settings.Save();
        if (switched) Logging.Log.Here.Info($"Claude accounts: Claude Code was signed in to {a.Email} outside the list (now its own sign-in here)");
        return (a, added, switched);
    }

    /// <summary>A sign-in made in a folder of its own (Add an account…) kept as its account; Claude Code's own sign-in
    /// isn't changed. The folder is deleted once it's kept.</summary>
    public static (AiAccount? Account, bool Added) KeepFrom(AiSettings settings, string folder, string? name)
    {
        if (Current(folder) is not { } now) return (null, false);
        var (a, added) = Store(settings, now, name);
        settings.Save();
        try { Directory.Delete(folder, true); } catch (Exception ex) { Logging.Log.Here.Warn($"Claude accounts: removing {folder}: {ex.Message}"); }
        return (a, added);
    }

    /// <summary>A folder for signing in to another account (Add an account…), outside Claude Code's own.</summary>
    public static string NewSignInFolder()
    {
        string folder = Path.Combine(Root, "new-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>The account a session on it uses (null: the one Claude Code is signed in to).</summary>
    /// <summary>The Claude accounts kept here, for another PC (remote control: a new session's, or an open one's).</summary>
    public static JsonArray ToRemote(AiSettings settings)
    {
        var accounts = new JsonArray();
        foreach (var a in settings.Accounts.Where(a => a.Kind == AccountKinds.Claude))
            accounts.Add(new JsonObject { ["id"] = a.Id, ["name"] = a.Label, ["about"] = a.Description, ["email"] = a.Email, ["plan"] = a.Plan });
        return accounts;
    }

    public static AiAccount? Of(AiSettings settings, string? id) =>
        id is { Length: > 0 } ? settings.Accounts.FirstOrDefault(a => a.Id == id && a.Kind == AccountKinds.Claude) : null;

    /// <summary>The account is the one Claude Code is signed in to now (its own folder, as a terminal's).</summary>
    public static bool IsClaudeCodes(AiAccount a) =>
        Current() is { } now && (now.Uuid.Length > 0 ? now.Uuid == a.Uuid : now.Email.Length > 0 && now.Email.Equals(a.Email, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Where Claude Code runs for a session on this account (CLAUDE_CONFIG_DIR): null for none picked or the one Claude
    /// Code is signed in to, else the account's own folder, made ready (<see cref="Prepare"/>). Throws, saying why, if it
    /// can't be.
    /// </summary>
    public static string? FolderFor(AiSettings settings, string? accountId)
    {
        if (Of(settings, accountId) is not { } a || IsClaudeCodes(a)) return null;
        string folder = Path.Combine(Root, a.Id);
        Prepare(settings, a, folder);
        return folder;
    }

    // Claude Code's folders not shared with an account's folder (its own .claude.json backups)
    private static readonly HashSet<string> NotShared = new(StringComparer.OrdinalIgnoreCase) { "backups" };

    /// <summary>
    /// An account's folder made ready for Claude Code: a junction to each of Claude Code's own folders (projects is made
    /// first if there isn't one, so the sessions' history is always shared), copies of its files (settings.json,
    /// CLAUDE.md…) where they're newer, and .claude.json as Claude Code's own with this account's oauthAccount and
    /// primaryApiKey. The sign-in: what's in the folder if Claude Code renewed it there since it was last kept (kept again
    /// here), else the one kept here.
    /// </summary>
    private static void Prepare(AiSettings settings, AiAccount a, string folder)
    {
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(ConfigDir, "projects"));
        foreach (var dir in Directory.GetDirectories(ConfigDir))
        {
            string name = Path.GetFileName(dir), link = Path.Combine(folder, name);
            if (NotShared.Contains(name) || Directory.Exists(link) || File.Exists(link)) continue;
            Junction(link, dir);
        }
        foreach (var file in Directory.GetFiles(ConfigDir))
        {
            string name = Path.GetFileName(file);
            if (name.Equals(".credentials.json", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".claude.json", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(".last-", StringComparison.OrdinalIgnoreCase) || name.Equals("history.jsonl", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
            string to = Path.Combine(folder, name);
            try { if (!File.Exists(to) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(to)) File.Copy(file, to, true); }
            catch (IOException ex) { Logging.Log.Here.Warn($"Claude accounts: copying {name}: {ex.Message}"); }
        }

        string credentials = Path.Combine(folder, ".credentials.json"), profilePath = Path.Combine(folder, ".claude.json");
        DateTime? renewed = File.Exists(credentials) ? File.GetLastWriteTime(credentials) : null;
        bool keptNewer = renewed == null || a.Kept is not { } kept || kept > renewed.Value;
        JsonObject? stored = null;
        if (keptNewer)
        {
            string json = Secret.Unprotect(a.SignInProtected);
            if (json.Length == 0) throw new InvalidOperationException(F("{0}'s sign-in can't be read here (it was kept by another Windows user, or on another PC): add it again (Settings → AI → Accounts).", a.Label));
            stored = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("not an object");
        }
        else if (Current(folder) is { } there && (there.Uuid.Length == 0 || there.Uuid == a.Uuid))
        {
            // renewed there since: that's the one kept now
            a.SignInProtected = Secret.Protect(there.Json);
            a.Kept = renewed;
            settings.Save();
        }

        // .claude.json: Claude Code's own, with this account's identity (as Claude Code last wrote it there, unless the
        // one kept here is newer)
        var mine = File.Exists(profilePath) ? JsonNode.Parse(ReadShared(profilePath)) as JsonObject : null;
        var profile = File.Exists(ProfilePath) ? JsonNode.Parse(ReadShared(ProfilePath)) as JsonObject ?? new JsonObject() : new JsonObject();
        foreach (var key in new[] { "oauthAccount", "primaryApiKey" })
        {
            var v = stored != null ? stored[key] : mine?[key];
            if (v != null) profile[key] = v.DeepClone(); else profile.Remove(key);
        }
        WriteSafely(profilePath, profile.ToJsonString(Indented));
        if (stored != null)
        {
            if (stored["credentials"] is { } creds) WriteSafely(credentials, creds.ToJsonString(Compact));
            else if (File.Exists(credentials)) File.Delete(credentials);
        }
    }

    /// <summary>A directory junction (no admin rights needed, unlike a symbolic link).</summary>
    private static void Junction(string link, string target)
    {
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) info.ArgumentList.Add(arg);
        using var p = System.Diagnostics.Process.Start(info)!;
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        if (!Directory.Exists(link)) throw new IOException(F("The link to {0} couldn't be made: {1}", target, error.Trim()));
    }

    /// <summary>An account removed: its sign-in in its folder goes too (the folder's links are left alone).</summary>
    public static void Forget(AiAccount a)
    {
        string folder = Path.Combine(Root, a.Id);
        foreach (var name in new[] { ".credentials.json", ".claude.json" })
            try { if (File.Exists(Path.Combine(folder, name))) File.Delete(Path.Combine(folder, name)); }
            catch (Exception ex) { Logging.Log.Here.Warn($"Claude accounts: removing {a.Email}'s {name}: {ex.Message}"); }
    }

    // as Claude Code writes them (not every non-ASCII character escaped)
    private static readonly JsonSerializerOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string ReadShared(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Written beside it, then moved into place (tried a few times: Claude Code writes these files too).</summary>
    private static void WriteSafely(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".daisysapp.tmp";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5) { Thread.Sleep(100 * attempt); }
        }
    }
}
