namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>
/// The DaisysSim sketch's serial protocol (version 1, 115200 baud; the sketch is in Arduino\DaisysSim\ next to the
/// module, and its README says it all). Every message is a frame: 0xA5, the body's length, the body (a command, a
/// slot, then its data), and the XOR of the body's bytes. A slot is one feature on the board (0..<see cref="MaxSlots"/>-1).
/// LEDs only change on <see cref="Show"/>, which the board answers with <see cref="Ready"/> once they're written
/// (writing WS2812 LEDs stops interrupts, so nothing is sent meanwhile: bytes would be lost).
/// </summary>
public static class ArduinoProtocol
{
    public const int Baud = 115200;
    public const byte Start = 0xA5;
    public const int Version = 1;
    public const int MaxSlots = 4;
    public const int MaxBody = 200;

    // ---- the PC's commands ----
    /// <summary>Who are you: answered with <see cref="HelloReply"/>.</summary>
    public const byte Hello = 0x01;
    /// <summary>A slot's feature: type, then the feature's own bytes (the LED ones: pin, count, brightness, flags).
    /// Answered with <see cref="ConfigReply"/>.</summary>
    public const byte Config = 0x02;
    /// <summary>Starts the board again (after a feature's pin or type changed: LED outputs can't be taken back).</summary>
    public const byte Reset = 0x03;
    /// <summary>LED colours: first LED, then R G B for each.</summary>
    public const byte Pixels = 0x10;
    /// <summary>An 8×8 picture: 8 rows (bit 7 = left), then R G B lit and R G B unlit.</summary>
    public const byte Bitmap = 0x11;
    /// <summary>Every LED of the slot (0xFF: of every slot) off.</summary>
    public const byte Clear = 0x12;
    /// <summary>Shows what was sent since the last one: answered with <see cref="Ready"/>.</summary>
    public const byte Show = 0x20;
    /// <summary>Still there: answered with <see cref="Pong"/>. Without anything for 3 s the board turns everything off.</summary>
    public const byte Ping = 0x7F;
    // (0x30-0x6F: for features' own commands; see the sketch's README)

    // ---- the board's answers ----
    /// <summary>Version, slots, then the sketch's name (ASCII).</summary>
    public const byte HelloReply = 0x81;
    /// <summary>The slot, then 0 (fine) or a <see cref="Problem"/> code.</summary>
    public const byte ConfigReply = 0x82;
    public const byte Ready = 0x8A;
    public const byte Pong = 0x8F;

    /// <summary>What a <see cref="ConfigReply"/>'s code means.</summary>
    public static string Problem(int code) => code switch
    {
        0 => "",
        1 => T("Not a feature this sketch has (update the sketch)"),
        2 => T("That pin can't drive LEDs"),
        3 => T("Too many LEDs for the board"),
        4 => T("Slot taken"),
        _ => F("Problem {0}", code),
    };

    /// <summary>A frame: the command, the slot and the data, wrapped.</summary>
    public static byte[] Frame(byte command, byte slot, ReadOnlySpan<byte> data)
    {
        int len = 2 + data.Length;
        if (len > MaxBody) throw new ArgumentException("frame too long");
        var f = new byte[len + 3];
        f[0] = Start;
        f[1] = (byte)len;
        f[2] = command;
        f[3] = slot;
        data.CopyTo(f.AsSpan(4));
        byte x = 0;
        for (int i = 2; i < 2 + len; i++) x ^= f[i];
        f[^1] = x;
        return f;
    }

    /// <summary>Reads frames out of bytes as they come (from the board), checking each.</summary>
    public sealed class Reader
    {
        private readonly byte[] body = new byte[MaxBody];
        private int state, len, at;

        /// <summary>Takes a byte; a whole good frame's body (command, slot, data) when it ends one.</summary>
        public byte[]? Take(byte b)
        {
            switch (state)
            {
                case 0:
                    if (b == Start) state = 1;
                    return null;
                case 1:
                    if (b < 2 || b > MaxBody) { state = b == Start ? 1 : 0; return null; }
                    len = b;
                    at = 0;
                    state = 2;
                    return null;
                case 2:
                    body[at++] = b;
                    if (at == len) state = 3;
                    return null;
                default:
                    state = 0;
                    byte x = 0;
                    for (int i = 0; i < len; i++) x ^= body[i];
                    return x == b ? body[..len] : null;
            }
        }
    }
}
