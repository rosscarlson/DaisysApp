# Session notes: the app (shell, Core, app-wide)

Notes on the shell, Core and app-wide work (hotkeys, widgets, title bubbles, the rig's crash hunts). Entries touching
several modules are in each one's notes too. All below is released; git and `CHANGELOG.md` keep the details. Keep it
short (under ~8 KB): condense what's done.

## Latest

- **3.30.0 (asked 2026-10-31: another AI's audit of duplicate code; change what I agree with):** Core 0.52 `Shared/Ui`, `Sizes`, `ConnectionForm`, `GroupGrid`, `SessionLog`, `Win32/`, `Shell/TabGroups` (Gaming's `[assembly: TabGroup]` in Core, seeded in `AppletCatalog`); Controllers 0.2: `InputRef`, `Capture`, `Actions/`; global using `DaisysApp.Shared` in Modules' props. Audit items 7 (overlay/mirrors on `Widget`: game HUD and live copies, README says not widgets, not testable without a game), 8 (samples differ: instant values vs. a run's avg/max) and 9 (wrong: DiskMark has no crash marker) left out. Files: new ones in Core/Controllers, every module's views, csprojs, READMEs, `lang/en.json`, `CHANGELOG.md`, `docs/UNTESTED.md`. Released (Release run passed). Tested: all tabs, sub-tabs and Settings pages opened in DaisysDev with no errors logged, HA's form and grid against the fake HA (`%TEMP%/hatest/fake_ha.py`); hand checks in `docs/UNTESTED.md`. Another session's Support → GPU sub-module (`Support/Gpu/`, `ModuleTasks.cs`, `SupportApplet.cs`, Support's `en.json`, NvAPIWrapper in `Support.csproj`) was left uncommitted in the tree. Lesson: a scripted `Ui.X` → `NetUi.X` hit the wrapper itself, a stack overflow only a run showed; `params` with a literal null is a null array.

- **3.26.8 app audio devices (asked 2026-10-30: a Settings → General setting right of Daisy's Server for the app's
  input and output, generic in Core, not for what controls Voicemeeter; released, Release run passed, 1 asset):**
  Core 0.51 `Shared/Audio/AppAudio.cs` (no packages: Core Audio COM for `Devices(input)`, winmm for `Play(wav)` and
  `Record(rate, ch)` (a blocking Stream, `Finish()`); `Output`/`Input` = endpoint id or null if default/unplugged;
  `WaveOut`/`WaveIn` = winmm number matched by `DRV_QUERYFUNCTIONINSTANCEID`, -1 default; saved in `AppAudio.json`)
  and `AudioSettingsView.cs` (card, lists refilled on open), placed by `SettingsPage.xaml.cs` in a 2-column Grid.
  Users: AI `Voice` (SpMMAudioOut.DeviceId), question chime (`AppAudio.Play`), `Dictation` (SetInputToAudioStream when
  an input is picked), Video `AudioTrack` mic. Left alone: AudioTools, shaker, `AudioRelay`, recording loopback.
  Lessons: create the enumerator by CLSID (NAudio's [ComImport] class for the same CLSID wins the cast); System.Speech
  reads Length/Position/Seek of its stream (return -1/0/0). Tested: DaisysDev card + saving, `--say` metered on the
  picked Speakers (default Voicemeeter), recording, dictation through a live stream; not: 🎤 and a recording in-app.
- **3.26.2 one pinger (asked 2026-10-29, yes to the offer):** Core 0.48 `Shared/Network/Pings.SendAsync(address,
  timeoutMs, maxAgeMs)`: an answer under maxAge reused, a ping waiting joined, else sent; `PingResult(Ms, Status,
  NotFound)`, each module words its errors (T). Performance asks maxAge 900, Support 90% of its timeout. Histories,
  stats, outages, CSV log unchanged per module. Tested: `%TEMP%\ptest` (two 1 s callers out of step: 40 asks, ~20
  pings); DaisysDev: Support's CSV filled (Performance's graph not looked at on screen).
  Released (Release run passed, 20 assets).
- **Idle audit (asked 2026-10-29: "what runs when the app sits still, how often, rough impact; no code"):** answered as
  a list, from reading the code (not measured). Biggest idle costs: Performance's per-process PDH query (`PerfMonitor.
  SampleProcesses`, Process V2 + GPU Engine(*) wildcards) every 10 s in the background; Video's ETW frame counter
  (`FrameMonitor`) always on because `GamingSettings.LogHistory` defaults true; Core `ControllerButtons` 25 ms poll
  while any controller-button shortcut is bound; LHM data.json every 2 s. Button2Key on = 4 ms poll + timeBeginPeriod(1).
  **Next (offered, waiting):** measure those on the rig (and cut them if they cost much).
- **3.25.3 controllers listed once:** `src/DaisysApp.Controllers` (`GameControllers.List()` cached, `RefreshAsync`
  → `Changed`, `RefreshButton`, `Gate` = Core's `DirectInput` lock; Core 0.46 `ControllerButtons.Rescan`); lost
  devices retried alone every 3 s. Cause was FFB waiting behind other modules' listings (119–165 ms each on the rig).

- **3.24.6:** the rig crashed in Fanatec's FF driver (`FAWForceFeedbackDrv64.dll`) with four DirectInputs running
  unsynchronised; now Core 0.44 `ControllerButtons.DirectInput` is the one lock for every DirectInput call
  (`joyConfigChanged` outside it). Build only. Ask for `DaisysApp-hang-*.dmp` if the rig hangs again.
## Reference (condensed)

- **Hotkeys (3.5.0, Core 0.33):** one system: `Shared/Hotkeys/Hotkeys.cs` (`HotkeySpec`, `Hotkeys.Add` → `Hotkey`,
  `Replace(prefix, items)`, `Suspend()`, one shared `HotkeyManager` + controller listener, warnings in the adding
  module's log), `HotkeyWindow`, `ShortcutBox`; Settings → Shortcuts is `Shell/ShortcutsPage.cs`. Storage stays in
  each module's settings via Get/Set. 3.12.6/3.12.8: `ShortcutBox.Set` binds before resuming; `ClearFirst` (a set value
  needs × before rebinding; HotkeyWindow's boxes; Settings → Shortcuts' don't). Presses logged at Info (3.6.1).
- **Confirm? (3.5.0, 3.17.2):** `Shared/ConfirmButton.cs`: `Make`, `Arm`, `ArmIcon` (red ✕). Kept as dialogs: end
  Windows process, Realtime, busy AI tab close, Storage Delete now…, ForzaWheel import replace.
- **Widgets (3.7.0, 3.17.2, Core 0.35/0.41):** `Shared/Widgets/Widget` (`Aspect` via WM_SIZING, edit strip via
  WM_NCHITTEST, `MakeText`/`MenuItem`/`Offer`/`Menu`, `DeleteButton`, `ListCard`); B2KR's `PopOutWindow` is one.
- **Title bubbles (3.13.0, 3.18.2, Core 0.39):** `TitleBadges` (`Green/Amber/Red`, `IsRed`, `Badge.Open` =
  "Module/SubTab" for `AppNavigation.OpenTab`; AI's always last), drawn by `Shell/TitleBadgeStrip` (click opens, drag
  moves the window). Tray icon colour follows (`Shell/TrayIcon.cs`, `Recolour`; `disposed` guard since 3.6.1).
- **Update banner (3.23.2):** `UpdateService.CountBehindAsync` → " (N Behind)"; 0 on failure.
- **Switched-off modules:** the main DLL is loaded once for `[Applet]` metadata only (no module code runs, no
  dependencies); nothing else is created. Applies after Restart now.
- **Core 0.42 (3.18.0):** `SubTabs.Right`, `AppletGroup.HeaderRight` (SimEffects' Profile drop-down).
- **Logs (3.12.9):** Logs moved to Tools (`ModuleMoves.LogsToTools` migrates settings each start); `Log.Clear(name)`;
  export zips only the rows shown.

## The rig's crashes (GAMER11, 192.168.1.10, AI remote port 38863)

- Heap corruption (0xc0000374) and AccessViolations in unrelated places, mostly seconds after the Xbox 360 pad
  (045E:028E) on port `9&DBBB1C1&0&4` drops and 1A34:F517 appears. Fixed so far: 3.12.4 `Joy2Key/XInput.cs`
  XINPUT_STATE_EX padding (wrote 4 bytes past the struct), `GameControllers.Reader` lock; 3.12.9 winmm joy* calls under
  `ControllerButtons.JoystickApi` lock; 3.24.6 one DirectInput lock (Fanatec FF driver crash). Still seen after 3.12.x. **Not found yet; next step:** a full crash
  dump on the rig (WER LocalDumps, maybe page heap). This PC has no controller, so stress tests here prove nothing.
- FFB-loss report (3.5 era): no code regression found; pointed at the USB dropout, the Xbox pad and Button2Key's keys
  (AutoSwitch turns it back on with Forza).
- Tools: logs come as the app's export zip (Tools → Logs); a read-only Claude session on the rig can read its logs.

## Lessons

- Posted WM_LBUTTONDOWN/UP don't reach WPF's mouse handling; a real click needs `SendInput` (only when
  WindowFromPoint is the target).
- Dialogs opened by a UIA call draw white; a harness's second `ShowDialog` needs an explicit `ShutdownMode`.
- Tree walks from a hit test or `OriginalSource` must handle `Run`/ContentElements (use the logical parent).
