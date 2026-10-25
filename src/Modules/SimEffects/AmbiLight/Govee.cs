using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DaisysApp.Logging;

namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>A Govee light that answered a scan.</summary>
internal sealed record GoveeFound(string Ip, string Mac, string Sku);

/// <summary>
/// Govee's LAN API (switch on "LAN Control" for the light in the Govee Home app): a scan multicast to
/// 239.255.255.250:4001, answers and status on UDP 4002 (heard here), commands to the light on UDP 4003. Colours go as
/// Govee's own desktop app sends them for DreamView, the "razer" command: binary frames, base64 in JSON (see
/// <see cref="Frame"/>; reverse-engineered by others: LedFx, govee2mqtt, GoveeDreamView).
/// </summary>
internal sealed class GoveeLan : IDisposable
{
    private const int ScanPort = 4001, ReplyPort = 4002, CommandPort = 4003;
    private static readonly IPAddress Multicast = IPAddress.Parse("239.255.255.250");

    private readonly Socket send = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private Socket? listen;
    private Thread? thread;
    private volatile bool closing;
    private readonly ConcurrentDictionary<string, long> heard = new();
    private readonly ConcurrentDictionary<string, GoveeFound> found = new();

    /// <summary>Why answers can't be heard (port 4002 taken), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Starts hearing answers on port 4002 (shared with other programs where they let it be).</summary>
    public void Listen()
    {
        if (listen != null || Problem != null) return;
        try
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            s.Bind(new IPEndPoint(IPAddress.Any, ReplyPort));
            listen = s;
            thread = new Thread(Receive) { IsBackground = true, Name = "AmbiLight-Govee" };
            thread.Start();
        }
        catch (Exception ex)
        {
            Problem = F("Port {0} is taken by another program", ReplyPort);
            Log.Here.Warn($"AmbiLight: can't listen on UDP {ReplyPort} for Govee's answers", ex);
        }
    }

    private void Receive()
    {
        var buffer = new byte[4096];
        while (!closing)
        {
            try
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n = listen!.ReceiveFrom(buffer, ref from);
                string ip = ((IPEndPoint)from).Address.ToString();
                heard[ip] = Environment.TickCount64;
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, n));
                var msg = doc.RootElement.GetProperty("msg");
                if (msg.GetProperty("cmd").GetString() != "scan") continue;
                var data = msg.GetProperty("data");
                string at = data.TryGetProperty("ip", out var i) ? i.GetString() ?? ip : ip;
                var f = new GoveeFound(at, data.TryGetProperty("device", out var d) ? d.GetString() ?? "" : "",
                    data.TryGetProperty("sku", out var k) ? k.GetString() ?? "" : "");
                if (found.TryAdd(at, f)) Log.Here.Info($"AmbiLight: Govee {f.Sku} at {f.Ip} ({f.Mac})");
                heard[at] = Environment.TickCount64;
            }
            catch (Exception) when (closing) { return; }
            catch (SocketException) { Thread.Sleep(200); }
            catch (Exception) { } // not JSON, not Govee's
        }
    }

    /// <summary>Whether the light at <paramref name="ip"/> answered in the last <paramref name="ms"/>.</summary>
    public bool Answered(string ip, int ms = 25000) => heard.TryGetValue(ip, out long t) && Environment.TickCount64 - t < ms;

    /// <summary>Asks every Govee light with LAN Control on to say where it is; what answers within 2 s.</summary>
    public async Task<List<GoveeFound>> ScanAsync()
    {
        Listen();
        found.Clear();
        var scan = Encoding.UTF8.GetBytes("{\"msg\":{\"cmd\":\"scan\",\"data\":{\"account_topic\":\"reserve\"}}}");
        // from each network the PC is on (a multicast leaves by one only)
        foreach (var address in LocalAddresses())
        {
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                s.Bind(new IPEndPoint(address, 0));
                s.SendTo(scan, new IPEndPoint(Multicast, ScanPort));
            }
            catch (Exception ex) { Log.Here.Debug($"AmbiLight: scan from {address}: {ex.Message}"); }
        }
        try { send.SendTo(scan, new IPEndPoint(Multicast, ScanPort)); } catch { }
        await Task.Delay(2000);
        return found.Values.OrderBy(f => f.Ip).ToList();
    }

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);

    /// <summary>Its on/off, brightness and colour, answered on port 4002 (so <see cref="Answered"/> knows it's there).</summary>
    public void AskStatus(string ip) => Json(ip, "{\"msg\":{\"cmd\":\"devStatus\",\"data\":{}}}");

    public void Power(string ip, bool on) => Json(ip, "{\"msg\":{\"cmd\":\"turn\",\"data\":{\"value\":" + (on ? 1 : 0) + "}}}");

    public void Brightness(string ip, int percent) =>
        Json(ip, "{\"msg\":{\"cmd\":\"brightness\",\"data\":{\"value\":" + Math.Clamp(percent, 1, 100) + "}}}");

    /// <summary>Into (or out of) streaming: <c>BB 00 01 B1 01 0A</c> on, <c>BB 00 01 B1 00 0B</c> off (back to its own scene).</summary>
    public void Stream(string ip, bool on) => Razer(ip, Packet(0xB1, new[] { (byte)(on ? 1 : 0) }));

    /// <summary>A colour per segment, first segment first.</summary>
    public void Colours(string ip, ReadOnlySpan<byte> rgb, bool stretch) => Razer(ip, Frame(rgb, stretch));

    /// <summary>
    /// <c>BB, length (2 bytes), B0, stretch, count, R G B × count, XOR of all before</c>; the length counts what comes
    /// after B0 (2 + 3 × count). 0x01 stretch spreads the colours over the light's LEDs, 0x00 one per segment.
    /// </summary>
    public static byte[] Frame(ReadOnlySpan<byte> rgb, bool stretch)
    {
        int count = Math.Min(255, rgb.Length / 3);
        var body = new byte[2 + count * 3];
        body[0] = (byte)(stretch ? 1 : 0);
        body[1] = (byte)count;
        rgb[..(count * 3)].CopyTo(body.AsSpan(2));
        return Packet(0xB0, body);
    }

    public static byte[] Packet(byte command, byte[] body)
    {
        var p = new byte[4 + body.Length + 1];
        p[0] = 0xBB;
        p[1] = (byte)(body.Length >> 8);
        p[2] = (byte)body.Length;
        p[3] = command;
        body.CopyTo(p, 4);
        byte x = 0;
        for (int i = 0; i < p.Length - 1; i++) x ^= p[i];
        p[^1] = x;
        return p;
    }

    private void Razer(string ip, byte[] packet) =>
        Json(ip, "{\"msg\":{\"cmd\":\"razer\",\"data\":{\"pt\":\"" + Convert.ToBase64String(packet) + "\"}}}");

    private void Json(string ip, string json)
    {
        if (!IPAddress.TryParse(ip, out var address)) return;
        try { send.SendTo(Encoding.UTF8.GetBytes(json), new IPEndPoint(address, CommandPort)); }
        catch (Exception ex) { Log.Here.Debug($"AmbiLight: sending to {ip}: {ex.Message}"); }
    }

    public void Dispose()
    {
        closing = true;
        listen?.Dispose();
        send.Dispose();
    }
}
