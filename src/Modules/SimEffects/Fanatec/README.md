# SimEffects → Fanatec

*A tool of [SimEffects](../README.md) (since 3.5.3).*

Shift lights on a Fanatec wheel's rev LEDs: the **ClubSport Steering Wheel RS**'s nine, and the other rims with a row
of nine single-colour rev LEDs (ClubSport BMW M3 GT2, BMW V2, F1 IS, Porsche 918 RSR, Podium DD GT). Rims with RGB
LEDs (Formula V2, F1 Esports V2, Podium GT3s) take another report and aren't driven yet; the rim found says so. They come on from the first light's RPM to the shift point, all at the shift point, flashing over-rev: the
same shift points and car redlines as [Notify](../Notify/README.md) (one set of settings).

## Using it

- **ON / OFF** (top left): while on, it looks for the wheelbase every 3 seconds. **Demo** runs a pretend car through
  the gears.
- **Wheel**: the base and rim found (*ClubSport Wheel Base V2.5 · ClubSport Steering Wheel RS*), the LEDs as they
  show now, **Right to Left** (if they light from the wrong end), and **Gear on the Display** (for rims with a
  display: the gear on it).
- **Game**, **Shift Points** and **Cars**: Notify's (see its guide).

**If the LEDs don't follow:** the game or the Fanatec App may be driving them too (most games send the rev LEDs to
Fanatec wheels themselves). Switch the game's own wheel LEDs off, or set the Fanatec App's LED profile to off for it.

## How it talks to the wheel

As the Linux driver [hid-fanatecff](https://github.com/gotzl/hid-fanatecff) does: the LEDs are an output report of
the wheelbase (VID 0EB7), `01 F8 09 08 hi lo 00 00`, a bit per LED (bit 0 the last LED), the rim's 3-digit display
`01 F8 09 01 02 a b c` (7-segment bits). The rim's id is byte 30 of the base's input report (0x14 for the RS).
Bases: ClubSport V2 / V2.5, Podium DD1 / DD2, CSL Elite (and PS4), CSL DD, CSR Elite, Porsche 911.

## Code

| File | |
|---|---|
| `FanatecApplet.cs` | `Fanatec.json` (`FanatecSettings`: on, Right to Left, Gear on the Display); the tool (the wheel looked for every 3 s on a timer while on; each lights frame turned into LED bits and the display's gear, sent only when they change; let go when a report fails; LEDs and display off when switched off or closing) and its tab (`FanatecView`: the wheel found, the LEDs drawn as they show, the shared cards) |
| `FanatecWheel.cs` | The wheelbase through Windows' HID calls: found by vendor and product id (the collection with the 8-byte output report), its product name, the rim (an input report read for half a second), `SetLeds`, `SetDisplay` (7-segment characters from hid-fanatecff's table); `HidD_SetOutputReport`, else `WriteFile` |
