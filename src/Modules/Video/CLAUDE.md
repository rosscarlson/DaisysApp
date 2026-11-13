# Session notes: Video

Past work on this part of Daisy's App (Claude Code reads this file only when it works in this folder). Entries touching several modules are in each one's notes. All released unless said; git and `CHANGELOG.md` keep the details. Keep it short: condense what's done.

## Latest

- **4.16.0:** the quality profiles moved to Core (`Shared/Video/VideoProfiles`, `VideoProfiles.json`, Core 0.64) for
  Plex → Scanner's transcodes; `GamingSettings.Profiles` is now `VideoProfiles.All` (JsonIgnore), `Load` touches it
  first so Gaming.json's old list is read before it's saved without it; `Save` saves the profiles too; 1080p and
  720p added (also to a migrated list). Tested: migration in a test copy (High/Medium/Low + 1080p/720p). Not
  tested: editing a profile in Settings, recording after the move.
- **3.37.16:** Recording's shortcuts button is Core's (`WebcamShortcutsButton`, badge checkbox via `extra`; its list
  skips bindings not added yet); Settings' `ShortcutWarning`/`FailedHotkeys` gone; camera hide matched by
  `ControllerButtons.Same`; Mini Mirror's `SelectionKeys` on Core's `HotkeyManager` (not tried after the change).
- **3.37.15 (asked 2026-11-03: Mini Mirror's mirror/group shortcuts didn't use the Core button; controller buttons kept by number, not device ID; released, Release run passed):** confirmed both. View: the inline `ShortcutBox` cards (not bound to a `Hotkey`, own warnings) gone; `HotkeyWindow.Button` inserted first in `MirrorButtons`/`GroupButtons` and after the chip in `OnOffRow`; service `MirrorHotkey(d)`/`GroupHotkey(g)`/`NewMirrorHotkey` (`Hotkeys.Find`), `FailedShortcuts` gone; Settings' New mirror box bound to its `Hotkey`; list rows and tooltips use `ControllerButtons.Describe`. The ID part is Core's (`src/DaisysApp/CLAUDE.md`). Tested in DaisysDev via UIA (fixture mirror "Controller 1 Button 5" + group, restored): buttons in accent, the window shows the binding. **Next:** the user tries it on the rig with a real controller (`docs/UNTESTED.md`).
- **3.37.10:** a region recording asks for the region every time (`GamingService.StartRecording(source, picked)` → `RegionSelector.Pick`, `pickingRegion`; `SelectorWindow.Closed` → `finish(null)`). Not tested: dragging a region then recording.
- **3.37.0:** Record card icon buttons; Record Monitor/Region/App shortcuts (`Gaming.RecordMonitor/…` → `ToggleRecording(CaptureSource?)`); webcam shortcuts in Core's `HotkeyWindow` (6 rows, bubble checkbox in `Extra`); a region's recording opens its folder on stop. Not tested: recording by the new shortcuts.

## Mini Mirror (condensed)

- **On / off (3.12.2):** `MiniMirrorData.Enabled`/`OnOffShortcut`, `SetEnabled` (off closes every `MirrorWindow`, other bindings `Enabled = false` but listed), `OnOffHotkey` = "MiniMirror.OnOff" (also in `RegisterHotkeys`' Replace list, same handle). Key test fixture: `%TEMP%\mmtest\key.py` presses Ctrl+Alt+Shift+F19/F20.
- **Process Watcher (3.18.4, 3.18.6):** `MirrorDefinition.Programs`/`MirrorGroup.Programs`, `MiniMirrorData.OnByProgram` (saved); `programTimer` 2 s → `CheckPrograms`; by hand clears `OnByProgram`. Card: `FillPrograms`, Add / Select App, `SetPrograms(list, text, owner)`. Not tested: the hand override, a group's list.
- **Capture on a friend's PC (DESKTOP-QTNJDH2; 2.6.x, 3.18.5, 3.24.4):** `MonitorCapture.CheckStall` GDI probe, 8-bit then GDI fallback, `CaptureDiagnostics`; on E_ACCESSDENIED (secure desktop) the D3D11 device is kept (`deviceLuid`), `NotNow` only retries the duplication, `retryMs` 0.5 → 5 s; one Info line per refusal spell. Not tested: the refusal path itself. 3.18.5's lock/unlock is for that PC to try.

## Other (condensed)

- **3.7.0:** `GamingSettings.WebcamOn` → the webcam restarts at start without sound; the relay's source defaults to `AudioRelay.MainPlayback()`. Not tested: the restart (would take the user's camera).
- **3.8.x Fire TV (removed in 3.8.6; in git at v3.8.0–v3.8.5).** Lessons: Fire OS 8.1.8.5 re-enables packages at boot; `disable-user` mostly holds; the launcher's ads can't be replaced; Private DNS cut the TV off.
