# Session notes: the app (shell, Core, app-wide)

Notes on the shell, Core and app-wide work (hotkeys, widgets, title bubbles, the rig's crash hunts). Entries touching
several modules are in each one's notes too. All below is released; git and `CHANGELOG.md` keep the details. Keep it
short (under ~8 KB): condense what's done.

## Latest

- **3.26.1 one process list (asked 2026-10-29: "find duplicate functions, put them in the API"; released, Release
  run passed):** six modules polled `Process.GetProcesses` (Button2Key, B2KR, ForzaWheel, Mini Mirror 2 s, SimEffects
  3 s, Resizer 1 s) and four pickers were copies. Now Core 0.47 `Shared/RunningPrograms`: `Get(maxAgeMs=1000)` /
  `GetAsync` (one shared listing; NtQuerySystemInformation into a pinned buffer, names + pids, 47 KB vs 630 KB),
  `Has`/`FirstOf`/`PidsOf`, `Bare`, `Foreground`, `Windowed(all)`; Template README rule. USB Monitor poll 3 → 30 s.
  Left: audio (notifications), sensors (`SensorHub`). Tested: `%TEMP%
ptest`; DaisysDev with PING.EXE as
  `ForzaHorizon5.exe`.
- **3.26.2 one pinger (asked 2026-10-29, yes to the offer):** Core 0.48 `Shared/Network/Pings.SendAsync(address,
  timeoutMs, maxAgeMs)`: an answer under maxAge reused, a ping waiting joined, else sent; `PingResult(Ms, Status,
  NotFound)`, each module words its errors (T). Performance asks maxAge 900, Support 90% of its timeout. Histories,
  stats, outages, CSV log unchanged per module. Tested: `%TEMP%ptest` (two 1 s callers out of step: 40 asks, ~20
  pings); DaisysDev: Support's CSV filled (Performance's graph not looked at on screen).
- **3.26.0 (asked 2026-10-29: "everything worked great, publish as 3.26: a known working build"):** 3.25.3's code
  unchanged; the user confirmed no FH6 lag spikes and Refresh working on the rig.
- **3.25.3 controllers listed once:** `src/DaisysApp.Controllers` (`GameControllers.List()` cached, `RefreshAsync`
  → `Changed`, `RefreshButton`, `Gate` = Core's `DirectInput` lock; Core 0.46 `ControllerButtons.Rescan`); lost
  devices retried alone every 3 s. Cause was FFB waiting behind other modules' listings (119–165 ms each on the rig).

- **3.25.1 (asked 2026-10-29):** tray icon follows a bubble's own colour (red > first own colour, not amber > green >
  blue; `TrayIcon.ShowBadges`, icons cached per hex). Built only.
- **3.25.0 Save Settings:** `Shell/SettingsBackup.cs` (Save/Load the whole settings folder in one JSON; Load
  stages a restore applied in `App.OnStartup` before settings load; old set kept in `settings-before-restore.json`).
  **Incident:** a harness run as `dotnet x.dll` wiped the user's real settings (memory `dotnet-harness-is-daisysapp.md`).
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
