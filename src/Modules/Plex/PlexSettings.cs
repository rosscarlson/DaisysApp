using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Plex;

/// <summary>Saved in %APPDATA%\DaisysApp\PlexServer.json (Plex.json is the tab's: its sub-tab). Tokens and keys are encrypted for this Windows account.</summary>
public sealed class PlexSettings
{
    private const string FileName = "PlexServer";

    // ---- Plex Media Server

    /// <summary>Where the server is: http://127.0.0.1:32400, an IP address, or a plex.direct https:// address.</summary>
    public string Address { get; set; } = "";
    public bool IgnoreCertificate { get; set; }
    public string TokenProtected { get; set; } = "";
    [JsonIgnore]
    public string Token { get => Unprotect(TokenProtected); set => TokenProtected = Protect(value); }
    /// <summary>A connection worked with these settings.</summary>
    public bool Configured { get; set; }
    /// <summary>Skip in the setup: don't ask again until it's filled in.</summary>
    public bool SetupSkipped { get; set; }
    /// <summary>This app's own id with plex.tv (its sign-in and requests), made once.</summary>
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");

    // ---- Tautulli

    public string TautulliAddress { get; set; } = "";
    public string TautulliKeyProtected { get; set; } = "";
    [JsonIgnore]
    public string TautulliKey { get => Unprotect(TautulliKeyProtected); set => TautulliKeyProtected = Protect(value); }
    public bool TautulliConfigured { get; set; }

    // ---- backups (the database's, by the scheduled task's script)

    /// <summary>Where backups go; empty = "Plex Backups" on the drive Plex keeps its data on (not Documents: often OneDrive's).</summary>
    public string BackupFolder { get; set; } = "";
    /// <summary>How many backups to keep (the oldest go).</summary>
    public int BackupKeep { get; set; } = 10;
    public bool BackupScheduled { get; set; }
    /// <summary>Daily, or Weekly on <see cref="BackupDay"/>.</summary>
    public bool BackupWeekly { get; set; }
    public DayOfWeek BackupDay { get; set; } = DayOfWeek.Sunday;
    /// <summary>The time of day, "HH:mm".</summary>
    public string BackupTime { get; set; } = "04:00";

    // ---- posters

    public BannerStyle Banner { get; set; } = new();
    /// <summary>Posters with a banner put on by the Posters tab, by rating key (to take it off again).</summary>
    public Dictionary<string, Overlaid> Overlaid { get; set; } = new();
    /// <summary>The library picked last on the Posters tab.</summary>
    public string? PosterLibrary { get; set; }

    // ---- Tautulli's tab

    public int StatsDays { get; set; } = 30;

    [JsonIgnore]
    public bool HasServer => Address.Trim().Length > 0;
    [JsonIgnore]
    public bool HasTautulli => TautulliAddress.Trim().Length > 0 && TautulliKey.Length > 0;

    [JsonIgnore]
    public string BackupFolderOrDefault => BackupFolder.Trim().Length > 0 ? BackupFolder.Trim()
        : Path.Combine(Path.GetPathRoot(PlexLocal.DataFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ?? @"C:\", "Plex Backups");

    /// <summary>The server's address as a base URL; no port given means Plex's 32400 over http.</summary>
    public Uri? BaseUri() => Url(Address, 32400);

    public Uri? TautulliUri() => Url(TautulliAddress, 8181);

    internal static Uri? Url(string address, int port)
    {
        string a = address.Trim().TrimEnd('/');
        if (a.Length == 0) return null;
        if (!a.Contains("://")) a = "http://" + a;
        if (!Uri.TryCreate(a, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) return null;
        bool portGiven = System.Text.RegularExpressions.Regex.IsMatch(a, @"^[a-z]+://[^/]*:\d+(/|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!portGiven && u.Scheme == "http") u = new UriBuilder(u) { Port = port }.Uri;
        return u;
    }

    public static PlexSettings Load() => JsonStore.Load<PlexSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
    /// <summary>Saved a moment after the last change (a slider).</summary>
    public void SaveSoon() => JsonStore.SaveSoon(FileName, this);

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

/// <summary>The banner put across the top of a poster.</summary>
public sealed class BannerStyle
{
    /// <summary>Text for a 4K title without HDR, with HDR10 / HLG, and with Dolby Vision.</summary>
    public string Text4K { get; set; } = "4K";
    public string TextHdr { get; set; } = "4K HDR";
    public string TextDv { get; set; } = "4K DOLBY VISION";
    /// <summary>The banner's height as a share of the poster's (0.06–0.2).</summary>
    public double Height { get; set; } = 0.09;
    public string Background { get; set; } = "#FF000000";
    public string Foreground { get; set; } = "#FFFFFF";
    /// <summary>A thin line in this colour under the banner; empty for none.</summary>
    public string Line { get; set; } = "#E5A00D";
    public bool Bold { get; set; } = true;
}

/// <summary>A poster with a banner on it: what it said, and the poster selected before (to put back).</summary>
public sealed class Overlaid
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>The poster selected before (its URL in the item's /posters list), or null if unknown.</summary>
    public string? Original { get; set; }
    /// <summary>That poster's image on the server (for the preview).</summary>
    public string? OriginalThumb { get; set; }
    public DateTime When { get; set; }
}
