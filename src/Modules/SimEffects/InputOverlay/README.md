# SimEffects → Overlays

*A tool of [SimEffects](../README.md) (since 3.33.0; the Input Overlay since 3.17.0, part of
[ForzaWheel](../../ForzaWheel/README.md) before). Its settings file is `InputOverlay.json` (the folder kept its old name).*

Widgets over the game, a box each (since 4.17.9; a list and the one picked's settings before), four across and two
down: Dash, Inputs, Performance, Tires, Track Map, Position, Timing, Leaderboard. Each box has a picture of its overlay
(made-up readings), all its settings, and at its top left a tick that switches it on: one that's off has no window and
nothing reading for it, and its settings are greyed. Beside the name: its **shortcut** (show / hide), the **eye**
(the same, here) and **Edit** (place it), all only while it's on. They show while **ON** (top left, kept per SimEffects
profile) and the profile plays, each one on and not hidden. Each is Core's widget: right-click it for its edit strip
(Background, On top, Click through, Done, and the trash can, which switches it off; since 3.35.0). They show over a game in borderless or
windowed mode, not exclusive full screen.

| Overlay | What it shows | Its settings |
|---|---|---|
| **Inputs** | The pedals' and wheel's last few seconds as racing telemetry traces (throttle green, brake red, clutch blue, handbrake orange, steering white about the centre), with live bars, the **speed** and a turning wheel (grey) at the right, each bar with how far it's pressed above it; after a gap (a pause, a menu, the next race loading) the traces start again empty (since 4.6.4; a straight line crossed the gap before) | History, background, traces, the speeds, Read From (below), all in its box; shown per profile; a shortcut (Overlays · Inputs · Overlay on / off, since 4.17.9) |
| **Dash** (new in 3.33.0) | **Analog** (since 4.17.9 a speedometer at the right of the tach, the speed large in its middle, its scale growing with the car: 160 mph, 200, 240, 320; 280 km/h, 360, 480): after Forza Horizon's, a round tach (the RPM in thousands round its ring, the redline's marks red, the ring lit up to the needle and red past the redline), the **gear** large in the middle (red and flashing at the shift point: the game's where it gives one, iRacing; else 92% of the redline), the **speed** under it, and two lights: **TCS** (amber while the wheels spin under power: Forza doesn't say when its own traction control works) and **ABS** (iRacing's own; in Forza a front wheel locking under braking); with the speedometer, the speed is on it, not under the gear; the speedometer alone has the gear and the lights at its foot. **Digital** (since 4.17.9): one low strip, a bar of 24 lights for the RPM (green, amber from 70% of the redline, red from it; all red while the gear flashes) with the RPM under it, the gear in a box, the speed, and TCS and ABS stacked | Look (Analog, Digital), Dials (Tach, Speedometer: one at least), Background, Units (mph, km/h, or as Inputs'), the lights, the gear's flash; a shortcut (Overlays · Dash · Overlay on / off). Another look or dial makes its window again in its new shape, as wide per dial as before |
| **Tires**, **Performance**, **Timing**, **Position**, **Leaderboard**, **Track Map** (4.6.3) | [Analysis](../Analysis/README.md)' blocks (their widgets were made there before 3.33.0) | Background, Title, the rows it shows, a shortcut (Overlays · *block* · Overlay on / off); shown only while Analysis is switched on as a sub-module |

**From before 3.33.0:** the list starts with Inputs and the Analysis widgets that were open. Those showed while
Analysis was on; now they show while Overlays is, so Overlays is switched on in each profile Analysis was on in (with
Inputs unticked there if the Input Overlay was off).

## Using it

- **ON / OFF** (top left) shows or hides the overlays; the controller icon beside it sets a shortcut for that (keys or
  a controller / button box button, also in Settings → Shortcuts). It stays on or off across restarts; the tray menu
  has **Overlays on** too. What it's reading shows next: *Forza Horizon: driving*, *Reading Fanatec CSL DD*…
  (nothing while the game isn't sending: the Play button's colour says whether it's running)
- **Title bar**: while it's on (always, since 3.17.2), a green bubble in the title bar names the profile in use, e.g. **Overlay: Forza
  Horizon** (click it to come here).
- **Profiles** (since 3.18.0): the game is the [SimEffects profile](../Profiles/README.md)'s, and on / off, Read From
  and what it draws (history, background, traces, speeds, Record) are kept per profile; its place and the axes are the
  same in all.
- **Inputs** (its box): **Edit** (Move / resize before 3.33.0), or right-click the overlay (since 3.18.3): its edit strip shows (**Background**, the same as the tab's, since 3.32.0; **On top**, **Click through**,
  **Done**, and the trash can, which switches it off); drag it, stretch it from an edge, then **Done**. Edit
  shows it even while off. **Reset
  position**, **History** (3–30 seconds at the normal speed: from halfway across the trace runs faster, easing in
  over a tenth of the width to twice as fast, so the right half, where you watch, moves as it should and the width holds 45% more; from
  three quarters across before 4.2.3), **Background** (how
  dark, 0% = only the traces), which traces show, and **Record a session log while it shows** (below).
- **Speed**: three numbers stacked between the bars and the wheel (since 3.18.3; at the left before), all the same size (since 3.18.1): the speed **now** in the middle (white), the **highest** of the
  last **Window Size** seconds (1–30, 5 to start with) above it (green) and the **lowest** below (orange). Slow to 50 and the middle and
  bottom both read 50; speed up again and the middle climbs while the bottom stays at 50 for the window, then catches
  up. The top works the same way. **Units**: mph or km/h. The speed comes from the game's telemetry (in both modes
  below), so it shows *–* while the game isn't sending.
- **Read From**:
  - **Game** (the default): the pedals and wheel from the profile's game's telemetry, as the game sees them (Forza's
    Data Out, iRacing's and ACC's shared memory). ACC gives no handbrake.
  - **Device**: from a controller's axes, as the device reports them (DirectInput, as ForzaWheel reads them): a row
    per trace with **Set** (then turn the wheel left and right, or press the pedal all the way and let it go; Esc
    cancels), its live value, **Invert** (for a pedal that reads full when released; Set ticks it when it sees one
    rest at the top) and **✕**. A device unplugged is opened again when it's back; one plugged in since the controllers were listed needs
    **Refresh** (the arrow beside Read From, in Device mode; since 3.25.3).

**The log** (SimEffects, since 4.17.7): while an overlay draws a game, 15 s in and then each minute it's behind (each
minute on Debug logging): its readings a second, how many it drew, how many frames a second the app really drew ("WPF
behind" under 30: the app draws all its windows on one thread, so a busy Mini Mirror can hold an overlay back), and
the app's see-through windows showing with their sizes.

**Session logs** (until 4.5.0, for Forza Analysis): while the overlay showed, the pedals and wheel were logged as
sessions (since 4.4.0 with the game's telemetry), races or not. Since 4.5.0 SimEffects → [Analysis](../Analysis/README.md)
logs each race itself and keeps only the races it records; **Record a session log while it shows** went.

**From ForzaWheel** (3.17.0): the first time, its place, size, history, background, traces and Record come from
ForzaWheel's overlay settings, and Device mode's axes from ForzaWheel's bindings of Steering, Throttle, Brake, Clutch
and Handbrake. If ForzaWheel's overlay was showing, it's on in every game's profile (3.17: Switch On With Game started ticked).

## Code

| File | What it is |
|---|---|
| `InputOverlayApplet.cs` | The tool: settings, the reader (only for an overlay that's on), the on / off shortcut (Core `Hotkeys`) and each overlay's show / hide (`Overlays.Dash`, `Overlays.Inputs`, `Analysis.Widget.<block>`; nothing for one that's off), its part in the SimEffects profiles (`SimProfiles.Register`, `ProfileLoaded`), the title bar bubble, on and shown (`OverlayKinds.Boxes`, `Made`/`IsOn`, `Enable`, `IsTicked`, `SetTicked`, `Delete`, `Edit`), the Dash made again in a new shape (`Redraw`), each overlay's window (`ShowOverlays`: open while made and ticked, `Showing` while on and playing; the blocks' from `AnalysisApplet.Current`, `Ticked`), `FromBefore` (3.33.0's first start), the tray item; a hidden window DirectInput reads in the background for |
| `InputOverlayView.cs` | The tab: the top line and the 8 boxes (`MakeBox`: the tick, shortcut, eye, Edit, picture, settings; `ShowStates`): Inputs (overlay, speed, Read From: Game, or a row per axis with Set, ForzaWheel's `Capture.cs`, linked), the Dash, a block; all drawn again for another profile |
| `OverlayPreviews.cs` | Each box's picture: the overlay's own drawing (`DashView`, `OverlayWindow.Trace`, a `BlockView` in its `OverlayFrame`) on made-up readings, drawn once, scaled over a dark backdrop |
| `DashWindow.cs` | The Dash: Core's widget (no card; its Background slider the dial's; its `Aspect` its space's) round `DashView`, drawn from the reader's last frame (`OverlayReader.Frame`): analog, 200 × 200 a dial (`TachDial`, `SpeedDial`); digital, a strip 64 high (`Strip`); `SpaceOf`, `LayoutOf`; `classic` (the tach alone) for Analysis' exported videos |
| `FrameHook.cs` | When the widgets draw (since 4.0.0): once per screen frame only while one shows and readings are coming (not with no game, nor paused), and only on a new reading (`OverlayReader.Version`); since 4.17.7 logs the pace WPF really drew at (Core's `RenderPace`) |
| `OverlayReader.cs` | Its thread while the overlay or the tab shows: the module's telemetry (`SimData.Open()`: the profile in use's game), the controllers' axes in Device mode (`GameControllers`, in `src/DaisysApp.Controllers`), the session log |
| `OverlayWindow.cs` | The overlay: the trace (history, bars, wheel) and the speeds (now, the window's highest and lowest) in Core's `Shared/Widgets/Widget` (no card; the edit strip on right-click, its Delete switching it off) |
| `OverlaySettings.cs` | `InputOverlay.json` (`Scoped`: what each SimEffects profile keeps, with `Inputs` ticked), `Overlays` (the list), `DashSettings`, and taking ForzaWheel's overlay settings and axes the first time |
