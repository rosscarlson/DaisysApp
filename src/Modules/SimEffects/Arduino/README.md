# SimEffects → Arduino

*A tool of [SimEffects](../README.md) (since 3.5.3).*

Arduinos that power things on your rig, each on its own COM port. You say what's wired to each one (its
**features**); the sub-tab that drives a kind of feature shows while some Arduino has one (**Notify** for the gear
and shift lights). A board runs either of two sketches, and the app finds out which when it connects:

- **SimHub's** (since 3.5.6): an Arduino already set up with SimHub's Arduino setup works as it is, with nothing to
  upload. What's on which pin (the LEDs' data pin, how many, their colour order, a matrix's wiring) is in that sketch,
  set in SimHub's setup; the board says how many RGB LEDs it has and whether it has an 8×8 matrix, so here you only
  say which of its LEDs are the shift lights (**First LED**, **LEDs**). SimHub and Daisy's App can't both use a COM
  port at once: close SimHub (or switch its Arduino off) while Daisy's App drives the board.
- **DaisysSim**, Daisy's App's own: the app tells it what's on it when it connects (pins, counts, wiring), so the
  sketch never needs changing for your wiring.

**ON / OFF** (top left, since 3.17.0; on to start with, as before): off, every board is let go (its COM port free for
SimHub, say). The controller icon beside it sets an on / off shortcut (Core's `Hotkeys`, also in Settings →
Shortcuts); while it's on with a board switched on, a green **Arduino** bubble shows in the title bar (**Titlebar
Status** leaves it out); the tray menu has **Arduino on**. The **Profile** card (under the boards) is the Shaker's
(`../Profiles/ProfileCard.cs`): with **Switch On With Game** ticked, a profile's program starting switches it on, and
its closing off again.

## Setting one up

With SimHub's sketch on it: skip to step 3; the window shows only the settings that sketch uses once it's connected.

1. For DaisysSim, install the [Arduino IDE](https://www.arduino.cc/en/software), then the **FastLED** library (Sketch → Include
   Library → Manage Libraries…, search FastLED).
2. **Open Sketch Folder** (on the Arduino tab) opens `DaisysSim.ino`, next to the module. Open it in the IDE, pick
   your board and port, and **Upload**. It runs on an Uno, Nano, Mega or Leonardo (Pro Micro); see
   [DaisysSim/README.md](DaisysSim/README.md) for the wiring.
3. **Arduinos…** opens the Arduinos window: **Add Arduino**, pick its **COM Port** (by Windows' name for it: "USB-SERIAL
   CH340 (COM5)", "Arduino Uno (COM3)"; ⟳ looks again), then **Add Feature** for each thing wired to it and set its
   pin (a drop-down) and the rest. Changes apply as they're made: the board is set up again (a pin or a kind changing
   restarts a DaisysSim board). **Remove** (beside Add Arduino, the same size) removes the board picked (Confirm?
   first).

Each board has a **status light**, in the window's list, beside its status and on the tab's card: green connected,
amber connecting, red not connected (the status says why), grey off. The tab shows a card per Arduino: what it's
doing (*Connected · SimEffects Strip (SimHub sketch) · 107 RGB LEDs*, *Connected · DaisysSim 1*, *Connecting…*, *COM5
isn't there*, *COM5 is in use by another program* (SimHub, often), *No answer: is SimHub's sketch (or DaisysSim) on
it?*), its features, and any problem with one (*That pin can't drive LEDs*, *Its SimHub sketch has 16 RGB LEDs: LEDs
17 to 24 aren't there*, *Its SimHub sketch has no 8×8 matrix*). **Edit** opens the window on it. A board that's
unplugged is looked for again every 3 seconds; **Use it** (in the window) switches one off without removing it.
**Test** works only while the board is connected (greyed out otherwise).

## Features

| Feature | What it is | Settings | Driven by |
|---|---|---|---|
| **Notify (8×8 matrix)** | An 8×8 WS2812B RGB matrix | Brightness, Rotation; DaisysSim only: Data Pin, Zigzag Wiring (every other row backwards, as most 8×8 boards are), Mirror, Colour Order | [Notify](../Notify/README.md): the gear |
| **Shift (LED strip)** | A row of WS2812B RGB LEDs: a stick of 8 or 16 (up to 32) | LEDs, Brightness, Fill (left to right, or both ends to the middle), Right to Left; SimHub's sketch only: First LED (where they start on its chain, from 1); DaisysSim only: Data Pin, Colour Order | [Notify](../Notify/README.md): shift lights |

Until a board has been connected once, the window shows every setting. A DaisysSim board takes up to 4 features and
128 LEDs in all; on SimHub's sketch, several strips can share its chain, each from its own First LED, and Brightness
dims the colours sent (SimHub's sketch has no brightness of its own). **Test** (on a feature) plays a test pattern for a few seconds:
the gears in rainbow colours on a matrix, a running rainbow on a strip.

## Plugins: adding a kind of feature

Each kind is a class implementing `IArduinoFeature` (`ArduinoFeatures.cs`): its id (kept in the settings), title,
the sketch's type number for it, the sub-tab that drives it (`Tab`), its settings (`FeatureField`s: numbers, choices
or on / off; the window builds their controls), the bytes the board is told when it connects (`Config`), and a test
pattern (`Test`). To add one (a motor controller, a fan's speed):

1. the class, and a line in `ArduinoFeatures.All`;
2. a type in the sketch (its README, "Adding a feature"), with its own commands if it needs them (0x30–0x6F);
3. the sub-tab that drives it: it reads the telemetry (`TelemetryHub`), sends with `ArduinoHub.Send(board, feature,
   command, data)`, and is shown by `SimEffectsApplet` while `hub.Has(itsTab)`.

## SimHub's protocol

As SimHub's sketch (DisplayClientV2, "J revision", in `C:\Program Files (x86)\SimHub\_Addons\Arduino\DisplayClientV2\`)
speaks it: `SimHubSketch.cs`. It starts at 19200 baud; after Hello the app moves it to 115200 (as SimHub does), or
stays at 19200 if it doesn't answer there. The PC's bytes go in packets: `01 01` id length data (at most 32 bytes)
CRC-8 (polynomial 0xD5, over id, length and data). The board answers each `03 id` (taken) or `04 last reason` (sent
again); one at a time, as the sketch only buffers one. Ids start at 255 (always taken), then 0–128 and round again.
The board's own bytes are `08 byte`, `06 length text 20` (text), `07 …` (debug text, logged at Debug) and
`09 type length data`. Commands are `03` and a letter:

| Command | Data | Answer |
|---|---|---|
| `1` Hello | one byte | `08` version letter (`j`) |
| `8` Baud | 11 = 115200 | (it changes 200 ms later) |
| `0` Features | | text: letters, then a newline (`R` = an RGB matrix) |
| `4` RGB LEDs | | `08` count |
| `N` Name | | text, newline |
| `6` RGB LEDs / `R` matrix | blocks: `1` every LED's R G B, `2` first count R G B…, `3` first count one R G B; `0` ends | `08 15` once shown |
| `A` Still there | | `08 03` |

The app sends the strips' LEDs as `2` blocks (every LED off with a `3` block when it connects), the matrix as a `1`
block of 64 colours row by row (the sketch knows its own wiring), only what changed and everything each second, and
`A` each second (the sketch turns a matrix off after 5 s with nothing).

## DaisysSim's protocol

115200 baud. Every message is a frame: `A5`, the body's length, the body (a command, a slot, then its data), and the
XOR of the body's bytes. A slot is one feature (its place in the board's list, 0–3).

| From the app | Body | Answer |
|---|---|---|
| Hello | `01 00` | `81 FF` version, slots, "DaisysSim" |
| Config | `02` slot type pin count brightness flags | `82` slot problem (0 fine, 1 unknown type, 2 bad pin, 3 too many LEDs, 4 slot taken) |
| Reset | `03 00` | (the board restarts) |
| Pixels | `10` slot first R G B… | |
| Bitmap | `11` slot 8 rows (bit 7 left) R G B lit, R G B unlit | |
| Clear | `12` slot (FF: every slot) | |
| Show | `20 00` | `8A 00` Ready, once the LEDs are written |
| Ping | `7F 00` | `8F 00` Pong |

LEDs change only on Show, and the app sends nothing more until Ready comes: writing WS2812 LEDs stops the board's
interrupts, so bytes arriving meanwhile would be lost. The app sends only what changed (and everything again each
second), at most about 60 times a second, and a Ping every second; with nothing from the app for 3 seconds the board
turns everything off. Flags (LEDs): bit 0 zigzag, bits 1–2 rotation, bit 3 mirror, bit 4 RGB order.

## Code

| File | |
|---|---|
| `ArduinoApplet.cs` | The tool (owns the `ArduinoHub`; on / off, its shortcut, the title bar bubble, Game Switching: `../Profiles/GameSwitcher.cs`) and its tab (`ArduinoView`: ON / OFF, Titlebar Status, Arduinos…, the Profile card, a card per board with its status and features' problems, refreshed every second; the Sketch card with Open Sketch Folder) |
| `ArduinoWindow.cs` | The Arduinos window: the boards with their status lights (Add Arduino, Remove), the picked one's Name, COM Port (⟳), Use it and status, its features (Add Feature from `ArduinoFeatures.All`, each with the fields its sketch uses (`FeatureField.Only`), Test while connected, Remove); changes applied 0.4 s after the last one |
| `ArduinoHub.cs` | Every board connected: a thread each (open, find the sketch: the one found last first, SimHub's on a new board; SimHub's: Hello, 115200, what it has, its LEDs off, each feature checked against them; DaisysSim: Hello up to 6 times while an Uno's bootloader runs, Config per feature; then the newest data per feature only when it changed, a wait until it's shown, a Ping each second; again every 3 s on a problem; a settings change that alters what a DaisysSim board was told sends Reset and sets it up again); works on a copy of the board's settings; only while the tool and the board are on (`On`); `Send`, `Test`, `Status`, `State` (the status light), `Problem`, `Has`, `FeaturesFor`; the sketch found kept in the board's `Sketch`; `OpenLink(port, baud)` (a test's stand-in). `SimHubLeds`: the features' frames as colours of a SimHub board's chain and matrix |
| `SimHubSketch.cs` | SimHub's sketch's protocol: packets and their answers, Hello, 115200, features / LED count / name, LEDs, matrix, still there |
| `ArduinoProtocol.cs` | The commands, `Frame`, and `Reader` (frames out of bytes, checked) |
| `ArduinoFeatures.cs` | `IArduinoFeature`, `FeatureField` (a number, a drop-down of numbers (`Menu`: the pin), a choice or on / off; `Only` one sketch's), `ArduinoFeatures.All`; `LedFeature` (WS2812s: pin, brightness, colour order), `LedMatrixFeature`, `LedStripFeature` (First LED on a SimHub chain) |
| `ArduinoSettings.cs` | `Arduino.json`: on, the shortcut, `TitleBadge`, the profiles, boards (`ArduinoBoard`: name, port, on, the sketch found, features) and features (`ArduinoFeature`: kind, name, values); `ArduinoSketch` |
| `SerialPorts.cs` | COM ports with Windows' names (the registry's SERIALCOMM and SetupAPI's friendly names) and `SerialLink` (Windows' own serial calls: 8N1, DTR and RTS on, reads that wait 20 ms; `SetBaud` without closing, which would restart an Uno) |
| `Glyphs.cs` | 8×8 pictures of R, N, P, 1–9 and a dash |
| `DaisysSim/` | The sketch and its README (copied next to the module) |
