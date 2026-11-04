# Session notes: Joy2Key (Button2Key)

Read only when working in this folder. Current state, decisions and lessons; what each release did is in git and
`CHANGELOG.md` (2.9.0–3.34.1 condensed here on 2026-11-04). Keep it short.

## Where things stand (2026-11-04)

- **4.0.0 (the audit):** games are Core's `RunningPrograms.Watch` (2 s; `OnPrograms` stores the game, the engine's loop
  takes it; `Recheck` on `SetProfiles` and when AutoSwitch comes back on) and Core's `GameSwitch` with
  `OffEvenIfByHand` (the user's 3.1.6 choice, below). The loop's runs are grouped by controller once per profile
  (`SetRuns`), not every 4 ms. winmm calls are Core's `Hotkeys/Joystick` (`GetPos`, `Caps`, `OemName`, under
  `ControllerButtons.JoystickApi`); the 1 ms timer is Core's `HighResTimer.Hold()`; the game list is `Ui.ProgramList`.
  Not done (needs the user's yes and the rig): profiles following a controller Windows renumbers (as shortcuts do
  since 3.37.15). Built and started in DaisysDev only; no controller on PLEX.

## Decisions and how it works

- Engine thread (`Joy2KeyEngine`) runs while off too (to see a game start); a game starting picks its profile once
  (a profile picked by hand afterwards stays) and switches it on if off; the game ending switches it off **however it
  was switched on** (3.1.6, the user's choice; B2KR and ForzaWheel never override a hand-set state). Off by hand with
  the game running stays off. `OnByGame` only makes the applet's `Dispose` save Off. No Default profile is required.
- Controllers: winmm ids (JoyToKey numbering, saved profiles) plus every Core `GameControllers.List()` device winmm
  doesn't list, as `DiOnly` (ids from 200, read through DirectInput) since 3.34.1 (the HOTAS Warthog throttle).
  XInput pads read through `XInput.cs` (XINPUT_STATE_EX's padding: 3.12.4's heap-corruption fix on the rig).
- Keys go out by `KeySender` (Controllers' `Actions/KeyCatalog.cs`, `Native.Send`), each logged ("Sent key: …", 30 per
  10 s) since 3.6.1's FFB-loss hunt (no regression found; the rig's evidence pointed at a USB dropout and the Xbox pad).
- The bubble reads "B2K: <profile>" (3.16.3).

## Lessons

- A profile already Active when a game switched it on wasn't loaded until the tab opened (3.2.5): the loop reloads
  when `profile == null`.
- Test a game with a renamed `ping.exe` (`%TEMP%\da\gw\ForzaHorizon5.exe` style), hidden, as the profile's program.
