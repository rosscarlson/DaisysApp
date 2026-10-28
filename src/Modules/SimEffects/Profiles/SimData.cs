namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>
/// The game's telemetry for the whole module (since 3.18.0): the profile in use's game and port, read once (one reader
/// in <see cref="TelemetryHub"/>) and handed to every tool that asks. A tool calls <see cref="Open"/> and reads the
/// source it gets, as it would a game's own reader: it follows the profile, so when another profile (another game) is
/// used, the next read comes from that one, with nothing for the tool to do.
/// </summary>
public static class SimData
{
    private static volatile Box current = new(null);

    private sealed record Box((Game Game, int Port)? Key);

    /// <summary>The game being read and its port (null: the profile has none).</summary>
    public static (Game Game, int Port)? Game => current.Key;

    /// <summary>The game changed (another profile, or the profile's game or port edited).</summary>
    public static event Action? Changed;

    private static SimProfile? followed;
    private static int? running;

    /// <summary>The game's version running (a program of the profile's game; <see cref="GameSwitcher"/> looks).</summary>
    internal static void Running(int? version)
    {
        if (version == running) return;
        running = version;
        if (followed != null) FollowVersion(followed);
    }

    /// <summary>The Forza being read (<see cref="CarNames.Edition"/>): the profile's version, or the one running.</summary>
    private static void FollowVersion(SimProfile p)
    {
        string? edition = p.Game is { } g ? CarNames.Key(g, p.Version ?? running) : null;
        if (edition == CarNames.Edition) return;
        CarNames.Edition = edition;
        if (edition != null) Logging.Log.Here.Info($"SimEffects: cars named as in {edition}{(p.Version == null ? " (the one running, or the newest)" : "")}");
        Changed?.Invoke();
    }

    internal static void Follow(SimProfile p)
    {
        followed = p;
        FollowVersion(p);
        (Game, int)? key = p.Game is { } g ? (g, Games.UsesPort(g) ? p.Port : 0) : null;
        if (key == current.Key) return;
        current = new Box(key);
        Logging.Log.Here.Info(key is { } k ? $"SimEffects: reading {k.Item1}{(Games.UsesPort(k.Item1) ? $" on UDP {k.Item2}" : "")}" : "SimEffects: no game to read");
        Changed?.Invoke();
    }

    /// <summary>The telemetry of the profile in use's game, for one tool. Dispose it when done.</summary>
    public static ITelemetrySource Open() => new Follower();

    /// <summary>What a tool's status line says about the game: driving, not driving, or its problem; nothing while
    /// it's not sending (since 3.24.3: the Play button's colour says whether the game is running).</summary>
    public static string Status(ITelemetrySource? source, bool receiving, bool live)
    {
        if (Game is not { } g) return T("No game in this profile");
        if (source?.Problem is { } problem) return problem;
        string game = Games.Title(g.Game);
        if (receiving) return live ? F("{0}: driving", game) : F("{0}: not driving", game);
        return "";
    }

    /// <summary>A program of the profile in use's game is running (since 3.24.3; <see cref="GameSwitcher"/> looks every
    /// 3 seconds): the Play button's colour.</summary>
    public static bool GameRunning { get; private set; }

    /// <summary><see cref="GameRunning"/> changed (on the UI thread).</summary>
    public static event Action? GameRunningChanged;

    internal static void SetGameRunning(bool on)
    {
        if (on == GameRunning) return;
        GameRunning = on;
        Logging.Log.Here.Info($"SimEffects: the profile's game {(on ? "is running" : "isn't running")}");
        GameRunningChanged?.Invoke();
    }

    /// <summary>A lease on the game in use's reader, opened again when the game changes.</summary>
    private sealed class Follower : ITelemetrySource
    {
        private ITelemetrySource? lease;
        private Box? leased;
        private bool disposed;

        public string? Problem => lease?.Problem;

        public bool Read(TelemetryFrame frame, int timeoutMs)
        {
            if (disposed) { Thread.Sleep(timeoutMs); return false; }
            var want = current;
            if (!ReferenceEquals(want, leased))
            {
                lease?.Dispose();
                lease = want.Key is { } k ? TelemetryHub.Open(k.Game, k.Port) : null;
                leased = want;
            }
            if (lease == null) { Thread.Sleep(Math.Max(1, timeoutMs)); return false; }
            return lease.Read(frame, timeoutMs);
        }

        public void Dispose()
        {
            disposed = true;
            lease?.Dispose();
            lease = null;
        }
    }
}
