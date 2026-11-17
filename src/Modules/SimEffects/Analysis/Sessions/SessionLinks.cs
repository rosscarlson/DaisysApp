using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using DaisysApp.Server;
using DaisysApp.Shared;
using DaisysApp.Shell;
using DaisysServer.Protocol;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.18.0) A shared session opened from a link, ForzaLeaderBoard's Open in Daisy's App:
/// <c>daisysapp://session/&lt;id&gt;?server=&lt;host&gt;:&lt;port&gt;&amp;key=&lt;fingerprint&gt;</c> (the shell hands it
/// here, Core's <see cref="OpenFiles.Links"/>, in the running copy). Checked first (the id 16 lower-case hex characters,
/// the server host:port, the key 43 base64url characters); Share Session's server (with its sign-in) when it's that
/// one, else that server anonymously with its key pinned, said and allowed once before anything comes from it. The
/// session's downloaded into Imported (once), the app brought to the front on Analysis, and it's Session 1.
/// </summary>
internal static class SessionLinks
{
    private const string BadgeId = "AnalysisLink";
    private static readonly Regex Id = new("^[0-9a-f]{16}$"), Key = new("^[A-Za-z0-9_-]{43}$"),
        Server = new(@"^(?<host>[A-Za-z0-9.-]+|\[[0-9A-Fa-f:.]+\]):(?<port>\d{1,5})$");
    private static bool busy;

    public static async void Open(Uri link, SessionsView view)
    {
        AppNavigation.OpenTab("SimEffects/" + SimEffectsApplet.Analysis);
        string id = link.AbsolutePath.Trim('/');
        var query = link.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .GroupBy(p => Uri.UnescapeDataString(p[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1].Replace('+', ' ')), StringComparer.OrdinalIgnoreCase);
        query.TryGetValue("server", out var server);
        query.TryGetValue("key", out var key);
        var leaderboard = SessionServer.Leaderboard;
        if (!Id.IsMatch(id)) { Say(T("That link isn't to a shared session")); return; }
        string host;
        int port;
        if (server == null && key == null)
        {
            // (no server named: Share Session's)
            (host, port, key) = (leaderboard.Host, leaderboard.Port, leaderboard.Key);
        }
        else
        {
            if (server == null || key == null || Server.Match(server) is not { Success: true } m || !int.TryParse(m.Groups["port"].Value, out port)
                || port is < 1 or > 65535 || !Key.IsMatch(key))
            {
                Say(T("That link's server isn't right"));
                return;
            }
            host = m.Groups["host"].Value.Trim('[', ']');
        }
        if (busy) return;
        busy = true;
        ServerClient? other = null;
        try
        {
            SessionServer from;
            // (the server is who has the key: a link naming forzaleaderboard.com is the built-in www.forzaleaderboard.com)
            bool ours = port == leaderboard.Port && (key == leaderboard.Key || host.Equals(leaderboard.Host, StringComparison.OrdinalIgnoreCase));
            if (ours && leaderboard.Key.Length > 0 && leaderboard.Key != key)
            {
                Say(F("The link's key for {0} isn't that server's: not opened", $"{host}:{port}"));
                return;
            }
            if (ours) from = SessionServer.Shared;
            else
            {
                string known = $"{host}:{port} {key}";
                var all = SessionShares.All;
                if (!all.Servers.Contains(known))
                {
                    if (!Ui.Ask(view.Cards.FirstOrDefault(), F("This session is on another server: {0}. Download it from there?", $"{host}:{port}"), T("Download"), T("Cancel"))) return;
                    all.Servers.Add(known);
                    all.Save();
                }
                Badge(T("Connecting…"));
                other = await ServerClient.ConnectAsync(host, port, key, null, null, Environment.MachineName, AppPaths.ShortName, ServerLink.Device(), CancellationToken.None);
                var client = other;
                from = new SessionServer(ct => client.OpenAsync(SessionServer.Module, "Analysis", ct));
            }
            Badge(F("Downloading {0:0}%", 0));
            var progress = new Progress<double>(p => Badge(F("Downloading {0:0}%", p * 100)));
            string got = await from.FetchAsync(id, null, null, progress);
            TitleBadges.Set(BadgeId, null);
            Logging.Log.Here.Info($"Analysis: session {id} opened from a link ({host}:{port})");
            await view.ShowLinked(got);
            if (Application.Current?.MainWindow is { } w) w.Activate();
        }
        catch (Exception ex) when (ex is ServerException or IOException or OperationCanceledException or InvalidDataException or FormatException)
        {
            Logging.Log.Here.Warn($"Analysis: session {id} from a link ({host}:{port}): {ex.Message}");
            Say(ex.Message.TrimEnd('.'));
        }
        finally
        {
            busy = false;
            other?.Dispose();
        }
    }

    private static void Badge(string text) =>
        TitleBadges.Set(BadgeId, F("Analysis: {0}", text), T("A shared session from a link"), "#58A6FF", "SimEffects/" + SimEffectsApplet.Analysis);

    /// <summary>A problem: the bubble gone, and said as a notification.</summary>
    private static void Say(string problem)
    {
        TitleBadges.Set(BadgeId, null);
        Logging.Log.Here.Info($"Analysis: a link: {problem}");
        Notifications.Show(T("Shared session"), problem);
    }
}
