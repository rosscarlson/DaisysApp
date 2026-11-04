# Session notes: Butt2KeyRemote

Read only when working in this folder. Current state, lasting facts and lessons; what each release did is in git and
`CHANGELOG.md` (2.7.0–3.7.0 condensed here on 2026-11-04). Keep it short.

## Where things stand (2026-11-04)

- **4.0.0:** games are Core's: `ButtHub.CheckGames(Snapshot)` is called by `RunningPrograms.Watch` (2 s, `lookingFor` =
  AutoSwitch/StartWithGame + each BigButt's programs, so an edited list is seen at once); on/off is Core's `GameSwitch`
  (its `set` returns false without StartWithGame; `SetEnabled(on, byGame)` calls `ByHand()` unless by game). The game
  list is Core's `Ui.ProgramList`; `ButtWire` frames through Core's `Network/Frames` (bytes unchanged). Tested in
  DaisysDev: a fake `ForzaHorizon5.exe` (renamed ping) switched it on and off, `tools/buttclient.py 127.0.0.1
  buttpass123 --port 38871` read the state.
- **Waiting on the user:** Rename's prompt, a real rounded screen, Link's menu, a game closing returning to Picked,
  templates in real games, Import's file dialog (`docs/UNTESTED.md`).

## How it fits together

- `ButtHub` (screens, BigButts = profiles with games, pop-outs, editors, the shortcut, the bubble), `ButtServer` +
  `ButtWire` (TCP 38870 by default; PBKDF2-SHA256 150 000 from the passphrase + per-connection salt, AES-GCM, frames:
  4-byte LE length, then 12-byte nonce (1 device / 2 server + count), tag, message; UDP discovery), `PressEngine`
  (Button2Key's actions, `DaisysApp.Controllers/Actions`), `EditorWindow` (Save & Close, undo, Turn), `PopOutWindow`
  (a Core `Widget`), `Templates.cs` + `TemplateWindow` (Butt Wizard; built-ins in `Templates/*.json`), `RemoteView`
  (cards: Status, BigButts with games, its ButtScreens, Remote Devices), `RemoteSettingsView`. The protocol is
  `API.md` (root); the Android app speaks it (`clients/android`, its own README).
- Decisions kept: no BigButt is special (`Active` "" when none; `Picked` = last hand-picked); a game switches it on only
  if off, and off only if the game switched it on; `Dispose` saves Off if on only by a game.

## Android and fixtures

- Toolchain in `%USERPROFILE%\Android` (JDK 17, SDK 36, AVD `tab` = Pixel Tablet Android 16; `Android\tools\ui.py`
  drives the emulator, `Android\tools\dawin.ps1` drives DaisysDev by UIA). Gradle output `%TEMP%\b2kr-build` (OneDrive
  locks the source folder). Release key + `keystore.properties` in `D:\OneDrive\Code\Windows\Butt2KeyRemote-signing\`
  (outside git; every update signed with it). targetSdk stays 35 (36 makes Android 16 tablets ignore orientation).
  App 1.1.4 is on the 3.0.3 release as `Button2Key-Remote-1.1.4.apk`; its code is also built into Daisy's App Mobile
  (`D:\OneDrive\Code\Android\DaisysApp`, `b2kremote/` points at `clients/android/app/src/main`).
- DaisysDev: port 38871, passphrase `buttpass123`, ButtScreens test1 "Test Pad" / test2 "Dash", BigButts Default /
  Forza (ForzaHorizon5.exe) / two Elite Dangerous.

## Lessons

- A UIA Toggle doesn't raise WPF's Click, and synthetic clicks can hit other windows: don't rely on them.
- Don't send keys or focus to DaisysDev windows: the user's own typing landed there once.
- A dialog opened by a UIA call can't be driven by UIA (post window messages to it).
