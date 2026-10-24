# SimEffects → Notify

*A tool of [SimEffects](../README.md) (since 3.5.3). It needs the [Arduino](../Arduino/README.md) tool, and its
sub-tab shows only while an Arduino has a Notify or Shift feature.*

The Arduinos' lights while you drive:

- **Notify** (an 8×8 RGB matrix): the gear (R, N, 1–9), white for gears, green for neutral, red for reverse; blue at
  the shift point; flashing red over-rev.
- **Shift** (8 or 16 RGB LEDs): shift lights, coming on from the first light's RPM to the shift point, green for the
  first half, yellow for the next 30%, red for the rest; all blue at the shift point; flashing over-rev.

All of them are dark while you're not driving (a menu, a pause, a replay).

## Using it

- **ON / OFF** (top left) starts or stops the lights (off turns them all off). What they follow shows next to it:
  *Forza Horizon: driving · Car 2352 (A 800) · 6,210 RPM, shift at 7,680*, or *Waiting for Forza Horizon on port 8000*.
  **Demo** (top right) runs a pretend car up through the gears for a few seconds, to see the lights without a game.
- **Game**: **Follow** whichever game is running (Forza Horizon, Forza Motorsport, iRacing, ACC: by their programs),
  or one; the ports for Forza's Data Out (Horizon 8000, Motorsport 5301). The Shaker can use the same port: they share
  the one reader.
- **Shift Points**: **First Light**, **Shift** and **Flash**, as % of the car's redline (75%, 96%, 99%), and how fast
  they flash. **iRacing's own shift lights**: in iRacing each car's own first-light, shift and flash RPM are used
  instead.
- **Gear Colours** and **Shift Light Colours**; **Flash over-rev** (the shift lights flash too, not only the gear).
- **Lights**: each Notify and Shift feature of every Arduino as it shows now, with its board's problem if it isn't
  connected.
- **Cars**: every car driven, newest first: its redline now and where it came from, or the highest RPM it's held;
  your own redline for it (RPM; empty: worked out), and **Forget**.

## Shift points and redlines

Each car's redline is worked out as SimHub does, the first of:

1. **yours**, typed for that car in Cars;
2. **iRacing's**: each car's own shift lights from the session (`DriverCarSLShiftRPM` and its first-light and flash
   RPM), when **iRacing's own shift lights** is on;
3. **learned**: the highest RPM the car has held for three frames in a row at full throttle in gear (bouncing off the
   limiter, the low of the bounce), once that's at least 85% of the game's maximum RPM;
4. **the game's maximum** (Forza's `EngineMaxRpm`, ACC's current maximum, iRacing's redline) until then.

So a new car starts from the game's figure, and once you've hit its limiter its lights follow where the engine really
stops. What's learned is kept per car (`SimLights.json`: Forza by car number, iRacing by car path, ACC by model), saved
10 seconds after it changes. The first light, shift and flash are then the % above of that redline.

The Fanatec tool uses the same settings and cars.

## Code

| File | |
|---|---|
| `NotifyApplet.cs` | The tool: the features it drives (from the `ArduinoHub`, again when the boards change), each frame of the lights sent to them (`ArduinoProtocol.Bitmap` for a matrix, `Pixels` for a strip), off when switched off; and its tab (`NotifyView`: on / off, status, Demo; the Game, Shift Points and colour cards; the previews (`LedView`) drawn 30 times a second while showing; Cars) |
| `NotifyRender.cs` | What the lights show for a `RevState`: the matrix's picture and colour, each strip LED's colour (zones, both ends, reversed, shift, flash) |
| `NotifySettings.cs` | `Notify.json`: on / off and the colours; `LightColours` (the pickers' colours) |
| `../Lights/LightsRunner.cs` | The shift lights' brain (shared with Fanatec): a thread while a tool wants it, the game (set, or the running one, every 3 s), a lease from `TelemetryHub`, each car's redline (learned, yours, iRacing's, the game's), the lights' state (`RevState`: fraction lit, at shift, over-rev, the blink) to `Frame` each frame; keeps the last state half a second without frames; Demo |
| `../Lights/LightsSettings.cs` | `SimLights.json`: the game, ports, shift points, flash rate, iRacing's own lights, the cars (`CarRedline`) |
| `../Lights/LightsCards.cs` | The Game, Shift Points and Cars cards (Notify's and Fanatec's), and the status line's text |
| `../Lights/LedView.cs` | LEDs on screen: a row or an 8×8 grid of dots |
