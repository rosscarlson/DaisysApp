using System.Text.RegularExpressions;

namespace DaisysApp.Applets.Video.FireTv;

public enum AppState { Installed, Disabled, Removed }

/// <summary>A package on the device, with what the catalog knows about it.</summary>
internal sealed class TvApp
{
    public string Package { get; init; } = "";
    public string Name { get; init; } = "";
    public string Group { get; init; } = "";
    public Advice Advice { get; init; }
    public string Note { get; init; } = "";
    /// <summary>Installed by the user (pm list packages -3): removing it deletes it.</summary>
    public bool Theirs { get; init; }
    public bool Locked { get; init; }
    public AppState State { get; set; }
}

/// <summary>A connected Fire TV: what it is, its packages, and removing / putting them back over <see cref="AdbClient"/>.</summary>
internal sealed partial class FireTvDevice : IDisposable
{
    public const int Port = 5555;
    private static readonly Logging.Logger log = Logging.Log.Here;

    public AdbClient Adb { get; }
    public string Model { get; private set; } = "";
    public string Maker { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string FireOs { get; private set; } = "";
    public string Android { get; private set; } = "";
    public string Serial { get; private set; } = "";
    public string ModelName => Models.Name(Model);
    public bool IsAmazon => Maker.Contains("Amazon", StringComparison.OrdinalIgnoreCase) || Model.StartsWith("AFT", StringComparison.OrdinalIgnoreCase);

    private FireTvDevice(AdbClient adb) => Adb = adb;

    /// <summary>"192.168.1.20" or "192.168.1.20:5555".</summary>
    public static (string Host, int Port) Parse(string address)
    {
        address = address.Trim();
        int colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out var p) && p is > 0 and < 65536) return (address[..colon], p);
        return (address, Port);
    }

    public static async Task<FireTvDevice> ConnectAsync(string address, Action asking, CancellationToken ct)
    {
        var (host, port) = Parse(address);
        var d = new FireTvDevice(await AdbClient.ConnectAsync(host, port, asking, ct));
        try
        {
            var lines = (await d.Adb.ShellAsync("getprop ro.product.model; getprop ro.product.manufacturer; getprop ro.build.version.name; " +
                "getprop ro.build.version.release; getprop ro.serialno; settings get global device_name", ct)).Split('\n');
            string At(int i) => i < lines.Length ? lines[i].Trim() : "";
            d.Model = At(0);
            d.Maker = At(1);
            d.FireOs = At(2);
            d.Android = At(3);
            d.Serial = At(4).Length > 0 ? At(4) : $"{d.Model}@{host}";
            d.Name = At(5) is { Length: > 0 } n && n != "null" ? n : d.ModelName;
            log.Info($"Connected to {d.Name} ({d.ModelName}, {d.Model}) at {d.Adb.Address}: {d.FireOs}, Android {d.Android}");
            return d;
        }
        catch
        {
            d.Dispose();
            throw;
        }
    }

    public async Task<List<TvApp>> AppsAsync(CancellationToken ct = default)
    {
        var parts = (await Adb.ShellAsync("pm list packages -u; echo @@; pm list packages; echo @@; pm list packages -d; echo @@; pm list packages -3", ct)).Split("@@");
        HashSet<string> Set(int i) => i < parts.Length
            ? parts[i].Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("package:")).Select(l => l[8..]).ToHashSet()
            : new HashSet<string>();
        var all = Set(0);
        var installed = Set(1);
        var disabled = Set(2);
        var theirs = Set(3);
        all.UnionWith(installed);
        var apps = new List<TvApp>();
        foreach (var p in all)
        {
            var k = Catalog.Find(p);
            bool mine = theirs.Contains(p);
            apps.Add(new TvApp
            {
                Package = p,
                Name = k?.Name ?? p,
                Group = k?.Group ?? (mine ? Catalog.Yours : Catalog.System),
                Advice = k?.Advice ?? (mine ? Advice.Remove : Advice.Optional),
                Note = k?.Note ?? "",
                Theirs = mine,
                Locked = !mine && Catalog.Locked(p),
                State = !installed.Contains(p) ? AppState.Removed : disabled.Contains(p) ? AppState.Disabled : AppState.Installed,
            });
        }
        return apps;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.]+$")]
    private static partial Regex PackageName();

    private static string Check(string package) =>
        PackageName().IsMatch(package) ? package : throw new AdbException(F("Not a package name: {0}", package));

    /// <summary>
    /// Removes for user 0 (the TV's only user: gone from the TV, its file kept, so it can come back), or, when Fire OS
    /// doesn't allow that, disables it. Returns how ("uninstall" / "disable" / "gone" for the user's own apps) or null
    /// and what the TV said.
    /// </summary>
    public async Task<(string? How, string Said)> RemoveAsync(TvApp app, CancellationToken ct = default)
    {
        var p = Check(app.Package);
        var said = (await Adb.ShellAsync($"pm uninstall --user 0 {p}", ct)).Trim();
        if (said.StartsWith("Success")) return (app.Theirs ? "gone" : "uninstall", said);
        if (app.Theirs) return (null, said);
        var off = (await Adb.ShellAsync($"pm disable-user --user 0 {p}", ct)).Trim();
        if (off.Contains("disabled", StringComparison.OrdinalIgnoreCase) && !off.Contains("Exception")) return ("disable", off);
        return (null, said + " / " + off);
    }

    /// <summary>Puts a removed or disabled package back (both ways are tried, so it works however it went).</summary>
    public async Task<(bool Ok, string Said)> RestoreAsync(string package, CancellationToken ct = default)
    {
        var p = Check(package);
        var said = (await Adb.ShellAsync($"cmd package install-existing --user 0 {p} ; pm enable --user 0 {p} ; pm list packages {p}", ct)).Trim();
        bool ok = said.Split('\n').Any(l => l.Trim() == "package:" + p);
        return (ok, said);
    }

    public async Task<string> GetSettingAsync(string space, string key, CancellationToken ct = default) =>
        (await Adb.ShellAsync($"settings get {space} {key}", ct)).Trim();

    /// <summary>Sets a setting; "null" deletes it (as it was when it had no value).</summary>
    public Task<string> PutSettingAsync(string space, string key, string value, CancellationToken ct = default) =>
        Adb.ShellAsync(value == "null" ? $"settings delete {space} {key}" : $"settings put {space} {key} {value}", ct);

    public async Task RebootAsync()
    {
        try { await Adb.ShellAsync("reboot", default, 5); } catch (AdbException) { }
        Dispose();
    }

    public void Dispose() => Adb.Dispose();
}
