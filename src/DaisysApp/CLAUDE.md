# Session notes: the app (shell, Core, app-wide)

Notes on the shell, Core and app-wide work (hotkeys, widgets, title bubbles, the rig's crash hunts). Entries touching
several modules are in each one's notes too. All below is released; git and `CHANGELOG.md` keep the details. Keep it
short (under ~8 KB): condense what's done.

## Latest

- **3.24.4 (asked 2026-10-28: logs zips of the rig and the friend's PC in Downloads, `Daisy's-App-…-1945.zip`,
  `Ex-Daisy's-App-…-1944.zip`):** the rig's 3.24.3 hung 34 s after a quick update's start (19:43:50 → 1002 at 19:44:24,
  restarted 3 s later), as 3.24.0 at 18:29; nothing logged, HangWatch's 5 s probe lost the race with a hand close.
  `HangWatch` now asks every 1 s with a 2 s wait, logs at the first miss and dumps at the second in a row (~4–5 s,
  before Windows' ghosting); sleeps again once dumped (an ABORTIFHUNG ask returns at once). Cause of the hang still
  unknown: the dump will tell. Mini Mirror log fix in Video's notes. Build only. Released (tag v3.24.4,
  Release run passed, 20 assets).
- **3.24.1 (asked 2026-10-28: "Something went wrong: 'System.Windows.Documents.Run' is not a Visual or Visual3D", rig
  logs zip in Downloads, `Daisy's-App-2026-10-28-1845.zip`):** the log showed it repeated from
  `Widget.IsInside` ← `Widget.WndProc` (WM_NCHITTEST while a widget is in edit mode: `InputHitTest` can return a
  `Run`, and `VisualTreeHelper.GetParent` throws on a non-Visual). Fix in `DaisysApp.Core/Shared/Widgets/Widget.cs`:
  walk with `p is Visual or Visual3D ? VisualTreeHelper.GetParent : LogicalTreeHelper.GetParent` (as FileSync's
  `IsInside`). Checked the other `VisualTreeHelper.GetParent` walks: none start from text. Core API unchanged (0.43).
  Released (tag v3.24.1, Release run passed, 20 assets). Build only; not tried in DaisysDev. Also in that zip, not
  acted on: an Application Hang at 18:29 (run of 18:27, nothing logged before it) and Mini Mirror
  `DuplicateOutput` E_INVALIDARG on DISPLAY11/12 (10-27). Next: the user to try a widget's edit mode over text; look at
  the Mini Mirror errors if they recur.

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
  `ControllerButtons.JoystickApi` lock. Still seen after those (3.12.x). **Not found yet; next step:** a full crash
  dump on the rig (WER LocalDumps, maybe page heap). This PC has no controller, so stress tests here prove nothing.
- FFB-loss report (3.5 era): no code regression found; pointed at the USB dropout, the Xbox pad and Button2Key's keys
  (AutoSwitch turns it back on with Forza).
- Tools: logs come as the app's export zip (Tools → Logs); a read-only Claude session on the rig can read its logs.

## Lessons

- Posted WM_LBUTTONDOWN/UP don't reach WPF's mouse handling; a real click needs `SendInput` (only when
  WindowFromPoint is the target).
- Dialogs opened by a UIA call draw white; a harness's second `ShowDialog` needs an explicit `ShutdownMode`.
- Tree walks from a hit test or `OriginalSource` must handle `Run`/ContentElements (use the logical parent).
