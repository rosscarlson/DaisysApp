using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Windows.Threading;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// One device's connection: JSON messages in frames over TCP (a 4-byte little-endian length, then the frame), encrypted
/// with AES-256-GCM under a key made from the passphrase (PBKDF2-SHA256, with a salt made up for each connection). The
/// server's first frame is in the clear: who it is, the salt and the iteration count. Every frame after that is a
/// 12-byte nonce (4 bytes: 1 from the device, 2 from here; 8 bytes: that way's count, from 0), the 16-byte tag, then the
/// message, so nothing can be replayed or moved. The device's first message proves it knows the passphrase. The README's
/// "For client makers" has the whole protocol.
/// </summary>
internal sealed class ButtWire : IDisposable
{
    private const int MaxFrame = 1 << 20;
    public const int Iterations = 150_000;
    private const uint FromDevice = 1, FromServer = 2;
    private readonly TcpClient tcp;
    private readonly NetworkStream stream;
    private readonly Channel<string> outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private AesGcm? aes;
    private ulong sent, received;
    private bool disposed;

    public string Endpoint { get; }

    private ButtWire(TcpClient tcp)
    {
        this.tcp = tcp;
        tcp.NoDelay = true;
        stream = tcp.GetStream();
        Endpoint = tcp.Client.RemoteEndPoint is IPEndPoint ip ? (ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4() : ip.Address).ToString() : "?";
    }

    /// <summary>A device connecting: its hello (the first message) and the wire, or null if it doesn't know the passphrase.</summary>
    public static async Task<(ButtWire Wire, JsonObject Hello)?> AcceptAsync(TcpClient tcp, string passphrase, CancellationToken ct)
    {
        var w = new ButtWire(tcp);
        try
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            var hi = new JsonObject
            {
                ["app"] = "DaisysApp", ["service"] = ButtServer.Service, ["v"] = ButtServer.Version,
                ["pc"] = Environment.MachineName, ["salt"] = Convert.ToBase64String(salt), ["iterations"] = Iterations,
            };
            await w.WriteFrameAsync(Encoding.UTF8.GetBytes(hi.ToJsonString()), ct);
            await Task.Run(() => w.aes = new AesGcm(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32), 16), ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var hello = await w.ReadAsync(timeout.Token);
            if (hello?["op"]?.GetValue<string>() != "hello") throw new CryptographicException("no hello");
            w.Start();
            return (w, hello);
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Button2Key Remote: {w.Endpoint} couldn't connect ({(ex is CryptographicException or AuthenticationTagMismatchException ? "not the same passphrase" : ex.Message)})");
            // (slows down guessing)
            try { await Task.Delay(1500, ct); } catch { }
            w.Dispose();
            return null;
        }
    }

    /// <summary>The next message, or null when the device has gone.</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken ct)
    {
        var frame = await ReadFrameAsync(ct);
        return frame == null ? null : JsonNode.Parse(Open(frame)) as JsonObject;
    }

    /// <summary>Sends a message (queued: they go in order).</summary>
    public void Post(JsonNode message) => outbox.Writer.TryWrite(message.ToJsonString());
    public void Post(string json) => outbox.Writer.TryWrite(json);

    private void Start() => _ = Task.Run(async () =>
    {
        var head = new byte[4];
        try
        {
            while (await outbox.Reader.WaitToReadAsync())
                while (outbox.Reader.TryRead(out var json))
                {
                    var frame = Seal(Encoding.UTF8.GetBytes(json));
                    BinaryPrimitives.WriteInt32LittleEndian(head, frame.Length);
                    await stream.WriteAsync(head);
                    await stream.WriteAsync(frame);
                }
        }
        catch (Exception ex)
        {
            if (!disposed) Logging.Log.Here.Warn($"Button2Key Remote: sending to {Endpoint}: {ex.Message}");
            Dispose();
        }
    });

    private byte[] Seal(byte[] plain)
    {
        var frame = new byte[12 + 16 + plain.Length];
        var nonce = frame.AsSpan(0, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce, FromServer);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], sent++);
        aes!.Encrypt(nonce, plain, frame.AsSpan(28), frame.AsSpan(12, 16));
        return frame;
    }

    private byte[] Open(byte[] frame)
    {
        if (frame.Length < 28 || aes == null) throw new CryptographicException("short frame");
        var nonce = frame.AsSpan(0, 12);
        if (BinaryPrimitives.ReadUInt32LittleEndian(nonce) != FromDevice || BinaryPrimitives.ReadUInt64LittleEndian(nonce[4..]) != received)
            throw new CryptographicException("out of order");
        var plain = new byte[frame.Length - 28];
        aes.Decrypt(nonce, frame.AsSpan(28), frame.AsSpan(12, 16), plain);
        received++;
        return plain;
    }

    private async Task<byte[]?> ReadFrameAsync(CancellationToken ct)
    {
        var head = new byte[4];
        try { await stream.ReadExactlyAsync(head, ct); }
        catch (EndOfStreamException) { return null; }
        int n = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (n < 0 || n > MaxFrame) throw new IOException("a frame too big");
        var body = new byte[n];
        await stream.ReadExactlyAsync(body, ct);
        return body;
    }

    private async Task WriteFrameAsync(byte[] body, CancellationToken ct)
    {
        var head = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(head, body.Length);
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        outbox.Writer.TryComplete();
        try { tcp.Dispose(); } catch { }
        aes?.Dispose();
    }
}

/// <summary>What the server needs from the rest of the module. Called on the UI thread.</summary>
internal interface IButtHost
{
    /// <summary>A device said hello (its name, platform and screen size).</summary>
    void DeviceSeen(SeenDevice device);
    /// <summary>A device picked a BigButt to use.</summary>
    void UseBigButt(string name, bool byHand = true);
}

/// <summary>
/// The server devices connect to (a phone, tablet or PC running a Button2Key Remote client): it listens on the port
/// for devices with the passphrase, tells them the ButtScreens and BigButts (and again whenever they change), sends a
/// ButtScreen's layout, icons and pictures when asked, and passes button presses to the <see cref="PressEngine"/>. It
/// also answers "who's there?" on the same port over UDP, so a device can find this PC on the network.
/// </summary>
internal sealed class ButtServer : IDisposable
{
    public const string Service = "Butt2KeyRemote";
    public const int Version = 1;
    /// <summary>A device that sends nothing (not even a ping) for this long is dropped, and what it held let go.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(30);

    private readonly RemoteSettings settings;
    private readonly PressEngine engine;
    private readonly IButtHost host;
    private readonly Dispatcher ui;
    private readonly object gate = new();
    private readonly List<Client> clients = new();
    private TcpListener? listener;
    private UdpClient? finder;
    private CancellationTokenSource? stop;
    private string running = "";
    private int nextClient;

    // what devices are told, made on the UI thread (Publish) and read from the connections' threads
    private volatile string stateJson = "{}";
    private volatile Dictionary<string, string> screenJson = new();
    private volatile Dictionary<string, string> screenNames = new();

    public sealed class Client
    {
        public required string Id;
        public required ButtWire Wire;
        public string Device = "", Platform = "";
        public DateTime Since = DateTime.Now;
    }

    public string Status { get; private set; } = "";
    public bool Failed { get; private set; }
    /// <summary>Raised on the UI thread when the status or the devices connected change.</summary>
    public event Action? StatusChanged;

    public IReadOnlyList<Client> Clients { get { lock (gate) return clients.ToList(); } }

    public ButtServer(RemoteSettings settings, PressEngine engine, IButtHost host, Dispatcher ui)
    {
        this.settings = settings;
        this.engine = engine;
        this.host = host;
        this.ui = ui;
    }

    /// <summary>Starts, stops or restarts it as the settings say (on the UI thread).</summary>
    public void Apply()
    {
        string passphrase = settings.Passphrase;
        string want = settings.Enabled ? $"{settings.Port}|{settings.PassphraseProtected}" : "";
        if (want == running) return;
        Close();
        running = want;
        if (!settings.Enabled) { Show(T("Off: devices can't connect."), false); return; }
        if (passphrase.Length < 8) { Show(T("Set a passphrase of 8 characters or more: devices connect with it."), true); return; }
        try
        {
            var l = new TcpListener(IPAddress.IPv6Any, settings.Port);
            l.Server.DualMode = true;
            l.Start();
            listener = l;
            stop = new CancellationTokenSource();
            _ = AcceptAsync(l, passphrase, stop.Token);
            Logging.Log.Here.Info($"Button2Key Remote: listening on port {settings.Port}");
            StartFinder(settings.Port, stop.Token);
            ShowListening();
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Button2Key Remote: port {settings.Port}: {ex.Message}");
            Show(F("It couldn't listen on port {0} ({1}): another program may be using it. Try another port (and the same on your devices).", settings.Port, ex.Message.TrimEnd('.')), true);
        }
    }

    /// <summary>This PC's addresses on the network, for typing into a device.</summary>
    public static string Addresses()
    {
        try
        {
            return string.Join(", ", Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()));
        }
        catch { return ""; }
    }

    private void ShowListening()
    {
        if (listener == null) return;
        string addresses = Addresses();
        int n = Clients.Count;
        Show(F("On: devices connect to {0}{1}, port {2}.", Environment.MachineName, addresses.Length > 0 ? " (" + addresses + ")" : "", settings.Port)
            + (n > 0 ? " " + P(n, "{0} device connected.", "{0} devices connected.") : ""), false);
    }

    private void Show(string status, bool failed)
    {
        Status = status;
        Failed = failed;
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// What devices are told: the state (on or off, the BigButts and the one in use, every ButtScreen's name, shape and
    /// version) and each ButtScreen's layout. Sent to every device connected when it changes. On the UI thread.
    /// </summary>
    public void Publish(IReadOnlyList<ButtScreen> screens, RemoteSettings s, string active)
    {
        var bigButts = new JsonArray();
        foreach (var b in s.BigButts)
            bigButts.Add(new JsonObject { ["name"] = b.Name, ["screens"] = new JsonArray(b.Screens.Where(id => screens.Any(x => x.Id == id)).Select(id => (JsonNode)id).ToArray()) });
        var list = new JsonArray();
        foreach (var x in screens)
            list.Add(new JsonObject { ["id"] = x.Id, ["name"] = x.Name, ["width"] = x.Width, ["height"] = x.Height, ["columns"] = x.Columns, ["rows"] = x.Rows, ["version"] = x.Version });
        // the BigButt in use, spelled out: how many ButtScreens and which, in the order a device goes through them
        var inUse = s.BigButts.FirstOrDefault(b => b.Name.Equals(active, StringComparison.OrdinalIgnoreCase));
        var current = new JsonArray();
        foreach (var id in inUse?.Screens ?? new())
            if (screens.FirstOrDefault(x => x.Id == id) is { } x)
                current.Add(new JsonObject { ["index"] = current.Count, ["id"] = x.Id, ["name"] = x.Name });
        var state = new JsonObject
        {
            ["op"] = "state", ["pc"] = Environment.MachineName, ["on"] = s.Enabled,
            ["bigButt"] = active,
            ["current"] = new JsonObject { ["name"] = inUse?.Name ?? active, ["count"] = current.Count, ["screens"] = current },
            ["bigButts"] = bigButts, ["screens"] = list,
        };
        var layouts = screens.ToDictionary(x => x.Id, x => Describe(x).ToJsonString());
        screenNames = screens.GroupBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.CurrentCultureIgnoreCase);
        screenJson = layouts;
        string json = state.ToJsonString();
        if (json == stateJson) return;
        stateJson = json;
        foreach (var c in Clients) c.Wire.Post(json);
    }

    /// <summary>A ButtScreen as a device gets it: its grid, colours and buttons, with the icons' paths and the pictures.</summary>
    private static JsonObject Describe(ButtScreen s)
    {
        var buttons = new JsonArray();
        var icons = new JsonObject();
        var images = new JsonObject();
        foreach (var b in s.Buttons)
        {
            var st = b.Style;
            var o = new JsonObject
            {
                ["id"] = b.Id, ["column"] = b.Column, ["row"] = b.Row, ["columnSpan"] = b.ColumnSpan, ["rowSpan"] = b.RowSpan,
                ["label"] = s.LabelOf(b),
                ["style"] = new JsonObject
                {
                    ["rounded"] = st.Rounded, ["background"] = st.Background, ["outline"] = st.Outline, ["outlineWidth"] = st.OutlineWidth,
                    ["font"] = st.Font, ["fontFamily"] = FontKind(st.Font), ["fontSize"] = st.FontSize, ["fontColor"] = st.FontColor, ["bold"] = st.Bold,
                },
            };
            if (b.Image is { } image && File.Exists(ScreenStore.ImagePath(image)))
            {
                o["image"] = image;
                if (!images.ContainsKey(image))
                    try { images[image] = Convert.ToBase64String(File.ReadAllBytes(ScreenStore.ImagePath(image))); } catch { }
            }
            else if (IconLibrary.Find(b.Icon) is { } icon)
            {
                o["icon"] = icon.Name;
                icons[icon.Name] = icon.Path;
            }
            if (b.Action?.Kind == Joy2Key.ActionKind.Profile && b.Action.Profile is { } show) o["shows"] = show;
            buttons.Add(o);
        }
        return new JsonObject
        {
            ["op"] = "screen",
            ["screen"] = new JsonObject
            {
                ["id"] = s.Id, ["name"] = s.Name, ["version"] = s.Version, ["width"] = s.Width, ["height"] = s.Height,
                ["columns"] = s.Columns, ["rows"] = s.Rows, ["gap"] = s.Gap, ["cornerRadius"] = Math.Round(s.CornerRadius, 2),
                ["background"] = s.Background, ["iconColor"] = s.IconColor, ["buttons"] = buttons,
            },
            ["icons"] = icons,
            ["images"] = images,
        };
    }

    /// <summary>Whether a font is serif, monospace or sans-serif, for a device that hasn't got it.</summary>
    private static string FontKind(string font)
    {
        string f = font.ToLowerInvariant();
        if (f.Contains("mono") || f.Contains("consol") || f.Contains("courier") || f.Contains("cascadia")) return "monospace";
        if (f.Contains("serif") && !f.Contains("sans") || f.Contains("times") || f.Contains("georgia") || f.Contains("cambria") || f.Contains("garamond") || f.Contains("palatino")) return "serif";
        return "sans-serif";
    }

    /// <summary>Tells a device (its source id) to show a ButtScreen (a Show a ButtScreen button pressed there).</summary>
    public void ShowScreen(string source, string name)
    {
        if (!screenNames.TryGetValue(name, out var id)) return;
        Client? c;
        lock (gate) c = clients.FirstOrDefault(x => x.Id == source);
        c?.Wire.Post(new JsonObject { ["op"] = "show", ["screen"] = id });
    }

    private async Task AcceptAsync(TcpListener l, string passphrase, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await l.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => ServeAsync(tcp, passphrase, ct));
        }
    }

    /// <summary>One device: its requests and presses, until it goes.</summary>
    private async Task ServeAsync(TcpClient tcp, string passphrase, CancellationToken ct)
    {
        var accepted = await ButtWire.AcceptAsync(tcp, passphrase, ct);
        if (accepted is not { } a) return;
        var (wire, hello) = a;
        var c = new Client
        {
            Id = "device:" + Interlocked.Increment(ref nextClient), Wire = wire,
            Device = Text(hello["device"]) ?? wire.Endpoint, Platform = Text(hello["platform"]) ?? "",
        };
        Logging.Log.Here.Info($"Button2Key Remote: {c.Device} ({c.Platform}, {wire.Endpoint}) connected");
        lock (gate) clients.Add(c);
        var seen = new SeenDevice { Name = c.Device, Platform = c.Platform, Width = Number(hello["width"]), Height = Number(hello["height"]), LastSeen = DateTime.Now };
        _ = ui.BeginInvoke(() => { host.DeviceSeen(seen); ShowListening(); });
        wire.Post(new JsonObject { ["op"] = "welcome", ["pc"] = Environment.MachineName, ["v"] = Version });
        wire.Post(stateJson);
        try
        {
            while (true)
            {
                using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
                quiet.CancelAfter(Silence);
                if (await wire.ReadAsync(quiet.Token) is not { } m) break;
                Handle(c, m);
            }
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or OperationCanceledException or System.Text.Json.JsonException or ObjectDisposedException or InvalidOperationException)
        {
            if (!ct.IsCancellationRequested) Logging.Log.Here.Info($"Button2Key Remote: {c.Device}: {(ex is OperationCanceledException ? "nothing heard for 30 seconds" : ex.Message)}");
        }
        finally
        {
            engine.LetGo(c.Id);
            lock (gate) clients.Remove(c);
            wire.Dispose();
            Logging.Log.Here.Info($"Button2Key Remote: {c.Device} went");
            _ = ui.BeginInvoke(ShowListening);
        }
    }

    private void Handle(Client c, JsonObject m)
    {
        string op = Text(m["op"]) ?? "";
        string screen = Text(m["screen"]) ?? "", button = Text(m["button"]) ?? "";
        switch (op)
        {
            case "down": engine.Down(c.Id, screen, button); break;
            case "up": engine.Up(c.Id, screen, button); break;
            case "tap": engine.Tap(c.Id, screen, button); break;
            case "ping": c.Wire.Post(new JsonObject { ["op"] = "pong" }); break;
            case "state": c.Wire.Post(stateJson); break;
            case "get":
                string id = Text(m["id"]) ?? screen;
                if (screenJson.TryGetValue(id, out var json)) c.Wire.Post(json);
                else c.Wire.Post(new JsonObject { ["op"] = "error", ["message"] = T("That ButtScreen isn't there any more."), ["id"] = id });
                break;
            case "use":
                if (Text(m["bigButt"]) is { } name) _ = ui.BeginInvoke(() => host.UseBigButt(name));
                break;
            default:
                c.Wire.Post(new JsonObject { ["op"] = "error", ["message"] = F("Unknown request: {0}", op) });
                break;
        }
    }

    private static string? Text(JsonNode? n)
    {
        try { return n?.GetValue<string>(); } catch { return n?.ToString(); }
    }

    private static int Number(JsonNode? n)
    {
        try { return n == null ? 0 : (int)n.GetValue<double>(); } catch { return 0; }
    }

    // ---- being found: a device sends {"op":"discover"} by UDP broadcast to the port, and each PC answers ----

    private void StartFinder(int port, CancellationToken ct)
    {
        try
        {
            var u = new UdpClient(AddressFamily.InterNetwork);
            u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            u.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            finder = u;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    UdpReceiveResult r;
                    try { r = await u.ReceiveAsync(ct); }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException) { continue; } // (a device that went: Windows reports it on the next receive)
                    catch { break; }
                    try
                    {
                        if (r.Buffer.Length > 512 || JsonNode.Parse(r.Buffer) is not JsonObject q || Text(q["op"]) != "discover") continue;
                        var here = new JsonObject
                        {
                            ["op"] = "here", ["app"] = "DaisysApp", ["service"] = Service, ["v"] = Version,
                            ["pc"] = Environment.MachineName, ["port"] = port,
                        };
                        var bytes = Encoding.UTF8.GetBytes(here.ToJsonString());
                        await u.SendAsync(bytes, r.RemoteEndPoint, ct);
                    }
                    catch { }
                }
            }, ct);
        }
        catch (Exception ex) { Logging.Log.Here.Warn($"Button2Key Remote: UDP port {port} (for being found): {ex.Message}"); }
    }

    private void Close()
    {
        stop?.Cancel();
        stop = null;
        try { listener?.Stop(); } catch { }
        listener = null;
        try { finder?.Dispose(); } catch { }
        finder = null;
        List<Client> all;
        lock (gate) all = clients.ToList();
        foreach (var c in all) c.Wire.Dispose();
        running = "";
    }

    public void Dispose() => Close();
}
