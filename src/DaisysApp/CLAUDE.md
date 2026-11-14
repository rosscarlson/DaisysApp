# Session notes: the app (shell, Core, app-wide)

Notes on the shell, Core and app-wide work (hotkeys, widgets, title bubbles, the rig's crash hunts). Entries touching
several modules are in each one's notes too. All below is released; git and `CHANGELOG.md` keep the details. Keep it
short (under ~8 KB): condense what's done.

## Latest

- **4.17.10, 4.17.13 (asked 2026-11-14: browser-like back / forward, mouse buttons + Core Hotkeys; then "not the 2nd
  level ones": the installed app was still 4.17.2):** `Shell/TabHistory.cs`. A place = every checked, visible
  RadioButton styled "TabButton" (visual-tree walk, collapsed branches skipped), outer first, so modules' `SubTabs`,
  Audio's own tabs and Settings' pages all count; ones whose content holds a ButtonBase (closable: Notepad, Terminal,
  AI, Logs) don't. Noted on `ToggleButton.Checked` (handledEventsToo) at Background priority (tab-then-sub-tab is one
  place); max 50; Go re-checks them in order, `going` set. Mouse: `PreviewMouseDown` XButton1/2; WM_APPCOMMAND by an
  HwndSource hook (WPF's BrowseBack binding never fired), skipped within 500 ms of a press. Hotkeys `App.Back` /
  `App.Forward` (unbound). Tested in DaisysDev with `%TEMP%\navtest\nav.ps1` (UIA selects, posted WM_XBUTTON).

- **4.0.0 (the 2026-11-03 audit, the user: "everything but 1, 3, 4", released as 4.0):** Core 0.55. What each item became
  and what's left: the status table in `docs/AUDIT-2026-11-03.md`. Watch points: `RunningPrograms.Watch` delivers on the
  watcher's UI context (made on the UI thread) or the pool, never twice at once, `Recheck` re-delivers; `lookingFor`
  re-delivers when a module's list changes. `DeviceChanges` makes its window on the UI thread the first time someone
  listens; `GameControllers`' static ctor and `ControllerButtons.Listen` subscribe by BeginInvoke (no Invoke from a
  pool thread). `Ui.Pen` freezes only frozen brushes (a theme's isn't). `JsonStore.Save` writes `.tmp` then moves;
  `SaveSoon` keeps the latest value per name, flushed in `App.OnExit` and ProcessExit. Tested in DaisysDev: clean start,
  fake ForzaHorizon5.exe (renamed ping, `%TEMP%\da\gw`) switched B2KR on/off and SimEffects' profile, buttclient.py
  read the state. Scripts of the edits: `%TEMP%\da\*.py`. Lesson: heredocs with quotes fail here; write .py files.
- **3.37.16 (asked 2026-11-03: audit that nothing uses its own shortcut button/system):** Core 0.54
  `HotkeyWindow.Button(title, hotkey, closed)` and `Button(title, () => list, () => shown, extra, closed)` (several
  bindings; `shown` = tooltip, accent when non-empty). Every hand-made joystick/ShortcutBox UI moved to it (list in
  `CHANGELOG.md`); raw values shown (GUID names) fixed with `Describe`/`Text`; string matches of shortcuts →
  `ControllerButtons.Same` (Resizer `OnHotkey`, Gaming's camera hide). Kept on purpose: Core's unbound `ShortcutBox`
  in Gaming's `CameraLayoutWindow` (cameras are a copy until Save; ids by index), Screen's `SpaceDrag`/`TopMost` LL
  hooks (features, not shortcuts), Button2Key's winmm reads (its mapping; under `JoystickApi`), AudioTools Tests'
  in-tab keys. Lesson: a view built before its service registers gets null from `Hotkeys.Find` (Recording's broke).
  Tested in DaisysDev via UIA (`%TEMP%\hkaudit\dump.ps1`, `open.ps1`).
- **3.37.15 (asked 2026-11-03, from Mini Mirror: controller buttons kept by number, not device ID; released, Release
  run passed):** Core 0.53. `ControllerButtons.Name` = "Controller <OEM name> Button N {guid}" when `devices` knows the
  winmm id (`Identify`: IDirectInputJoyConfig8.GetConfig per id, guid at offset 4, type at 136, OEMName from the
  registry; run at scan, Rescan and when a lost one answers again), else the old "Controller N Button M". `Same` (guid,
  or number through the current map) is how `Hotkeys` matches presses, Clashes and let-go; `Describe` for display
  (ShortcutBox, tray hints, `Hotkey.Text`, modules' tooltips); `Hotkeys.Upgrade` on `Identified` rewrites old number
  values through `Spec.Set`. `HotkeyWindow.Button(title, () => hotkey)` is the standard joystick button (accent when
  bound). Tested by reflection with fake devices (`%TEMP%\mmfix\harness`: swap, upgrade); PLEX has no controllers, so
  GetConfig on real ones is untested (on the rig: `docs/UNTESTED.md`).
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
  4.6.8: every strip in rows (`stacked` ignored); a new widget (`Width <= 0`, strip) starts `ClickThrough`.
  4.15.2: every widget not clicking through rests on the 1-alpha `CatchBrush` (card or not; before, card-less ones
  caught right-clicks only where their body drew), and so does a click-through one while Ctrl+Shift is held.
- **Title bubbles (3.13.0, 3.18.2, Core 0.39):** `TitleBadges` (`Green/Amber/Red`, `IsRed`, `Badge.Open` =
  "Module/SubTab"; AI's always last), drawn by `Shell/TitleBadgeStrip`. Tray icon colour follows (`Shell/TrayIcon.cs`).
- **Update banner (3.23.2):** `UpdateService.CountBehindAsync` → " (N Behind)"; 0 on failure.
- **Switched-off modules:** the main DLL is loaded once for `[Applet]` metadata only; nothing else is created.
- **Core 0.42 (3.18.0):** `SubTabs.Right`, `AppletGroup.HeaderRight`. **Logs (3.12.9):** in Tools, `Log.Clear(name)`.

## The rig's crashes (GAMER11, 192.168.1.10, AI remote port 38863)

- **Found and fixed in 4.3.1 (2026-11-05):** the crashes since 0.6.3 (0xc0000374, access violations in victims, mostly
  seconds after a controller drops) were a Windows bug in dinput.dll and dinput8.dll `DIHid_CheckHidList`: closing a gap
  in `g_phdl` it scans from slot [capacity] (one past the end), copies it if +0x18 isn't 0 and memsets 0x68 bytes past
  the block. Caught by full page heap + WER full dump (`%TEMP%\da-pageheap\enable.ps1`/`disable.ps1`, WinDbg's cdb from
  winget): joyGetPosEx → joyOpen → CJoyCfg_GetConfig → DIHid_BuildHidList (non-forced rebuild). Core's
  `DirectInputFix.Apply()` (App.OnStartup, Joystick's static ctor) patches 13 bytes in both DLLs when the 31-byte
  pattern matches exactly once (scan from the last slot; `jle` when none between); log line "DirectInput fix … patched".
  A Windows update changing the code → "not needed" in the log: check the new code. Earlier fixes (3.12.4 XInput
  padding, 3.12.9 winmm lock, 3.24.6 DirectInput lock) stay. Mouse crawl before the crash: the process's LL hooks
  stalled while it died. Watch: no crash on the rig after a pad goes.
- Logs come as the app's export zip (Tools → Logs); a read-only Claude session on the rig can read its logs.

## Lessons

- Local builds pick up OneDrive's `*-GAMER11.cs` copies: `dotnet build DaisysApp.sln
  -p:CustomAfterMicrosoftCommonTargets=<file with Compile Remove="**/*-GAMER11*.cs">`.
- Posted WM_LBUTTONDOWN/UP don't reach WPF's mouse handling; a real click needs `SendInput`.
- Dialogs opened by a UIA call draw white; a harness's second `ShowDialog` needs an explicit `ShutdownMode`.
- Tree walks from a hit test or `OriginalSource` must handle `Run`/ContentElements (use the logical parent).
- Bash heredocs with many quotes fail in this harness: write Python scripts with the Write tool and run them.
