# SimEffects → Wheel

*A tool of [SimEffects](../README.md) (since 3.5.3 as Fanatec; Wheel, with Moza, since 4.11.0).*

Shift lights on your wheel's rev LEDs, Fanatec or Moza (**Make**). They come on from the first light's RPM to the
shift point, all at the shift point, flashing over-rev: the same shift points and car redlines as
[Notify](../Notify/README.md) (one set of settings).

- **Fanatec**: the **ClubSport Steering Wheel RS**'s nine, and the other rims with a row of nine single-colour rev LEDs
  (ClubSport BMW M3 GT2, BMW V2, F1 IS, Porsche 918 RSR, Podium DD GT). Rims with RGB LEDs (Formula V2, F1 Esports V2,
  Podium GT3s) take another report and aren't driven yet; the rim found says so.
- **Moza** (since 4.11.0): the ten RPM LEDs of the newer rims (CS V2P, RS V2, GS V2P, KS, FSR V2, TSW, Vision GS…), in
  the colours you pick, and the ES's in its own colours.

## Using it

- **ON / OFF** (top left): when switched on, it looks for the wheel; **Refresh** (the arrow beside Wheel, or any module's) looks again, and one it had and lost is looked for every 3 seconds until it's back (since 3.25.3: before, every 3 seconds until found, which a game feels). **Demo** runs a pretend car through
  the gears.
- **Wheel**: **Make** (Fanatec or Moza), what was found (*ClubSport Wheel Base V2.5 · ClubSport Steering Wheel RS*,
  *Moza wheel on COM5*), the LEDs as they show now, **Right to Left** (if they light from the wrong end), and, for
  Fanatec, **Gear on the Display** (for rims with a display: the gear on it).
- **Shift Light Colours** (an RGB Moza rim's): the first half, the next 30%, the last 20% and all of them at the
  shift point, and **Flash over-rev**, as Notify's.
- **Shift Points** and **Cars**: Notify's (see its guide); the game is the SimEffects profile's, and on / off is kept per profile (since 3.18.0).

**If the LEDs don't follow:** the game or the Fanatec App may be driving them too (most games send the rev LEDs to
Fanatec wheels themselves). Switch the game's own wheel LEDs off, or set the Fanatec App's LED profile to off for it.
A Moza base's port is one program's at a time: while Moza Pit House has it, it says so (close Pit House, then
Refresh); while the Wheel has it, Pit House can't reach the base.

## How it talks to the wheel

**Fanatec**, as the Linux driver [hid-fanatecff](https://github.com/gotzl/hid-fanatecff) does: the LEDs are an output
report of the wheelbase (VID 0EB7), `01 F8 09 08 hi lo 00 00`, a bit per LED (bit 0 the last LED), the rim's 3-digit
display `01 F8 09 01 02 a b c` (7-segment bits). The rim's id is byte 30 of the base's input report (0x14 for the RS).
Bases: ClubSport V2 / V2.5, Podium DD1 / DD2, CSL Elite (and PS4), CSL DD, CSR Elite, Porsche 911.

**Moza**, as Pit House and the Linux tool [boxflat](https://github.com/Lawstorant/boxflat) do: over the wheelbase's
serial port (Moza's devices are COM ports, VID 346E; 115200 baud). A message is `7E`, its length (the command's id and
data), the group, the device (the rim: 23, or 21), the command's id and data, and a checksum (13 plus every byte
before it, the low byte). The rim is found by reading its telemetry mode (group 64, `1C 00`; the ES: `04`), which
also tells newer rims from the ES. Newer rims: telemetry mode 1 (group 63, `1C 00`), the LEDs' colours (`19 00`,
five of index, R, G, B a message, sent when they change), which are lit (`1A 00`, 2 bytes, bit 0 the first LED). The
ES: indicator mode 1 (`04`), then the lit ones in group 65 (`FD DE`, 4 bytes). Sent again each second; switched off,
the LEDs go out and the rim's mode goes back to what it was.

## Code

| File | |
|---|---|
| `WheelApplet.cs` | `Fanatec.json` (`WheelSettings`: on, Make, Right to Left, Gear on the Display, an RGB rim's colours; the file and the tool's id kept from Fanatec so settings carry over); `IWheelLights` (a make's LEDs: each LED's colour from the first, the display); the tool (the make's wheel looked for when switched on, on `GameControllers.Changed` (Refresh) and every 3 s after losing it; each lights frame turned into colours by Notify's `NotifyRender.Strip`; let go when a report fails; LEDs and display off when switched off, another make picked, or closing) and its tab (`WheelView`: Make, the wheel found, the LEDs drawn as they show, the colours for RGB rims, the shared cards) |
| `FanatecWheel.cs` | A Fanatec wheelbase through Windows' HID calls: found by vendor and product id (the collection with the 8-byte output report), its product name, the rim (an input report read for half a second), its LEDs (sent when they change), `SetDisplay` (7-segment characters from hid-fanatecff's table); `HidD_SetOutputReport`, else `WriteFile` |
| `MozaWheel.cs` | A Moza rim through its base's COM port (`Arduino/SerialPorts`: Moza's ports by vendor id, `SerialLink`): found by asking each port's rim ids for its mode; the messages and their checksum; colours, lit LEDs, the mode put back |
