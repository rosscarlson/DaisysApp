using System.Windows.Threading;

namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// Game Switching for a tool with profiles: every 3 seconds, a profile's program starting picks that profile (once:
/// picking another by hand meanwhile stays), and with Switch On With Game switches the tool on if it was off; the game
/// closing switches it off again only if it was the one that switched it on (one switched by hand in between is left).
/// </summary>
internal sealed class GameSwitcher<T> : IDisposable where T : GameProfile
{
    private readonly string tool;
    private readonly IProfileSettings<T> settings;
    private readonly Action<T> use;
    private readonly Func<bool> isOn;
    private readonly Action<bool> setOn;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? lastGame;
    private bool checking, closing, switchedOn, switching;

    /// <param name="use">Uses a profile.</param>
    /// <param name="isOn">Whether the tool is on.</param>
    /// <param name="setOn">Switches the tool on or off (as its ON / OFF does).</param>
    public GameSwitcher(string tool, IProfileSettings<T> settings, Action<T> use, Func<bool> isOn, Action<bool> setOn)
    {
        this.tool = tool;
        this.settings = settings;
        this.use = use;
        this.isOn = isOn;
        this.setOn = setOn;
        timer.Tick += async (_, _) => await Check();
    }

    public void Start()
    {
        timer.Start();
        _ = Check();
    }

    /// <summary>Switched on or off by hand (its ON / OFF, shortcut or tray): a game closing leaves it as it is.</summary>
    public void ByHand()
    {
        if (!switching) switchedOn = false;
    }

    private async Task Check()
    {
        if (checking || closing || !settings.GameSwitching) return;
        checking = true;
        try
        {
            var wanted = settings.Profiles.SelectMany(p => p.Programs.Select(prog => (p, prog: Games.Bare(prog)))).ToList();
            if (wanted.Count == 0 && lastGame == null) return;
            var running = await RunningPrograms.GetAsync();
            if (closing) return;
            var hit = wanted.FirstOrDefault(w => running.Contains(w.prog));
            string? game = hit.p != null ? hit.prog : null;
            if (game == lastGame) return;
            lastGame = game;
            if (hit.p != null)
            {
                Logging.Log.Here.Info($"{tool}: {game}.exe is running");
                use(hit.p);
                if (settings.SwitchOnWithGame && !isOn()) Switch(true);
            }
            else if (switchedOn)
            {
                Logging.Log.Here.Info($"{tool}: the game closed");
                if (isOn()) Switch(false);
                switchedOn = false;
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write($"{tool} games", ex); }
        finally { checking = false; }
    }

    private void Switch(bool on)
    {
        switching = true;
        try { setOn(on); }
        finally { switching = false; }
        switchedOn = on;
    }

    public void Dispose()
    {
        closing = true;
        timer.Stop();
    }
}

/// <summary>The programs running (names as the process list has them), looked at once for every tool asking within
/// two seconds.</summary>
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
