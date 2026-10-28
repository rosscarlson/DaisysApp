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

    public GameSwitcher() => timer.Tick += async (_, _) => await Check();

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
            if (wanted.Count == 0 && played == null) { lastGame = null; return; }
            var running = await RunningPrograms.GetAsync();
            if (closing) return;
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

/// <summary>The programs running (names as the process list has them), looked at once for every caller within two seconds.</summary>
internal static class RunningPrograms
{
    private static HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime at = DateTime.MinValue;
    private static Task<HashSet<string>>? looking;

    public static Task<HashSet<string>> GetAsync()
    {
        if (DateTime.UtcNow - at < TimeSpan.FromSeconds(2)) return Task.FromResult(names);
        return looking ??= Look();
    }

    private static async Task<HashSet<string>> Look()
    {
        try
        {
            names = await Task.Run(() =>
            {
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var proc in System.Diagnostics.Process.GetProcesses())
                {
                    try { found.Add(proc.ProcessName); } catch { }
                    proc.Dispose();
                }
                return found;
            });
            at = DateTime.UtcNow;
            return names;
        }
        finally { looking = null; }
    }
}
