using System.Text.Json;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex;

/// <summary>
/// The module's connection to its Plex Media Server and Tautulli: which server it is (name, version, identifier), whether
/// it's this PC's (its identifier is the one in this account's registry, so its folders and database are here), and the
/// clients every tab uses. Nothing runs in the background: the tabs ask while they show.
/// </summary>
public sealed class PlexService
{
    public PlexSettings Settings { get; }
    public PlexClient? Client { get; private set; }
    public TautulliClient? Tautulli { get; private set; }

    public bool Connected { get; private set; }
    public bool Connecting { get; private set; }
    public string? Error { get; private set; }
    public string ServerName { get; private set; } = "";
    public string Version { get; private set; } = "";
    public string MachineId { get; private set; } = "";
    public string Platform { get; private set; } = "";
    /// <summary>The server set up is the one on this PC: its database, logs and program are here.</summary>
    public bool IsLocal { get; private set; }

    public bool TautulliConnected { get; private set; }
    public string? TautulliError { get; private set; }
    public string TautulliVersion { get; private set; } = "";

    /// <summary>The connection changed (from any thread; views go to their dispatcher).</summary>
    public event Action? Changed;

    private int generation;

    public PlexService(PlexSettings settings) => Settings = settings;

    public void Start() => Reconnect();

    public void Reconnect() => _ = ConnectAsync();

    private async Task ConnectAsync()
    {
        int gen = Interlocked.Increment(ref generation);
        Client = PlexClient.For(Settings);
        Tautulli = TautulliClient.For(Settings);
        Connecting = Client != null;
        Connected = false;
        Error = null;
        TautulliConnected = false;
        TautulliError = null;
        Raise();
        if (Client is { } c)
        {
            try
            {
                var root = await c.GetAsync("/");
                if (gen != generation) return;
                ServerName = PlexClient.Str(root, "friendlyName") ?? "Plex";
                Version = PlexClient.Str(root, "version") ?? "";
                MachineId = PlexClient.Str(root, "machineIdentifier") ?? "";
                Platform = PlexClient.Str(root, "platform") ?? "";
                IsLocal = MachineId.Length > 0 && string.Equals(MachineId, PlexLocal.MachineIdentifier, StringComparison.OrdinalIgnoreCase);
                Connected = true;
                Log.Here.Info($"Connected to Plex \"{ServerName}\" {Version}{(IsLocal ? " (this PC)" : "")}");
            }
            catch (Exception ex)
            {
                if (gen != generation) return;
                Error = ex is PlexException ? ex.Message : F("Couldn't connect: {0}", ex.Message);
                Log.Here.Warn($"Plex: {Error}");
            }
            Connecting = false;
            Raise();
        }
        if (Tautulli is { } t)
        {
            try
            {
                var info = await t.CallAsync("get_tautulli_info");
                if (gen != generation) return;
                TautulliVersion = PlexClient.Str(info, "tautulli_version") ?? "";
                TautulliConnected = true;
            }
            catch (Exception ex)
            {
                if (gen != generation) return;
                TautulliError = ex is PlexException ? ex.Message : F("Couldn't connect: {0}", ex.Message);
                Log.Here.Warn($"Tautulli: {TautulliError}");
            }
            Raise();
        }
    }

    public void Raise() => Changed?.Invoke();

    /// <summary>The server's client, or a sentence saying why there isn't one.</summary>
    public PlexClient Need() => Client ?? throw new PlexException(T("Plex isn't set up: Settings → Plex."));

    /// <summary>The server's libraries: id, name, type (movie, show, artist, photo).</summary>
    public async Task<List<PlexLibrary>> LibrariesAsync()
    {
        var mc = await Need().GetAsync("/library/sections");
        return PlexClient.List(mc, "Directory").Select(d => new PlexLibrary(
            PlexClient.Str(d, "key") ?? "", PlexClient.Str(d, "title") ?? "", PlexClient.Str(d, "type") ?? "",
            string.Join("; ", PlexClient.List(d, "Location").Select(l => PlexClient.Str(l, "path"))),
            PlexClient.Time(d, "scannedAt"), PlexClient.Flag(d, "refreshing"))).ToList();
    }

    /// <summary>How many items a library query finds (asks for none of them).</summary>
    public async Task<long> CountAsync(string path)
    {
        var mc = await Need().GetAsync(path + (path.Contains('?') ? "&" : "?") + "X-Plex-Container-Start=0&X-Plex-Container-Size=0");
        return PlexClient.Num(mc, "totalSize") is > 0 and var n ? n : PlexClient.Num(mc, "size");
    }
}

public sealed record PlexLibrary(string Key, string Title, string Type, string Folders, DateTime? Scanned, bool Refreshing)
{
    public override string ToString() => Title;
    public string TypeName => Type switch
    {
        "movie" => T("Movies"), "show" => T("TV shows"), "artist" => T("Music"), "photo" => T("Photos"), _ => Type,
    };
}
