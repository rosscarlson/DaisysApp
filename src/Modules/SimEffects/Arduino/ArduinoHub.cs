using System.Collections.Concurrent;
using System.IO;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>An open connection to a board (a COM port, or a test's stand-in).</summary>
public interface IBoardLink : IDisposable
{
    void Write(byte[] data);
    /// <summary>What's come in, waiting a few ms for something; 0 when nothing.</summary>
    int Read(byte[] buffer);
    /// <summary>A new speed, the port kept open.</summary>
    void SetBaud(int baud);
}

/// <summary>A board's status light.</summary>
public enum BoardState { Off, Connecting, Connected, Problem }

/// <summary>
/// Every Arduino, connected: a thread per board opens its port (again every 3 s while it can't), finds which sketch
/// is on it (SimHub's, <see cref="SimHubSketch"/>, or DaisysSim, <see cref="ArduinoProtocol"/>), sets it up, then
/// sends what the sub-tabs give it (<see cref="Send"/>: the newest per feature, only when it changed, then a wait
/// until it's shown), and a Ping each second. Shared by the Arduino sub-tab (which owns it) and the sub-tabs that
/// drive features (Notify).
/// </summary>
public sealed class ArduinoHub : IDisposable
{
    private readonly ArduinoSettings settings;
    private readonly ConcurrentDictionary<string, Board> boards = new();
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private volatile bool disposed;

    /// <summary>Opens a board's port at a speed (a test puts its own here).</summary>
    public static Func<string, int, IBoardLink> OpenLink = SerialPorts.Open;

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

    /// <summary>A board is connected while it and the Arduino tool are on.</summary>
    private bool On(ArduinoBoard b) => settings.On && b.Enabled;

    /// <summary>Starts the boards' threads (and stops those of boards gone or switched off); after any change, on the UI thread.</summary>
    public void Apply()
    {
        if (disposed) return;
        foreach (var (key, b) in boards)
        {
            var s = settings.Boards.FirstOrDefault(x => x.Key == key);
            if (s == null || !On(s) || s.Port.Length == 0 || !s.Port.Equals(b.Port, StringComparison.OrdinalIgnoreCase))
            {
                boards.TryRemove(key, out _);
                b.Stop();
            }
        }
        foreach (var s in settings.Boards.Where(b => On(b) && b.Port.Length > 0))
        {
            if (boards.TryGetValue(s.Key, out var b)) b.Update(s);
            else boards[s.Key] = new Board(s, Found);
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
        !On(board) ? T("Off") : board.Port.Length == 0 ? T("No COM port") : boards.TryGetValue(board.Key, out var b) ? b.Status : "";

    public bool Connected(ArduinoBoard board) => boards.TryGetValue(board.Key, out var b) && b.Connected;

    /// <summary>For its status light.</summary>
    public BoardState State(ArduinoBoard board) =>
        !On(board) || board.Port.Length == 0 ? BoardState.Off
        : !boards.TryGetValue(board.Key, out var b) ? BoardState.Connecting
        : b.Connected ? BoardState.Connected : b.Trouble ? BoardState.Problem : BoardState.Connecting;

    /// <summary>A feature's problem the board reported (a pin it can't use, say), or null.</summary>
    public string? Problem(ArduinoBoard board, ArduinoFeature feature) =>
        boards.TryGetValue(board.Key, out var b) && b.Problems.TryGetValue(feature.Key, out var p) ? p : null;

    // a board's sketch was found: kept in its settings (the window shows that sketch's fields, and it's tried first)
    private void Found(string key, string sketch)
    {
        void Keep()
        {
            var s = settings.Boards.FirstOrDefault(b => b.Key == key);
            if (s == null || s.Sketch == sketch) return;
            s.Sketch = sketch;
            settings.Save();
            Changed?.Invoke();
        }
        if (ui != null) ui.Post(_ => { if (!disposed) Keep(); }, null);
        else Keep();
    }

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
        private readonly Action<string, string> found;
        private ArduinoBoard settings;
        private string configured = "";     // the settings the board was last given
        private readonly Dictionary<string, (byte Command, byte[] Data)> pending = new();
        private readonly Dictionary<string, byte[]> sent = new();
        private readonly Dictionary<string, DateTime> tests = new();
        private readonly Thread thread;
        private volatile bool stop;
        private IBoardLink? link;
        private SimHubSketch? simhub;       // when SimHub's sketch is on it
        private SimHubLeds? leds;
        private bool slowOnly;              // SimHub's sketch didn't answer at 115200 once
        private string foundHere = "";      // the sketch found on it since this thread started
        private string? logged;   // the last problem logged (each is logged once)
        private readonly ArduinoProtocol.Reader reader = new();
        private readonly byte[] buffer = new byte[256];
        public readonly ConcurrentDictionary<string, string> Problems = new();

        public volatile string Status = T("Connecting…");
        public volatile bool Connected;
        /// <summary>The last try failed (the status says why), and it hasn't connected since.</summary>
        public volatile bool Trouble;

        public string Port { get; }

        public Board(ArduinoBoard s, Action<string, string> found)
        {
            settings = Clone(s);
            this.found = found;
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
            string.Join(";", s.Features.Select(f => f.Kind + ":" + Convert.ToHexString(f.Info?.Config(f) ?? Array.Empty<byte>())
                + ":" + (f.Info is LedStripFeature strip ? strip.First(f) : 0)));

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
                    if (ex is SimHubSketch.SlowOnly) slowOnly = true;
                    if (logged != ex.Message) Logging.Log.Here.Info($"Arduino {port}: {ex.Message}");
                    logged = ex.Message;
                    Status = ex.Message;
                    Trouble = true;
                }
                catch (Exception ex)
                {
                    Status = ex.Message;
                    Trouble = true;
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
            simhub = null;
            leds = null;
        }

        /// <summary>Finds the sketch on it (the one found before first; SimHub's on a new board) and sets it up.</summary>
        private void Connect()
        {
            ArduinoBoard s;
            lock (gate) s = settings;
            if (!Trouble) Status = T("Connecting…");
            bool daisysFirst = (foundHere.Length > 0 ? foundHere : s.Sketch) == ArduinoSketch.DaisysSim;
            for (int i = 0; i < 2 && !stop; i++)
            {
                if (daisysFirst == (i == 1) ? TrySimHub(s) : TryDaisysSim(s)) return;
                Close();
            }
            if (stop) return;
            throw new TimeoutException(T("No answer: is SimHub's sketch (or DaisysSim) on it?"));
        }

        private void IsConnected(ArduinoBoard s, string sketch, string what)
        {
            Connected = true;
            Trouble = false;
            Status = what;
            logged = null;
            foundHere = sketch;
            if (s.Sketch != sketch) found(s.Key, sketch);
            Logging.Log.Here.Info($"Arduino {s.Port}: {what} ({s.Features.Count} features)");
        }

        // ---- SimHub's sketch ----

        private bool TrySimHub(ArduinoBoard s)
        {
            link = OpenLink(s.Port, SimHubSketch.SlowBaud);
            var sh = new SimHubSketch(link, () => stop);
            // opening the port restarts an Uno or Nano: its bootloader waits about a second and a half first
            if (!sh.Hello(10)) return false;
            if (!slowOnly && !sh.GoFast()) throw new SimHubSketch.SlowOnly();
            sh.Ask();
            simhub = sh;
            ConfigureSimHub(s);
            var parts = new List<string>();
            if (sh.Leds > 0) parts.Add(P(sh.Leds, "{0} RGB LED", "{0} RGB LEDs"));
            if (sh.HasMatrix) parts.Add(T("8×8 matrix"));
            if (parts.Count == 0) parts.Add(T("no LEDs"));
            IsConnected(s, ArduinoSketch.SimHub, F("Connected · {0} (SimHub sketch) · {1}", sh.Name.Length > 0 ? sh.Name : "SimHub", string.Join(", ", parts)));
            return true;
        }

        /// <summary>Checks each feature against what the board has, and starts with every LED off.</summary>
        private void ConfigureSimHub(ArduinoBoard s)
        {
            var sh = simhub!;
            Problems.Clear();
            lock (gate) sent.Clear();
            leds = new SimHubLeds(sh.Leds);
            foreach (var f in s.Features)
            {
                if (f.Info is not { } info) Problems[f.Key] = T("Not a feature this version knows");
                else if (info is LedMatrixFeature && !sh.HasMatrix) Problems[f.Key] = T("Its SimHub sketch has no 8×8 matrix (add one in SimHub's Arduino setup)");
                else if (info is LedStripFeature strip && strip.First(f) + strip.Count(f) > sh.Leds)
                    Problems[f.Key] = sh.Leds == 0 ? T("Its SimHub sketch has no RGB LEDs")
                        : F("Its SimHub sketch has {0} RGB LEDs: LEDs {1} to {2} aren't there", sh.Leds, strip.First(f) + 1, strip.First(f) + strip.Count(f));
                if (Problems.TryGetValue(f.Key, out var p)) Logging.Log.Here.Warn($"Arduino {s.Port}: {f.Name}: {p}");
            }
            sh.ClearLeds();
            configured = Shape(s);
        }

        // ---- DaisysSim ----

        private bool TryDaisysSim(ArduinoBoard s)
        {
            link = OpenLink(s.Port, ArduinoProtocol.Baud);
            byte[]? hello = null;
            for (int attempt = 0; attempt < 6 && hello == null && !stop; attempt++)
            {
                link.Write(ArduinoProtocol.Frame(ArduinoProtocol.Hello, 0, ReadOnlySpan<byte>.Empty));
                hello = Await(ArduinoProtocol.HelloReply, 600);
            }
            if (hello == null) return false;
            string name = hello.Length > 4 ? System.Text.Encoding.ASCII.GetString(hello, 4, hello.Length - 4) : "DaisysSim";
            int version = hello.Length > 2 ? hello[2] : 0;
            if (version != ArduinoProtocol.Version) Logging.Log.Here.Warn($"Arduino {s.Port}: sketch version {version}, the app speaks {ArduinoProtocol.Version}");
            if (s.Features.Count > ArduinoProtocol.MaxSlots) Logging.Log.Here.Warn($"Arduino {s.Port}: only the first {ArduinoProtocol.MaxSlots} features are used");
            Configure(s);
            IsConnected(s, ArduinoSketch.DaisysSim, F("Connected · {0} {1}", name, version));
            return true;
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

        // ---- sending ----

        private void Run()
        {
            var lastPing = DateTime.UtcNow;
            while (!stop && link != null)
            {
                ArduinoBoard s;
                var batch = new List<(int Slot, ArduinoFeature Feature, byte Command, byte[] Data)>();
                lock (gate)
                {
                    if (pending.Count == 0 && !tests.Any()) Monitor.Wait(gate, 100);
                    s = settings;
                    var now = DateTime.UtcNow;
                    int slots = simhub != null ? s.Features.Count : Math.Min(s.Features.Count, ArduinoProtocol.MaxSlots);
                    for (int i = 0; i < slots; i++)
                    {
                        var f = s.Features[i];
                        if (tests.TryGetValue(f.Key, out var until))
                        {
                            if (now >= until) { tests.Remove(f.Key); sent.Remove(f.Key); batch.Add((i, f, ArduinoProtocol.Clear, Array.Empty<byte>())); continue; }
                            double t = (until - now).TotalSeconds;
                            foreach (var (c, d) in f.Info?.Test(f, 4 - t) ?? Enumerable.Empty<(byte, byte[])>())
                                batch.Add((i, f, c, d));
                            continue;
                        }
                        if (!pending.Remove(f.Key, out var p)) continue;
                        var said = new byte[p.Data.Length + 1];
                        said[0] = p.Command;
                        p.Data.CopyTo(said, 1);
                        // only what changed (and everything again now and then, in case something was lost)
                        if (sent.TryGetValue(f.Key, out var was) && was.AsSpan().SequenceEqual(said) && now - lastPing < TimeSpan.FromMilliseconds(900)) continue;
                        sent[f.Key] = said;
                        batch.Add((i, f, p.Command, p.Data));
                    }
                    pending.Clear();
                }
                if (Shape(s) != configured)
                {
                    if (simhub != null)
                    {
                        // SimHub's sketch knows its own pins: only what's checked against it changed
                        ConfigureSimHub(s);
                        continue;
                    }
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
                    if (simhub != null)
                    {
                        foreach (var (_, f, c, d) in batch)
                            if (!Problems.ContainsKey(f.Key)) leds!.Take(f, c, d);
                        leds!.Flush(simhub);
                    }
                    else
                    {
                        foreach (var (slot, _, c, d) in batch) link!.Write(ArduinoProtocol.Frame(c, (byte)slot, d));
                        link!.Write(ArduinoProtocol.Frame(ArduinoProtocol.Show, 0, ReadOnlySpan<byte>.Empty));
                        if (Await(ArduinoProtocol.Ready, 500) == null) throw new TimeoutException(T("Stopped answering"));
                    }
                }
                if (DateTime.UtcNow - lastPing > TimeSpan.FromSeconds(1))
                {
                    lastPing = DateTime.UtcNow;
                    if (simhub != null) simhub.Ping();
                    else
                    {
                        link!.Write(ArduinoProtocol.Frame(ArduinoProtocol.Ping, 0, ReadOnlySpan<byte>.Empty));
                        if (Await(ArduinoProtocol.Pong, 500) == null) throw new TimeoutException(T("Stopped answering"));
                    }
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

/// <summary>
/// The LEDs of a board with SimHub's sketch: the features' frames (the same as DaisysSim's: <see cref="ArduinoProtocol.Pixels"/>,
/// <see cref="ArduinoProtocol.Bitmap"/>, <see cref="ArduinoProtocol.Clear"/>) made colours of its chain (a strip from
/// its First LED) and of its matrix, at the feature's brightness; <see cref="Flush"/> sends what changed.
/// </summary>
internal sealed class SimHubLeds
{
    private readonly byte[] chain;
    private readonly byte[] matrix = new byte[192];
    private readonly List<(int First, int Count)> dirty = new();
    private bool matrixDirty;

    public SimHubLeds(int count) => chain = new byte[count * 3];

    public void Take(ArduinoFeature f, byte command, byte[] data)
    {
        if (f.Info is not LedFeature info) return;
        int bright = (int)Math.Round(f.Get(LedFeature.Brightness) * 2.55);
        if (info is LedStripFeature strip)
        {
            int first = strip.First(f), count = Math.Min(strip.Count(f), chain.Length / 3 - first);
            if (count <= 0) return;
            if (command == ArduinoProtocol.Clear) Array.Clear(chain, first * 3, count * 3);
            else if (command == ArduinoProtocol.Pixels && data.Length > 0)
                for (int i = 0; data[0] + i < count && 3 + i * 3 < data.Length; i++)
                    for (int c = 0; c < 3; c++) chain[(first + data[0] + i) * 3 + c] = Scale(data[1 + i * 3 + c], bright);
            else return;
            dirty.Add((first, count));
        }
        else if (info is LedMatrixFeature)
        {
            if (command == ArduinoProtocol.Clear) Array.Clear(matrix);
            else if (command == ArduinoProtocol.Bitmap && data.Length >= 14)
            {
                int turns = f.Get(LedMatrixFeature.Rotation) & 3;
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        bool lit = (data[y] & 0x80 >> x) != 0;
                        // turned as DaisysSim turns it
                        int tx = x, ty = y;
                        for (int r = 0; r < turns; r++) (tx, ty) = (7 - ty, tx);
                        int at = (ty * 8 + tx) * 3;
                        for (int c = 0; c < 3; c++) matrix[at + c] = Scale(data[(lit ? 8 : 11) + c], bright);
                    }
            }
            else return;
            matrixDirty = true;
        }
    }

    public void Flush(SimHubSketch board)
    {
        if (dirty.Count > 0)
        {
            var blocks = dirty.Distinct().Select(d => (d.First, chain.AsSpan(d.First * 3, d.Count * 3).ToArray())).ToList();
            dirty.Clear();
            board.SetLeds(blocks);
        }
        if (matrixDirty && board.HasMatrix)
        {
            matrixDirty = false;
            board.SetMatrix(matrix);
        }
    }

    // as FastLED's scale8_video: dimmed, but never off when it was on
    private static byte Scale(byte v, int bright) => v == 0 || bright == 0 ? (byte)0 : (byte)Math.Max(1, v * bright / 255);
}
