# Gaming: Devices

*A module of [Daisy's App](../../../README.md): the **Devices** sub-tab of the [Gaming](../Gaming/README.md) tab (on a
tab of its own when it's the only Gaming module installed). Switch it on or off in Settings → General → Modules. Its
code is in this folder; it's installed to `modules\GameDevices\`. New in 3.35.0.*

Windows' Game Controllers page (joy.cpl), done better: every game controller it lists, vJoy's too, what number each
one is, a warning when that changes, a live picture of what each one is doing, and its axes calibrated in Windows
([below](#calibration); the Calibration module until 4.6.7).

## The tab

- **Devices** (left): a block per device, in number order. A **green dot** while it has input, whichever device is
  showing on the right: an axis that moved, a button or hat that changed (a button held keeps it lit for 5 seconds, so a
  switch left on doesn't light it for ever; jitter under 1.5% of an axis doesn't count). The **number** is blue for the
  first four. **Drag a block** to change its number (below). **Double-click the name** to rename it (Enter keeps it, Esc doesn't; empty goes back to Windows' name).
  **Refresh** (top right) looks for devices again. A device in the normal order but not plugged in stays, greyed.
- **Details** (the arrow on a block): the **type** at the top right, the pencil to rename, **Alert on Change**, then its number, DirectInput
  order, what's normal, Windows' name, VID:PID, whether the joystick API has it, axes, buttons, hats and force feedback.
- **The device picked** (right): its picture, its axes, each with a bar, its **role** (what it does in the picture) and
  **Invert** (the bar and the picture the other way round), its buttons, which light when pressed, and under them its
  [Calibration](#calibration) (click an axis to calibrate it). At the top right: **Cancel** and **Apply** while the
  calibration has changes, **Device Widget** (below), **Make Widget** (also on a right-click of the picture) and
  **Properties**.

Names, types, roles and Invert are only how the tab shows a device: they stay in this module's settings, and games and
Windows see the device as they did. Only the calibration's Apply and Apply Order change what Windows reports.

## Numbers and order

Older games take the first few devices Windows lists and don't let you choose, so which device is number 1 to 4
matters. Windows tracks this twice, and both are watched:

- **Number**: the joystick number in Windows' classic joystick API (16 at most), and DirectInput's `DIPROP_JOYSTICKID`.
  If the joystick API left a device out (it can: a HOTAS Warthog's throttle), Details says so: older games can't see it.
- **DirectInput order**: its place in DirectInput's list, which is what Game Controllers shows.

The first time, what's plugged in is **normal**. The check runs when the app starts, on Refresh and when Windows says a
device came or went (a HID device, 2.5 seconds after things settle), not on a timer, because listing opens every device
and a running game feels it. It needs neither the tab nor the window open.

| What differs | Alert | Goes when |
|---|---|---|
| A number or the order changed | Red, on the device, on top of the tab and in the title bar (**Devices: order changed**) | **Set as Normal**, and only then: it stays even if the order goes back |
| A device that's normal isn't plugged in | Amber bubble, **Devices: check** | It's back, or Set as Normal |
| A device that isn't in the normal order | Amber bubble | It's unplugged, or Set as Normal |

DirectInput's order is compared among the devices that are in both lists, so one coming or going doesn't move the
others (since 4.2.4). **Alert on Change** (in a device's details, on by default): off, the device has none of these
alerts and doesn't count in the others' order, for one that comes and goes, such as an Xbox pad. The others still warn
if their numbers change.

### Changing the numbers

**Drag a device** up or down the list: a line shows where it lands, the ones between close up, and each takes the number
of its new place. Numbers that changed show a red edge. Nothing happens until **Apply Order** (**Cancel** forgets it).
Only the numbers devices plugged in have are used.

Windows has no sure way to do this, so the app tries two and then reads the numbers back; only what Windows reports
counts:

1. DirectInput is asked (`IDirectInputJoyConfig8`). No administrator rights.
2. If the numbers didn't change, Windows asks for administrator rights, the devices that have to move are switched off,
   the number Windows keeps for each is set, and they're switched on lowest first, as plugging them in one by one
   would. They drop out for a few seconds, so games shouldn't be using them. One left off comes back with a replug or
   a restart. Windows won't switch off a device a program has open, so the whole app lets go of every controller
   first; if another program has one, the tab names the device and the program (the details are in the Devices log).
   With **Force** ticked, those programs are closed and it's tried again (up to three times; Windows' own processes
   and Daisy's App are left alone), and the tab lists what it closed, to start again if needed.

When every number is right that's the new normal, and the alerts go. Otherwise the tab says so and the alert stays.
Windows can still renumber devices later: the alert shows, and this puts them back.

**Set as Normal** makes what's plugged in now, in this order, the normal one. A notification shows when a new alert
appears, and each one goes in the log with the whole order. Click the bubble to come here.

A device moved to another USB port gets a new instance from Windows: one with the VID and PID of a device not plugged
in is taken for that device, so it keeps its name, type and place. Two alike that both move can swap.

## Types and pictures

The type is guessed from the name, then what Windows says it is, then what it has. **Auto** in the drop-down goes back
to that guess.

Flight pedals (rudder and toe brakes) · Racing pedals · Throttle · Joystick · Stick and throttle (HOTAS) · Yoke · Wheel
base · Wheel, pedals and H-shifter (all in one) · H-pattern shifter · Sequential shifter · Handbrake (an axis, or any
button) · Xbox controller · Gamepad · Button box · Other (a bar per axis, its hats).

Roles start on the axes they're usually on, then the next ones free. A pedal set's order differs from maker to maker:
set them with each axis's drop-down, and Invert a pedal that rests at 100%. Shifters take a **first gear button**:
gears are that button and the ones after it, R seventh and 7 eighth (Logitech's order); a sequential shifter's are Up
and the next one. An Xbox pad through DirectInput has both triggers on one axis, 50% at rest.

The pictures and bars show what the control does, not the number sent: a pedal rises from the bottom as it's pressed, a
throttle lever is at the top at full throttle. Devices send a throttle and a stick's up / down lowest at the top, so
those axes' bars and percentages are the reading turned over (by role: `DeviceKinds.Reversed`). One that sends it the
other way takes Invert.

## Widget

The picture, in Core's widget: right-click it for its strip, the trash can deletes it. Devices are read while a widget
shows, as they are while the tab does.

## Device Widget

New in 4.6.0: the controller icon at the top right makes a small widget (Core's, one per device) of what the device is
doing. On the left, its main control, always: a wheel's wheel (with its steering in %), a stick's stick (a yoke's too),
a throttle's lever, a HOTAS's stick and lever, a handbrake's lever as the tab draws it (its % and its axis; since
4.6.2); pedals and the other types have none. Then, side by side in the order they came, a tile for each other axis
that moved (its value, a bar, and which axis it is: X Axis, Z Rotation…, since 4.6.2; throttle green, brake red, clutch
blue, by role), the buttons pressed last (their numbers; several held together share a tile) and the hat (the way it
was pushed). Each tile fades out 2.5 seconds after it last changed (5 until 4.6.2), so a pedal held still goes too. A
move under 1.5% doesn't count, as with the green dot; the first reading after it opens isn't a move.

## Calibration

The min, centre and max of each axis, as Heusinkveld's **DIView** (and Windows' own Game Controllers page) calibrates
them: written to Windows, so every game reading the device through DirectInput (or the classic joystick API) gets the
calibrated axis, after restarts too, with Daisy's App closed. Made for SimJack Pro pedals; any controller with an axis
works (vJoy's own aren't calibrated). Until 4.6.7 this was the Gaming → Calibration module, with a window per axis and a
wizard; now it's one block under the device's axes, and the wizard is Find Ranges.

- **The axis**: click one in the axes above (it's highlighted; an axis being moved has its name in the accent colour, so
  you can see which pedal is which; one with a change not applied yet, green). Beside its name, what Windows has
  (*min · centre · max*, or *Not calibrated*), and → the change.
  - **Raw**: what the device sends (its whole range, end to end), the line where it is now, and ticks at the calibration
    (the change's, or Windows'); the number is the raw reading. **Output**: what games get now (0 to 100%).
  - **The graph**: raw readings across, what games get up. The line is flat up to **min** (0%), rises to 50% at the
    **centre** and 100% at **max**, and stays flat after; the dashed line is Windows' default (the whole range,
    straight). The dot is the axis now, with what it would give.
  - **Min / Centre / Max**: a slider over the whole range and a number each; **Here** takes where the axis is now;
    **Midpoint** puts the centre halfway, a straight line (a centre off the middle bends it: softer at one end, sharper
    at the other).
  - **Reset** goes back to what Windows has (and finds its range again), **Default** fills in Windows' own
    (uncalibrated), **Original** what the axis had before Daisy's App first changed it (shown once it has). **Leave out
    of Find Ranges** (the wizard's Ignore until 4.6.7).
- **Find Ranges** (the block's top right): move each axis all the way, both ways. Its lowest and highest readings since
  it was switched on are its min and max (shaded on the bar and the graph), with the centre halfway; the light says
  **Move it** (amber) until it has moved through a tenth of its range, then **Range found** (green), and the change is
  made. An axis set by hand is left alone (**Set by hand**) until its Reset. For pedals, rest your foot off the pedal and
  press it to the floor (a load cell: as hard as you'd brake); one that rests a little above its lowest reading gets
  that as its min, so it reads 0% at rest.
- **Apply** (green, at the top beside the device's name, while there are changes) gives Windows every axis changed and
  switches Find Ranges off; **Cancel** (red: click it, then its ✕) drops them. Picking another device drops them too.
  Min, centre and max must go up in that order.

The device is open for this only while the tab shows, and is let go during Apply Order.

**Where Windows keeps it:** DirectInput's calibration (`DIPROP_CALIBRATION`, as joy.cpl and DIView set it), per axis, in
`HKEY_CURRENT_USER\System\CurrentControlSet\Control\MediaProperties\PrivateProperties\DirectInput\VID_xxxx&PID_xxxx\Calibration\<n>\Type\Axes\<axis>`,
value `Calibration` (12 bytes: min, centre, max). It's per Windows user and per VID/PID: two identical devices share it.
No administrator rights are needed. Programs that read the device's raw HID reports (or XInput) don't use it, and some
games read it only when they start. Calibration made in the device's own software (SimJack's, Fanatec's) is separate
and comes first: Windows' calibration is applied to what the device sends.

## Properties

The gear opens the page Windows' Game Controllers would (the maker's own where it has one). Windows has no way in but
that window, so it's started unseen, the device picked in its list, Properties pressed for you, and the window closed
when the page is. If the device isn't in its list the window shows as it is. Its calibration writes the same place as
[Calibration](#calibration) here.

## Files

Settings: `%APPDATA%\DaisysApp\GameDevices.json` (names, types, roles, widgets and Device Widgets, what's normal, the
alerts) and `Calibration.json` (the axes left out of Find Ranges, and each axis's calibration before Daisy's App first
changed it: the Calibration module's file, kept as it was). Log: `GameDevices-<date>.log`, with each calibration
applied and what it was before (`Calibration-<date>.log` until 4.6.7).

To try the tab with nothing plugged in, start the app with the environment variable `DAISYSAPP_FAKE_CONTROLLER=1`:
one pretend device of each type, moving by themselves (calibrated only in memory).

## Code

| File | |
|---|---|
| `GameDevicesApplet.cs` | The module: its sub-tab on the Gaming tab (described in Core's `Shell/TabGroups.cs`), and a hidden window for DirectInput and Windows' device messages |
| `JoyOrder.cs` | Changing the numbers: `JoyOrder` (DirectInput's way), `ReorderTask` (as administrator, off and on in order) |
| `DevicesHub.cs` | `Device`, `DevicesHub`: the list (Core's `GameControllers.ListAll` in `src/DaisysApp.Controllers`, with winmm's 16 numbers), a device matched to what's kept (`KnownFor`), numbers (`Numbers`), alerts (`Compare`), Set as Normal, the bubble, widgets, the read timer |
| `LiveDevices.cs` | Reading every device on a thread of its own; the green dot |
| `DeviceKinds.cs` | The types, their roles, the guess, which axis does what (`Assign`) |
| `DeviceActivity.cs` | The Device Widget's body: the main control (wheel, stick, lever, handbrake), and a tile per axis, buttons and hat as they're used, each fading out |
| `DeviceArt.cs` | The pictures: one element drawn in a 480 × 280 space, for the tab and a widget |
| `DevicesView.cs` | The tab |
| `JoyProperties.cs` | Properties, through joy.cpl (control ids 1020 and 1002 in its dialog, its list through UI Automation) |
| `DevicesSettings.cs` | `GameDevices.json` |
| `FakeDevices.cs` | The pretend devices |
| `Calibration/CalibrationPanel.cs` | The Calibration block: the axis picked (bars, graph, min / centre / max), Find Ranges, the changes, Apply and Cancel; read on the tab's tick |
| `Calibration/Calibrator.cs` | `Calibrator.Open`, `Axis`, `IController`, `DiController` (each controller open twice: in raw mode (`DIPROP_CALIBRATIONMODE`) and as games see it; `DIPROP_LOGICALRANGE` and `DIPROP_CALIBRATION` got and set through the COM vtable, as Vortice keeps them internal; the state read into a DIJOYSTATE2 buffer, each axis given its place there by its type (`GameControllers.AxisIndex`): an object's own `Offset` is the device's native one, and Vortice's `GetObjectInfoByOffset` always fails), `FakeController` (a pretend device's axes) |
| `Calibration/Cal.cs` | `Cal` (min, centre, max; `Cook`: what DirectInput gives for a raw reading; `Straight`), `RangeFinder` (Find Ranges' lowest and highest) |
| `Calibration/Visuals.cs` | `AxisBar` (the bars), `CalGraph` (the graph) |
| `Calibration/CalibrationSettings.cs` | `Calibration.json` |
