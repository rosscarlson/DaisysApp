# DaisysSim

The sketch for Daisy's App's [SimEffects → Arduino](../README.md). One sketch for every board: the app tells it what's
wired to it when it connects, so it never needs changing for your wiring.

**Needs:** the Arduino IDE and the **FastLED** library (Sketch → Include Library → Manage Libraries…). Compiles for
the Uno and Nano (about 20 KB of 32, 80% of the RAM), Mega, and Leonardo / Pro Micro.

**Upload:** open `DaisysSim.ino`, pick the board (Tools → Board) and its port (Tools → Port), **Upload**. Close the
IDE's Serial Monitor afterwards: only one program can have the port, and the app needs it.

## Wiring WS2812B LEDs (the matrix and the strips)

- Each matrix or strip's **DIN** to a pin from 2 to 13 (set the same pin for its feature in the app), through a
  300–500 Ω resistor if you have one.
- **GND** to the Arduino's GND.
- **5V** from a 5 V supply for anything but a few LEDs: an 8×8 matrix at full white draws up to 3.8 A, more than USB
  gives. Join the supply's ground to the Arduino's. The app's Brightness (40% to start) keeps it far lower.
- One feature per pin.

## Adding a feature

1. A type number after `TYPE_MATRIX`, and a case for it in `configure()` (check its data; set up what it drives).
2. Its own commands, if it needs them, from 0x30 to 0x6F, in `handle()`.
3. The app's side: a class implementing `IArduinoFeature` with the same type number (the Arduino tool's README,
   "Plugins").

Change `VERSION` when the protocol itself changes (the app logs a mismatch).
