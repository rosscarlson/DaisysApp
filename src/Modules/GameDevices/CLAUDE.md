# Session notes: Devices (GameDevices)

Read only when working in this folder. Keep it short.

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
