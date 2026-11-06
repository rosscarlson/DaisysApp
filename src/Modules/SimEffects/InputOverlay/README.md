# SimEffects → Overlays

*A tool of [SimEffects](../README.md) (since 3.33.0; the Input Overlay since 3.17.0, part of
[ForzaWheel](../../ForzaWheel/README.md) before). Its settings file is `InputOverlay.json` (the folder kept its old name).*

Widgets over the game: a list of them at the left (**New ▾** makes one, **Edit** places it, the trash can deletes it,
the tick shows or hides it), and the one picked's settings at the right. They show while **ON** (top left, kept per
SimEffects profile) and the profile plays, each one ticked. Each is Core's widget: right-click it for its edit strip
(Background, On top, Click through, Done, and the trash can, which deletes it, as on the tab; since 3.35.0). They show over a game in borderless or
windowed mode, not exclusive full screen.

| Overlay | What it shows | Its settings |
|---|---|---|
| **Inputs** | The pedals' and wheel's last few seconds as racing telemetry traces (throttle green, brake red, clutch blue, handbrake orange, steering white about the centre), with live bars, the **speed** and a turning wheel (grey) at the right, each bar with how far it's pressed above it | History, background, traces, the session log, the speeds, Read From (below); ticked per profile |
| **Dash** (new in 3.33.0) | After Forza Horizon's: a round tach (the RPM in thousands round its ring, the redline's marks red, the ring lit up to the needle and red past the redline), the **gear** large in the middle (red and flashing at the shift point: the game's where it gives one, iRacing; else 92% of the redline), the **speed** under it, and two lights: **TCS** (amber while the wheels spin under power: Forza doesn't say when its own traction control works) and **ABS** (iRacing's own; in Forza a front wheel locking under braking) | Background, Units (mph, km/h, or as Inputs'), the lights, the gear's flash; a shortcut (Overlays · Dash · Overlay on / off) |
| **Tires**, **Performance**, **Timing**, **Position**, **Leaderboard** | [Analysis](../Analysis/README.md)' blocks (their widgets were made there before 3.33.0) | Background, Title, the rows it shows, a shortcut (Overlays · *block* · Overlay on / off); shown only while Analysis is switched on as a sub-module |

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
- **Inputs** (the card): **Edit** in the list (Move / resize before 3.33.0), or right-click the overlay (since 3.18.3): its edit strip shows (**Background**, the same as the tab's, since 3.32.0; **On top**, **Click through**,
  **Done**, and the trash can, which unticks it); drag it, stretch it from an edge, then **Done**. Edit
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

**Session logs** (for [Forza Analysis](../../ForzaAnalysis/README.md)): while the overlay shows, the pedals and wheel
are logged as they're read (every 10 ms at most): a session starts when something moves and ends when the overlay
closes or after two minutes of nothing (one with under 20 s of driving isn't kept), as a CSV file in
`%LOCALAPPDATA%\DaisysApp\ForzaSessions\` (Core's `Shared/SessionLog.cs`). Before 3.17.0 none was ever written (a bug: the first
row's time check overflowed). Since 4.4.0 each row also has the game's telemetry while it sends (speed, g, lap, time into it, distance, place,
suspension; `OverlayReader.Telemetry`) and a `# car,` line names the car: Forza Analysis' laps, comparisons, friction
circle and contact come from it.

**From ForzaWheel** (3.17.0): the first time, its place, size, history, background, traces and Record come from
ForzaWheel's overlay settings, and Device mode's axes from ForzaWheel's bindings of Steering, Throttle, Brake, Clutch
and Handbrake. If ForzaWheel's overlay was showing, it's on in every game's profile (3.17: Switch On With Game started ticked).

## Code

| File | What it is |
|---|---|
| `InputOverlayApplet.cs` | The tool: settings, the reader, the on / off shortcut (Core `Hotkeys`) and the Dash's and blocks' (`Overlays.Dash`, `Analysis.Widget.<block>`), its part in the SimEffects profiles (`SimProfiles.Register`, `ProfileLoaded`), the title bar bubble, the list (`OverlayKinds`, `Made`, `IsTicked`, `Create`, `Delete`, `Edit`), each overlay's window (`ShowOverlays`: open while made and ticked, `Showing` while on and playing; the blocks' from `AnalysisApplet.Current`, `Ticked`), `FromBefore` (3.33.0's first start), the tray item; a hidden window DirectInput reads in the background for |
| `InputOverlayView.cs` | The tab: the top line, the list (New ▾, Edit, Delete) and the one picked's settings: Inputs (Overlay and Speed, drawn again for another profile; Read From: Game, or a row per axis with Set, ForzaWheel's `Capture.cs`, linked), the Dash, a block |
| `DashWindow.cs` | The Dash: Core's widget (no card; its Background slider the dial's) round `DashView`, drawn from the reader's last frame (`OverlayReader.Frame`) in a 200 × 200 space |
| `FrameHook.cs` | When the widgets draw (since 4.0.0): once per screen frame only while one shows and readings are coming (not with no game, nor paused), and only on a new reading (`OverlayReader.Version`) |
| `OverlayReader.cs` | Its thread while the overlay or the tab shows: the module's telemetry (`SimData.Open()`: the profile in use's game), the controllers' axes in Device mode (`GameControllers`, in `src/DaisysApp.Controllers`), the session log |
| `OverlayWindow.cs` | The overlay: the trace (history, bars, wheel) and the speeds (now, the window's highest and lowest) in Core's `Shared/Widgets/Widget` (no card; the edit strip on right-click, its Delete switching it off) |
| `OverlaySettings.cs` | `InputOverlay.json` (`Scoped`: what each SimEffects profile keeps, with `Inputs` ticked), `Overlays` (the list), `DashSettings`, and taking ForzaWheel's overlay settings and axes the first time |
