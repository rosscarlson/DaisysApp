# Session notes: ForzaWheel

Read only when working in this folder. Current state, decisions and lessons; what each release did is in git and
`CHANGELOG.md` (condensed here on 2026-11-04). Keep it short.

## Where things stand (2026-11-04)

- **4.0.0 (the audit):** games that start the emulator are Core's `RunningPrograms.Watch` (`lookingFor` = the games,
  so an edited list is seen at once) and `GameSwitch` (`set(true)` = `emulator.Start()`, false if it couldn't; the
  emulator stopping by hand calls `ByHand()` in `Remember`, so a hand stop/start is left as it is). The list is Core's
  `Ui.ProgramList`; the feed loop's 1 ms timer is Core's `HighResTimer.Hold()`; the tab's timer is `Ui.Every`.
  Not tried with vJoy or a game (none on PLEX).
- **Waiting on the user:** 3.36.13's **Test FFB** with vJoy and a wheel (`docs/UNTESTED.md`).
- **FFB dropouts while driving (2026-11-06):** Forza cuts FFB for ~1 s on any input not from vJoy. 4.3.2's `InputWatch`
  (Log other input → `ForzaWheelInput` log) found the cause on the rig; the user then had it taken out again (not
  released since). The audit found nothing in ForzaWheel; Button2Key's d-pad keys are menus only, not the cause.
- **FH5 menus with the vJoy wheel (2026-11-06):** FH5 gives an unknown wheel (vJoy) only Custom Wheel Profile 1–5,
  with empty, unbindable menu bindings: paddles don't change tabs, the d-pad doesn't move. Failed on the rig (Store
  copy): GameInput registry UINavigation and button labels for 1234BEAD. Defaults with menus exist only per VID/PID
  in `media\inputmappingprofiles.zip`; 4.4.2's `Fh5MenuFix` adds one for 0x1234bead (Steam only: WindowsApps is
  locked, custom profiles are saved encrypted, vJoy's VID/PID is in its signed driver). The user refused a virtual
  Xbox pad's bumpers (FFB risk). Waiting on a friend with Steam to try it.

- **4.17.19, FFB stuck and the app frozen (2026-11-14, FH4 on GAMER11):** two hang dumps (logs folder,
  `DaisysApp-hang-*.dmp`; cdbX64 from WinDbg's WindowsApps + dotnet-dump): UI thread in Stop → `ForceFeedback.Dispose`
  → `SendForceFeedbackCommand(StopAll)` waiting in FAWForceFeedbackDrv64, whose worker thread and SimEffects'
  `FanatecWheel.Send` were both in `WriteFile` to the base (input still read). Began the day SimEffects → Wheel first
  ran with ForzaWheel. Now: driver commands via `Bounded` (Gate held, call on its own thread, 1 s / 0.5 s), a stuck
  wheel's reader kept in `abandoned`, not disposed (Unacquire would hang too), `ffbStuck` until the next Start.
  Untested: the stall can't be made on purpose. If it recurs with the Wheel off, it's the base or Fanatec's driver.
- **4.17.24, frozen again with 4.17.23 (19:13, no dump: killed within seconds):** the Wheel's 250 ms timeout fired,
  ForzaWheel logged nothing: the stuck call was an effect update in `OnPacket` (unbounded, under Gate), so Gate was
  held for ever and the UI's Stop/Reopen (emulator lock → Tick → `Reader.Read` → Gate) and the tab's `Devices` hung.
  Now every driver call after creation runs on the `ForzaWheel FFB` worker via `Bounded` (Gate held, 500 ms, then
  `Stuck`); Tick drops a stuck FFB at once and keeps reading its reader (in `abandoned`, never disposed); losing that
  reader (base power-cycled) clears `ffbStuck`. UI-side `Locked` (1 s, else queued in `pending` for Tick); `Devices`
  is TryEnter with the last count. SimEffects' `FanatecWheel`: stalled handles never closed, no 30 s retry.

## How it works, decisions

- The emulator (`WheelEmulator`): wheel, pedals, shifter, handbrake and button boxes as one vJoy device for Forza;
  Hush (`GameInputRegistry`, elevated by `Elevated`) mutes the bound real devices while it runs (Auto Hush); bindings
  as JSON. `WasRunning` restarts it at launch only if it was started by hand; the bubble always shows while it runs
  (3.18.3). The input overlay moved to SimEffects → Overlays in 3.17.0 (`ForzaWheelSettings.Overlay` kept for it to read).
- **Test FFB (3.36.13):** `ForceFeedback.Test` opens vJoy by DirectInput (exclusive, as a game): constant +3000 then
  −3000 (`SendConstant` sends the wheel the other sign), 0 before Stop (Stop replays the last force), then a sine whose
  strength is its gain (EmuWheel reads the gain). Changes by `SetParameters(… | Start)` so an EffectOperation arrives.
  vJoy's calls aren't under `GameControllers.Gate` (forces come back on vJoy's thread, which takes it).

## Another project (no code here)

- FH2 in the Azimuth emulator (`iamAzoDx/fh2wheel`; the user's FH1 work is `rosscarlson/pinyon-shift`): telemetry
  there needs reverse-engineering the car state (memory scans while driving, then stable pointers); estimated a few
  sessions for basics. Waiting on the user to say go; it gets its own working folder, not this repo.
