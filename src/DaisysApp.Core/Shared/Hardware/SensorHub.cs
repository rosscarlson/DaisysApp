using DaisysApp.Logging;

namespace DaisysApp.Shared.Hardware;

/// <summary>
/// One connection to LibreHardwareMonitor for the whole app: the Sensors applet shows and logs its sensors, and others
/// (Performance's Temperatures tile) read a few of them. Polled on one background thread while anything uses it
/// (<see cref="Use"/>), so LibreHardwareMonitor is asked once, however many applets want its numbers.
/// </summary>
public static class SensorHub
{
    private static readonly HardwareMonitor monitor = new();
    private static readonly object gate = new();
    private static int users;
    private static Thread? thread;
    private static bool wasConnected, checkedOnce;

    /// <summary>Where LibreHardwareMonitor's web server is (Settings → Sensors).</summary>
    public static string Address
    {
        get => monitor.Address;
        set
        {
            monitor.Address = string.IsNullOrWhiteSpace(value) ? HardwareMonitor.DefaultAddress : value.Trim();
            monitor.RetryNow();
        }
    }

    /// <summary>The latest readings, or null while LibreHardwareMonitor isn't answering.</summary>
    public static HwSnapshot? Latest => monitor.Latest;

    public static bool Connected => monitor.Connected;

    /// <summary>
    /// Raised on the background thread with each new reading, or with null when LibreHardwareMonitor stops answering
    /// (and once after the first check).
    /// </summary>
    public static event Action<HwSnapshot?>? Updated;

    /// <summary>Looks for LibreHardwareMonitor again straight away.</summary>
    public static void CheckNow() => monitor.RetryNow();

    /// <summary>Starts polling (if nothing else has); dispose the result to stop using it.</summary>
    public static IDisposable Use()
    {
        lock (gate)
        {
            if (users++ == 0)
            {
                thread = new Thread(Run) { IsBackground = true, Name = "DaisysApp-Sensors", Priority = ThreadPriority.BelowNormal };
                thread.Start();
            }
        }
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            lock (gate) users--;
        }
    }

    private static void Run()
    {
        while (true)
        {
            lock (gate)
                if (users == 0) { thread = null; return; }
            try
            {
                var snapshot = monitor.Poll(); // every 2 s while it answers, every 15 s while it doesn't
                if (snapshot != null || monitor.Connected != wasConnected || !checkedOnce)
                {
                    if (monitor.Connected != wasConnected || !checkedOnce)
                        Log.App.Info(monitor.Connected
                            ? $"LibreHardwareMonitor answering at {monitor.Address}: {(snapshot ?? monitor.Latest)?.Sensors.Count ?? 0} sensors"
                            : $"LibreHardwareMonitor isn't answering at {monitor.Address}");
                    wasConnected = monitor.Connected;
                    checkedOnce = true;
                    Updated?.Invoke(snapshot ?? monitor.Latest);
                }
            }
            catch (Exception ex) { ErrorLog.Write("LibreHardwareMonitor", ex); }
            Thread.Sleep(1000 - (int)(Environment.TickCount64 % 1000));
        }
    }
}
