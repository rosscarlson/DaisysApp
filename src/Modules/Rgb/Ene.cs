using System.Text;

namespace DaisysApp.Applets.Rgb;

/// <summary>An SMBus / I2C bus (the PC's SMBus through PawnIO, or a graphics card's I2C through NVIDIA's driver). False / null: no answer.</summary>
public interface ISmbus
{
    string Name { get; }
    /// <summary>A byte with no register (SMBus "receive byte"): whether something is at the address.</summary>
    int? ReadByte(byte address);
    int? ReadByteData(byte address, byte register);
    bool WriteByteData(byte address, byte register, byte value);
    bool WriteWordData(byte address, byte register, ushort value);
    bool WriteBlockData(byte address, byte register, ReadOnlySpan<byte> data);
}

/// <summary>
/// An ENE lighting controller (what ASUS calls Aura on the SMBus): on G.Skill Trident Z RGB / Neo and other RAM, older
/// ASUS motherboards (before their USB controller) and ASUS graphics cards. Its registers are 16-bit, reached through
/// two SMBus registers: the register number is written to 0x00 (as a word, bytes swapped), then a byte is read from
/// 0x81 or written to 0x01 (or a block to 0x03). The effects run on the chip itself.
/// The protocol as OpenRGB's ENESMBusController documents it.
/// </summary>
public sealed class EneChip
{
    private const ushort RegName = 0x1000, RegMicron = 0x1030, RegConfig = 0x1C00;
    private const ushort RegColoursDirect = 0x8000, RegColoursEffect = 0x8010, RegDirect = 0x8020, RegMode = 0x8021,
        RegSpeed = 0x8022, RegDirection = 0x8023, RegApply = 0x80A0, RegColoursDirect2 = 0x8100, RegColoursEffect2 = 0x8160;
    public const ushort RegSlotIndex = 0x80F8, RegI2cAddress = 0x80F9;
    private const byte Apply = 0x01, SaveValue = 0xAA;

    private readonly ISmbus bus;
    private readonly ushort effectReg;

    public byte Address { get; }
    /// <summary>The chip's name for itself, e.g. "DIMM_LED-0103" (RAM), "AUMA0-E6K5-0107" (a motherboard or graphics card).</summary>
    public string Version { get; }
    public int Leds { get; }
    /// <summary>A version this code knows the register layout of (others are listed but not changed).</summary>
    public bool Known { get; }
    public bool IsRam => Version.StartsWith("DIMM_LED", StringComparison.Ordinal) || Version.StartsWith("AUDA", StringComparison.Ordinal);

    private EneChip(ISmbus bus, byte address)
    {
        this.bus = bus;
        Address = address;
        var name = new byte[16];
        for (int i = 0; i < 16; i++) name[i] = Read((ushort)(RegName + i));
        Version = Encoding.ASCII.GetString(name).Split('\0')[0].Trim();
        var config = new byte[64];
        for (int i = 0; i < 64; i++) config[i] = Read((ushort)(RegConfig + i));

        // which colour registers and where the LED count is, by version (as OpenRGB has them)
        (bool v2, int countAt, bool known) = Version switch
        {
            "LED-0116" or "DIMM_LED-0102" or "DIMM_LED-0103" or "AUMA0-E8K4-0101" => (false, 2, true),
            "AUDA0-E6K5-0101" or "AUMA0-E6K5-0106" or "AUMA0-E6K5-0105" or "AUMA0-E6K5-0104" => (true, 2, true),
            "AUMA0-E6K5-0107" or "AUMA0-E6K5-1110" or "AUMA0-E6K5-1111" or "AUMA0-E6K5-1107"
                or "AUMA0-E6K5-0008" or "AUMA0-E6K5-1113" or "AUMA0-E6K5-1114" => (true, 3, true),
            _ => (false, 2, false),
        };
        effectReg = v2 ? RegColoursEffect2 : RegColoursEffect;
        Known = known;
        Leds = known ? config[countAt] : 0;
    }

    /// <summary>
    /// Whether an ENE chip answers at <paramref name="address"/>: something there, and its registers 0xA0–0xAF read back
    /// 0–15 (as only these chips do), and not Micron's RAM (which looks the same but isn't one). Reads only.
    /// </summary>
    public static bool Test(ISmbus bus, byte address)
    {
        if (bus.ReadByte(address) == null && bus.ReadByteData(address, 0x00) == null) return false;
        for (int i = 0xA0; i < 0xB0; i++)
            if (bus.ReadByteData(address, (byte)i) != i - 0xA0) return false;
        var micron = new byte[16];
        for (int i = 0; i < 16; i++) micron[i] = Read(bus, address, (ushort)(RegMicron + i));
        return !Encoding.ASCII.GetString(micron).StartsWith("Micron", StringComparison.Ordinal);
    }

    public static EneChip? Open(ISmbus bus, byte address) => Test(bus, address) ? new EneChip(bus, address) : null;

    /// <summary>Starts an effect on the chip (it runs it by itself).</summary>
    public void Set(LightSetting s)
    {
        if (!Known || Leds == 0) return;
        if (LightSetting.HasColour(s.Effect))
        {
            // R, B, G for each LED, three bytes at a time (the most one block takes)
            var c = s.Rgb;
            var colours = new byte[Leds * 3];
            for (int i = 0; i < Leds; i++) { colours[i * 3] = c.R; colours[i * 3 + 1] = c.B; colours[i * 3 + 2] = c.G; }
            for (int sent = 0; sent < colours.Length; sent += 3)
                WriteBlock((ushort)(effectReg + sent), colours.AsSpan(sent, Math.Min(3, colours.Length - sent)));
            Write(RegApply, Apply);
        }
        Write(RegMode, ModeOf(s.Effect));
        Write(RegSpeed, s.Speed switch { Speed.Slow => 0x03, Speed.Fast => 0x01, _ => 0x02 });
        Write(RegDirection, 0);
        Write(RegApply, Apply);
        Write(RegDirect, 0);   // the effect, not colours set from outside
        Write(RegApply, Apply);
    }

    /// <summary>Keeps the effect through a power cut (written to the chip's own memory).</summary>
    public void Save() { if (Known) Write(RegApply, SaveValue); }

    private static byte ModeOf(LightEffect e) => e switch
    {
        LightEffect.Static => 1,
        LightEffect.Breathing => 2,
        LightEffect.Flashing => 3,
        LightEffect.ColourCycle => 4,
        LightEffect.Rainbow => 5,
        _ => 0,
    };

    public static readonly LightEffect[] ChipEffects = { LightEffect.Off, LightEffect.Static, LightEffect.Breathing, LightEffect.Flashing, LightEffect.ColourCycle, LightEffect.Rainbow };

    private byte Read(ushort reg) => Read(bus, Address, reg);

    private static byte Read(ISmbus bus, byte address, ushort reg)
    {
        bus.WriteWordData(address, 0x00, Swap(reg));
        return (byte)(bus.ReadByteData(address, 0x81) ?? 0);
    }

    public static void Write(ISmbus bus, byte address, ushort reg, byte value)
    {
        bus.WriteWordData(address, 0x00, Swap(reg));
        bus.WriteByteData(address, 0x01, value);
    }

    private void Write(ushort reg, byte value) => Write(bus, Address, reg, value);

    private void WriteBlock(ushort reg, ReadOnlySpan<byte> data)
    {
        bus.WriteWordData(Address, 0x00, Swap(reg));
        if (bus.WriteBlockData(Address, 0x03, data)) return;
        foreach (byte b in data) bus.WriteByteData(Address, 0x01, b);   // a bus without block writes
    }

    private static ushort Swap(ushort reg) => (ushort)((reg << 8) | (reg >> 8));
}

/// <summary>One or more ENE chips as one device (all the RAM sticks; a motherboard; a graphics card).</summary>
public sealed class EneDevice : IRgbDevice
{
    private readonly List<EneChip> chips;

    public EneDevice(string id, string name, DeviceKind kind, IEnumerable<EneChip> chips, string bus)
    {
        Id = id;
        Name = name;
        Kind = kind;
        this.chips = chips.ToList();
        About = $"ENE {string.Join(", ", this.chips.Select(c => c.Version).Distinct())} · {bus}";
    }

    public string Id { get; }
    public string Name { get; }
    public DeviceKind Kind { get; }
    public string About { get; }
    public IReadOnlyCollection<LightEffect> Effects => EneChip.ChipEffects;
    public bool HasSpeed => true;
    public int Leds => 0;
    public bool NeedsAdmin { get; init; }
    public object Owner => this;
    public IReadOnlyList<EneChip> Chips => chips;

    public void Apply(LightSetting s) { foreach (var c in chips) c.Set(s); }
    public void Save() { foreach (var c in chips) c.Save(); }
    public void Show(Rgb[] colours) { }
    public void Dispose() { }
}
