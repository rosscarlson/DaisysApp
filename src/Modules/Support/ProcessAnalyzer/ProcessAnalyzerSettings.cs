using DaisysApp.Settings;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>Saved in %APPDATA%\DaisysApp\ProcessAnalyzer.json (the database itself is in %LOCALAPPDATA%: <see cref="ProcessDb"/>).</summary>
public sealed class ProcessAnalyzerSettings
{
    private const string FileName = "ProcessAnalyzer";

    /// <summary>Keep the database in step with Daisy's Server's (when the server's set up in Settings → General).</summary>
    public bool UseServer { get; set; } = true;
    /// <summary>Look every few minutes while the tab isn't showing, and notify about a risky or dangerous program.</summary>
    public bool Watch { get; set; } = true;
    public int WatchMinutes { get; set; } = 5;
    /// <summary>VirusTotal's API key (free from virustotal.com): files' hashes looked up.</summary>
    public string VirusTotalKey { get; set; } = "";
    /// <summary>Look up programs that aren't known and aren't signed by themselves (needs the key).</summary>
    public bool AutoVirusTotal { get; set; } = true;
    /// <summary>Days of Hardware → Performance's log read for each program's history.</summary>
    public int HistoryDays { get; set; } = 7;
    /// <summary>The table's filter: 0 everything, 1 suggestions, 2 not reviewed, 3 risky.</summary>
    public int Filter { get; set; } = 1;
    /// <summary>Programs the user said are fine here: no suggestion for them (by name).</summary>
    public List<string> Ignored { get; set; } = new();

    public static ProcessAnalyzerSettings Load() => JsonStore.Load<ProcessAnalyzerSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
    public void SaveSoon() => JsonStore.SaveSoon(FileName, this);
}
