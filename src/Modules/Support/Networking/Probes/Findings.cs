namespace DaisysApp.Applets.Support.Networking;

/// <summary>How much a finding matters: High is a way in, Medium weakens the device, Low and Info are worth knowing.</summary>
internal enum Severity { Info, Low, Medium, High }

/// <summary>Something found that's worth fixing (or knowing), with a page about it.</summary>
internal sealed record Finding(Severity Level, string Title, string Detail, string? Url = null)
{
    public string LevelText => Level switch
    {
        Severity.High => T("High"),
        Severity.Medium => T("Medium"),
        Severity.Low => T("Low"),
        _ => T("Info"),
    };

    /// <summary>The colour of its level: red, orange, yellow, or the accent.</summary>
    public string Color => Level switch
    {
        Severity.High => "#FF5C5C",
        Severity.Medium => NetUi.Orange,
        Severity.Low => "#F2CC60",
        _ => "AccentBrush",
    };
}

/// <summary>A label and a value (a link when it has a URL).</summary>
internal sealed record Fact(string Label, string Value, string? Url = null);

/// <summary>What a deeper look at one open port found: the product and version, facts, findings, and hints about the device.</summary>
internal sealed class ServiceInfo
{
    /// <summary>"HTTP", "SSH", "TLS"…: what was found speaking on it (not what the port number is usually for).</summary>
    public string Protocol = "";
    public string Product = "";
    public string Version = "";
    public readonly List<Fact> Facts = new();
    public readonly List<Finding> Findings = new();
    /// <summary>Hints at the operating system ("Ubuntu", "Windows").</summary>
    public readonly List<string> OsHints = new();
    /// <summary>Hints at what the device is ("printer", "tv"), each with why.</summary>
    public readonly List<(string Kind, string Why)> DeviceHints = new();
    /// <summary>The page to open in a browser, if it's a web server.</summary>
    public string? Url;
    /// <summary>Everything read, for the details window.</summary>
    public string Raw = "";

    public string ProductText => Version.Length > 0 ? $"{Product} {Version}" : Product;

    public void Add(string label, string? value, string? url = null)
    {
        if (!string.IsNullOrWhiteSpace(value)) Facts.Add(new Fact(label, value.Trim(), url));
    }

    /// <summary>A search of the U.S. National Vulnerability Database for a product's version.</summary>
    public static string NvdUrl(string product) =>
        "https://nvd.nist.gov/vuln/search/results?form_type=Basic&results_type=overview&search_type=all&query=" + Uri.EscapeDataString(product);
}
