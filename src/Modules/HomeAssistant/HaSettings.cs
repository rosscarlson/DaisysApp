using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.HomeAssistant;

/// <summary>Saved in %APPDATA%\DaisysApp\HomeAssistant.json. The token is encrypted for this Windows account.</summary>
public sealed class HaSettings
{
    private const string FileName = "HomeAssistant";

    // ---- the server

    /// <summary>Where Home Assistant is: http://homeassistant.local:8123, an IP address, or an https:// address.</summary>
    public string Address { get; set; } = "";
    /// <summary>For a server with a certificate Windows doesn't trust (a self-signed one).</summary>
    public bool IgnoreCertificate { get; set; }
    public string TokenProtected { get; set; } = "";

    [JsonIgnore]
    public string Token { get => Unprotect(TokenProtected); set => TokenProtected = Protect(value); }

    /// <summary>A connection worked with these settings.</summary>
    public bool Configured { get; set; }
    /// <summary>The user chose Skip in the setup (don't ask again until they fill it in).</summary>
    public bool SetupSkipped { get; set; }

    // ---- what's shown

    /// <summary>How the boxes are grouped: Area, Floor or Type.</summary>
    public string GroupBy { get; set; } = "Area";
    /// <summary>The groups' order on the grid, as dragged, per way of grouping (by key; groups not in it come after, by name).</summary>
    public Dictionary<string, List<string>> GroupOrder { get; set; } = new();
    /// <summary>Devices and entities not shown (by <see cref="HaThing.Key"/>), with their names for the Settings page.</summary>
    public List<HiddenThing> Hidden { get; set; } = new();
    /// <summary>Show devices that only have sensors (nothing to switch or set).</summary>
    public bool ShowSensorsOnly { get; set; } = true;
    /// <summary>The grid's columns; 0 = as many as fit (a box ~250 wide).</summary>
    public int GridColumns { get; set; }
    /// <summary>The grid's rows to fit in the tab's height; 0 = boxes as tall as they need.</summary>
    public int GridRows { get; set; }

    // ---- shortcuts

    /// <summary>Each entity's shortcuts (by entity id), kept here through Core's Hotkeys.</summary>
    public Dictionary<string, EntityKeys> Keys { get; set; } = new();

    [JsonIgnore]
    public bool HasServer => Address.Trim().Length > 0;

    public bool IsHidden(string key) => Hidden.Any(h => h.Key == key);

    /// <summary>The address as a base URL (scheme, host, port, path), e.g. http://homeassistant.local:8123.</summary>
    public Uri? BaseUri()
    {
        string a = Address.Trim().TrimEnd('/');
        if (a.Length == 0) return null;
        if (!a.Contains("://")) a = "http://" + a;
        if (!Uri.TryCreate(a, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) return null;
        // no port given: Home Assistant's own 8123 over http; https (a proxy, Nabu Casa) on 443
        bool portGiven = System.Text.RegularExpressions.Regex.IsMatch(a, @"^[a-z]+://[^/]*:\d+(/|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!portGiven && u.Scheme == "http") u = new UriBuilder(u) { Port = 8123 }.Uri;
        return u;
    }

    /// <summary>The WebSocket API's address: ws(s)://…/api/websocket.</summary>
    public Uri? WebSocketUri()
    {
        if (BaseUri() is not { } u) return null;
        var b = new UriBuilder(u) { Scheme = u.Scheme == "https" ? "wss" : "ws" };
        b.Path = b.Path.TrimEnd('/') + "/api/websocket";
        return b.Uri;
    }

    public static HaSettings Load() => JsonStore.Load<HaSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);

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

public sealed class HiddenThing
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>One entity's shortcuts: on / off, and up / down for one with a level (brightness, position, volume…).</summary>
public sealed class EntityKeys
{
    public string Name { get; set; } = "";
    public bool CanToggle { get; set; }
    public bool HasLevel { get; set; }
    public string? Toggle { get; set; }
    public string? Up { get; set; }
    public string? Down { get; set; }

    [JsonIgnore]
    public bool Any => Toggle != null || Up != null || Down != null;
}
