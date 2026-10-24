using System.Collections.Concurrent;
using System.IO;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>An open connection to a board (a COM port, or a test's stand-in).</summary>
public interface IBoardLink : IDisposable
{
    void Write(byte[] data);
    /// <summary>What's come in, waiting a few ms for something; 0 when nothing.</summary>
    int Read(byte[] buffer);
}

/// <summary>
/// Every Arduino, connected: a thread per board opens its port (again every 3 s while it can't), says Hello, sends
/// each feature's settings, then sends what the sub-tabs give it (<see cref="Send"/>: the newest per feature, only when
/// it changed, then Show and a wait for Ready), and a Ping each second. Shared by the Arduino sub-tab (which owns it)
/// and the sub-tabs that drive features (Notify).
/// </summary>
public sealed class ArduinoHub : IDisposable
{
    private readonly ArduinoSettings settings;
    private readonly ConcurrentDictionary<string, Board> boards = new();
    private volatile bool disposed;

    /// <summary>Opens a board's port (a test puts its own here).</summary>
    public static Func<string, IBoardLink> OpenLink = port => SerialPorts.Open(port, ArduinoProtocol.Baud);

    /// <summary>Boards or features were added, removed or changed (on the UI thread): sub-tabs show or hide.</summary>
    public event Action? Changed;

    public ArduinoHub(ArduinoSettings settings)
    {
        this.settings = settings;
    }

    public ArduinoSettings Settings => settings;

    /// <summary>Some board (switched on) has a feature that <paramref name="tab"/> drives.</summary>
    public bool Has(string tab) => settings.Boards.Any(b => b.Enabled && b.Features.Any(f => f.Info?.Tab == tab));

    /// <summary>Every board's feature that <paramref name="tab"/> drives (switched-on boards only).</summary>
    public IEnumerable<(ArduinoBoard Board, ArduinoFeature Feature)> FeaturesFor(string tab) =>
        settings.Boards.Where(b => b.Enabled).SelectMany(b => b.Features.Where(f => f.Info?.Tab == tab).Select(f => (b, f)));

    /// <summary>Starts the boards' threads (and stops those of boards gone or switched off); after any change, on the UI thread.</summary>
    public void Apply()
    {
        if (disposed) return;
        foreach (var (key, b) in boards)
        {
            var s = settings.Boards.FirstOrDefault(x => x.Key == key);
            if (s == null || !s.Enabled || s.Port.Length == 0 || !s.Port.Equals(b.Port, StringComparison.OrdinalIgnoreCase))
            {
                boards.TryRemove(key, out _);
                b.Stop();
            }
        }
        foreach (var s in settings.Boards.Where(b => b.Enabled && b.Port.Length > 0))
        {
            if (boards.TryGetValue(s.Key, out var b)) b.Update(s);
            else boards[s.Key] = new Board(s);
        }
        settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Sends a feature's data (the newest is kept: one not sent yet is replaced). From any thread.</summary>
    public void Send(ArduinoBoard board, ArduinoFeature feature, byte command, byte[] data)
    {
        if (boards.TryGetValue(board.Key, out var b)) b.Put(feature.Key, command, data);
    }

    /// <summary>Plays the feature's test pattern for a few seconds.</summary>
    public void Test(ArduinoBoard board, ArduinoFeature feature)
    {
        if (boards.TryGetValue(board.Key, out var b)) b.TestUntil(feature.Key, DateTime.UtcNow.AddSeconds(4));
    }

    /// <summary>What a board is doing, for its line: connected, or why not.</summary>
    public string Status(ArduinoBoard board) =>
        !board.Enabled ? T("Off") : board.Port.Length == 0 ? T("No COM port") : boards.TryGetValue(board.Key, out var b) ? b.Status : "";

    public bool Connected(ArduinoBoard board) => boards.TryGetValue(board.Key, out var b) && b.Connected;

    /// <summary>A feature's problem the board reported (a pin it can't use, say), or null.</summary>
    public string? Problem(ArduinoBoard board, ArduinoFeature feature) =>
        boards.TryGetValue(board.Key, out var b) && b.Problems.TryGetValue(feature.Key, out var p) ? p : null;

    public void Dispose()
    {
        disposed = true;
        foreach (var b in boards.Values) b.Stop();
        boards.Clear();
    }

    /// <summary>One board's connection and its thread.</summary>
    private sealed class Board
    {
        private readonly object gate = new();
        private ArduinoBoard settings;
        private string configured = "";     // the settings the board was last given
        private readonly Dictionary<string, (byte Command, byte[] Data)> pending = new();
        private readonly Dictionary<string, byte[]> sent = new();
        private readonly Dictionary<string, DateTime> tests = new();
        private readonly Thread thread;
        private volatile bool stop;
        private IBoardLink? link;
        private string? logged;   // the last problem logged (each is logged once)
        private readonly ArduinoProtocol.Reader reader = new();
        private readonly byte[] buffer = new byte[256];
        public readonly ConcurrentDictionary<string, string> Problems = new();

        public volatile string Status = T("Connecting…");
        public volatile bool Connected;

        public string Port { get; }

        public Board(ArduinoBoard s)
        {
            settings = Clone(s);
            Port = s.Port;
            thread = new Thread(Loop) { IsBackground = true, Name = "DaisysApp-Arduino " + s.Port };
            thread.Start();
        }

        /// <summary>The settings changed: the board is set up again if what it needs to know did.</summary>
        public void Update(ArduinoBoard s)
        {
            var copy = Clone(s);
            lock (gate) settings = copy;
        }

        // (the thread works on its own copy: the window changes the settings as it likes)
        private static ArduinoBoard Clone(ArduinoBoard b) =>
            System.Text.Json.JsonSerializer.Deserialize<ArduinoBoard>(System.Text.Json.JsonSerializer.Serialize(b))!;

        public void Put(string feature, byte command, byte[] data)
        {
            lock (gate)
            {
                if (tests.ContainsKey(feature)) return;
                pending[feature] = (command, data);
                Monitor.PulseAll(gate);
            }
        }

        public void TestUntil(string feature, DateTime until)
        {
            lock (gate)
            {
                tests[feature] = until;
                Monitor.PulseAll(gate);
            }
        }

        public void Stop()
        {
            stop = true;
            lock (gate) Monitor.PulseAll(gate);
            thread.Join(1500);
        }

        /// <summary>What the board needs to know, as text (a change means setting it up again).</summary>
        private static string Shape(ArduinoBoard s) =>
            string.Join(";", s.Features.Select(f => f.Kind + ":" + Convert.ToHexString(f.Info?.Config(f) ?? Array.Empty<byte>())));

        private void Loop()
        {
            string port = Port;
            while (!stop)
            {
                try
                {
                    Connect();
                    Run();
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
                {
                    if (logged != ex.Message) Logging.Log.Here.Info($"Arduino {port}: {ex.Message}");
                    logged = ex.Message;
                    Status = ex.Message;
                }
                catch (Exception ex)
                {
                    Status = ex.Message;
                    Logging.ErrorLog.Write("Arduino " + port, ex);
                }
                Close();
                for (int i = 0; i < 30 && !stop; i++) Thread.Sleep(100);
            }
            Close();
        }

        private void Close()
        {
            Connected = false;
            try { link?.Dispose(); } catch { }
            link = null;
        }

        private void Connect()
        {
            ArduinoBoard s;
            lock (gate) s = settings;
            Status = T("Connecting…");
            link = OpenLink(s.Port);
            // opening the port restarts an Uno or Nano: its bootloader waits about a second and a half first
            byte[]? hello = null;
            for (int attempt = 0; attempt < 6 && hello == null && !stop; attempt++)
            {
                link.Write(ArduinoProtocol.Frame(ArduinoProtocol.Hello, 0, ReadOnlySpan<byte>.Empty));
                hello = Await(ArduinoProtocol.HelloReply, 600);
            }
            if (stop) return;
            if (hello == null) throw new TimeoutException(T("No answer: is the DaisysSim sketch on it?"));
            string name = hello.Length > 4 ? System.Text.Encoding.ASCII.GetString(hello, 4, hello.Length - 4) : "DaisysSim";
            int version = hello.Length > 2 ? hello[2] : 0;
            if (version != ArduinoProtocol.Version) Logging.Log.Here.Warn($"Arduino {s.Port}: sketch version {version}, the app speaks {ArduinoProtocol.Version}");
            if (s.Features.Count > ArduinoProtocol.MaxSlots) Logging.Log.Here.Warn($"Arduino {s.Port}: only the first {ArduinoProtocol.MaxSlots} features are used");
            Configure(s);
            Connected = true;
            Status = F("Connected · {0} {1}", name, version);
            logged = null;
            Logging.Log.Here.Info($"Arduino {s.Port}: connected ({name} {version}, {s.Features.Count} features)");
        }

        private void Configure(ArduinoBoard s)
        {
            Problems.Clear();
            lock (gate) { sent.Clear(); }
            for (int i = 0; i < s.Features.Count && i < ArduinoProtocol.MaxSlots; i++)
            {
                var f = s.Features[i];
                if (f.Info is not { } info) { Problems[f.Key] = T("Not a feature this version knows"); continue; }
                link!.Write(ArduinoProtocol.Frame(ArduinoProtocol.Config, (byte)i, info.Config(f)));
                var reply = Await(ArduinoProtocol.ConfigReply, 500) ?? throw new TimeoutException(T("No answer to its settings"));
                if (reply.Length > 2 && reply[2] != 0)
                {
                    Problems[f.Key] = ArduinoProtocol.Problem(reply[2]);
                    Logging.Log.Here.Warn($"Arduino {s.Port}: {f.Name}: {Problems[f.Key]}");
                }
            }
            configured = Shape(s);
        }

        private void Run()
        {
            var lastPing = DateTime.UtcNow;
            while (!stop)
            {
                ArduinoBoard s;
                var batch = new List<byte[]>();
                lock (gate)
                {
                    if (pending.Count == 0 && !tests.Any()) Monitor.Wait(gate, 100);
                    s = settings;
                    var now = DateTime.UtcNow;
                    for (int i = 0; i < s.Features.Count && i < ArduinoProtocol.MaxSlots; i++)
                    {
                        var f = s.Features[i];
                        if (tests.TryGetValue(f.Key, out var until))
                        {
                            if (now >= until) { tests.Remove(f.Key); sent.Remove(f.Key); batch.Add(ArduinoProtocol.Frame(ArduinoProtocol.Clear, (byte)i, ReadOnlySpan<byte>.Empty)); continue; }
                            double t = (until - now).TotalSeconds;
                            foreach (var (c, d) in f.Info?.Test(f, 4 - t) ?? Enumerable.Empty<(byte, byte[])>())
                                batch.Add(ArduinoProtocol.Frame(c, (byte)i, d));
                            continue;
                        }
                        if (!pending.Remove(f.Key, out var p)) continue;
                        var frame = ArduinoProtocol.Frame(p.Command, (byte)i, p.Data);
                        // only what changed (and everything again now and then, in case a frame was lost)
                        if (sent.TryGetValue(f.Key, out var was) && was.AsSpan().SequenceEqual(frame) && now - lastPing < TimeSpan.FromMilliseconds(900)) continue;
                        sent[f.Key] = frame;
                        batch.Add(frame);
                    }
                    pending.Clear();
                }
                if (Shape(s) != configured)
                {
                    // a pin or a type changed: the board starts again (the next connect sets it up)
                    Logging.Log.Here.Info($"Arduino {s.Port}: settings changed, starting it again");
                    link!.Write(ArduinoProtocol.Frame(ArduinoProtocol.Reset, 0, ReadOnlySpan<byte>.Empty));
                    Thread.Sleep(200);
                    Close();
                    Connect();
                    continue;
                }
                if (batch.Count > 0)
                {
                    foreach (var f in batch) link!.Write(f);
                    link!.Write(ArduinoProtocol.Frame(ArduinoProtocol.Show, 0, ReadOnlySpan<byte>.Empty));
                    if (Await(ArduinoProtocol.Ready, 500) == null) throw new TimeoutException(T("Stopped answering"));
                }
                if (DateTime.UtcNow - lastPing > TimeSpan.FromSeconds(1))
                {
                    lastPing = DateTime.UtcNow;
                    link!.Write(ArduinoProtocol.Frame(ArduinoProtocol.Ping, 0, ReadOnlySpan<byte>.Empty));
                    if (Await(ArduinoProtocol.Pong, 500) == null) throw new TimeoutException(T("Stopped answering"));
                }
                // ~60 a second at most
                Thread.Sleep(15);
            }
        }

        /// <summary>Reads until a frame with that command comes (others are dropped); null after <paramref name="ms"/>.</summary>
        private byte[]? Await(byte command, int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until && !stop)
            {
                int n = link!.Read(buffer);
                if (n == 0) continue;
                for (int i = 0; i < n; i++)
                    if (reader.Take(buffer[i]) is { } body && body[0] == command) return body;
            }
            return null;
        }
    }
}
