using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>Saved in %APPDATA%\DaisysApp\ForzaAnalysis.json.</summary>
public sealed class ForzaAnalysisSettings
{
    private const string FileName = "ForzaAnalysis";

    /// <summary>The session last looked at (its file name).</summary>
    public string? Selected { get; set; }

    /// <summary>Names given to sessions (file name, or Importedile name → name).</summary>
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowSteering { get; set; } = true;
    public bool ShowClutch { get; set; }
    public bool ShowSpeed { get; set; } = true;

    /// <summary>The name an exported session carries (empty: Windows' user name).</summary>
    public string Driver { get; set; } = "";

    /// <summary>Compare: what the chart shows (<see cref="Channel"/> names).</summary>
    public List<string> CompareChannels { get; set; } = new() { "Delta", "Speed", "Throttle", "Brake", "Steering" };

    /// <summary>Smoothness: what it shows.</summary>
    public bool SmoothBrake { get; set; } = true;
    public bool SmoothThrottle { get; set; } = true;
    public bool SmoothOn { get; set; } = true;
    public bool SmoothOff { get; set; } = true;

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
    /// <summary>The fastest complete lap, seconds (since 4.4.0).</summary>
    public double? BestLap { get; set; }
}
