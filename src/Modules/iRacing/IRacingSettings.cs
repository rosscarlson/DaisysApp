using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>Saved in %APPDATA%\DaisysApp\iRacing.json. The password, the client secret and the sign-in (refresh
/// token) are encrypted for this Windows account.</summary>
public sealed class IRacingSettings
{
    private const string FileName = "iRacing";

    // ---- signing in (iRacing's OAuth "password limited" sign-in: your account and your own OAuth client)

    public string Email { get; set; } = "";
    public string PasswordProtected { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecretProtected { get; set; } = "";
    /// <summary>The last sign-in's refresh token (used once, then replaced), so the password needn't be sent each time.</summary>
    public string RefreshTokenProtected { get; set; } = "";
    public DateTime RefreshTokenExpires { get; set; }

    [JsonIgnore]
    public string Password { get => Unprotect(PasswordProtected); set => PasswordProtected = Protect(value); }

    [JsonIgnore]
    public string ClientSecret { get => Unprotect(ClientSecretProtected); set => ClientSecretProtected = Protect(value); }

    [JsonIgnore]
    public string RefreshToken { get => Unprotect(RefreshTokenProtected); set => RefreshTokenProtected = Protect(value); }

    [JsonIgnore]
    public bool HasCredentials => Email.Trim().Length > 0 && PasswordProtected.Length > 0 && ClientId.Trim().Length > 0 && ClientSecretProtected.Length > 0;

    // ---- what's fetched

    /// <summary>Hosted and league races too, not only official series.</summary>
    public bool IncludeHosted { get; set; } = true;

    /// <summary>Fetch what's new when the tab opens, if the last time was more than a few hours ago.</summary>
    public bool SyncWhenOpened { get; set; } = true;

    /// <summary>Hours between those.</summary>
    public int SyncHours { get; set; } = 6;

    // ---- what's shown

    /// <summary>The category shown ("" = all).</summary>
    public string Category { get; set; } = "";

    /// <summary>The time shown: All, Year, Twelve, Ninety, Thirty.</summary>
    public string Range { get; set; } = "All";

    public bool OfficialOnly { get; set; }

    public static IRacingSettings Load() => JsonStore.Load<IRacingSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);

    /// <summary>Forgets the sign-in (after the account or client changes).</summary>
    public void ForgetSignIn()
    {
        RefreshTokenProtected = "";
        RefreshTokenExpires = default;
    }

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
