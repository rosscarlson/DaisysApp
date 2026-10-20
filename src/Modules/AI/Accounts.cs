using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DaisysApp.Applets.AI;

/// <summary>The kinds of account (Settings → AI → Accounts): each a way of signing in that more than one account can be
/// kept for, one of each kind in use at a time. Only Claude so far; the API keys of other AIs are Providers.</summary>
internal static class AccountKinds
{
    /// <summary>A Claude account for Claude Code: a subscription (Pro, Max, Team) or an Anthropic Console sign-in.</summary>
    public const string Claude = "claude";

    /// <summary>Every kind, in the order Add an account… lists them: its id, name and what it is.</summary>
    public static (string Id, string Name, string About)[] All => new[]
    {
        (Claude, T("Claude (for Claude Code)"), T("The account Claude Code signs in with: a Claude subscription (Pro, Max, Team) or the Anthropic Console. One is in use at a time, for every Claude Code tab.")),
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
/// sign-in). Each account's copy of those is kept here, encrypted; switching keeps the one in use (its tokens may have
/// been renewed since) and puts the other one's in its place. Everything else (sessions, settings, CLAUDE.md, skills)
/// stays where it is, shared by every account.
/// </summary>
internal static class ClaudeAccounts
{
    private static string ConfigDir => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    private static string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");
    private static string ProfilePath => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? Path.Combine(c, ".claude.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    /// <summary>The sign-in Claude Code has now: its parts as one JSON object, and who it is. Null if it has none.</summary>
    public sealed record SignIn(string Json, string Uuid, string Email, string Org, string Plan);

    public static SignIn? Current()
    {
        try
        {
            var o = new JsonObject();
            string plan = "";
            if (File.Exists(CredentialsPath))
            {
                var creds = JsonNode.Parse(ReadShared(CredentialsPath));
                // (signed out, it's empty)
                if (creds?["claudeAiOauth"] != null) o["credentials"] = creds;
                plan = creds?["claudeAiOauth"]?["subscriptionType"]?.GetValue<string>() ?? "";
            }
            JsonNode? account = null;
            if (File.Exists(ProfilePath) && JsonNode.Parse(ReadShared(ProfilePath)) is JsonObject profile)
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

    /// <summary>
    /// The sign-in in use kept as its account (added to the list if it's a new one, with this name), which becomes the
    /// active one. True in Switched if a different account was active here before (signed in some other way).
    /// </summary>
    public static (AiAccount? Account, bool Added, bool Switched) Keep(AiSettings settings, string? name = null)
    {
        if (Current() is not { } now) return (null, false, false);
        var a = settings.Accounts.FirstOrDefault(x => x.Kind == AccountKinds.Claude && now.Uuid.Length > 0 && x.Uuid == now.Uuid)
            ?? settings.Accounts.FirstOrDefault(x => x.Kind == AccountKinds.Claude && x.Uuid.Length == 0 && now.Email.Length > 0 && x.Email.Equals(now.Email, StringComparison.OrdinalIgnoreCase));
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
        bool switched = settings.ActiveAccount is { } was && was != a.Id;
        settings.ActiveAccount = a.Id;
        settings.Save();
        if (added) Logging.Log.Here.Info($"Claude accounts: {a.Email} added");
        if (switched) Logging.Log.Here.Info($"Claude accounts: Claude Code was signed in to {a.Email} outside the list (now the active one)");
        return (a, added, switched);
    }

    /// <summary>Claude Code switched to this account: the one in use kept first, then this one's sign-in put in its
    /// place. The reason it couldn't be, or null.</summary>
    public static string? Switch(AiSettings settings, AiAccount to)
    {
        string json = Secret.Unprotect(to.SignInProtected);
        if (json.Length == 0) return F("{0}'s sign-in can't be read here (it was kept by another Windows user, or on another PC): add it again (Settings → AI → Accounts).", to.Label);
        Keep(settings);
        try
        {
            var o = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("not an object");
            // .credentials.json: this account's, or none (a Console sign-in has only its key)
            if (o["credentials"] is { } creds) WriteSafely(CredentialsPath, creds.ToJsonString(Compact));
            else if (File.Exists(CredentialsPath)) File.Delete(CredentialsPath);
            // .claude.json: who it is, and a Console sign-in's key (the rest of the file left as it is)
            var profile = File.Exists(ProfilePath) ? JsonNode.Parse(ReadShared(ProfilePath)) as JsonObject ?? new JsonObject() : new JsonObject();
            foreach (var key in new[] { "oauthAccount", "primaryApiKey" })
                if (o[key] is { } v) profile[key] = v.DeepClone(); else profile.Remove(key);
            WriteSafely(ProfilePath, profile.ToJsonString(Indented));
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Claude accounts: switching to {to.Email}: {ex.Message}");
            return F("Claude Code couldn't be switched to {0}: {1}", to.Label, ex.Message);
        }
        string? from = settings.Accounts.FirstOrDefault(a => a.Id == settings.ActiveAccount)?.Email;
        settings.ActiveAccount = to.Id;
        settings.Save();
        Logging.Log.Here.Info($"Claude accounts: switched from {from ?? "(none)"} to {to.Email}");
        settings.NotifyAccountsChanged();
        settings.NotifyAccountSwitched();
        return null;
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
