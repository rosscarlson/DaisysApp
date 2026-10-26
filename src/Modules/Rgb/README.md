# Gaming → RGB

*A sub-tab of [Gaming](../Gaming/README.md) in [Daisy's App](../../../README.md) (a tab of its own when it's the only
gaming module). Switch it on or off in Settings → General → Modules. Since 3.11.0.*

The lights on your motherboard (and the fans and strips plugged into its headers), your RAM and your graphics card,
set **without the makers' programs** (no Armoury Crate, no G.Skill or EVGA software): each device to an effect of its
own, all of them to one, or **all off** at once.

## Using it

- **Lights ON / OFF** (top): OFF turns every light off; ON puts each back to what it was set to. Each device keeps its
  own setting while they're off.
- **Find Devices**: looks for lights again (they're looked for when the app starts).
- **All Devices**: an effect (with its colour and speed) and **Set All**: every device that can do it gets it.
- **A card per device**: its **effect** (Static, Breathing, Flashing, Colour Cycle, Rainbow, Off), its **colour** (for
  Static, Breathing and Flashing: the swatch opens a palette and a box for a hex colour such as `#FF8000`) and its
  **speed** (where the device has one). A change applies at once and is kept on the device itself, so it stays as set
  after a restart, even without Daisy's App running.
- A device Daisy's App has never set is left as it was, at the start too; once set here, the app sets it again at each
  start.
- **Drawn while Daisy's App runs** under a device: it can only show one colour by itself (EVGA's GTX 10 cards), so the
  app draws that effect, 30 times a second, while it runs. It stops on the effect's colour when the app closes.

### RAM and older boards

The RAM's lights (and an ASUS motherboard from before Aura moved to USB, around 2019) are on the PC's SMBus, which
Windows doesn't let programs reach. Daisy's App reaches it through **PawnIO**, a small signed driver that isn't any
maker's (LibreHardwareMonitor installs it, and OpenRGB uses it; or get it from [pawnio.eu](https://pawnio.eu)).
Without it the card says so, with **Get PawnIO**.

PawnIO only opens for administrators, so **Find RAM** and every change to the RAM go through **Windows' question**
(one each time): find it once with **Find RAM**, then a RAM card's changes wait for its **Apply** (so effect, colour
and speed are one question). **Set All** and **Lights ON / OFF** include the RAM, with one question. Answering no
leaves the RAM as it was. The RAM keeps what was set through a restart.

## What it knows

| Device | How | Notes |
|---|---|---|
| **ASUS motherboards with Aura on USB** (most since 2019, e.g. TUF GAMING Z790-PLUS WIFI): the board's own lights and each **ARGB header** (with everything on it: fans, strips, a hub), each a device | USB HID (0B05:18F3, 1939, 19AF, 1AA6, 1BED; addressable controllers 1867, 1872, 18A3, 18A5) | No rights or driver needed. An ARGB header is one device: every fan chained on it (or on a hub on it) shows the same effect |
| **ASUS graphics cards** (TUF, ROG Strix, KO; RTX 30, 40 and 50 series) | NVIDIA's driver's I2C (`nvapi64.dll`), the ENE chip at 0x67 | No rights needed |
| **EVGA GTX 1070 / 1080 / 1080 Ti** (FTW2, FTW3, SC2, K\|NGP\|N): name plate and backplate | NVIDIA's I2C, EVGA's chip at 0x4A / 0x4F / 0x2A | Off or a colour by itself; other effects drawn by the app. Keeps nothing through a restart |
| **RAM with an ENE controller**: G.Skill Trident Z RGB and Trident Z Neo, and others ENE makes the chip for | SMBus (PawnIO), as administrator | All sticks as one device |
| **Older ASUS motherboards** (Aura on the SMBus, e.g. Z370) | SMBus (PawnIO), as administrator | |

Every chip is checked by **reading** first (ENE chips answer registers 0xA0–0xAF with 0–15; EVGA's register 0x04 with
0x1F or 0x91) and only changed once it answered as that chip does. An ENE chip whose version isn't known is listed in
the log and left alone. Micron RAM (which looks like an ENE chip) is skipped.

Not yet: AMD and Intel graphics cards, Corsair, Kingston FURY and other RAM, MSI and Gigabyte boards, other cards'
makers, keyboards and mice, Windows' Dynamic Lighting devices. Each is a protocol of its own; ask for the one you have.

### If a device doesn't show or doesn't change

- **Armoury Crate / Aura Sync / iCUE / OpenRGB / SignalRGB running**: they set the same lights and win; close them (and
  their services, e.g. ASUS's `LightingService`) or leave the device unset here.
- **Windows' Dynamic Lighting** (Settings → Personalization → Dynamic Lighting) may hold a device that supports it:
  turn it off for that device.
- The Rgb log (`%LOCALAPPDATA%\DaisysApp\logs\Rgb-<date>.log`; with Debug logging, more) names each controller found,
  its version and config, and what was sent.

## Code

How the protocols work is from [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB)'s controllers (written again
here, not copied); its names are given where it helps.

| File | What it does |
|---|---|
| `RgbApplet.cs` | The applet (`Rgb`, Gaming sub-tab, GroupOrder 60): the settings, the hub, the view; `Start` finds the devices |
| `RgbSettings.cs` | `Rgb.json`: `Lights` (device id → `LightSetting`), `Off`, `All`, `Smbus` (the SMBus devices last found) |
| `Effects.cs` | `LightEffect`, `Speed`, `Rgb` (a colour, hex), `LightSetting` (effect, colour, speed), `Animation` (the effects the app draws: each LED's colour at a moment) |
| `Devices.cs` | `IRgbDevice` (Id, Name, Kind, About, `Effects` it runs itself, `HasSpeed`, `Leds` it can be given colours for, `NeedsAdmin`, `Owner`, `Apply`, `Save`, `Show`) and `Devices.Offered` / `Drawn` |
| `RgbHub.cs` | One thread: finds the devices (`Find`), applies settings (`Apply`; `Save` once per controller), draws effects at 30 fps while one needs it; `SmbusProxy` for the SMBus devices last found |
| `AuraUsb.cs` | ASUS Aura USB: config table (0xB0: ARGB headers at [2], board LEDs at [0x1B]), effects (0x35 + 0x36 colours by LED mask; 0x3B on addressable-only controllers), keep (0x3F 0x55); `AuraZone` per zone; `Hid` (Windows' HID paths by vendor and usage page 0xFF72) |
| `NvidiaGpus.cs` | `Nvapi` (nvapi64.dll by `nvapi_QueryInterface` numbers: GPUs, PCI ids, `NV_I2C_INFO_V3` read / write on port 1), `NvapiBus`, `NvidiaGpus.Find` (ASUS: ENE at 0x67; EVGA Pascal: `EvgaGp102` zones), `EvgaGp102Device` (off = black and mode 0) |
| `Ene.cs` | `ISmbus`; `EneChip` (16-bit registers through 0x00 / 0x81 / 0x01 / 0x03, version at 0x1000, config at 0x1C00, colours R-B-G at 0x8010 or 0x8160, mode 0x8021, speed 0x8022, apply 0x80A0, keep 0xAA); `EneDevice` (several chips as one device) |
| `Smbus.cs` | `PawnIoBus` (PawnIOLib.dll: open, load `PawnIO\SmbusI801.bin` / `SmbusPIIX4.bin`, `ioctl_smbus_xfer` holding the shared `Global\Access_SMBUS.HTP.Method` mutex), `SmbusDevices.Find` (RAM chips moved off 0x77 one slot at a time as OpenRGB does, then checked; boards at 0x40 / 0x4E / 0x4F), `RunAsync` + `SmbusTask` (the administrator job: request / result JSON in `%TEMP%\<app>-Rgb`) |
| `RgbView.cs` | The tab (Lights ON / OFF, Find Devices, All Devices + Set All, a card per device, RAM and Older Boards, Apply for SMBus devices) and `ColourButton` |
| `PawnIO\` | PawnIO's signed SMBus modules from [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) 0.2.11, unchanged (LGPL-2.1, `COPYING.txt`) |

Tested (3.11.0): a harness over simulated devices (4 RAM sticks at 0x77 moved and set, register order, keep, a v2
graphics card chip, unknown versions and Micron left alone, the Aura USB packets byte by byte); on a real EVGA GTX
1080 Ti FTW3 through NVIDIA's I2C without administrator rights (found, red set and read back, a rainbow and breathing
drawn by the app, Off, put back); the tab in a test copy (effect picked, Lights OFF / ON, Set All, the setting applied
at the start). Not tested on real hardware: Aura USB boards, ASUS graphics cards, RAM and older boards (the
administrator job was only run without rights, where it reports PawnIO's refusal).
