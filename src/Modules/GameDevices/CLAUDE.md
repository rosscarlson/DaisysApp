# Session notes: Devices (GameDevices)

Read only when working in this folder. Keep it short.

**Where things stand (2026-11-06):** 4.6.7 released. Waiting on the user (rig): 4.6.7's Calibration (Find Ranges,
Apply) on the SimJack pedals and the PXN-HB (4.6.2's axis fix never tried on a real device); 4.6.2/4.6.0's Device Widget with real
devices (fading, handbrake lever, axis-named tiles), Apply Order with a real veto (3.37.x), and what real devices show
from 3.35.x (type guesses, default roles, 1.5% / 5 s dot limits, Properties picking the device). If Apply Order fails
again, read `Reorder:` and `SetConfig` lines in the rig's `GameDevices-<date>.log`; drop whichever way never works.

**4.6.7 (asked 2026-11-06: no Wizard button, the wizard on the main page; click an axis above to calibrate it below;
green Apply / red Cancel (✕ confirm) where Wizard was; then move all of Calibration into Devices under the device's
visual, remove the module):** `Calibration/` folder here (namespace `DaisysApp.Applets.GameDevices`): `Cal.cs`,
`Visuals.cs`, `CalibrationSettings.cs` (same `Calibration.json`, `Selected` dropped), `Calibrator.cs` (was
`Controllers.cs`; its own `ControllerInfo` dropped for Controllers', `FakeController` now any FakeDevices device, raw
following FakeDevices' sine, cals in a static dict), new `CalibrationPanel.cs` (persistent element re-added by
`ShowDevice`; pending/saved by DIJOYSTATE2 place = Offset/4 = Devices' axis index; `Apply`/`Cancel` buttons detached from
the old header row before re-adding; opened in `Tick` while showing, `Paused` during Apply Order, `failed` reset on
relist). Axis rows are FlatRow Buttons (UIA "Calibrate X Axis"; drop-down/Invert still work inside). AxisWindow and
WizardWindow deleted; `GameControllers.Calibrate` removed; installer deletes `modules\Calibration`. Decisions (mine):
Find Ranges a chip, off by default (on, any axis moved 10% pends); hand-set axes skip it until Reset; picking another
device drops changes; Cancel = `ConfirmButton.ArmIcon` (Apply one click). Tested: DaisysDev fakes by UIA
(`%TEMP%\cal467\t.ps1 -Steps 'open;press=Test Pedals;…'`, `bottom=100` scrolls, PrintWindow shots): Find Ranges, Apply
(log + Original), Cancel ✕, Here. No real device.

**Calibration's history (the module, 3.10.0–4.6.6; its notes folded here):** DIPROP_CALIBRATION (11) set on an
unacquired device persists in HKCU `…\DirectInput\VID_x&PID_y\Calibration\<n>\Type\Axes\<axis>` value `Calibration`
(12 bytes), no admin; Vortice keeps Get/SetProperty internal, so vtable 5 Get, 6 Set, 9 GetDeviceState (272-byte
DIJOYSTATE2); raw from a second instance with CALIBRATIONMODE raw. 3.10.2: an object's `Offset` is native, not
DIJOYSTATE2's (SimJack's Rx never moved). 4.6.2: Vortice 3.8.3's `GetObjectInfoByOffset` always throws (dwSize 0), so
no axes were found; axes are now placed by type (`GameControllers.AxisIndex`). If the rig's axes are still wrong, read
the `Opened …` line (now in `GameDevices-<date>.log`).

**4.6.2 (asked 2026-11-06: fade 2.5 s; tiles named by axis id, not role; handbrake: the tab's visual small, never
fading, axis named):** `DeviceActivity`: `Hold` 2500; `Axis` labels `K.AxisName` (role only picks the colour);
`Main.Handbrake` (100 wide; a button handbrake: any button held = pulled; `down` count in the redraw hash). Same
release fixed Calibration (its notes). Tested: DaisysDev fakes (`%TEMP%/devs462/uia.ps1 -List 'A;B'` makes widgets,
`shot.ps1` presses Done and PrintWindows each). DaisysDev's own restart into `--software` loses the fakes: start it
with `--software` and `DAISYSAPP_FAKE_CONTROLLER=1`.

**4.6.0 (Device Widget):** `DeviceActivity` (second widget per device, `KnownDevice.Activity`, hub's `activities`,
`MakeActivity`); button left of Calibrate (glyph E7FC). Decisions (mine): main control per type (wheel; stick for
Joystick/Yoke; lever for Throttle; HOTAS both; handbrake since 4.6.2; others none) and its axes get no tile; tiles
fade after their last change, held or not; buttons one tile, a press with none held starts it again; hat a tile.

**4.2.4:** `Compare` ranks DirectInput order among devices in both lists (an Xbox pad coming and going moved every
device after it); `order:` alert ids; `KnownDevice.Alert` (Alert on Change): off = no own alerts, left out of ranks.

**4.0.0:** Core's `DeviceChanges` replaces `HotPlug.cs`; `JoyOrder` uses Core's `Hotkeys/Joystick`.

**Apply Order (3.36.2–3.37.3):** SetConfig (`IDirectInputJoyConfig8`, un-elevated) then `ReorderTask` (elevated via
`--module-task`: locate all, CM_Disable_DevNode, write "Joystick Id", enable ascending); success only by reading numbers
back, then SetNormal. `GameControllers.LetGo()` releases every reader (and `ControllerButtons.Paused`) during it; a
veto's program comes from Kernel-PnP 225 events; **Force** kills those (not system names, not DaisysApp*), up to 3
rounds, and lists them. Drag a block to renumber (own mouse capture; 3.36.7: the host takes only its own capture, or
LostMouseCapture cancels mid-drag). Never tested with a real veto.

**3.35.x (the module):** folder/id `GameDevices`, GroupOrder 45 (48 later, after Binder's 47). Number = winmm id
checked against VID:PID, DI's `DIPROP_JOYSTICKID` (`Properties.JoystickId`); order alerts stick until Set as Normal;
Missing/New clear by themselves. Pictures go the way the control does (`Device.Shown`, `DeviceKinds.Reversed`); Invert
and roles are display only. Properties: joy.cpl's dialog (list 1020, Properties 1002, `#32770` "Game Controllers");
`gcdef.dll` in-process hosting rejected. Calibrate: `GameControllers.Calibrate`.

**Working here:** a Controllers change: refresh DaisysDev from the solution's bin (Core too), or copy
`DaisysApp.Controllers.dll` into every module folder. Local builds: `-p:CustomAfterMicrosoftCommonTargets=<file with
Compile Remove="**/*-GAMER11*.cs">`. Write files with Write (Bash heredocs collapse `\\`). `git commit -- <names>`.
UIA can't drag or double-click; PostMessage'd mouse input doesn't reach WPF.
