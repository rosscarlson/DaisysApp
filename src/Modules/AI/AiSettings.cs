using DaisysApp.Settings;

namespace DaisysApp.Applets.AI;

/// <summary>Saved in %APPDATA%\DaisysApp\AI.json (ClaudeCode.json before 1.8, moved on the first start): the session
/// tabs, the providers, and the defaults for new ones.</summary>
public sealed class AiSettings
{
    private const string FileName = "AI";

    /// <summary>The open tabs, in order.</summary>
    public List<SessionInfo> Sessions { get; set; } = new();

    /// <summary>Tabs closed after their first message (here or on another PC), the newest first (a new tab lists them to open one again).</summary>
    public List<SessionInfo> Closed { get; set; } = new();
    public const int ClosedKept = 30;

    /// <summary>Raised (on the UI thread) when Closed changes.</summary>
    public event Action? ClosedChanged;

    /// <summary>A tab closed: kept at the top of Closed (not one never started). A session on another PC carries on
    /// there, and is opened again from here.</summary>
    public void RememberClosed(SessionInfo s)
    {
        if (!s.Started) return;
        s.ClosedAt = DateTime.Now;
        Closed.RemoveAll(c => c.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
        Closed.Insert(0, s);
        if (Closed.Count > ClosedKept) Closed.RemoveRange(ClosedKept, Closed.Count - ClosedKept);
        ClosedChanged?.Invoke();
    }

    /// <summary>A session taken off Closed (opened again, or forgotten).</summary>
    public void ForgetClosed(string id)
    {
        if (Closed.RemoveAll(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0) return;
        Save();
        ClosedChanged?.Invoke();
    }

    /// <summary>The Claude Code problem cleared from the top of the tab (it shows again if it changes).</summary>
    public string? ClearedWarning { get; set; }

    /// <summary>The tab showing (its session id).</summary>
    public string? Selected { get; set; }

    /// <summary>The folder a new session starts in (the last one used).</summary>
    public string? LastFolder { get; set; }

    /// <summary>A new session's model ("" = Claude Code's own default) and permission mode.</summary>
    public string Model { get; set; } = "";
    public string Mode { get; set; } = "manual";

    /// <summary>claude.exe, if it isn't where it's looked for (%USERPROFILE%\.local\bin, then PATH).</summary>
    public string? ClaudePath { get; set; }

    /// <summary>
    /// Allow everything, never ask (Claude Code's --dangerously-skip-permissions): every session runs in
    /// bypassPermissions mode and anything that would still ask is allowed. Settings → AI, a session's dashboard, or
    /// Always allow everything on a question.
    /// </summary>
    public bool SkipPermissions { get; set; }

    /// <summary>Switches allowing everything on or off: saved, and every tab follows at once.</summary>
    public void AllowEverything(bool on)
    {
        if (SkipPermissions == on) return;
        SkipPermissions = on;
        Save();
        NotifyChanged();
    }

    /// <summary>How the conversation looks: colours, fonts, sizes, the box's height (Settings → AI).</summary>
    public Appearance Look { get; set; } = new();

    /// <summary>Raised (on the UI thread) when Settings → AI changes something the tabs show.</summary>
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>The models besides Claude Code: each an OpenAI-compatible API (Gemini, OpenAI, OpenRouter, Ollama…).</summary>
    public List<Provider> Providers { get; set; } = new();

    /// <summary>A new session's provider (a Provider's Id; "" = Claude Code).</summary>
    public string LastProvider { get; set; } = "";

    /// <summary>When a session stops at a usage limit, carry on by itself once the limit resets, with this message.</summary>
    public bool AutoResume { get; set; } = true;
    public string ResumeMessage { get; set; } = "Please continue where you left off.";

    /// <summary>
    /// Session notes: after each request is done, the AI is asked to bring CLAUDE.md (and README.md, if it's out of
    /// date) in the session's folder up to date with this message, so the work can be picked up again from them if the
    /// session ends. Claude Code reads CLAUDE.md when it starts, and the agent is told to.
    /// </summary>
    public bool KeepNotes { get; set; }
    public string NotesMessage { get; set; } = DefaultNotesMessage;
    public const string DefaultNotesMessage =
        "Update the session notes now, so this work can be picked up from them if the session ends. In CLAUDE.md in the " +
        "working folder (create it if it isn't there), keep a section headed \"Session notes\": what I asked for, what " +
        "you did and which files changed, decisions and why, where things stand now, and what's next. Keep it current " +
        "and concise rather than a growing log: update what's there and condense older entries. If the project's " +
        "README.md describes something that has changed, bring it up to date too. Also add what I asked for since the " +
        "last notes, word for word (not the files I had you read first), under today's date at the end of REQUESTS.md " +
        "in the working folder (create it if it isn't there), so it can be searched later: that file is a log, so " +
        "never condense it. Change nothing else, and reply in one line.";

    /// <summary>The default message before (2.0.0 and earlier): recognised in older conversations, and replaced by
    /// the new default where it was still the one in use.</summary>
    public static readonly string[] EarlierNotesMessages =
    {
        "Update the session notes now, so this work can be picked up from them if the session ends. In CLAUDE.md in the " +
        "working folder (create it if it isn't there), keep a section headed \"Session notes\": what I asked for, what " +
        "you did and which files changed, decisions and why, where things stand now, and what's next. Keep it current " +
        "and concise rather than a growing log: update what's there and condense older entries. If the project's " +
        "README.md describes something that has changed, bring it up to date too. Change nothing else, and reply in one line.",
    };

    /// <summary>The folder a new session works in when there's no last one: Documents\AI (made if need be), rather
    /// than the user's own folder, where notes would be read by every project under it.</summary>
    public static string DefaultFolder()
    {
        string d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AI");
        try { System.IO.Directory.CreateDirectory(d); return d; }
        catch { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
    }

    /// <summary>Your plan's usage: the warnings, how often it's checked, and the readings so far (PlanUsage).</summary>
    public UsageSettings Usage { get; set; } = new();

    /// <summary>Remote control: this PC's sessions controlled from another PC (server), or another PC's from here (client).</summary>
    public RemoteSettings Remote { get; set; } = new();

    /// <summary>Raised (on the UI thread) when the remote control settings change (the server starts or stops).</summary>
    public event Action? RemoteChanged;
    public void NotifyRemoteChanged() => RemoteChanged?.Invoke();

    public static AiSettings Load()
    {
        // the module was Claude Code until 1.8: its settings carry over
        if (!JsonStore.Exists(FileName) && JsonStore.Exists("ClaudeCode"))
            try { System.IO.File.Copy(JsonStore.PathFor("ClaudeCode"), JsonStore.PathFor(FileName)); } catch { }
        var s = JsonStore.Load<AiSettings>(FileName);
        if (EarlierNotesMessages.Contains(s.NotesMessage.Trim())) s.NotesMessage = DefaultNotesMessage;
        return s;
    }
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>One tab: a Claude Code session (its id is the session's, so it's resumed with its history).</summary>
public sealed class SessionInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Model { get; set; } = "";
    public string Mode { get; set; } = "manual";
    /// <summary>Who answers: "" = Claude Code, else a Provider's Id (the built-in agent over its API).</summary>
    public string Provider { get; set; } = "";
    /// <summary>Stopped at a usage limit: carry on by itself at this time (AutoResume).</summary>
    public DateTime? ResumeAt { get; set; }
    /// <summary>False until the first message: then it exists in Claude Code and is resumed from then on.</summary>
    public bool Started { get; set; }
    /// <summary>A session on claude.ai/code (the web or the mobile app) to bring here at the next start (--teleport):
    /// its link or id. Once it's here it's an ordinary session (its id from then on).</summary>
    public string? Teleport { get; set; }
    /// <summary>Tokens and cost so far (all turns, as the results report them).</summary>
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public double CostUsd { get; set; }
    public int Turns { get; set; }
    /// <summary>Remote control: a session running on another PC ("host:port"; null: on this PC), that PC's name, and
    /// which AI it uses there.</summary>
    public string? Remote { get; set; }
    public string? RemotePc { get; set; }
    public string? RemoteWho { get; set; }
    /// <summary>When its tab was closed (AiSettings.Closed).</summary>
    public DateTime? ClosedAt { get; set; }
}

/// <summary>
/// Remote control (Settings → AI). As the server, this PC listens on a port and lets a PC that knows the passphrase
/// see its sessions, open them (their output shows on both) and start new ones. As the client, it connects to one other
/// PC. The passphrase is the same on both, and everything sent is encrypted with it (RemoteWire).
/// </summary>
public sealed class RemoteSettings
{
    public const int DefaultPort = 38863;

    public bool Serve { get; set; }
    public int Port { get; set; } = DefaultPort;
    public string PassphraseProtected { get; set; } = "";

    public bool Connect { get; set; }
    /// <summary>The other PC: its name or IP address.</summary>
    public string Address { get; set; } = "";
    public int RemotePort { get; set; } = DefaultPort;
    public string RemotePassphraseProtected { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Passphrase { get => Secret.Unprotect(PassphraseProtected); set => PassphraseProtected = Secret.Protect(value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public string RemotePassphrase { get => Secret.Unprotect(RemotePassphraseProtected); set => RemotePassphraseProtected = Secret.Protect(value); }

    /// <summary>"host:port", what a remote tab remembers.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Target => $"{Address.Trim()}:{RemotePort}";
}

/// <summary>Text kept encrypted for this Windows user (DPAPI): API keys, passphrases.</summary>
internal static class Secret
{
    public static string Protect(string value) => value.Length == 0 ? "" :
        Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    public static string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        try { return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(value), null, System.Security.Cryptography.DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

/// <summary>Plan usage warnings and the readings kept (PlanUsage).</summary>
public sealed class UsageSettings
{
    /// <summary>Windows notifications: at the two levels (%), when the pace reaches the limit within the hour, and when it resets.</summary>
    public bool Notify { get; set; } = true;
    public int WarnAt { get; set; } = 80;
    public int UrgentAt { get; set; } = 95;
    public bool NotifyForecast { get; set; } = true;
    public bool NotifyReset { get; set; } = true;
    /// <summary>An amber (close) or red (out) bubble in the title bar.</summary>
    public bool TitleBadge { get; set; } = true;
    /// <summary>The usage details are open at the top of the tab.</summary>
    public bool Open { get; set; }
    /// <summary>How often Claude Code is asked (it costs nothing); every 3 minutes when you're close.</summary>
    public int CheckMinutes { get; set; } = 10;

    public List<UsageSample> History { get; set; } = new();
    public List<SavedWindow>? Last { get; set; }
    public DateTime? LastChecked { get; set; }
    /// <summary>Notifications given (window, its reset, the level), so each is said once.</summary>
    public Dictionary<string, DateTime> Notified { get; set; } = new();
    /// <summary>Windows you were close on, to say when they reset (window → when).</summary>
    public Dictionary<string, DateTime> ResetWatch { get; set; } = new();
}

/// <summary>A reading: how much of this 5 hours and this week were used (0–1).</summary>
public sealed class UsageSample
{
    public DateTime At { get; set; }
    public double Session { get; set; }
    public double Week { get; set; }
}

/// <summary>A window as last read, shown until the first check after a start.</summary>
public sealed class SavedWindow
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public double Used { get; set; }
    public DateTime? Resets { get; set; }
}

/// <summary>The conversation's look. A colour left empty follows the app's theme (light or dark).</summary>
public sealed class Appearance
{
    /// <summary>Behind the conversation.</summary>
    public string ChatBackground { get; set; } = "";
    /// <summary>Claude's text.</summary>
    public string TextColor { get; set; } = "";
    /// <summary>Your messages: the bubble and its text.</summary>
    public string BubbleBackground { get; set; } = "";
    public string BubbleText { get; set; } = "";
    /// <summary>Code blocks: behind them, and their text.</summary>
    public string CodeBackground { get; set; } = "";
    public string CodeText { get; set; } = "";
    /// <summary>The box you write in.</summary>
    public string InputBackground { get; set; } = "";
    public string InputText { get; set; } = "";

    public string Font { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 15.5;
    public string CodeFont { get; set; } = "Cascadia Mono";
    public double CodeFontSize { get; set; } = 14;
    /// <summary>Line height as a multiple of the font size.</summary>
    public double LineSpacing { get; set; } = 1.5;
    /// <summary>The tool cards, notes and thinking chips (their text size).</summary>
    public double CardFontSize { get; set; } = 14;
    /// <summary>The box you write in: how many lines tall (it grows to twice that), and its text size.</summary>
    public int InputRows { get; set; } = 6;
    public double InputFontSize { get; set; } = 15.5;
    /// <summary>The widest Claude's text and your messages go (0 = the whole width).</summary>
    public int MaxTextWidth { get; set; }
}

/// <summary>
/// A model to use besides Claude Code: an OpenAI-compatible chat API (the built-in agent, ApiAgent, does the tools).
/// The API key is kept encrypted for this Windows user (DPAPI).
/// </summary>
public sealed class Provider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>Which preset it started from (gemini, openai, openrouter, ollama, lmstudio, custom).</summary>
    public string Preset { get; set; } = "custom";
    public string BaseUrl { get; set; } = "";
    public string ApiKeyProtected { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>The models it offers (from its /models, or typed), for the session's Model box.</summary>
    public List<string> Models { get; set; } = new();
    public int MaxTokens { get; set; } = 8192;
    /// <summary>Empty: the model's own.</summary>
    public double? Temperature { get; set; }
    public int ContextWindow { get; set; } = 128000;
    /// <summary>Added to the agent's instructions.</summary>
    public string Instructions { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiKey
    {
        get
        {
            if (ApiKeyProtected.Length == 0) return "";
            try { return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(ApiKeyProtected), null, System.Security.Cryptography.DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set => ApiKeyProtected = value.Length == 0 ? "" : Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }

    /// <summary>The presets: name, base URL, a model to start with, its context window, and where to get a key.</summary>
    public static readonly (string Preset, string Name, string Url, string Model, int Context, string? KeyUrl)[] Presets =
    {
        ("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.5-flash", 1_000_000, "https://aistudio.google.com/apikey"),
        ("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4.1", 1_000_000, "https://platform.openai.com/api-keys"),
        ("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "google/gemini-2.5-flash", 1_000_000, "https://openrouter.ai/keys"),
        ("ollama", "Ollama (on this PC)", "http://localhost:11434/v1", "qwen3:8b", 32768, null),
        ("lmstudio", "LM Studio (on this PC)", "http://localhost:1234/v1", "", 32768, null),
        ("custom", "Custom (OpenAI-compatible)", "", "", 128000, null),
    };
}
