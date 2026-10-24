# SimEffects → Arduino

*A tool of [SimEffects](../README.md) (since 3.5.3).*

Arduinos that power things on your rig, each on its own COM port. You say what's wired to each one (its
**features**); the sub-tab that drives a kind of feature shows while some Arduino has one (**Notify** for the gear
and shift lights). Every board runs the same sketch, **DaisysSim**: the app tells it what's on it when it connects, so
the sketch never needs changing for your wiring.

## Setting one up

1. Install the [Arduino IDE](https://www.arduino.cc/en/software), then the **FastLED** library (Sketch → Include
   Library → Manage Libraries…, search FastLED).
2. **Open Sketch Folder** (on the Arduino tab) opens `DaisysSim.ino`, next to the module. Open it in the IDE, pick
   your board and port, and **Upload**. It runs on an Uno, Nano, Mega or Leonardo (Pro Micro); see
   [DaisysSim/README.md](DaisysSim/README.md) for the wiring.
3. **Arduinos…** opens the Arduinos window: **Add Arduino**, pick its **COM Port** (by Windows' name for it: "USB-SERIAL
   CH340 (COM5)", "Arduino Uno (COM3)"; ⟳ looks again), then **Add Feature** for each thing wired to it and set its
   pin and the rest. Changes apply as they're made: the board is set up again (a pin or a kind changing restarts it).

The tab shows a card per Arduino: what it's doing (*Connected · DaisysSim 1*, *Connecting…*, *COM5 isn't there*, *COM5
is in use by another program*, *No answer: is the DaisysSim sketch on it?*), its features, and any problem the board
reported for one (*That pin can't drive LEDs*). **Edit** opens the window on it. A board that's unplugged is looked for
again every 3 seconds; **Use it** (in the window) switches one off without removing it.

## Features

| Feature | What it is | Settings | Driven by |
|---|---|---|---|
| **Notify (8×8 matrix)** | An 8×8 WS2812B RGB matrix | Data Pin, Brightness, Rotation, Zigzag Wiring (every other row backwards, as most 8×8 boards are), Mirror, Colour Order | [Notify](../Notify/README.md): the gear |
| **Shift (LED strip)** | A row of WS2812B RGB LEDs: a stick of 8 or 16 (up to 32) | Data Pin, LEDs, Brightness, Fill (left to right, or both ends to the middle), Right to Left, Colour Order | [Notify](../Notify/README.md): shift lights |

A board takes up to 4 features and 128 LEDs in all. **Test** (on a feature) plays a test pattern for a few seconds:
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

## The protocol

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
| `ArduinoApplet.cs` | The tool (owns the `ArduinoHub`) and its tab (`ArduinoView`: Arduinos…, a card per board with its status and features' problems, refreshed every second; the Sketch card with Open Sketch Folder) |
| `ArduinoWindow.cs` | The Arduinos window: the boards (Add Arduino), the picked one's Name, COM Port (⟳), Use it, status and Remove, its features (Add Feature from `ArduinoFeatures.All`, each with its fields' controls, Test, Remove); changes applied 0.4 s after the last one |
| `ArduinoHub.cs` | Every board connected: a thread each (open, Hello up to 6 times while an Uno's bootloader runs, Config per feature, then the newest data per feature only when it changed, Show and wait for Ready, a Ping each second; again every 3 s on a problem; a settings change that alters what the board was told sends Reset and sets it up again); works on a copy of the board's settings; `Send`, `Test`, `Status`, `Problem`, `Has`, `FeaturesFor`; `OpenLink` (a test's stand-in) |
| `ArduinoProtocol.cs` | The commands, `Frame`, and `Reader` (frames out of bytes, checked) |
| `ArduinoFeatures.cs` | `IArduinoFeature`, `FeatureField`, `ArduinoFeatures.All`; `LedFeature` (WS2812s: pin, brightness, colour order), `LedMatrixFeature`, `LedStripFeature` |
| `ArduinoSettings.cs` | `Arduino.json`: boards (`ArduinoBoard`: name, port, on, features) and features (`ArduinoFeature`: kind, name, values) |
| `SerialPorts.cs` | COM ports with Windows' names (the registry's SERIALCOMM and SetupAPI's friendly names) and `SerialLink` (Windows' own serial calls: 8N1, DTR and RTS on, reads that wait 20 ms) |
| `Glyphs.cs` | 8×8 pictures of R, N, P, 1–9 and a dash |
| `DaisysSim/` | The sketch and its README (copied next to the module) |
