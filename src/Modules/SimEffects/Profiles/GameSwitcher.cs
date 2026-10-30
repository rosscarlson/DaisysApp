using System.Windows.Threading;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// Auto Start for the whole module (Game Switching before 3.23.4): every 3 seconds, a program of a profile with Auto
/// Start on starting uses and plays that profile (once: picking another by hand meanwhile stays), and when it ends,
/// stops it, if it's still the one in use.
/// </summary>
internal sealed class GameSwitcher : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? lastGame;
    private SimProfile? played;
    private bool checking, closing;

    public GameSwitcher()
    {
        timer.Tick += async (_, _) => await Check();
        // (another profile: whether its game runs, now)
        SimProfiles.Changed += () => _ = Check();
    }

    public void Start()
    {
        timer.Start();
        _ = Check();
    }

    private async Task Check()
    {
        if (checking || closing) return;
        checking = true;
        try
        {
            var wanted = SimProfiles.All.Where(p => p.AutoStart == true).SelectMany(p => p.Programs.Select(prog => (p, prog: Games.Bare(prog)))).ToList();
            // (which version of the profile's game is running, when it doesn't say: its cars' names)
            bool version = SimProfiles.Active is { Game: { } ag, Version: null } && CarNames.Versions(ag).Count > 0;
            // (the profile in use's game running: its programs, or its game's)
            var active = SimProfiles.Active;
            var mine = (active.Programs.Count > 0 ? active.Programs : active.Game is { } game0 ? Games.Programs(game0) : new()).Select(Games.Bare).ToList();
            if (wanted.Count == 0 && played == null && !version && mine.Count == 0) { lastGame = null; SimData.SetGameRunning(false); return; }
            var running = (await Shared.RunningPrograms.GetAsync()).Names;
            if (closing) return;
            SimData.SetGameRunning(mine.Any(running.Contains));
            if (version && SimProfiles.Active.Game is { } g)
            {
                int? v = running.Select(r => CarNames.VersionOf(g, r)).FirstOrDefault(x => x != null);
                if (v != null) SimData.Running(v);
            }
            if (wanted.Count == 0 && played == null) { lastGame = null; return; }
            var hit = wanted.FirstOrDefault(w => running.Contains(w.prog));
            string? game = hit.p != null ? hit.prog : null;
            if (game == lastGame) return;
            string? ended = lastGame;
            lastGame = game;
            if (hit.p != null)
            {
                played = hit.p;
                SimProfiles.Use(hit.p, $"{game}.exe started");
            }
            else if (played is { } p)
            {
                played = null;
                if (SimProfiles.Active == p && SimProfiles.Playing) SimProfiles.Play(false, $"{ended}.exe ended");
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("SimEffects games", ex); }
        finally { checking = false; }
    }

    public void Dispose()
    {
        closing = true;
        timer.Stop();
    }
}
