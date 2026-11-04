using System.Windows.Threading;

namespace DaisysApp.Shared;

/// <summary>
/// Runs an action once things have been quiet for a while: each <see cref="Poke"/> starts the wait again (a search box
/// as it's typed in, a slider dragged, settings saved soon after a change). Made on the UI thread, the action runs
/// there; made elsewhere, on the thread pool. (Since Core 0.55: before, each module had its own one-shot timers.)
/// </summary>
public sealed class Debounce : IDisposable
{
    private readonly Action action;
    private readonly TimeSpan delay;
    private readonly DispatcherTimer? ui;
    private readonly Timer? pool;
    private volatile bool pending;

    public Debounce(TimeSpan delay, Action action)
    {
        this.delay = delay;
        this.action = action;
        if (Dispatcher.FromThread(Thread.CurrentThread) is { } d)
        {
            ui = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = delay };
            ui.Tick += (_, _) => Run();
        }
        else pool = new Timer(_ => Run());
    }

    public Debounce(int ms, Action action) : this(TimeSpan.FromMilliseconds(ms), action) { }

    /// <summary>Whether the action is waiting to run.</summary>
    public bool Pending => pending;

    /// <summary>Runs the action after the delay, unless poked again first.</summary>
    public void Poke()
    {
        pending = true;
        if (ui != null)
        {
            if (ui.Dispatcher.CheckAccess()) { ui.Stop(); ui.Start(); }
            else ui.Dispatcher.BeginInvoke(() => { ui.Stop(); ui.Start(); });
        }
        else pool!.Change(delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Runs it now if it's waiting.</summary>
    public void Flush()
    {
        if (!pending) return;
        if (ui != null && !ui.Dispatcher.CheckAccess() && !ui.Dispatcher.HasShutdownStarted) { ui.Dispatcher.Invoke(Flush); return; }
        Run();
    }

    /// <summary>Forgets a waiting run.</summary>
    public void Cancel()
    {
        pending = false;
        if (ui != null) { if (ui.Dispatcher.CheckAccess()) ui.Stop(); else ui.Dispatcher.BeginInvoke(ui.Stop); }
        else pool!.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Run()
    {
        ui?.Stop();
        if (!pending) return;
        pending = false;
        try { action(); }
        catch (Exception ex) { Logging.Log.App.Warn("A delayed action failed", ex); }
    }

    public void Dispose()
    {
        Cancel();
        pool?.Dispose();
    }
}
