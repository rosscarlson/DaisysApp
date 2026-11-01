# Session notes: Devices (GameDevices)

Read only when working in this folder. Keep it short.

**Where things stand (2026-11-01):** 3.36.5 released. Apply Order has never moved a real number: PLEX has no controller. **Next:** the user tries Apply Order on the rig (`docs/UNTESTED.md` 3.36.5); read the `SetConfig` lines in `GameDevices-<date>.log`, and drop whichever way doesn't work. Then what real devices show from 3.35.x: type guesses, default roles, the 1.5% / 5 s dot limits, Properties picking the device, Calibrate landing on the right device (Calibration's fake list has one entry).

- **3.35.1–2 (from the rig):** pictures and bars go the way the control does (`Device.Shown`, `DeviceKinds.Reversed`, by role); Invert flips bar and picture; all display only. Calibrate: `GameControllers.Calibrate` (set by Calibration's view; null: off).
- **Working here:** a Controllers change: copy its DLL into every module folder of DaisysDev (6), by a Unix path (`cygpath -u`). The Bash tool's heredocs collapse `\\`: write files with Write. `git commit -- <names>`: the shared index can hold another session's stale staging.
- **3.36.2 (asked 2026-11-01: swap numbers, e.g. 2 and 5, with a confirm; explore first, say "yes, coding now" aloud if likely, admin is fine):**
  - **Found:** on PLEX (Win 11 26200) `IDirectInputJoyConfig8` QIs and acquires un-elevated, `GetConfig` takes dwSize 1176. `dinput8.dll` holds the string "Joystick Id": a REG_BINARY in HKCU `...PrivateProperties\DirectInput\VID_x&PID_x\Calibration\N`, next to a GUID value (assumed the instance GUID; no device to confirm). Being in dinput8, numbers look given out by a client, so the task calls `Look()` after each enable. Docs: SetConfig takes no DIJC_GUIDINSTANCE and don't say it moves ids. One forum post (IL-2, jollyjack): JoyID.exe stopped working "since some windows 10 update"; numbers follow plug order; several say they change on reboot. No source for disable/enable doing it.
  - **Decisions (mine):** both ways, SetConfig then `ReorderTask` (elevated, CM_Disable_DevNode not persistent, polite then absolute, Keep writes Joystick Id, enable ascending); success only by reading numbers back; success = SetNormal. 3.36.2's number menu: the user hated it; 3.36.5 drags the block (`DragMoved`, `DragEnded`, `MoveTo`; own mouse capture, not OLE drag and drop; the others close up, the sorted numbers go down the new list; a line shows where). Only numbers in use. Devices' GroupOrder is 48, after Binder's 47 (the other session had `BinderApplet.cs` open). Task has no `Controllers` use so it needs no assembly resolving.
  - **Tested:** fakes in DaisysDev: drag down and up through the real methods at real positions (a temporary button, removed; UIA can't drag, PostMessage'd mouse input doesn't reach WPF here, and real input would take the user's cursor), line, red edge, Apply Order, saved normal; **a physical mouse press never**; `JoyOrder.Assign`/`Look`/`InstanceId` on real Windows calls with no device (harness); `ReorderTask` through `--module-task` (unknown device: exit 2). **Not tested, PLEX has no controller and nothing was run elevated:** whether either way moves a real number, the disable/enable, a real SetConfig, whether it lasts a reboot. If SetConfig harms or never works, drop it; see `docs/UNTESTED.md`.
- **3.35.0 (asked 2026-11-01: new Gaming sub-module Devices, a much better joy.cpl):** left blocks that expand, numbers
  tracked with a sticky alert until "the new normal", right a picture per type (type drop-down, auto-detected), green
  dot on any device's input, editable names, picture as a Core widget, Properties icon opening Windows' page.
  - **Decisions (mine):** folder/id `GameDevices` (Hardware has a Devices sub-tab too), tab title Devices, GroupOrder 45.
    The type drop-down is in the left block's Details (top right). Number = winmm id checked against VID:PID, DI's
    `DIPROP_JOYSTICKID` first; DirectInput order watched too, only reported if the number didn't change. Order alerts
    stick even if the order goes back; Missing and New clear by themselves (a wheel switched off isn't an order change).
    Added types: HOTAS, Yoke, H-pattern, Sequential, Gamepad, Button box, Other. Roles and Invert per axis (only the
    picture). Changing the order (Advanced's Preferred device) not done: not asked for.
  - **Found:** `Properties.JoystickId` (Vortice) is DIPROP_JOYSTICKID. joy.cpl's dialog: list 1020, Properties 1002, `#32770`
    title Game Controllers, in `rundll32 shell32.dll,Control_RunDLL joy.cpl`. `gcdef.dll` is the default sheet
    (CLSID {92187326-72B4-11d0-A1AC-0000F8026977}, IDIGameCntrlPropSheet): hosting it in-process was rejected, an
    undocumented COM layout with no device here to test it against. `IDirectInputDevice8::RunControlPanel` not tried
    (no device). The Bash tool collapses `\\` in heredocs: write files with Write.
  - **Local build:** `AnalysisView-GAMER11.cs` breaks SimEffects; `dotnet build DaisysApp.sln
    -p:CustomAfterMicrosoftCommonTargets=<file with Compile Remove="**/*-GAMER11*.cs">`. The rig's copy isn't affected.
  - **Tested:** DaisysDev + `DAISYSAPP_FAKE_CONTROLLER=1` (13 fakes), by UIA: every picture, details, alerts (seeded
    `GameDevices.json`: red, amber, bubble, Set as Normal), rename, type, role, invert, widget make and delete, Properties'
    fallback (window shown at alpha 255). `%TEMP%\devs335\`: `run.ps1`, `seed.py`, `uia.ps1`, `combo.ps1`, `menu.ps1`,
    `b.sh`. DaisysDev restarts itself ~20 s in here (BlankWindowCheck, `--software`).
  - **Not tested (PLEX has no game controller, DirectInput lists none):** any real device, reading, the green dot's
    thresholds, `JoystickId` and numbers, a real plug or unplug, Properties selecting a device. Double-click rename
    (UIA can't; the pencil path was tested). See `docs/UNTESTED.md`.
