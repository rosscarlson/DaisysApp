using System.IO;
using System.Text;

namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>
/// SimHub's own Arduino sketch (DisplayClientV2, its "J revision"), spoken as SimHub speaks it, so a board set up with
/// SimHub's Arduino setup needs no new sketch: what's on which pin is in that sketch, and the board says how many RGB
/// LEDs it has and whether it has an 8×8 matrix. The PC's bytes go in packets (<c>01 01</c> id length data CRC-8, at
/// most 32 bytes), each answered <c>03 id</c> (or <c>04</c> last-good reason, then sent again); the board's are
/// <c>08</c> byte, <c>06</c>/<c>07</c> length text <c>20</c> (text, debug text) and <c>09</c> type length data. A
/// command is <c>03</c> then a letter: <c>'1'</c> hello (answered with the sketch's version letter), <c>'8'</c> baud,
/// <c>'0'</c> features ("…R…\n": R = an RGB matrix), <c>'4'</c> RGB LED count, <c>'N'</c> name, <c>'6'</c> RGB LEDs and
/// <c>'R'</c> the matrix (blocks: 1 all, 2 first count, 3 first count one colour; 0 ends; answered <c>08 15</c> once
/// shown), <c>'A'</c> still there (answered <c>08 03</c>). It starts at 19200 baud; SimHub then moves it to 115200.
/// </summary>
internal sealed class SimHubSketch
{
    public const int SlowBaud = 19200, FastBaud = 115200;
    public const string Kind = "SimHub";

    private const byte Command = 0x03;
    private const int MaxPacket = 32;

    private readonly IBoardLink link;
    private readonly Func<bool> stopped;
    private readonly byte[] buffer = new byte[256];
    private int id = 255;   // 255 is always taken; then 0..128, round again (as the sketch counts)

    // what the board has said
    private readonly Queue<int> acks = new();
    private bool nacked;
    private readonly Queue<byte> bytes = new();
    private readonly StringBuilder text = new();
    private int state, need;
    private byte kind;
    private readonly List<byte> body = new();

    public char Version { get; private set; }
    public string Name { get; private set; } = "";
    public int Leds { get; private set; }
    public bool HasMatrix { get; private set; }

    public SimHubSketch(IBoardLink link, Func<bool> stopped)
    {
        this.link = link;
        this.stopped = stopped;
    }

    /// <summary>Hello: true when SimHub's sketch answers (an Uno's bootloader takes a second and a half first).</summary>
    public bool Hello(int tries)
    {
        if (!Packet(new byte[] { Command, (byte)'1', 0 }, tries, 350)) return false;
        if (AwaitByte(_ => true, 600) is not { } v) return false;
        Version = (char)v;
        return true;
    }

    /// <summary>Moves the board (and the port) to 115200 baud, as SimHub does; false when it doesn't answer there.</summary>
    public bool GoFast()
    {
        Send(Command, (byte)'8', 11);
        Thread.Sleep(400);   // the sketch waits 200 ms, then starts again at the new speed
        link.SetBaud(FastBaud);
        Clear();
        return Hello(4);
    }

    /// <summary>Asks what's on the board: its name, its RGB LEDs, a matrix.</summary>
    public void Ask()
    {
        Send(Command, (byte)'0');
        string features = AwaitLine(800) ?? throw new TimeoutException(T("No answer to its features"));
        HasMatrix = features.Contains('R');
        Send(Command, (byte)'4');
        Leds = AwaitByte(_ => true, 600) ?? throw new TimeoutException(T("No answer to its LED count"));
        Send(Command, (byte)'N');
        Name = (AwaitLine(800) ?? "").Trim();
    }

    /// <summary>LEDs of the chain: each block its first LED and colours (R G B each); shown together.</summary>
    public void SetLeds(IEnumerable<(int First, byte[] Rgb)> blocks)
    {
        var m = new List<byte> { Command, (byte)'6' };
        foreach (var (first, rgb) in blocks)
        {
            int n = Math.Min(rgb.Length / 3, Leds - first);
            if (first < 0 || n <= 0) continue;
            m.Add(2); m.Add((byte)first); m.Add((byte)n);
            m.AddRange(rgb.AsSpan(0, n * 3).ToArray());
        }
        m.Add(0);
        Send(m.ToArray());
        Shown();
    }

    /// <summary>Every LED of the chain off (a SimHub test mode lights them all red until then).</summary>
    public void ClearLeds()
    {
        if (Leds <= 0) return;
        Send(Command, (byte)'6', 3, 0, (byte)Leds, 0, 0, 0, 0);
        Shown();
    }

    /// <summary>The 8×8 matrix: 64 colours, row by row from the top left (the sketch knows its own wiring).</summary>
    public void SetMatrix(byte[] rgb)
    {
        var m = new byte[3 + 192 + 1];
        m[0] = Command; m[1] = (byte)'R'; m[2] = 1;
        rgb.AsSpan(0, 192).CopyTo(m.AsSpan(3));
        Send(m);
        Shown();
    }

    /// <summary>Still there (without anything for 5 s the sketch turns its matrix off).</summary>
    public void Ping()
    {
        Send(Command, (byte)'A');
        if (AwaitByte(b => b == 0x03, 500) == null) throw new TimeoutException(T("Stopped answering"));
    }

    private void Shown()
    {
        if (AwaitByte(b => b == 0x15, 600) == null) throw new TimeoutException(T("Stopped answering"));
    }

    // ---- packets ----

    private void Send(params byte[] data)
    {
        for (int at = 0; at < data.Length; at += MaxPacket)
            if (!Packet(data.AsSpan(at, Math.Min(MaxPacket, data.Length - at)).ToArray(), 5, 150))
                throw new TimeoutException(T("Stopped answering"));
    }

    /// <summary>Sends one packet until the board takes it.</summary>
    private bool Packet(byte[] data, int tries, int ms)
    {
        var p = new byte[data.Length + 5];
        p[0] = 1; p[1] = 1; p[2] = (byte)id; p[3] = (byte)data.Length;
        data.CopyTo(p, 4);
        p[^1] = Crc(p.AsSpan(2, data.Length + 2));
        for (int t = 0; t < tries && !stopped(); t++)
        {
            lock (acks) { acks.Clear(); nacked = false; }
            link.Write(p);
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until && !stopped())
            {
                Pump();
                lock (acks)
                {
                    if (acks.Contains(id))
                    {
                        id = id > 127 ? 0 : id + 1;
                        return true;
                    }
                    if (nacked) break;
                }
            }
        }
        return false;
    }

    /// <summary>The sketch's CRC-8 (polynomial 0xD5) over the id, the length and the data.</summary>
    public static byte Crc(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (byte)((crc & 0x80) != 0 ? crc << 1 ^ 0xD5 : crc << 1);
        }
        return crc;
    }

    // ---- what comes back ----

    private void Clear()
    {
        lock (acks) { acks.Clear(); nacked = false; }
        bytes.Clear();
        text.Clear();
        state = 0;
    }

    private int? AwaitByte(Func<byte, bool> want, int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do
        {
            while (bytes.Count > 0)
            {
                byte b = bytes.Dequeue();
                if (want(b)) return b;
            }
            if (DateTime.UtcNow >= until || stopped()) return null;
            Pump();
        } while (true);
    }

    private string? AwaitLine(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do
        {
            string s = text.ToString();
            int nl = s.IndexOf('\n');
            if (nl >= 0)
            {
                text.Remove(0, nl + 1);
                return s[..nl];
            }
            if (DateTime.UtcNow >= until || stopped()) return null;
            Pump();
        } while (true);
    }

    private void Pump()
    {
        int n = link.Read(buffer);
        for (int i = 0; i < n; i++) Take(buffer[i]);
    }

    // 0 waiting; 1 ack id; 2-3 nack; 4 byte; 5 text length; 6 text; 7 text's end; 8 custom type; 9 custom length; 10 custom data
    private void Take(byte b)
    {
        switch (state)
        {
            case 0:
                kind = b;
                state = b switch { 0x03 => 1, 0x04 => 2, 0x08 => 4, 0x06 or 0x07 => 5, 0x09 => 8, _ => 0 };
                return;
            case 1: lock (acks) acks.Enqueue(b); state = 0; return;
            case 2: state = 3; return;
            case 3: lock (acks) nacked = true; state = 0; return;
            case 4: bytes.Enqueue(b); state = 0; return;
            case 5: need = b; body.Clear(); state = need > 0 ? 6 : 7; return;
            case 6: body.Add(b); if (body.Count == need) state = 7; return;
            case 7:
                var s = Encoding.ASCII.GetString(body.ToArray());
                if (kind == 0x06) text.Append(s);
                else Logging.Log.Here.Debug($"Arduino (SimHub sketch): {s.TrimEnd()}");
                state = 0;
                return;
            case 8: state = 9; return;
            case 9: need = b; state = need > 0 ? 10 : 0; body.Clear(); return;
            default: body.Add(b); if (body.Count >= need) state = 0; return;
        }
    }

    /// <summary>Thrown when the board took 115200 baud but didn't answer there (it's tried at 19200 next).</summary>
    public sealed class SlowOnly : IOException
    {
        public SlowOnly() : base(T("Didn't answer at 115200 baud: trying 19200")) { }
    }
}
