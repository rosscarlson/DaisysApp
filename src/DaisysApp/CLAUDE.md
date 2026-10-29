# Session notes: the app (shell, Core, app-wide)

Notes on the shell, Core and app-wide work (hotkeys, widgets, title bubbles, the rig's crash hunts). Entries touching
several modules are in each one's notes too. All below is released; git and `CHANGELOG.md` keep the details. Keep it
short (under ~8 KB): condense what's done.

## Latest

- **3.25.1 (asked 2026-10-29):** tray icon follows a bubble's own colour (red > first own colour, not amber > green >
  blue; `TrayIcon.ShowBadges`, icons cached per hex). Built only.
- **3.25.0 Save Settings (asked 2026-10-28):** Settings → General, a **Settings** card right of Updates (third
  column, `Width=Auto`): **Save…** / **Load…** (`SettingsPage.xaml(.cs)`). `Shell/SettingsBackup.cs`: Save = every file
  under `AppPaths.SettingsFolder` (skips `Logs/`, `Preview music/`, `PluginData/`, `Plugins/`, `*.jsonl`, `*.capturing`,
  `*-GAMER11*`, >2 MB) in one JSON (`format: "DaisysApp settings"`, `files` parsed, `otherFiles` base64). Load
  validates (paths kept inside the folder), stages `%LOCALAPPDATA%\<app>\settings-restore.json`, restarts;
  `App.OnStartup` → `ApplyPending()` before `AppSettings.Load()` (modules saving on exit can't overwrite): current set
  to `settings-before-restore.json`, files the backup lacks deleted, its files written; notice via `Restored`.
  Decision: no module changes: every module already saves via `JsonStore` (or subfolders) in that folder.
  Tested: a harness as its own exe (`sbharness.exe`, aborts unless ShortName is its own): 16 checks (round trip,
  byte-for-byte non-JSON, extra files removed, logs/laps untouched, traversal/rooted/foreign/empty rejected). Not
  tested: the card on screen and the restart path in the real app. **Incident:** the first harness ran as
  `dotnet x.dll` → ShortName "DaisysApp" → it wiped the user's real settings; a clean exit rewrote them (memory
  `dotnet-harness-is-daisysapp.md`).
- **3.24.6 (asked 2026-10-28: rig zip `Daisy's-App-2026-10-28-2123.zip`, "fix and publish"):** 3.24.3 crashed with an
  AV in `GetAdaptersAddresses` (Support's `EventMonitor.CheckAdapters`, a victim), then 8 s after the next start in
  **`FAWForceFeedbackDrv64.dll`** (Fanatec 8.20.5.600, 0xc0000409), DirectInput's FF driver inside our process. Four
  DirectInputs ran unsynchronised (`GameControllers.cs` built into ForzaWheel, Joy2Key and SimEffects, each its own
  static gate; Calibration's own). Now Core 0.44 `ControllerButtons.DirectInput` is the one lock: `GameControllers.gate`
  (List per device, Reader.Read/Dispose lock this → gate), `ForceFeedback.gate` = it (and `Create`), Calibration's
  gate, `DiController` Read/Get/Set/Dispose; `joyConfigChanged` outside it (JoystickApi never inside DirectInput).
  Build only (no controllers here). Also a hang on 3.24.4 (20:22:07, start 20:21:13): the zip had only ERROR rows, so
  whether HangWatch dumped is unknown: ask for `DaisysApp-hang-*.dmp`. Next: the rig runs with the Fanatec wheel.
- **3.24.4 (released):** `HangWatch` asks every 1 s (2 s wait), logs at the first miss, dumps at the second; hang cause
  still unknown (await a dump).
- **3.24.1 (released):** `Widget.IsInside` walks `Run`s with the logical parent (WM_NCHITTEST in edit mode threw).

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
