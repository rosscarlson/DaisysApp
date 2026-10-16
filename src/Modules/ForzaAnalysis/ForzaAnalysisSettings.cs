using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>Saved in %APPDATA%\DaisysApp\ForzaAnalysis.json.</summary>
public sealed class ForzaAnalysisSettings
{
    private const string FileName = "ForzaAnalysis";

    /// <summary>The session last looked at (its file name).</summary>
    public string? Selected { get; set; }

    /// <summary>Names given to sessions (file name → name).</summary>
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowSteering { get; set; } = true;
    public bool ShowClutch { get; set; }

    /// <summary>The brake comparison lines up the zones at the brake point (false) or at the start of the release (true).</summary>
    public bool AlignRelease { get; set; }

    /// <summary>Each session's summary for the list, worked out once (file name → summary).</summary>
    public Dictionary<string, SessionSummary> Summaries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ForzaAnalysisSettings Load() => JsonStore.Load<ForzaAnalysisSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>What the session list shows of a session, kept so it needn't be read again.</summary>
public sealed class SessionSummary
{
    public int Version { get; set; }
    public long Size { get; set; }
    public double Seconds { get; set; }
    public double ActiveSeconds { get; set; }
    public double? Overall { get; set; }
    public double? BrakeRelease { get; set; }
    public double? ThrottleApply { get; set; }
    public int Zones { get; set; }
}
