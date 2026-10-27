using System.Windows.Threading;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// Game Switching for the whole module: every 3 seconds, a profile's program starting switches every tool to that
/// profile (once: picking another by hand meanwhile stays), and when none of them runs any more, to the No Game
/// profile (<see cref="SimProfilesSettings.NoGame"/>), if there is one.
/// </summary>
internal sealed class GameSwitcher : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? lastGame;
    private bool checking, closing, started;

    public GameSwitcher() => timer.Tick += async (_, _) => await Check();

    public void Start()
    {
        timer.Start();
        _ = Check();
    }

    private async Task Check()
    {
        var s = SimProfiles.Settings;
        if (checking || closing || !s.GameSwitching) return;
        checking = true;
        try
        {
            var wanted = SimProfiles.All.SelectMany(p => p.Programs.Select(prog => (p, prog: Games.Bare(prog)))).ToList();
            var running = await RunningPrograms.GetAsync();
            if (closing) return;
            var hit = wanted.FirstOrDefault(w => running.Contains(w.prog));
            string? game = hit.p != null ? hit.prog : null;
            // (at the start with no game running, the profile in use stays: it's where it was left)
            if (game == lastGame && started) return;
            bool first = !started;
            started = true;
            lastGame = game;
            if (hit.p != null) SimProfiles.Use(hit.p, $"{game}.exe is running");
            else if (!first && SimProfiles.All.FirstOrDefault(p => p.Name == s.NoGame) is { } none) SimProfiles.Use(none, "no game running");
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
