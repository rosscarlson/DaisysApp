using DaisysApp.Settings;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>The race analysis' choices, saved in %APPDATA%\DaisysApp\ForzaAnalysis.json (Forza Analysis' file, a module
/// of its own until 4.5.0).</summary>
public sealed class SessionSettings
{
    private const string FileName = "ForzaAnalysis";

    /// <summary>The session last looked at (its file name, or Imported\file name).</summary>
    public string? Selected { get; set; }

    /// <summary>The name an exported session carries (empty: Windows' user name; since 4.7.0 no box sets it, one set
    /// before is kept).</summary>
    public string Driver { get; set; } = "";

    /// <summary>Compare: what the chart shows (<see cref="Channel"/> names).</summary>
    public List<string> CompareChannels { get; set; } = new() { "Delta", "Speed", "Throttle", "Brake", "Steering" };

    /// <summary>(since 4.6.2) Compare: each run's lines hidden, "Speed.1" (this run) or "Brake.2" (the other).</summary>
    public List<string> CompareHidden { get; set; } = new();

    /// <summary>(since 4.10.9) The popped-out Video's timeline: the lines hidden (<see cref="Channel"/> names).</summary>
    public List<string> VideoHidden { get; set; } = new();

    /// <summary>(since 4.10.9) The race video's volume (0 to 1) and mute.</summary>
    public double VideoVolume { get; set; } = 1;
    public bool VideoMuted { get; set; }

    /// <summary>Smoothness: what it shows.</summary>
    public bool SmoothBrake { get; set; } = true;
    public bool SmoothThrottle { get; set; } = true;
    public bool SmoothOn { get; set; } = true;
    public bool SmoothOff { get; set; } = true;

    public static SessionSettings Load() => JsonStore.Load<SessionSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
    /// <summary>(since 4.10.9) Saved a little later (a slider dragged).</summary>
    public void SaveSoon() => JsonStore.SaveSoon(FileName, this);
}
