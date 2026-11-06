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
- **FFB dropouts while driving (2026-11-06):** Forza cuts FFB for ~1 s on any input not from vJoy. The audit found
  nothing in ForzaWheel (no lost devices, no reopens); Button2Key's d-pad keys are menus only (the vJoy hat doesn't work
  there without them: not the cause). Added `InputWatch` (Log other input → `ForzaWheelInput` log) to catch it on the
  rig: the next dropout's time, then what's logged just before it. On a first try here the Gladiator (not bound, not
  hushed) jittered on Y (0.2%) unprompted. Released in 4.3.2 (another session, 2026-11-06, at the user's word: a dropout came with Plex pausing, so media keys (consumer control, page 0x0C) were added).

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
