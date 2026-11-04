using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>A program's row in the table, updated in place from each look (so sorting and the selection stay).</summary>
public sealed class ProgramRow(string name) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public Assessment? Last { get; private set; }

    private string title = "", publisher = "", category = "", suggestion = "", necessity = "", source = "";
    private Threat threat;
    private double cpu, memoryMB, score;
    private int count;

    public string Title { get => title; private set => Set(ref title, value); }
    public string Publisher { get => publisher; private set => Set(ref publisher, value); }
    public string Category { get => category; private set => Set(ref category, value); }
    public string Necessity { get => necessity; private set => Set(ref necessity, value); }
    public string Suggestion { get => suggestion; private set => Set(ref suggestion, value); }
    /// <summary>✓ reviewed (by a person, or the list it comes with); blank: classified automatically.</summary>
    public string Source { get => source; private set => Set(ref source, value); }
    public Threat Threat
    {
        get => threat;
        private set { if (Set(ref threat, value)) { Changed(nameof(ThreatText)); Changed(nameof(ThreatBrush)); } }
    }
    public string ThreatText => Text(threat);
    public Brush ThreatBrush => BrushOf(threat);
    public double Cpu { get => cpu; private set { if (Set(ref cpu, value)) Changed(nameof(CpuText)); } }
    public string CpuText => cpu < 0.05 ? "0" : cpu.ToString(cpu < 10 ? "0.0" : "0");
    public double MemoryMB { get => memoryMB; private set { if (Set(ref memoryMB, value)) Changed(nameof(MemoryText)); } }
    public string MemoryText => Sizes.Bytes((long)(memoryMB * 1048576));
    public int Count { get => count; private set => Set(ref count, value); }
    public double Score { get => score; private set => Set(ref score, value); }
    public bool Reviewed => Last?.Entry.Reviewed ?? false;

    public void Update(Assessment a)
    {
        Last = a;
        var e = a.Entry;
        Title = e.Title.Length > 0 ? e.Title : a.Name;
        Publisher = e.Publisher.Length > 0 ? e.Publisher : a.Facts?.Company ?? "";
        Category = CategoryText(e.Category);
        Necessity = Text(e.Necessity);
        Suggestion = a.Suggestion;
        Source = e.Reviewed ? "✓" : "";
        Threat = a.Threat;
        Cpu = Math.Round(a.Cpu, 1);
        MemoryMB = Math.Round(a.MemoryMB, 1);
        Count = a.Procs.Count;
        Score = Math.Round(a.Score, 1);
    }

    // ---------------------------------------------------------------- words and colours (the details use them too)

    public static string Text(Threat t) => t switch
    {
        Threat.Safe => T("Safe"), Threat.Low => T("Low"), Threat.Medium => T("Medium"), Threat.High => T("High"), _ => T("Dangerous"),
    };

    public static string Text(Necessity n) => n switch
    {
        ProcessAnalyzer.Necessity.Required => T("Required"), ProcessAnalyzer.Necessity.Recommended => T("Recommended"),
        ProcessAnalyzer.Necessity.Optional => T("Optional"), ProcessAnalyzer.Necessity.Unnecessary => T("Unnecessary"), _ => T("Unknown"),
    };

    public static string Text(Resource r) => r switch { Resource.Light => T("Light"), Resource.Moderate => T("Moderate"), _ => T("Heavy") };

    /// <summary>The categories offered when editing (the stored value stays English, as the server shares it).</summary>
    public static readonly string[] Categories =
        ["Windows", "Microsoft", "Security", "Hardware", "Browser", "Communication", "Gaming", "Launcher", "Media", "Cloud", "Office",
         "Developer", "Utility", "Remote access", "Updater", "Telemetry", "Background", "App", "Adware", "Malware", "Unknown"];

    public static string CategoryText(string c) => c switch
    {
        "Windows" => T("Windows"), "Microsoft" => T("Microsoft"), "Security" => T("Security"), "Hardware" => T("Hardware"),
        "Browser" => T("Browser"), "Communication" => T("Communication"), "Gaming" => T("Gaming"), "Launcher" => T("Launcher"),
        "Media" => T("Media"), "Cloud" => T("Cloud"), "Office" => T("Office"), "Developer" => T("Developer"), "Utility" => T("Utility"),
        "Remote access" => T("Remote access"), "Updater" => T("Updater"), "Telemetry" => T("Telemetry"), "Background" => T("Background"),
        "App" => T("App"), "Adware" => T("Adware"), "Malware" => T("Malware"), "Unknown" => T("Unknown"), _ => c,
    };

    public static string Hex(Threat t) => t switch
    {
        Threat.Safe => "#4CAF50", Threat.Low => "#7CB342", Threat.Medium => "#FFB300", Threat.High => "#FB8C00", _ => "#E53935",
    };

    public static Brush BrushOf(Threat t) => Ui.Brush(Hex(t));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<TV>(ref TV field, TV value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<TV>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name!);
        return true;
    }
}
