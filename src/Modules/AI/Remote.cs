using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Windows.Threading;

namespace DaisysApp.Applets.AI;

/// <summary>What the remote control server needs from the AI tab (AiView): its sessions, a new one, one by its id. Called on the UI thread.</summary>
internal interface IRemoteHost
{
    /// <summary>This PC's sessions, its AIs and its last folder ({op: "sessions"}).</summary>
    JsonObject Describe();
    /// <summary>A new session tab (Claude Code: on this Claude account, or null for Claude Code's own sign-in): its id.
    /// Throws a RemoteException to say why it can't.</summary>
    string Create(string name, string folder, string provider, List<string> readFirst, string? account);
    SessionView? Find(string id);
    /// <summary>This PC's plan usage (sent to a PC with one of its sessions open, as it changes).</summary>
    PlanUsage Usage { get; }
}

/// <summary>Something remote control says went wrong, worded for the user.</summary>
internal sealed class RemoteException(string message) : Exception(message);

/// <summary>
/// One connection between two PCs' Daisy's Apps, for remote control: JSON messages in frames over TCP, encrypted with
/// AES-GCM under a key made from the passphrase (PBKDF2, with a salt the server makes up for each connection). The
/// server sends the salt in the clear; the client's first message (encrypted) proves it knows the passphrase, and the
/// server's answer proves it does too. Each frame's nonce counts up, one count each way, so nothing can be replayed or
/// moved. Messages go out in order, from a queue.
/// </summary>
internal sealed class RemoteWire : Shell.IRemoteChannel
{
    private const int MaxFrame = 64 << 20;
    private const int Iterations = 150_000;
    private const int Gather = 40;
    private readonly TcpClient tcp;
    private readonly NetworkStream stream;
    private readonly uint sendWay, receiveWay;
    private readonly Channel<string> outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private AesGcm? aes;
    private ulong sent, received;
    private long bytesSent, bytesReceived;
    private bool disposed;

    /// <summary>What went each way on it so far, for the log ("sent 1.2 MB, received 3 KB").</summary>
    public string Traffic => $"sent {Size(Interlocked.Read(ref bytesSent))}, received {Size(Interlocked.Read(ref bytesReceived))}";
    private static string Size(long n) => n >= 1 << 20 ? $"{n / 1048576.0:0.0} MB" : $"{(n + 1023) / 1024} KB";

    /// <summary>The other PC's name, and where it connected from.</summary>
    public string Peer { get; private set; } = "";
    public string Endpoint { get; }

    private RemoteWire(TcpClient tcp, bool client)
    {
        this.tcp = tcp;
        tcp.NoDelay = true;
        stream = tcp.GetStream();
        sendWay = client ? 1u : 2u;
        receiveWay = client ? 2u : 1u;
        Endpoint = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
    }

    /// <summary>Connects to the other PC (the client): it's there, it's Daisy's App, and the passphrase is the same.</summary>
    public static async Task<RemoteWire> ConnectAsync(string host, int port, string passphrase, CancellationToken ct = default)
    {
        if (host.Trim().Length == 0) throw new RemoteException(T("Set the other PC's name or address first (Settings → AI → Remote control)."));
        if (passphrase.Length == 0) throw new RemoteException(T("Set the passphrase first (Settings → AI → Remote control)."));
        var tcp = new TcpClient();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await tcp.ConnectAsync(host.Trim(), port, timeout.Token); }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                tcp.Dispose();
                throw new RemoteException(F("{0} (port {1}) didn't answer ({2}). Is Daisy's App running there, with remote control on, and the port let through its firewall?",
                    host.Trim(), port, ex is OperationCanceledException ? T("no reply") : ex.Message.TrimEnd('.')));
            }
        }
        var w = new RemoteWire(tcp, client: true);
        try
        {
            var hello = await w.ReadFrameAsync(ct) is { } h ? JsonNode.Parse(h) as JsonObject : null;
            if (hello?["app"]?.GetValue<string>() != "DaisysApp" || hello["salt"]?.GetValue<string>() is not { } salt)
                throw new RemoteException(T("That isn't Daisy's App's remote control."));
            await Task.Run(() => w.Key(passphrase, Convert.FromBase64String(salt)), ct);
            w.Start();
            w.Post(new JsonObject { ["op"] = "hello", ["pc"] = Environment.MachineName });
            JsonObject? welcome;
            try { welcome = await w.ReadAsync(ct); }
            catch (Exception ex) when (ex is IOException or CryptographicException or JsonException) { welcome = null; }
            if (welcome?["op"]?.GetValue<string>() != "welcome") throw new RemoteException(T("The other PC turned the connection down: the passphrase isn't the same as there."));
            w.Peer = welcome["pc"]?.GetValue<string>() ?? host;
            return w;
        }
        catch
        {
            w.Dispose();
            throw;
        }
    }

    /// <summary>A PC connecting (the server): null if it doesn't know the passphrase (or isn't Daisy's App).</summary>
    public static async Task<RemoteWire?> AcceptAsync(TcpClient tcp, string passphrase, CancellationToken ct)
    {
        var w = new RemoteWire(tcp, client: false);
        try
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            await w.WriteFrameAsync(Encoding.UTF8.GetBytes(new JsonObject { ["app"] = "DaisysApp", ["v"] = 1, ["salt"] = Convert.ToBase64String(salt) }.ToJsonString()), ct);
            await Task.Run(() => w.Key(passphrase, salt), ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var hello = await w.ReadAsync(timeout.Token);
            if (hello?["op"]?.GetValue<string>() != "hello") throw new CryptographicException("no hello");
            w.Peer = hello["pc"]?.GetValue<string>() ?? w.Endpoint;
            w.Start();
            w.Post(new JsonObject { ["op"] = "welcome", ["pc"] = Environment.MachineName });
            return w;
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Remote control: {w.Endpoint} couldn't connect ({(ex is CryptographicException ? "not the same passphrase" : ex.Message)})");
            // (slows down guessing)
            try { await Task.Delay(1500, ct); } catch { }
            w.Dispose();
            return null;
        }
    }

    private void Key(string passphrase, byte[] salt) =>
        aes = new AesGcm(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32), 16);

    /// <summary>The next message, or null when the other end has closed.</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken ct = default)
    {
        var frame = await ReadFrameAsync(ct);
        if (frame == null) return null;
        return JsonNode.Parse(Open(frame)) as JsonObject;
    }

    /// <summary>Sends a message (queued: they go in order).</summary>
    public void Post(JsonNode message) => Post(message.ToJsonString());
    public void Post(string json) => outbox.Writer.TryWrite(json);

    /// <summary>
    /// The sender: a session streaming a reply posts a message for every few words, so whatever is queued after a
    /// moment's wait goes out together, in one write (not two small packets for each message).
    /// </summary>
    private void Start() => _ = Task.Run(async () =>
    {
        var batch = new MemoryStream();
        var head = new byte[4];
        try
        {
            while (await outbox.Reader.WaitToReadAsync())
            {
                await Task.Delay(Gather);
                batch.SetLength(0);
                while (batch.Length < 1 << 20 && outbox.Reader.TryRead(out var json))
                {
                    var frame = Seal(Encoding.UTF8.GetBytes(json));
                    BinaryPrimitives.WriteInt32LittleEndian(head, frame.Length);
                    batch.Write(head);
                    batch.Write(frame);
                }
                await stream.WriteAsync(batch.GetBuffer().AsMemory(0, (int)batch.Length));
                Interlocked.Add(ref bytesSent, batch.Length);
            }
        }
        catch (Exception ex)
        {
            if (!disposed) Logging.Log.Here.Warn($"Remote control: sending to {Endpoint}: {ex.Message}");
            Dispose();
        }
    });

    private byte[] Seal(byte[] plain)
    {
        var frame = new byte[12 + 16 + plain.Length];
        var nonce = frame.AsSpan(0, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce, sendWay);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], sent++);
        aes!.Encrypt(nonce, plain, frame.AsSpan(28), frame.AsSpan(12, 16));
        return frame;
    }

    private byte[] Open(byte[] frame)
    {
        if (frame.Length < 28 || aes == null) throw new CryptographicException("short frame");
        var nonce = frame.AsSpan(0, 12);
        if (BinaryPrimitives.ReadUInt32LittleEndian(nonce) != receiveWay || BinaryPrimitives.ReadUInt64LittleEndian(nonce[4..]) != received)
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
        Interlocked.Add(ref bytesReceived, 4 + n);
        return body;
    }

    private async Task WriteFrameAsync(byte[] body, CancellationToken ct)
    {
        var head = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(head, body.Length);
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
        Interlocked.Add(ref bytesSent, 4 + body.Length);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        outbox.Writer.TryComplete();
        try { tcp.Dispose(); } catch { }
    }
}

/// <summary>
/// Remote control, this PC the server (Settings → AI): listens on the port for a PC with the passphrase, which can list
/// the sessions here, start a new one, and open one: everything that session does is sent on to it as it happens (and
/// its conversation so far when it opens), and what it sends (messages, Stop, answers to questions, model and
/// permissions) is done here as if typed here. Each open session is a connection of its own.
/// </summary>
internal sealed class RemoteServer : IDisposable
{
    private readonly AiSettings settings;
    private readonly IRemoteHost host;
    private readonly Dispatcher ui;
    private TcpListener? listener;
    private CancellationTokenSource? stop;
    private string running = "";
    private int connected;

    public string Status { get; private set; } = "";
    public bool Failed { get; private set; }
    /// <summary>It's listening (other modules' channels use it too: Core's RemoteChannels).</summary>
    public bool Listening => listener != null;
    public event Action? StatusChanged;

    public RemoteServer(AiSettings settings, IRemoteHost host, Dispatcher ui)
    {
        this.settings = settings;
        this.host = host;
        this.ui = ui;
    }

    /// <summary>Starts, stops or restarts it as the settings say (on the UI thread).</summary>
    public void Apply()
    {
        var r = settings.Remote;
        string passphrase = r.Passphrase;
        string want = r.Serve ? $"{r.Port}|{r.PassphraseProtected}" : "";
        if (want == running) return;
        Close();
        running = want;
        if (!r.Serve) { Show(T("Off."), false); return; }
        if (passphrase.Length < 8) { Show(T("Set a passphrase of 8 characters or more: other PCs connect with it."), true); return; }
        try
        {
            var l = new TcpListener(IPAddress.IPv6Any, r.Port);
            l.Server.DualMode = true;
            l.Start();
            listener = l;
            stop = new CancellationTokenSource();
            _ = AcceptAsync(l, passphrase, stop.Token);
            Logging.Log.Here.Info($"Remote control: listening on port {r.Port}");
            ShowListening();
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Remote control: port {r.Port}: {ex.Message}");
            Show(F("It couldn't listen on port {0} ({1}): another program, or Windows, may be keeping it. Try another port (the same on the other PC).", r.Port, ex.Message.TrimEnd('.')), true);
        }
    }

    private void ShowListening()
    {
        if (listener == null) return;
        string addresses;
        try
        {
            addresses = string.Join(", ", Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()));
        }
        catch { addresses = ""; }
        Show(F("On: other PCs connect to {0}{1}, port {2}.", Environment.MachineName, addresses.Length > 0 ? " (" + addresses + ")" : "", settings.Remote.Port)
            + (connected > 0 ? " " + P(connected, "{0} connection now.", "{0} connections now.") : ""), false);
    }

    private void Show(string status, bool failed)
    {
        Status = status;
        Failed = failed;
        StatusChanged?.Invoke();
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

    /// <summary>One connected PC: its requests, until it goes.</summary>
    private async Task ServeAsync(TcpClient tcp, string passphrase, CancellationToken ct)
    {
        using var wire = await RemoteWire.AcceptAsync(tcp, passphrase, ct);
        if (wire == null) return;
        Logging.Log.Here.Info($"Remote control: {wire.Peer} ({wire.Endpoint}) connected");
        Interlocked.Increment(ref connected);
        _ = ui.BeginInvoke(ShowListening);
        var link = new Link(wire);
        try
        {
            while (await wire.ReadAsync(ct) is { } m)
            {
                // (another module's channel, e.g. Tools → Notepad: the connection is its own from here)
                if (S(m, "op") == "channel")
                {
                    if (Shell.RemoteChannels.Handler(S(m, "name")) is { } channel)
                    {
                        Logging.Log.Here.Info($"Remote control: {wire.Peer} opened channel {S(m, "name")}");
                        await channel(wire, ct);
                        break;
                    }
                    wire.Post(new JsonObject { ["op"] = "error", ["message"] = F("{0} isn't switched on on {1}.", S(m, "name"), Environment.MachineName) });
                    continue;
                }
                try { await HandleAsync(link, m); }
                catch (Exception ex) when (ex is not (IOException or CryptographicException))
                {
                    wire.Post(new JsonObject { ["op"] = "error", ["message"] = ex is RemoteException ? ex.Message : F("Something went wrong there: {0}", ex.Message) });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or OperationCanceledException or SocketException or JsonException or ObjectDisposedException) { }
        finally
        {
            try { await ui.InvokeAsync(link.Detach); } catch { }
            Interlocked.Decrement(ref connected);
            _ = ui.BeginInvoke(ShowListening);
            Logging.Log.Here.Info($"Remote control: {wire.Peer} ({wire.Endpoint}) disconnected ({wire.Traffic})");
        }
    }

    private static string S(JsonObject m, string name) => m[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private async Task HandleAsync(Link link, JsonObject m)
    {
        switch (S(m, "op"))
        {
            case "list":
                link.Wire.Post(await ui.InvokeAsync(host.Describe));
                break;
            case "files":
                {
                    string folder = S(m, "folder");
                    bool exists = Directory.Exists(folder);
                    var md = new JsonArray();
                    var docs = new JsonArray();
                    if (exists)
                    {
                        foreach (var f in SessionView.MarkdownFiles(folder)) md.Add(f);
                        foreach (var (path, name) in await Task.Run(() => SessionView.ModuleDocs(folder)))
                            docs.Add(new JsonObject { ["path"] = path, ["name"] = name });
                    }
                    link.Wire.Post(new JsonObject { ["op"] = "files", ["folder"] = folder, ["exists"] = exists, ["md"] = md, ["docs"] = docs });
                    break;
                }
            case "dirs":
                link.Wire.Post(Dirs(S(m, "path")));
                break;
            case "mkdir":
                link.Wire.Post(MakeDir(S(m, "parent"), S(m, "name")));
                break;
            case "usage":
                // (this PC's plan, without opening a session: Claude Code's own sign-in)
                link.Wire.Post(await ui.InvokeAsync(() => host.Usage.ToRemote()));
                break;
            case "new":
                {
                    var read = (m["read"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList() ?? new();
                    var (id, why) = await ui.InvokeAsync(() =>
                    {
                        try { return (host.Create(S(m, "name"), S(m, "folder"), S(m, "provider"), read, S(m, "account") is { Length: > 0 } account ? account : null), (string?)null); }
                        catch (RemoteException ex) { return ("", ex.Message); }
                    });
                    if (why != null) throw new RemoteException(why);
                    link.Wire.Post(new JsonObject { ["op"] = "created", ["id"] = id });
                    break;
                }
            case "attach":
                {
                    string id = S(m, "id");
                    var info = await ui.InvokeAsync(() => host.Find(id)?.Info);
                    if (info == null) throw new RemoteException(F("That session isn't open on {0} any more.", Environment.MachineName));
                    var history = await Task.Run(() => SessionView.RemoteHistory(info));
                    await ui.InvokeAsync(() =>
                    {
                        if (host.Find(id) is not { } view) return;
                        var reply = view.Snapshot();
                        reply["history"] = history;
                        link.Wire.Post(reply);
                        // (then the plan's usage here, and again whenever it changes: older clients ignore it)
                        link.Attach(view, host.Usage);
                        foreach (var ask in view.PendingAsks) link.Event(ask);
                    });
                    Logging.Log.Here.Info($"Remote control: {link.Wire.Peer} opened session {id}");
                    break;
                }
            case "send":
                {
                    var images = SessionView.Images(m["images"]);
                    string text = S(m, "text");
                    await ui.InvokeAsync(() => link.View?.Send(text, bubble: true, images));
                    break;
                }
            case "interrupt":
                await ui.InvokeAsync(() => link.View?.RemoteInterrupt());
                break;
            case "answer":
                {
                    bool allow = m["allow"] is JsonValue a && a.TryGetValue<bool>(out var b) && b;
                    string request = S(m, "request_id");
                    // (Claude's questions: the answers too)
                    var answers = m["answers"]?.DeepClone() as JsonObject;
                    await ui.InvokeAsync(() => link.View?.RemoteAnswer(request, allow, answers));
                    break;
                }
            case "control":
                {
                    string subtype = S(m, "subtype"), value = S(m, "value");
                    await ui.InvokeAsync(() => link.View?.RemoteControl(subtype, value));
                    break;
                }
            default:
                // (asked by a newer version)
                throw new RemoteException(F("Daisy's App on {0} can't do that yet: update it there.", Environment.MachineName));
        }
    }

    /// <summary>
    /// Browsing this PC's folders from the other PC (picking a new session's folder): a folder's subfolders (not hidden
    /// or system ones), or, for "", the drives and the user's own folders.
    /// </summary>
    private static JsonObject Dirs(string path)
    {
        var dirs = new JsonArray();
        var places = new JsonArray();
        string Home(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        foreach (var (name, place) in new[]
                 {
                     (T("Home"), Home(Environment.SpecialFolder.UserProfile)), (T("Documents"), Home(Environment.SpecialFolder.MyDocuments)),
                     (T("Desktop"), Home(Environment.SpecialFolder.DesktopDirectory)),
                 })
            if (place.Length > 0 && Directory.Exists(place)) places.Add(new JsonObject { ["name"] = name, ["path"] = place });
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                string label = d.VolumeLabel is { Length: > 0 } v ? $"{d.Name.TrimEnd('\\')} ({v})" : d.Name.TrimEnd('\\');
                places.Add(new JsonObject { ["name"] = label, ["path"] = d.Name });
            }
            catch { }
        }
        string? full = null, parent = null;
        if (path.Trim().Length > 0)
        {
            if (!Path.IsPathFullyQualified(path)) throw new RemoteException(F("{0} isn't a full path (C:\\…).", path));
            full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) throw new RemoteException(F("The folder {0} isn't on {1}.", full, Environment.MachineName));
            parent = Directory.GetParent(full)?.FullName;
            try
            {
                foreach (var d in new DirectoryInfo(full).EnumerateDirectories().Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                    dirs.Add(d.Name);
            }
            catch (UnauthorizedAccessException) { throw new RemoteException(F("{0} can't be opened there (no access).", full)); }
        }
        return new JsonObject { ["op"] = "dirs", ["path"] = full ?? "", ["parent"] = parent, ["dirs"] = dirs, ["places"] = places };
    }

    /// <summary>A new folder in a folder on this PC (from the other PC's folder browser).</summary>
    private static JsonObject MakeDir(string parent, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new RemoteException(F("\"{0}\" can't be a folder's name.", name));
        if (!Path.IsPathFullyQualified(parent) || !Directory.Exists(parent)) throw new RemoteException(F("The folder {0} isn't on {1}.", parent, Environment.MachineName));
        string path = Path.Combine(Path.GetFullPath(parent), name);
        if (Directory.Exists(path)) throw new RemoteException(F("There's a folder {0} there already.", name));
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new RemoteException(F("It couldn't be made: {0}", ex.Message)); }
        Logging.Log.Here.Info($"Remote control: folder {path} made from another PC");
        return new JsonObject { ["op"] = "made", ["path"] = path };
    }

    /// <summary>A connection, and the session it has open (what the session does goes out on it).</summary>
    private sealed class Link(RemoteWire wire)
    {
        public RemoteWire Wire { get; } = wire;
        public SessionView? View { get; private set; }
        private Action<JsonElement>? shared;
        private Action? closing, usageChanged;
        private SessionView? planOf;
        private PlanUsage? usage;

        public void Event(JsonElement e) => Wire.Post("{\"op\":\"event\",\"msg\":" + e.GetRawText() + "}");

        public void Attach(SessionView view, PlanUsage plan)
        {
            Detach();
            View = view;
            usage = plan;
            shared = Event;
            closing = () => Wire.Post(new JsonObject { ["op"] = "closed" });
            // (the session's own account's, when it runs on another one than Claude Code's own)
            usageChanged = () => Wire.Post(view.PlanToRemote());
            view.Shared += shared;
            view.Closing += closing;
            plan.Changed += usageChanged;
            view.PlanChanged += usageChanged;
            // (another account's usage read here: the list on the other PC follows)
            AccountsUsage.OthersChanged += usageChanged;
            planOf = view;
            usageChanged();
        }

        public void Detach()
        {
            if (View == null) return;
            View.Shared -= shared;
            View.Closing -= closing;
            if (usage != null) usage.Changed -= usageChanged;
            if (planOf != null) planOf.PlanChanged -= usageChanged;
            AccountsUsage.OthersChanged -= usageChanged;
            planOf = null;
            View = null;
            usage = null;
        }
    }

    private void Close()
    {
        stop?.Cancel();
        stop = null;
        try { listener?.Stop(); } catch { }
        listener = null;
    }

    public void Dispose() => Close();
}

/// <summary>Remote control, this PC the client: a question for the other PC on a connection of its own (its sessions,
/// a folder's files, a new session).</summary>
internal static class RemoteClient
{
    public static (string Host, int Port) Split(string target, int port)
    {
        target = target.Trim();
        if (target.StartsWith('[') && target.IndexOf("]:", StringComparison.Ordinal) is int b and > 0 && int.TryParse(target[(b + 2)..], out int p6)) return (target[1..b], p6);
        int i = target.LastIndexOf(':');
        if (i > 0 && target.IndexOf(':') == i && int.TryParse(target[(i + 1)..], out int p)) return (target[..i], p);
        return (target, port);
    }

    /// <summary>A question for another PC in the list.</summary>
    public static async Task<JsonObject> AskAsync(RemotePc pc, JsonObject request)
    {
        using var w = await RemoteWire.ConnectAsync(pc.Address, pc.Port, pc.Passphrase);
        w.Post(request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var reply = await w.ReadAsync(timeout.Token) ?? throw new RemoteException(T("The other PC closed the connection."));
        if (reply["op"]?.GetValue<string>() == "error") throw new RemoteException(reply["message"]?.GetValue<string>() ?? "?");
        return reply;
    }
}

/// <summary>
/// A tab whose session runs on another PC (remote control): it connects there, opens the session (its conversation so
/// far comes back with it: <see cref="Attached"/>), and from then on everything the session does there arrives as
/// Claude Code's stream-json, as it would from a session here, so the tab draws it the same way. Messages, Stop,
/// answers and changes go there; until it's open they wait.
/// </summary>
internal sealed class RemoteAgent : IAgent
{
    private readonly SessionInfo info;
    private readonly Dispatcher ui;
    private readonly string host, passphrase;
    private readonly int port;
    private readonly List<JsonObject> waiting = new();
    private RemoteWire? wire;
    private bool disposed, ended, attached;

    public event Action<JsonElement>? Message;
    public event Action<string>? Exited;
    /// <summary>It's open: the session as it is there ({op: "attached"}: its name, folder, AI, state and history).</summary>
    public event Action<JsonObject>? Attached;
    /// <summary>The plan's usage there ({op: "usage"}: since 2.6.0, on opening and as it changes).</summary>
    public event Action<PlanUsage.Elsewhere>? Usage;
    /// <summary>The Claude account the session runs on there, by id (in {op: "usage"} since 3.5.2).</summary>
    public event Action<string>? AccountIs;
    /// <summary>That PC's Claude accounts with their usage, and its Claude Code's own sign-in (a usage message, since 3.15.2).</summary>
    public event Action<JsonArray, string?>? AccountsAre;

    public bool Running => !disposed && !ended;
    public int? ContextWindow { get; private set; }

    private RemoteAgent(SessionInfo info, Dispatcher ui, string host, int port, string passphrase)
    {
        this.info = info;
        this.ui = ui;
        this.host = host;
        this.port = port;
        this.passphrase = passphrase;
    }

    public static RemoteAgent Start(RemoteSettings r, SessionInfo info, Dispatcher ui)
    {
        var pc = r.Find(info.Remote);
        var (host, port) = pc != null ? (pc.Address.Trim(), pc.Port) : RemoteClient.Split(info.Remote ?? "", RemoteSettings.DefaultPort);
        var a = new RemoteAgent(info, ui, host, port, pc?.Passphrase ?? "");
        if (pc == null) a.End(F("{0} isn't in the list of other PCs any more (Settings → AI → Remote control).", info.RemotePc ?? info.Remote ?? "?"));
        else _ = Task.Run(a.RunAsync);
        return a;
    }

    private async Task RunAsync()
    {
        try
        {
            wire = await RemoteWire.ConnectAsync(host, port, passphrase);
            if (disposed) { wire.Dispose(); return; }
            Logging.Log.Here.Info($"Remote control: connected to {wire.Peer} for session {info.Id}");
            wire.Post(new JsonObject { ["op"] = "attach", ["id"] = info.Id });
            while (await wire.ReadAsync() is { } m)
            {
                switch (m["op"]?.GetValue<string>())
                {
                    case "attached":
                        ContextWindow = m["context"] is JsonValue c && c.TryGetValue<int>(out int n) && n > 0 ? n : null;
                        await ui.InvokeAsync(() =>
                        {
                            if (disposed) return;
                            attached = true;
                            Attached?.Invoke(m);
                            foreach (var w in waiting) wire.Post(w);
                            waiting.Clear();
                        });
                        break;
                    case "event":
                        if (m["msg"] is not { } msg) break;
                        JsonElement e;
                        using (var d = JsonDocument.Parse(msg.ToJsonString())) e = d.RootElement.Clone();
                        _ = ui.BeginInvoke(() => { if (!disposed) Message?.Invoke(e); });
                        break;
                    case "usage":
                        {
                            var plan = PlanUsage.FromRemote(m);
                            string? account = m["sessionAccount"] is JsonValue sa && sa.TryGetValue<string>(out var said) ? said : null;
                            string? own = m["ownAccount"] is JsonValue oa && oa.TryGetValue<string>(out var o) ? o : null;
                            var accounts = m["accounts"] as JsonArray;
                            _ = ui.BeginInvoke(() =>
                            {
                                if (disposed) return;
                                Usage?.Invoke(plan);
                                if (accounts != null) AccountsAre?.Invoke(accounts, own);
                                if (account != null) AccountIs?.Invoke(account);
                            });
                            break;
                        }
                    case "closed":
                        End(F("The session was closed on {0}.", wire.Peer));
                        return;
                    case "error":
                        End(m["message"]?.GetValue<string>() ?? "?");
                        return;
                }
            }
            End(T("The other PC closed the connection."));
        }
        catch (RemoteException ex) { End(ex.Message); }
        catch (Exception ex) { End(ex.Message); }
    }

    private void End(string why)
    {
        if (ended) return;
        ended = true;
        wire?.Dispose();
        if (!disposed) Logging.Log.Here.Info($"Remote control: session {info.Id} disconnected ({why}; {wire?.Traffic})");
        _ = ui.BeginInvoke(() => { if (!disposed) Exited?.Invoke(why); });
    }

    /// <summary>To the other PC, on the UI thread: at once if the session's open, else when it is.</summary>
    private void Post(JsonObject o)
    {
        if (!Running) return;
        if (attached && wire != null) wire.Post(o); else waiting.Add(o);
    }

    public void Send(string text, IReadOnlyList<ImageData>? images = null)
    {
        var o = new JsonObject { ["op"] = "send", ["text"] = text };
        if (images is { Count: > 0 }) o["images"] = SessionView.ImagesJson(images);
        Post(o);
    }

    public void Control(string subtype, Dictionary<string, object?>? fields)
    {
        switch (subtype)
        {
            case "interrupt": Interrupt(); break;
            case "set_model": Post(new JsonObject { ["op"] = "control", ["subtype"] = subtype, ["value"] = fields?.GetValueOrDefault("model") as string ?? "" }); break;
            case "set_permission_mode": Post(new JsonObject { ["op"] = "control", ["subtype"] = subtype, ["value"] = fields?.GetValueOrDefault("mode") as string ?? "" }); break;
            case "set_account": Post(new JsonObject { ["op"] = "control", ["subtype"] = subtype, ["value"] = fields?.GetValueOrDefault("account") as string ?? "" }); break;
        }
    }

    public void Interrupt()
    {
        if (attached) Post(new JsonObject { ["op"] = "interrupt" });
    }

    public void Answer(string requestId, bool allow, JsonElement input, string? reason = null)
    {
        var o = new JsonObject { ["op"] = "answer", ["request_id"] = requestId, ["allow"] = allow };
        if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("answers", out var answers)) o["answers"] = JsonNode.Parse(answers.GetRawText());
        Post(o);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (wire != null && !ended) Logging.Log.Here.Info($"Remote control: session {info.Id} disconnected (the tab closed; {wire.Traffic})");
        wire?.Dispose();
    }
}
