using System.Diagnostics;
using System.IO;
using DaisysApp.Applets.SimEffects.Arduino;

namespace DaisysApp.Applets.SimEffects.Wheel;

/// <summary>
/// A Moza wheel's RPM LEDs, through its wheelbase's serial port (Moza's USB devices are COM ports, vendor 0x346E), as
/// Pit House and the Linux tool boxflat talk to it. A message: <c>7E</c>, its length (the command's id and data), the
/// group, the device (the rim: 23, some 21), the command's id, its data, and a checksum (13 plus every byte before it).
/// Newer rims (CS V2P, RS V2, GS V2P, KS, FSR V2, TSW…): telemetry mode on (group 63, <c>1C 00</c> = 1), the LEDs'
/// colours (<c>19 00</c>: five of index, R, G, B a message) and which are lit (<c>1A 00</c>: 2 bytes, bit 0 the first).
/// The ES: its indicator mode (<c>04</c> = 1), then the lit ones (group 65, <c>FD DE</c>: 4 bytes) in its own colours.
/// </summary>
internal sealed class MozaWheel : IWheelLights
{
    public const int Vendor = 0x346E;
    private const byte Start = 0x7E, Magic = 13, Read = 64, Write = 63, Telemetry = 65;
    private static readonly byte[] ModeNew = { 28, 0 }, ModeOld = { 4 }, Colours = { 25, 0 }, LitNew = { 26, 0 }, LitOld = { 253, 222 };
    /// <summary>The rim's device ids, as Pit House finds it.</summary>
    private static readonly byte[] RimIds = { 23, 21 };

    private readonly SerialLink link;
    private readonly byte device;
    private readonly bool newer;
    private readonly byte? mode;
    private readonly (byte R, byte G, byte B)[] palette = new (byte, byte, byte)[10];
    private readonly byte[] drain = new byte[256];
    private int lastBits = -1;
    private bool paletteSent;
    private long lastSent, lastDrain;

    public string Name { get; }
    public int Leds => 10;
    public bool Rgb => newer;
    public bool HasDisplay => false;

    private MozaWheel(SerialLink link, string name, byte device, bool newer, byte? mode)
    {
        this.link = link;
        this.device = device;
        this.newer = newer;
        this.mode = mode;
        Name = name;
        // the LEDs follow what's sent, not the wheel's own RPM
        Send(Write, device, newer ? ModeNew : ModeOld, 1);
    }

    /// <summary>The first Moza rim answering on a Moza COM port, or null; <paramref name="why"/> when there's none.</summary>
    public static MozaWheel? Find(out string why)
    {
        var ports = SerialPorts.OfVendor(Vendor);
        why = ports.Count == 0 ? T("No Moza wheelbase found") : T("No Moza wheel found on the wheelbase");
        foreach (var port in ports)
        {
            SerialLink link;
            try { link = SerialPorts.Open(port.Port, 115200); }
            catch (IOException ex)
            {
                why = F("{0} (Moza Pit House may have it)", ex.Message);
                Logging.Log.Here.Info($"Moza: {port.Display}: {ex.Message}");
                continue;
            }
            var keep = false;
            try
            {
                foreach (var id in RimIds)
                {
                    // (reading its mode: the newer rims answer the first, the ES the second)
                    if (Ask(link, id, ModeNew) is { } m)
                    {
                        keep = true;
                        Logging.Log.Here.Info($"Moza: a rim at {id} on {port.Display}, telemetry mode {m}");
                        return new MozaWheel(link, F("Moza wheel on {0}", port.Port), id, true, m);
                    }
                    if (Ask(link, id, ModeOld) is { } o)
                    {
                        keep = true;
                        Logging.Log.Here.Info($"Moza: an ES rim at {id} on {port.Display}, indicator mode {o}");
                        return new MozaWheel(link, F("Moza ES wheel on {0}", port.Port), id, false, o);
                    }
                }
                Logging.Log.Here.Info($"Moza: {port.Display}: no rim answered");
            }
            catch (IOException ex) { Logging.Log.Here.Info($"Moza: {port.Display}: {ex.Message}"); }
            finally { if (!keep) link.Dispose(); }
        }
        return null;
    }

    /// <summary>Reads a 1-byte setting of the rim: its value, or null when nothing answers within 150 ms.</summary>
    private static byte? Ask(SerialLink link, byte device, byte[] command)
    {
        link.Write(Message(Read, device, command, 1));
        var got = new List<byte>();
        var buffer = new byte[256];
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 150)
        {
            int n = link.Read(buffer);
            for (int i = 0; i < n; i++) got.Add(buffer[i]);
            // an answer: 7E, length, the group with its top bit set, the device (its halves swapped), the id, the value
            for (int i = 0; i + 4 + command.Length < got.Count; i++)
            {
                if (got[i] != Start || got[i + 2] != (Read | 0x80) || got[i + 1] != command.Length + 1) continue;
                bool match = true;
                for (int k = 0; k < command.Length && match; k++) match = got[i + 4 + k] == command[k];
                if (match) return got[i + 4 + command.Length];
            }
        }
        return null;
    }

    private static byte[] Message(byte group, byte device, byte[] command, params byte[] data)
    {
        var m = new byte[4 + command.Length + data.Length + 1];
        m[0] = Start;
        m[1] = (byte)(command.Length + data.Length);
        m[2] = group;
        m[3] = device;
        command.CopyTo(m, 4);
        data.CopyTo(m, 4 + command.Length);
        int sum = Magic;
        for (int i = 0; i < m.Length - 1; i++) sum += m[i];
        m[^1] = (byte)sum;
        return m;
    }

    private void Send(byte group, byte device, byte[] command, params byte[] data) => link.Write(Message(group, device, command, data));

    public bool Show((byte R, byte G, byte B)[] leds, bool force)
    {
        try
        {
            int bits = 0;
            bool recolour = force || !paletteSent;
            for (int i = 0; i < Math.Min(leds.Length, Leds); i++)
            {
                if (leds[i] == default) continue;
                bits |= 1 << i;
                // (an LED's colour kept while it's off: sent again only when one changes, at the shift point)
                if (palette[i] != leds[i]) { palette[i] = leds[i]; recolour = true; }
            }
            // (sent again each second, as Pit House does, else when it changes)
            long now = Environment.TickCount64;
            if (newer && recolour)
            {
                for (int half = 0; half < 2; half++)
                {
                    var data = new byte[20];
                    for (int k = 0; k < 5; k++)
                    {
                        int i = half * 5 + k;
                        data[k * 4] = (byte)i;
                        (data[k * 4 + 1], data[k * 4 + 2], data[k * 4 + 3]) = palette[i];
                    }
                    Send(Write, device, Colours, data);
                }
                paletteSent = true;
                lastBits = -1;
            }
            if (!force && bits == lastBits && now - lastSent < 1000) return true;
            if (newer) Send(Write, device, LitNew, (byte)bits, (byte)(bits >> 8));
            else Send(Telemetry, device, LitOld, (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits);
            lastBits = bits;
            lastSent = now;
            // (what the wheel says back isn't needed: read once a second so it doesn't pile up; a read can wait 20 ms)
            if (now - lastDrain >= 1000) { lastDrain = now; link.Read(drain); }
            return true;
        }
        catch (IOException) { return false; }
    }

    public bool SetDisplay(string text) => true;

    public void Off()
    {
        try
        {
            if (newer) Send(Write, device, LitNew, 0, 0);
            else Send(Telemetry, device, LitOld, 0, 0, 0, 0);
            // its own mode back, as it was found
            if (mode is { } m && m != 1) Send(Write, device, newer ? ModeNew : ModeOld, m);
        }
        catch (IOException) { }
    }

    public void Dispose() => link.Dispose();
}
