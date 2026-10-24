namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>
/// One reader per game (and port), shared by every tool that wants it: the Shaker, Notify and Fanatec can all follow
/// Forza Horizon on port 8000 at once, though only one socket can listen there. <see cref="Open"/> hands out a lease
/// that reads like a source of its own; the game's reader runs on its own thread while any lease is open.
/// </summary>
public static class TelemetryHub
{
    private static readonly object gate = new();
    private static readonly Dictionary<(Game, int), Feed> feeds = new();

    /// <summary>A lease on the game's telemetry (the port only counts for the games that use one). Dispose it when done.</summary>
    public static ITelemetrySource Open(Game game, int port)
    {
        var key = (game, Games.UsesPort(game) ? port : 0);
        lock (gate)
        {
            if (!feeds.TryGetValue(key, out var feed)) feeds[key] = feed = new Feed(key);
            feed.Users++;
            return new Lease(feed);
        }
    }

    private static void Release(Feed feed)
    {
        lock (gate)
        {
            if (--feed.Users > 0) return;
            feeds.Remove(feed.Key);
        }
        feed.Stop();
    }

    /// <summary>A game's reader and its newest frame.</summary>
    private sealed class Feed
    {
        public readonly (Game Game, int Port) Key;
        public int Users;
        private readonly TelemetryFrame latest = new();
        private long seq;
        private volatile bool stop;
        private readonly Thread thread;
        private ITelemetrySource? source;

        public Feed((Game, int) key)
        {
            Key = key;
            thread = new Thread(Loop) { IsBackground = true, Name = "DaisysApp-Telemetry", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        public string? Problem => source?.Problem;

        private void Loop()
        {
            try
            {
                source = Games.Source(Key.Game, Key.Port);
                Logging.Log.Here.Info($"Telemetry: listening for {Key.Game}{(Games.UsesPort(Key.Game) ? $" on UDP {Key.Port}" : "")}");
                var frame = new TelemetryFrame();
                while (!stop)
                {
                    if (!source.Read(frame, 50)) continue;
                    lock (latest)
                    {
                        frame.CopyTo(latest);
                        seq++;
                        Monitor.PulseAll(latest);
                    }
                }
            }
            catch (Exception ex) { Logging.ErrorLog.Write("Telemetry", ex); }
            finally
            {
                source?.Dispose();
                Logging.Log.Here.Info($"Telemetry: stopped listening for {Key.Game}");
            }
        }

        /// <summary>Waits for a frame newer than <paramref name="after"/> and copies it; its number, or -1.</summary>
        public long Next(TelemetryFrame into, long after, int timeoutMs)
        {
            lock (latest)
            {
                if (seq <= after && !Monitor.Wait(latest, timeoutMs)) return -1;
                if (seq <= after) return -1;
                latest.CopyTo(into);
                return seq;
            }
        }

        /// <summary>Stops, and waits for the reader to let go (so a new one can listen on the same port at once).</summary>
        public void Stop()
        {
            stop = true;
            lock (latest) Monitor.PulseAll(latest);
            if (Thread.CurrentThread != thread) thread.Join(1000);
        }
    }

    private sealed class Lease : ITelemetrySource
    {
        private readonly Feed feed;
        private long seen;
        private bool disposed;

        public Lease(Feed feed) => this.feed = feed;

        public string? Problem => feed.Problem;

        public bool Read(TelemetryFrame frame, int timeoutMs)
        {
            if (disposed) { Thread.Sleep(timeoutMs); return false; }
            long got = feed.Next(frame, seen, timeoutMs);
            if (got < 0) return false;
            // frames missed in between: the game's time step isn't this one's
            if (seen > 0 && got > seen + 1) frame.Dt = 0;
            seen = got;
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Release(feed);
        }
    }
}
