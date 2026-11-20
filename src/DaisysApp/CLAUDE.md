# Session notes: the app (shell, Core, app-wide)

Notes on the shell, Core and app-wide work (hotkeys, widgets, title bubbles, the rig's crash hunts). Entries touching
several modules are in each one's notes too. All below is released; git and `CHANGELOG.md` keep the details. Keep it
short (under ~8 KB): condense what's done.

## Latest

- **4.17.28 (asked 2026-11-17 after 4.17.26's flood killed Plex: audit everything that retries for ever, add limits
  with a log line or a popup by severity):** Core 0.65 `Shared/Network/RetryGuard` (waits doubled to a cap; Warn after
  N in a row, a notification if `notify`; `budget` tries in 10 min → Paused until Reset). ServerLink: one guard for
  every connect (5 s→5 min, budget 30, notify; a connection closed within 30 s counts as a failure; Succeeded after
  30 s up; Reset on SetUp/ForgetKey/Forget). KeptChannel and ServerSync back off to 5 min, reset after a channel
  lasted 60 s. Zabbix (30 s→10 min; Refresh = `now`), HA (5 s→5 min, reset after 60 s up), Plex Now Playing
  (10 s→2 min), ApiAgent (8 limit waits), ButtServer's UDP finder (no spin). Left as they were (bounded, local or
  by hand): iRacing (4 tries), updates (Attempts), YouTube uploads (5), LibreHardwareMonitor (15 s, localhost),
  Arduino (15 s, COM), Govee (200 ms on a socket error, UDP), AI remote (by hand), Support's pings (the user's rate),
  ShareQueue (a minute; through ServerLink's guard). Harness in Temp (`retryguard-test`).

- **4.17.10, 4.17.13 (asked 2026-11-14: browser-like back / forward, mouse buttons + Core Hotkeys; then "not the 2nd
  level ones": the installed app was still 4.17.2):** `Shell/TabHistory.cs`. A place = every checked, visible
  RadioButton styled "TabButton" (visual-tree walk, collapsed branches skipped), outer first, so modules' `SubTabs`,
  Audio's own tabs, Settings' pages and (4.17.15, asked: AI's sessions) closable tabs all count; Go passes places
  with a button no longer in the tree (closed) or equal to here. Noted on `ToggleButton.Checked` (handledEventsToo) at Background priority (tab-then-sub-tab is one
  place); max 50; Go re-checks them in order, `going` set. Mouse: `PreviewMouseDown` XButton1/2; WM_APPCOMMAND by an
  HwndSource hook (WPF's BrowseBack binding never fired), skipped within 500 ms of a press. Hotkeys `App.Back` /
  `App.Forward` (unbound). Tested in DaisysDev with `%TEMP%\navtest\nav.ps1` (UIA selects, posted WM_XBUTTON).

- **4.0.0 (2026-11-03 audit, Core 0.55):** status in `docs/AUDIT-2026-11-03.md`. `RunningPrograms.Watch` delivers on
  the watcher's UI context, never twice at once; `DeviceChanges`' window made on the UI thread; `JsonStore.Save` via
  `.tmp`, `SaveSoon` flushed on exit.
- **3.37.16 / 3.37.15 (Core 0.54 / 0.53):** every shortcut button is `HotkeyWindow.Button`; controller buttons kept by
  DirectInput instance ("Controller <OEM> Button N {guid}", `ControllerButtons.Same`/`Describe`, `Hotkeys.Upgrade`).
  A view built before its service registers gets null from `Hotkeys.Find`. GetConfig on real pads: `docs/UNTESTED.md`.
- **3.32.0:** Core `Widget`: **Background** slider (`IWidgetPlace.Opacity`, the background's only), **Click through**
  (`IWidgetPlace.ClickThrough`; Ctrl+Shift held catches clicks), strip hit test `OnControl`. Green logo; `TrayIcon`
  idle blue from it.
- **3.30.0 (another AI's duplicate-code audit):** Core 0.52 `Shared/Ui`, `Sizes`, `ConnectionForm`, `GroupGrid`,
  `SessionLog`, `Win32/`, `Shell/TabGroups`; Controllers 0.2 `InputRef`, `Capture`, `Actions/`. Lesson: a scripted
  `Ui.X` → `NetUi.X` hit the wrapper itself (stack overflow only a run showed).
- **3.26.8:** Core 0.51 `Shared/Audio/AppAudio` (no packages; Core Audio COM list, winmm play/record, `AppAudio.json`)
  + `AudioSettingsView`. Lessons: create the enumerator by CLSID (NAudio's [ComImport] wins the cast); System.Speech
  reads Length/Position/Seek of its stream.
- **3.26.2:** Core 0.48 `Shared/Network/Pings.SendAsync(address, timeoutMs, maxAgeMs)` shared by Performance and Support.
- **Idle audit (2026-10-29, answered from the code):** biggest idle costs: Performance's per-process PDH every 10 s,
  Video's ETW frame counter (`LogHistory` on by default), `ControllerButtons` 25 ms poll while a button is bound, LHM
  every 2 s. **Offered, waiting:** measure them on the rig.
- **3.25.3 / 3.24.6:** `DaisysApp.Controllers` lists controllers once (`RefreshAsync`, `RefreshButton`), Core 0.46
  `Rescan`; Core 0.44 `ControllerButtons.DirectInput` is the one lock for every DirectInput call.

## Reference (condensed)

- **Hotkeys (3.5.0, Core 0.33):** `Shared/Hotkeys/Hotkeys.cs` (`HotkeySpec`, `Hotkeys.Add` → `Hotkey`,
  `Replace(prefix, items)`, `Suspend()`, one `HotkeyManager` + controller listener, warnings in the adding module's
  log), `HotkeyWindow` (+ `Button`, 3.37.15), `ShortcutBox` (give it a `Hotkey`; `ClearFirst` in windows);
  Settings → Shortcuts is `Shell/ShortcutsPage.cs`. Storage stays in each module's settings via Get/Set. 3.37.14:
  **Also:** a row per clash (`ShortcutBox.Brief`). Controller buttons by instance since 3.37.15 (above).
- **Confirm? (3.5.0, 3.17.2):** `Shared/ConfirmButton.cs`: `Make`, `Arm`, `ArmIcon` (red ✕). Kept as dialogs: end
  Windows process, Realtime, busy AI tab close, Storage Delete now…, ForzaWheel import replace.
- **Widgets (3.7.0, 3.17.2, Core 0.35/0.41):** `Shared/Widgets/Widget` (`Aspect` via WM_SIZING, edit strip via
  WM_NCHITTEST, `MakeText`/`MenuItem`/`Offer`/`Menu`, `DeleteButton`, `ListCard`); B2KR's `PopOutWindow` is one.
  4.6.8: every strip in rows (`stacked` ignored); a new widget (`Width <= 0`, strip) starts `ClickThrough`. 4.18.10: modules leave a new place's size 0 (Performance, Zabbix pre-sized it, so never got it; Zabbix host's 260×190 is its `firstSize`).
  4.15.2: every widget not clicking through rests on the 1-alpha `CatchBrush` (card or not; before, card-less ones
  caught right-clicks only where their body drew), and so does a click-through one while Ctrl+Shift is held.
  4.18.8 (asked: a widget at the screen's bottom hid its strip): strip above (`PlaceStrip`) when it won't fit below
  in the monitor's work area (`StripAboveFor`); edit grows upward, `SaveBounds` takes `grownPx` off the top; WM_MOVING
  places it from the pointer (`grabY` at WM_NCLBUTTONDOWN): Windows builds each proposed rect on the last one as
  changed, so offsetting proposals drifted. Tested in DaisysDev with a real drag.
- **Title bubbles (3.13.0, 3.18.2, Core 0.39):** `TitleBadges` (`Green/Amber/Red`, `IsRed`, `Badge.Open` =
  "Module/SubTab"; AI's always last), drawn by `Shell/TitleBadgeStrip`. Tray icon colour follows (`Shell/TrayIcon.cs`).
- **Update banner (3.23.2):** `UpdateService.CountBehindAsync` → " (N Behind)"; 0 on failure.
- **Switched-off modules:** the main DLL is loaded once for `[Applet]` metadata only; nothing else is created.
- **Core 0.42 (3.18.0):** `SubTabs.Right`, `AppletGroup.HeaderRight`. **Logs (3.12.9):** in Tools, `Log.Clear(name)`.

## The rig's crashes (GAMER11, 192.168.1.10, AI remote port 38863)

- **Fixed in 4.3.1:** the crashes (0xc0000374 after a controller drops) were a Windows bug in dinput(8).dll
  `DIHid_CheckHidList` (scans one past `g_phdl`'s end). Core's `DirectInputFix.Apply()` patches 13 bytes when its
  pattern matches once (log "DirectInput fix … patched"; "not needed" after a Windows update: check the new code).
  Found with full page heap + WER dumps (`%TEMP%\da-pageheap\`).
- Logs come as the app's export zip (Tools → Logs); a read-only Claude session on the rig can read its logs.

## Lessons

- Local builds pick up OneDrive's `*-GAMER11.cs` copies: `dotnet build DaisysApp.sln
  -p:CustomAfterMicrosoftCommonTargets=<file with Compile Remove="**/*-GAMER11*.cs">`.
- Posted WM_LBUTTONDOWN/UP don't reach WPF's mouse handling; a real click needs `SendInput`.
- Dialogs opened by a UIA call draw white; a harness's second `ShowDialog` needs an explicit `ShutdownMode`.
- Tree walks from a hit test or `OriginalSource` must handle `Run`/ContentElements (use the logical parent).
- Bash heredocs with many quotes fail in this harness: write Python scripts with the Write tool and run them.
