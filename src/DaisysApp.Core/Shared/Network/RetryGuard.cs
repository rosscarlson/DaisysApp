using DaisysApp.Logging;

namespace DaisysApp.Shared.Network;

/// <summary>
/// (since 4.17.28, Core 0.65) Keeps something that keeps failing (a connection, a server's API) from trying again for
/// ever, fast: each failure in a row waits longer (<c>first</c>, doubled each time, up to <c>longest</c>); after
/// <c>alertAfter</c> in a row it says so once (a warning in the log, and a notification when <c>notify</c>); and with a
/// <c>budget</c>, more tries than that in ten minutes stop it (<see cref="Paused"/>, a warning and a notification)
/// until <see cref="Reset"/> (the user connecting again or changing the settings). <see cref="Succeeded"/> clears it.
/// Every module that connects to something by itself, again and again, goes through one (4.17.26 didn't, and used up
/// the PC's network ports in minutes). Callable from any thread.
/// <code>
/// static readonly RetryGuard guard = new("Zabbix", Log.Here, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
/// if (!guard.Begin()) return;            // too soon after the last failure (or paused): nothing goes out
/// try { …; guard.Succeeded(); }
/// catch (Exception ex) { guard.Failed(ex.Message); }
/// </code>
/// </summary>
public sealed class RetryGuard
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private readonly object gate = new();
    private readonly Queue<long> tries = new();
    private readonly string what;
    private readonly Logger log;
    private readonly TimeSpan first, longest;
    private readonly int alertAfter, budget;
    private readonly bool notify;
    private int failures;
    private long notBefore;
    private bool alerted;

    /// <param name="what">What's tried, as the user knows it ("Daisy's Server", "Zabbix"), in the log and the notification.</param>
    /// <param name="budget">The most tries in ten minutes before it stops until <see cref="Reset"/> (0: no limit, only the waits).</param>
    public RetryGuard(string what, Logger log, TimeSpan first, TimeSpan longest, int alertAfter = 10, int budget = 0, bool notify = false)
    {
        this.what = what;
        this.log = log;
        this.first = first;
        this.longest = longest;
        this.alertAfter = Math.Max(1, alertAfter);
        this.budget = budget;
        this.notify = notify;
    }

    /// <summary>Failures in a row.</summary>
    public int Failures { get { lock (gate) return failures; } }

    /// <summary>Stopped (too many tries in ten minutes) until <see cref="Reset"/>.</summary>
    public bool Paused { get; private set; }

    /// <summary>How long until the next try may go (zero: now).</summary>
    public TimeSpan Wait
    {
        get
        {
            long left = Volatile.Read(ref notBefore) - Environment.TickCount64;
            return left > 0 ? TimeSpan.FromMilliseconds(left) : TimeSpan.Zero;
        }
    }

    /// <summary>Why it isn't trying (paused, or waiting after failures), worded for the user; null while it may.</summary>
    public string? Holding =>
        Paused ? F("{0}: stopped trying after {1} tries in 10 minutes", what, budget)
        : Wait > TimeSpan.Zero ? F("{0}: trying again in {1}", what, Sizes.Duration(Wait))
        : null;

    /// <summary>A try is starting: false (and nothing should go out) while paused or waiting after a failure, or when
    /// it would go over the budget (which pauses it).</summary>
    public bool Begin()
    {
        string? stopped = null;
        lock (gate)
        {
            if (Paused) return false;
            long now = Environment.TickCount64;
            if (now < notBefore) return false;
            if (budget > 0)
            {
                while (tries.Count > 0 && now - tries.Peek() > Window.TotalMilliseconds) tries.Dequeue();
                if (tries.Count >= budget)
                {
                    Paused = true;
                    stopped = $"{what}: {tries.Count} tries in 10 minutes, {failures} failed in a row: stopped trying until it's connected again by hand";
                }
                else tries.Enqueue(now);
            }
        }
        if (stopped == null) return true;
        log.Warn(stopped);
        Shell.Notifications.Show(what, F("Stopped trying to connect after {0} tries in 10 minutes. Connect again in its settings once it's fixed.", budget));
        return false;
    }

    /// <summary>A try failed: the wait before the next one (first, doubled each time in a row, up to longest).</summary>
    public TimeSpan Failed(string why)
    {
        TimeSpan delay;
        bool alert;
        lock (gate)
        {
            failures++;
            double ms = first.TotalMilliseconds * Math.Pow(2, Math.Min(failures - 1, 30));
            delay = TimeSpan.FromMilliseconds(Math.Min(ms, longest.TotalMilliseconds));
            notBefore = Environment.TickCount64 + (long)delay.TotalMilliseconds;
            alert = failures == alertAfter && !alerted;
            if (alert) alerted = true;
        }
        if (alert)
        {
            log.Warn($"{what}: {failures} failures in a row ({why}); trying again at most every {Sizes.Duration(longest)}");
            if (notify) Shell.Notifications.Show(what, F("Can't connect ({0}). Trying again less often.", why.TrimEnd('.')));
        }
        return delay;
    }

    /// <summary>It worked: the waits start again from the first.</summary>
    public void Succeeded()
    {
        int was;
        lock (gate)
        {
            was = failures;
            failures = 0;
            notBefore = 0;
            alerted = false;
        }
        if (was >= alertAfter) log.Info($"{what}: working again after {was} failures");
    }

    /// <summary>The user connected again or changed the settings: everything forgotten, tries allowed at once.</summary>
    public void Reset()
    {
        lock (gate)
        {
            failures = 0;
            notBefore = 0;
            alerted = false;
            tries.Clear();
            Paused = false;
        }
    }
}
