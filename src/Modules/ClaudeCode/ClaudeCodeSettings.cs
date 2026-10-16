using DaisysApp.Settings;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>Saved in %APPDATA%\DaisysApp\ClaudeCode.json: the session tabs, and the defaults for new ones.</summary>
public sealed class ClaudeCodeSettings
{
    private const string FileName = "ClaudeCode";

    /// <summary>The open tabs, in order.</summary>
    public List<SessionInfo> Sessions { get; set; } = new();

    /// <summary>The tab showing (its session id).</summary>
    public string? Selected { get; set; }

    /// <summary>The folder a new session starts in (the last one used).</summary>
    public string? LastFolder { get; set; }

    /// <summary>A new session's model ("" = Claude Code's own default) and permission mode.</summary>
    public string Model { get; set; } = "";
    public string Mode { get; set; } = "manual";

    /// <summary>claude.exe, if it isn't where it's looked for (%USERPROFILE%\.local\bin, then PATH).</summary>
    public string? ClaudePath { get; set; }

    public static ClaudeCodeSettings Load() => JsonStore.Load<ClaudeCodeSettings>(FileName);
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
    /// <summary>False until the first message: then it exists in Claude Code and is resumed from then on.</summary>
    public bool Started { get; set; }
    /// <summary>Tokens and cost so far (all turns, as the results report them).</summary>
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public double CostUsd { get; set; }
    public int Turns { get; set; }
}
