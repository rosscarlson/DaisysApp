# SimEffects → Input Overlay

*A tool of [SimEffects](../README.md) (since 3.17.0; before, the input overlay was part of
[ForzaWheel](../../ForzaWheel/README.md)). Its settings file is `InputOverlay.json`.*

The pedals' and wheel's last few seconds as racing telemetry traces over the game (throttle green, brake red, clutch
blue, handbrake orange, steering white about the centre), with live bars, the **speed** and a turning wheel (grey) at
the right, each bar with how far it's pressed above it (0–100, in its colour). A see-through widget that stays on top
of the game (since 3.18.3 a widget like every other: right-click it for its edit strip, so it no longer lets clicks
through). It shows over a game in borderless or windowed mode, not exclusive full screen.

## Using it

- **ON / OFF** (top left) shows or hides the overlay; the controller icon beside it sets a shortcut for that (keys or
  a controller / button box button, also in Settings → Shortcuts). It stays on or off across restarts; the tray menu
  has **Input Overlay on** too. What it's reading shows next: *Forza Horizon: driving*, *Reading Fanatec CSL DD*…
  (nothing while the game isn't sending: the Play button's colour says whether it's running)
- **Title bar**: while it's on (always, since 3.17.2), a green bubble in the title bar names the profile in use, e.g. **Overlay: Forza
  Horizon** (click it to come here).
- **Profiles** (since 3.18.0): the game is the [SimEffects profile](../Profiles/README.md)'s, and on / off, Read From
  and what it draws (history, background, traces, speeds, Record) are kept per profile; its place and the axes are the
  same in all.
- **Overlay**: **Move / resize**, or right-click the overlay (since 3.18.3): its edit strip shows (opacity, **On top**,
  **Done**, and the trash can, which switches it off); drag it, stretch it from an edge, then **Done**. Move / resize
  shows it even while off. **Reset
  position**, **History** (3–30 seconds at the normal speed: the left fifth of the trace runs twice as fast, easing in
  from the rest, so the right, where you watch, moves as it should and the width holds 20% more), **Background** (how
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
`%LOCALAPPDATA%\DaisysApp\ForzaSessions\` (`SessionLog.cs`). Before 3.17.0 none was ever written (a bug: the first
row's time check overflowed).

**From ForzaWheel** (3.17.0): the first time, its place, size, history, background, traces and Record come from
ForzaWheel's overlay settings, and Device mode's axes from ForzaWheel's bindings of Steering, Throttle, Brake, Clutch
and Handbrake. If ForzaWheel's overlay was showing, it's on in every game's profile (3.17: Switch On With Game started ticked).

## Code

| File | What it is |
|---|---|
| `InputOverlayApplet.cs` | The tool: settings, the reader, the on / off shortcut (Core `Hotkeys`), its part in the SimEffects profiles (`SimProfiles.Register`, `ProfileLoaded`), the title bar bubble, the overlay shown while on (or being moved), the tray item; a hidden window DirectInput reads in the background for |
| `InputOverlayView.cs` | The tab: the top line, Overlay and Speed (drawn again for another profile), Read From (Game, or a row per axis with Set: ForzaWheel's `Capture.cs`, linked) |
| `OverlayReader.cs` | Its thread while the overlay or the tab shows: the module's telemetry (`SimData.Open()`: the profile in use's game), the controllers' axes in Device mode (`GameControllers`, in `src/DaisysApp.Controllers`), the session log |
| `OverlayWindow.cs` | The overlay: the trace (history, bars, wheel) and the speeds (now, the window's highest and lowest) in Core's `Shared/Widgets/Widget` (no card; the edit strip on right-click, its Delete switching it off) |
| `OverlaySettings.cs` | `InputOverlay.json` (`Scoped`: what each SimEffects profile keeps), and taking ForzaWheel's overlay settings and axes the first time |
| `SessionLog.cs` | The session log's format and its writer (`SessionRecorder`); Forza Analysis links it in to read them |
