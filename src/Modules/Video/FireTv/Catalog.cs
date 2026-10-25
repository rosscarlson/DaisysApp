namespace DaisysApp.Applets.Video.FireTv;

/// <summary>Fire TV models by their code (ro.product.model). Fire OS models (Android) take ADB; Vega OS ones don't.</summary>
internal static class Models
{
    private static readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AFTB"] = "Fire TV (1st gen)",
        ["AFTM"] = "Fire TV Stick (1st gen)",
        ["AFTS"] = "Fire TV (2nd gen)",
        ["AFTT"] = "Fire TV Stick (2nd gen)",
        ["AFTN"] = "Fire TV (3rd gen)",
        ["AFTA"] = "Fire TV Cube (1st gen)",
        ["AFTMM"] = "Fire TV Stick 4K (1st gen)",
        ["AFTR"] = "Fire TV Cube (2nd gen)",
        ["AFTSS"] = "Fire TV Stick Lite",
        ["AFTSSS"] = "Fire TV Stick (3rd gen)",
        ["AFTKA"] = "Fire TV Stick 4K Max (1st gen)",
        ["AFTGAZL"] = "Fire TV Cube (3rd gen)",
        ["AFTKM"] = "Fire TV Stick 4K (2nd gen)",
        ["AFTKRT"] = "Fire TV Stick 4K Max (2nd gen)",
    };

    public static string Name(string code) => names.TryGetValue(code, out var n) ? n : code.StartsWith("AFT", StringComparison.OrdinalIgnoreCase) ? F("Fire TV ({0})", code) : code;
}

public enum Advice { Remove, Optional, Keep }

/// <summary>A package the tool knows: what it is and whether removing it is advised.</summary>
internal sealed record Known(string Package, string Name, string Group, Advice Advice, string Note = "");

/// <summary>
/// What's known about Fire OS's packages. Only packages on the device are shown; anything not here and not installed by
/// the user stays hidden unless Show System is ticked, and <see cref="Locked"/> ones (the home screen, settings,
/// remote, keyboard…) are never offered, since without them the TV can't be used.
/// </summary>
internal static class Catalog
{
    public const string Amazon = "Amazon", Ads = "Ads/Tracking", Yours = "Installed Apps", Updates = "System Updates", System = "System";

    /// <summary>The apps the user keeps by default (Plex, F1 TV, YouTube TV).</summary>
    public static readonly string[] DefaultKeep =
    {
        "com.plexapp.android", "com.formulaone.production", "com.amazon.firetv.youtube.tv", "com.google.android.youtube.tvunplugged",
    };

    public static readonly Known[] All =
    {
        // ads, recommendations and tracking
        new("com.amazon.kso", "Special offers", Ads, Advice.Remove, "Ads"),
        new("com.amazon.aria", "Recommendations", Ads, Advice.Remove),
        // (from a Fire TV Stick 4K 2nd gen on Fire OS 8.1.8.5: hedwig and cloud9 are turned back on at every restart; logmanager,
        // crashmanager and the OTA updaters can be neither disabled nor removed: DELETE_FAILED_INTERNAL_ERROR)
        new("com.amazon.hedwig", "Push notifications", Ads, Advice.Optional, "Fire OS turns it back on"),
        new("com.amazon.bueller.photos", "Amazon Photos screensaver", Ads, Advice.Remove),
        new("com.amazon.ftv.screensaver", "Screensaver", Ads, Advice.Optional),
        new("com.amazon.device.crashmanager", "Crash reports", Ads, Advice.Optional, "Tracking; Fire OS 8 keeps it"),
        new("com.amazon.client.metrics", "Usage metrics", Ads, Advice.Remove, "Tracking"),
        new("com.amazon.client.metrics.api", "Usage metrics library", Ads, Advice.Optional, "Tracking; some apps use it"),
        new("com.amazon.dp.logger", "Device logger", Ads, Advice.Remove, "Tracking"),
        new("com.amazon.device.logmanager", "Log manager", Ads, Advice.Optional, "Tracking; Fire OS 8 keeps it"),
        new("com.amazon.tv.fireos.metrics", "Fire OS metrics", Ads, Advice.Remove, "Tracking"),
        new("com.amazon.ags.app", "GameCircle", Ads, Advice.Remove),
        // Amazon's apps
        new("com.amazon.avod", "Prime Video", Amazon, Advice.Remove),
        new("com.amazon.firebat", "Prime Video", Amazon, Advice.Remove),
        new("com.amazon.amazonvideo.livingroom", "Prime Video", Amazon, Advice.Remove),
        new("com.amazon.imdb.tv.android.app", "Freevee", Amazon, Advice.Remove),
        new("com.amazon.bueller.music", "Amazon Music", Amazon, Advice.Remove),
        new("com.amazon.mp3", "Amazon Music", Amazon, Advice.Remove),
        new("com.amazon.cloud9", "Silk Browser", Amazon, Advice.Optional, "Fire OS turns it back on"),
        new("com.amazon.tv.livetv", "Live TV", Amazon, Advice.Optional),
        new("com.amazon.awvflingreceiver", "Casting from Amazon apps", Amazon, Advice.Optional),
        new("com.amazon.tv.alexaalerts", "Alexa alerts", Amazon, Advice.Optional),
        new("com.amazon.vizzini", "Alexa", Amazon, Advice.Optional, "Voice search on the remote"),
        new("com.amazon.alexa.externalmediaplayer.fireos", "Alexa media player", Amazon, Advice.Optional),
        new("com.amazon.venezia", "Appstore", Amazon, Advice.Keep, "Updates your apps"),
        new("com.netflix.ninja", "Netflix", Amazon, Advice.Remove),
        // system updates (they can bring removed apps back)
        new("com.amazon.device.software.ota", "System updates", Updates, Advice.Optional, "Updates can bring removed apps back; Fire OS 8 keeps it"),
        new("com.amazon.device.software.ota.override", "System updates (override)", Updates, Advice.Optional, "Fire OS 8 keeps it"),
        new("com.amazon.tv.forcedotaupdater.v2", "Forced updates", Updates, Advice.Optional),
        // apps people install, named
        new("com.plexapp.android", "Plex", Yours, Advice.Keep),
        new("com.formulaone.production", "F1 TV", Yours, Advice.Keep),
        new("com.amazon.firetv.youtube.tv", "YouTube TV", Yours, Advice.Keep),
        new("com.google.android.youtube.tvunplugged", "YouTube TV", Yours, Advice.Keep),
        new("com.amazon.firetv.youtube", "YouTube", Yours, Advice.Remove),
        new("com.disney.disneyplus", "Disney+", Yours, Advice.Remove),
        new("com.hulu.plus", "Hulu", Yours, Advice.Remove),
        new("com.spotify.tv.android", "Spotify", Yours, Advice.Remove),
        new("com.wbd.stream", "Max", Yours, Advice.Remove),
        new("com.peacocktv.peacockandroid", "Peacock", Yours, Advice.Remove),
        new("com.cbs.ott", "Paramount+", Yours, Advice.Remove),
        new("com.apple.atve.amazon.appletv", "Apple TV", Yours, Advice.Remove),
        new("com.pluto.tv", "Pluto TV", Yours, Advice.Remove),
        new("com.tubitv", "Tubi", Yours, Advice.Remove),
    };

    private static readonly Dictionary<string, Known> byPackage = All.GroupBy(k => k.Package).ToDictionary(g => g.Key, g => g.First());

    public static Known? Find(string package) => byPackage.GetValueOrDefault(package);

    /// <summary>Parts of the TV it can't be used without: never offered.</summary>
    private static readonly string[] lockedParts =
    {
        "launcher", "settings", "systemui", "keyboard", ".ime", "inputmethod", "bluetooth", "controller", "remote", "oobe",
        "providers", "packageinstaller", "permission", "webview", "framework", "wifi", "network", "connectivity", "hdmi",
        "display", "audio", "input", "media", "shell", "service", "phone", "telephony", "certinstaller", "location",
        "systembrowserprovider", "device.sync", "identity", "account", "authenticator", "map", "dcp", "frameworks",
    };

    public static bool Locked(string package)
    {
        if (byPackage.ContainsKey(package)) return false;
        if (package == "android" || package.StartsWith("com.android.", StringComparison.Ordinal)) return true;
        return lockedParts.Any(p => package.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
