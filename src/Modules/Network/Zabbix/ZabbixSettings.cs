using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Network.Zabbix;

/// <summary>Saved in %APPDATA%\DaisysApp\Zabbix.json. The token and password are encrypted for this Windows account.</summary>
public sealed class ZabbixSettings
{
    private const string FileName = "Zabbix";

    // ---- the server

    /// <summary>The server's IP address or name (where the Zabbix web interface is).</summary>
    public string Address { get; set; } = "";
    /// <summary>0 = the usual one (80, or 443 with HTTPS).</summary>
    public int Port { get; set; }
    public bool Https { get; set; }
    /// <summary>Where the web interface is on the server: "/zabbix" for most packages, "/" for Docker and some others.</summary>
    public string Path { get; set; } = "/zabbix";
    /// <summary>For a server with a certificate Windows doesn't trust (a self-signed one).</summary>
    public bool IgnoreCertificate { get; set; }

    /// <summary>True: an API token (Zabbix 5.4 and later); false: a user name and password.</summary>
    public bool UseToken { get; set; } = true;
    public string User { get; set; } = "";
    public string TokenProtected { get; set; } = "";
    public string PasswordProtected { get; set; } = "";

    [JsonIgnore]
    public string Token { get => Unprotect(TokenProtected); set => TokenProtected = Protect(value); }

    [JsonIgnore]
    public string Password { get => Unprotect(PasswordProtected); set => PasswordProtected = Protect(value); }

    /// <summary>How often to ask the server for new values while the tab or a widget is showing, in seconds.</summary>
    public int RefreshSeconds { get; set; } = 30;

    /// <summary>A connection worked with these settings.</summary>
    public bool Configured { get; set; }

    /// <summary>The user chose Skip in the setup (don't ask again until they fill it in).</summary>
    public bool SetupSkipped { get; set; }

    /// <summary>Switched off (Switch off Zabbix in its setup, or Settings → Network → Zabbix): no sub-tab, no widgets,
    /// nothing read. (Before 0.39 Zabbix was a module of its own, switched off in Settings → General.)</summary>
    public bool Off { get; set; }

    // ---- what's shown

    /// <summary>Hosts not shown on the main grid (by host id).</summary>
    public List<string> HiddenHosts { get; set; } = new();

    /// <summary>The host grid's columns; 0 = as many as fit (a box ~250 wide).</summary>
    public int GridColumns { get; set; }
    /// <summary>The host grid's rows to fit in the tab's height (boxes taller to fill it); 0 = boxes as tall as they need.</summary>
    public int GridRows { get; set; }
    /// <summary>The host groups' order on the grid, as dragged (by name; groups not in it come after, by name). Alerts are always first.</summary>
    public List<string> GroupOrder { get; set; } = new();

    /// <summary>Show hosts whose monitoring is switched off in Zabbix.</summary>
    public bool ShowDisabled { get; set; }

    /// <summary>Graphs made with New graph, per host.</summary>
    public List<CustomGraph> Graphs { get; set; } = new();

    public List<ZabbixWidgetDefinition> Widgets { get; set; } = new();

    /// <summary>The top of each unit's graphs, set by clicking a scale ("bps" → 1e9); missing = fit to the data.</summary>
    public Dictionary<string, double> ScaleMax { get; set; } = new();

    [JsonIgnore]
    public bool HasServer => Address.Trim().Length > 0;

    /// <summary>The URL of the server's API, e.g. http://192.168.1.10/zabbix/api_jsonrpc.php.</summary>
    public string ApiUrl(string? path = null)
    {
        string host = Address.Trim();
        if (host.Contains("://")) host = host[(host.IndexOf("://", StringComparison.Ordinal) + 3)..];
        host = host.TrimEnd('/');
        if (host.Contains(':') && !host.StartsWith('[') && host.Count(c => c == ':') > 1) host = $"[{host}]"; // IPv6
        string port = Port > 0 && Port != (Https ? 443 : 80) ? ":" + Port : "";
        string p = (path ?? Path ?? "").Trim().Trim('/');
        return $"{(Https ? "https" : "http")}://{host}{port}/{(p.Length > 0 ? p + "/" : "")}api_jsonrpc.php";
    }

    public static ZabbixSettings Load() => JsonStore.Load<ZabbixSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);

    public ZabbixSettings Clone() => (ZabbixSettings)MemberwiseClone();

    private static string Protect(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    private static string Unprotect(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

/// <summary>A graph of several items of one host (all in the same unit), made with New graph.</summary>
public sealed class CustomGraph
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HostId { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The items by key (stable when an item is re-created), with their ids as a fallback.</summary>
    public List<string> ItemKeys { get; set; } = new();
}

/// <summary>What a widget shows, and where it is.</summary>
public enum ZabbixWidgetKind { Host, Group, Items }

/// <summary>A widget: a host's box, one of a host's graphs, or items (a custom graph or a single item); where it is, in physical pixels.</summary>
public sealed class ZabbixWidgetDefinition : DaisysApp.Shared.Widgets.IWidgetPlace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ZabbixWidgetKind Kind { get; set; }
    public string HostId { get; set; } = "";
    /// <summary>For a group widget: the graph group's key ("cpu", "net"…).</summary>
    public string Group { get; set; } = "";
    /// <summary>For an items widget: the item keys, and its title.</summary>
    public List<string> ItemKeys { get; set; } = new();
    public string Title { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Topmost { get; set; } = true;
    /// <summary>How much time the graph shows.</summary>
    public int Minutes { get; set; } = 60;
}
