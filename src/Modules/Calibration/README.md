# Gaming: Calibration

*A module of [Daisy's App](../../../README.md): the **Calibration** sub-tab of the [Gaming](../Gaming/README.md) tab
(on a tab of its own when it's the only Gaming module installed). Switch it on or off in Settings → General → Modules.
Its code is in this folder; it's installed to `modules\Calibration\`. New in 3.10.0.*

The min, centre and max of each axis of your pedals, wheel, sticks and handbrake, as Heusinkveld's **DIView** (and
Windows' own Game Controllers page) calibrates them: written to Windows, so every game reading the device through
DirectInput (or the classic joystick API) gets the calibrated axis, after restarts too, with Daisy's App closed. Made
for SimJack Pro pedals; any controller with an axis works.

## The tab

- **Controllers** (left): every game controller plugged in that has an axis, with its VID:PID and number of axes
  (vJoy's own devices are left out). Its axes are the eight a game controller has in DirectInput (X, Y, Z, X / Y / Z
Rotation and two sliders); an axis Windows can't name as one of those isn't listed. The list follows plugging in and out (every 3 seconds while the tab shows).
- **The controller picked** (right): each axis with two bars:
  - **Raw**: what the device sends (its whole range, end to end), the line where it is now, and ticks at the
    calibration Windows has for it (min, centre, max); the number is the raw reading.
  - **Output**: what games get now, after Windows' calibration (0 to 100%).

  Under the axis's name: its calibration (*min · centre · max*), or *Not calibrated*, and *Ignored* when the wizard
  leaves it out. **Click an axis** to calibrate it by hand; **Wizard** goes through them all.

Nothing is read while the tab isn't showing.

## Calibrating an axis (click it)

A small window with a graph of what the calibration does: raw readings across (the axis's whole range), what games
get up. The line is flat up to **min** (0%), rises to 50% at the **centre** and 100% at **max**, and stays flat after;
the dashed line is Windows' default (the whole range, straight). The dot is the axis now, with its raw reading and
the output it would give.

- **Min / Centre / Max**: a slider over the whole range and a number for each; **Here** takes where the axis is now.
- **Midpoint** puts the centre halfway between min and max: a straight line. A centre off the middle bends it (two
  straight halves), softer at one end and sharper at the other.
- **Default** fills in Windows' own (uncalibrated); **Original** what the axis had before Daisy's App first changed it
  (shown once it has).
- **Ignore in Wizard**: the wizard leaves this axis out (or brings it back).
- **Save** gives it to Windows (*✓ Saved*; the window stays open to carry on); **Close** closes it, unsaved changes
  dropped. Min, centre and max must go up in that order.

## The wizard

Each axis in turn (the ones not ignored), with a numbered dot per axis at the top: green the one showing, blue an
axis being moved (so you can see which pedal is which), a green ring found, struck through ignored, hollow skipped;
the last (✓) is the summary. **Click a dot** to go straight to that axis (since 3.10.2).

1. **Move it all the way, both ways.** The lowest and highest readings since the step started are its min and max
   (shaded on its bar and the graph), and the centre is worked out halfway between them, for a straight line. The
   light says **Move it** (amber) until it has moved through at least a tenth of its range, then **Range found**
   (green).
2. **Reset** starts the axis again (a bad reading, say); **Next** takes what was found; **Skip** leaves the axis as it
   is this time; **Ignore** leaves it out of the wizard from now on (its window's **Ignore in Wizard** brings it back);
   **Back** goes to the axis before.
3. **The summary** lists each axis: its calibration now → the new one, or Skipped / Ignored. **Save** gives Windows the
   ones found; **Close** leaves everything as it was.

For pedals, rest your foot off the pedal and press it to the floor (a load cell: as hard as you'd brake). A pedal that
rests a little above its lowest reading gets that as its min, so it reads 0% at rest.

## Where Windows keeps it

DirectInput's calibration (`DIPROP_CALIBRATION`, as joy.cpl and DIView set it), per axis, in
`HKEY_CURRENT_USER\System\CurrentControlSet\Control\MediaProperties\PrivateProperties\DirectInput\VID_xxxx&PID_xxxx\Calibration\<n>\Type\Axes\<axis>`,
value `Calibration` (12 bytes: min, centre, max). It's per Windows user and per VID/PID: two identical devices share it.
No administrator rights are needed. Programs that read the device's raw HID reports (or XInput) don't use it, and some
games read it only when they start. Calibration made in the device's own software (SimJack's, Fanatec's) is separate
and comes first: Windows' calibration is applied to what the device sends.

Settings: `%APPDATA%\DaisysApp\Calibration.json` (the controller picked, the axes ignored, each axis's calibration
before Daisy's App first changed it). Log: `Calibration-<date>.log` (each calibration saved, with what it was before).

To try the tab with nothing plugged in, start the app with the environment variable `DAISYSAPP_FAKE_CONTROLLER=1`: a
pretend *Test Pedals* with three 12-bit axes that move by themselves (calibrated only in memory).

## Code

| File | |
|---|---|
| `CalibrationApplet.cs` | The module: its sub-tab on the Gaming tab (`../Joy2Key/GamingTab.cs`, linked), and a hidden window DirectInput reads in the background for |
| `Controllers.cs` | `Controllers` (the list, opening one; `joyConfigChanged` tells joystick-API programs), `IController`, `DiController` (each controller open twice: in raw mode (`DIPROP_CALIBRATIONMODE`) and as games see it; `DIPROP_LOGICALRANGE`, `DIPROP_CALIBRATION` get and set through the COM vtable, as Vortice keeps them internal; the state read into a DIJOYSTATE2 buffer; each axis found by its place in it (`GetObjectInfoByOffset`), since an object's own `Offset` is the device's native one: 3.10.0–3.10.1 read there, so a SimJack's X Rotation never moved and only its Z, whose two places match, did), `FakeController` |
| `Cal.cs` | `Cal` (min, centre, max; `Cook`: what DirectInput gives for a raw reading; `Straight`), `RangeFinder` (the wizard's lowest / highest) |
| `CalibrationView.cs` | The tab, and `Live`: the controller picked, read about 30 times a second while the tab or a window of it shows |
| `AxisWindow.cs` | One axis's window |
| `WizardWindow.cs` | The wizard |
| `Visuals.cs` | `AxisBar` (the bars), `CalGraph` (the graph) |
| `CalibrationSettings.cs` | `Calibration.json` |
