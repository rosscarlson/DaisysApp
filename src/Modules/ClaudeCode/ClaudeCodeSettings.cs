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

    /// <summary>
    /// Never ask before using a tool (Claude Code's --dangerously-skip-permissions): every session runs in
    /// bypassPermissions mode and anything that would still ask is allowed. Settings → Claude Code.
    /// </summary>
    public bool SkipPermissions { get; set; }

    /// <summary>How the conversation looks: colours, fonts, sizes, the box's height (Settings → Claude Code).</summary>
    public Appearance Look { get; set; } = new();

    /// <summary>Raised (on the UI thread) when Settings → Claude Code changes something the tabs show.</summary>
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Your plan's usage: the warnings, how often it's checked, and the readings so far (PlanUsage).</summary>
    public UsageSettings Usage { get; set; } = new();

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
