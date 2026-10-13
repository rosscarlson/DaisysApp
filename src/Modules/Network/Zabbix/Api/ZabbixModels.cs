namespace DaisysApp.Applets.Network.Zabbix.Api;

/// <summary>A host as Zabbix has it (host.get), with what Daisy's App works out from its interfaces.</summary>
public sealed class ZHost
{
    public string HostId { get; init; } = "";
    /// <summary>The technical name (host).</summary>
    public string Host { get; init; } = "";
    /// <summary>The visible name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Zabbix's own description of the host.</summary>
    public string Description { get; init; } = "";
    /// <summary>False when monitoring is switched off for it in Zabbix.</summary>
    public bool Monitored { get; init; } = true;
    public bool InMaintenance { get; init; }
    public IReadOnlyList<ZInterface> Interfaces { get; init; } = Array.Empty<ZInterface>();
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();

    public string Display => Name.Length > 0 ? Name : Host;

    /// <summary>The main interface's address (its IP, or its DNS name when it connects by name).</summary>
    public string Address => (Interfaces.FirstOrDefault(i => i.Main) ?? Interfaces.FirstOrDefault()) is { } i ? i.Address : "";

    /// <summary>Whether Zabbix can reach it: Available if any interface is, Unavailable if all that were checked aren't.</summary>
    public Availability Availability
    {
        get
        {
            if (Interfaces.Count == 0) return Availability.Unknown;
            if (Interfaces.Any(i => i.Available == Availability.Available)) return Availability.Available;
            if (Interfaces.Any(i => i.Available == Availability.Unavailable)) return Availability.Unavailable;
            return Availability.Unknown;
        }
    }

    /// <summary>Why it's unavailable, from Zabbix (the first interface with an error).</summary>
    public string Error => Interfaces.Select(i => i.Error).FirstOrDefault(e => e.Length > 0) ?? "";
}

public enum Availability { Unknown, Available, Unavailable }

public sealed class ZInterface
{
    /// <summary>1 agent, 2 SNMP, 3 IPMI, 4 JMX.</summary>
    public int Type { get; init; }
    public bool Main { get; init; }
    public string Ip { get; init; } = "";
    public string Dns { get; init; } = "";
    public bool UseIp { get; init; } = true;
    public string Port { get; init; } = "";
    public Availability Available { get; init; }
    public string Error { get; init; } = "";

    public string Address => UseIp || Dns.Length == 0 ? Ip : Dns;

    public string TypeName => Type switch { 1 => T("Agent"), 2 => "SNMP", 3 => "IPMI", 4 => "JMX", _ => T("Other") };
}

/// <summary>An item (one value Zabbix collects, e.g. CPU utilization), with its latest value.</summary>
public sealed class ZItem
{
    public string ItemId { get; init; } = "";
    public string HostId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Key { get; init; } = "";
    /// <summary>0 float, 1 character, 2 log, 3 unsigned, 4 text (5 binary).</summary>
    public int ValueType { get; init; }
    public string Units { get; init; } = "";
    public string LastValue { get; init; } = "";
    public DateTime? LastClock { get; init; }
    /// <summary>1 when Zabbix can't get it (with <see cref="Error"/>).</summary>
    public bool NotSupported { get; init; }
    public string Error { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>How often Zabbix gets it, as Zabbix writes it ("1m", "30s", "{$INTERVAL}").</summary>
    public string Delay { get; init; } = "";

    public bool IsNumeric => ValueType is 0 or 3;

    public double Value => IsNumeric && double.TryParse(LastValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

    public ZUnit Unit => ZUnit.Of(Units);

    /// <summary>The latest value as it reads (with its unit), or the text for a text item.</summary>
    public string ValueText => LastClock == null ? "—" : IsNumeric ? Unit.Text(Value) : LastValue;
}

/// <summary>A trigger that's in the problem state (trigger.get, value 1).</summary>
public sealed class ZProblem
{
    public string TriggerId { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>0 not classified, 1 information, 2 warning, 3 average, 4 high, 5 disaster.</summary>
    public int Severity { get; init; }
    public DateTime Since { get; init; }
    public IReadOnlyList<string> HostIds { get; init; } = Array.Empty<string>();

    public string SeverityName => SeverityText(Severity);

    public static string SeverityText(int severity) => severity switch
    {
        1 => T("Information"),
        2 => T("Warning"),
        3 => T("Average"),
        4 => T("High"),
        5 => T("Disaster"),
        _ => T("Not classified"),
    };

    /// <summary>Zabbix's own colours for each severity.</summary>
    public static string SeverityColor(int severity) => severity switch
    {
        1 => "#7499FF",
        2 => "#FFC859",
        3 => "#FFA059",
        4 => "#E97659",
        5 => "#E45959",
        _ => "#97AAB3",
    };
}
