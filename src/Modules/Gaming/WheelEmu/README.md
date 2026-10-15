# Gaming: WheelEmu

*A tool in [Daisy's App](../../../../README.md)'s [Gaming](../README.md) tab: its **WheelEmu** sub-tab. Switch Gaming on or off in Settings → General → Applets. Its code is in this folder, part of the Gaming module (installed to `modules\Gaming\`).*

Makes a wheel, pedals, an H-pattern shifter, a handbrake and any number of button boxes into **one game controller**
for Forza (or any game), the way [Forza EmuWheel](https://forzatools.weebly.com/forza-emuwheel.html) does: a
[vJoy](https://github.com/jshafer817/vJoy/releases) device the game sees, driven from the real devices. EmuWheel's
three parts are here in one place:

- **The emulator** (**Start**): reads the bound devices about a thousand times a second and drives the vJoy device:
  its axes from the steering and pedals, its buttons from everything bound to them, its hat from the d-pad. Devices
  unplugged while it runs are looked for every 2 seconds. **Start it when Daisy's App starts** starts it at launch,
  even with the window hidden in the tray; the tray menu has **Gaming → Wheel emulator running**.
- **Hush**: hides the real devices from the game, so it sees only the vJoy controller and binding in the game isn't
  confused by the same press arriving from two devices. It uses [HidHide](https://github.com/nefarius/HidHide/releases/latest)
  (install it first): Daisy's App adds itself to HidHide's allowed programs (so it can still read the devices), hides
  each ticked device, and switches hiding on. Every bound device is ticked to start with. **Unmute** shows them again;
  do that when you're done, as HidHide hides them from every program not on its list, not only Forza. Both ask
  Windows for permission (administrator). Xbox (XInput) pads can't be hidden this way, as with EmuWheel's Hush.
- **The binding tool** (the list on the right): what drives each control of the game's controller. **Set**, then press
  the real button or move the real axis all the way, on any device; **+** adds another control for the same button
  (any of them presses it: paddles and a button box both shifting, say); **✕** clears it. Each row shows its live
  value, so it's easy to see it works. The bindings are kept in `WheelEmu.json` and used as soon as they change;
  **Save to file…** and **Load from file…** keep or move them as a .json file of their own (EmuWheel's
  configuration.json), and **Clear all** starts again.

## What can be bound

Everything the vJoy device has, up to vJoy's limits (8 axes, 128 buttons, a hat):

| Group | Controls | On the vJoy device |
|---|---|---|
| Steering and pedals | Steering, Throttle, Brake, Clutch, Handbrake; or **Combined throttle / brake** for pedals that share one axis (up throttle, down brake; pressing both cancels out, as in EmuWheel) | Axes X, Y, Z, Rx, Ry (combined: Y and Z) |
| Driving | Shift up, Shift down, Handbrake (button), Clutch (button) | Buttons 1–4 |
| H-pattern shifter | Gear 1–10, Reverse | Buttons 5–15 |
| Controller buttons (menus) | A, B, X, Y, LB, RB, Start / Menu, Back / View, left and right stick clicks, Page up and Page down (the triggers in menus) | Buttons 16–27 |
| D-pad | Up, right, down, left | The hat (one direction at a time; a continuous hat gets diagonals) |
| Game actions | Horn, Look back, Change camera, Rewind, Telemetry, ANNA / navigation, Photo mode | Buttons 28–34 |
| Extra axes | Rz, slider 1, slider 2 | Axes Rz, Slider, Slider 2 |
| Extra buttons | Button 35–128, for anything else (folded away until opened) | Buttons 35–128 |

The names are a guide: Forza binds whatever it sees, in **Settings → Controls → Advanced** (a new custom layout),
where pressing the real control shows the vJoy one. A real control can be a button, a hat direction or an axis: an
axis bound to a button presses it past three quarters of its travel (a sequential shifter or paddle that's an axis),
and a button bound to an axis drives it full or nothing (a handbrake button as the handbrake axis). A control bound
to two things is marked (**Also bound to …**), as EmuWheel warns of duplicate bindings.

Each axis has **Invert** (for a pedal that reads full when released; **Set** ticks it when it sees one rest at the top)
and a **Deadzone**: ignored at the released end of a pedal, or around the centre of steering and the combined pedal.
EmuWheel's troubleshooting applies: a menu that moves on its own is an axis not at 0%; give it a deadzone, or invert it.

## The vJoy device

The **vJoy device** card checks the device (1 unless you pick another) has what Forza needs, as EmuWheel's setup
guide shows: **all 8 axes**, **128 buttons** (vJoy's own default device has 8, so most can't be bound), **one
4-direction POV hat and no continuous one** (the default has a continuous hat), and **force feedback switched on**
(Enable Effects). It also says if another program (EmuWheel itself, a feeder or Joystick Gremlin) is using the
device. **Set up vJoy device** runs vJoyConfig as administrator to set all of that (`vJoyConfig enable on`, then
`vJoyConfig 1 -f -a x y z rx ry rz sl0 sl1 -b 128 -p 1 -s 0 -e …`); if anything is still missing it says how to do it
in **Configure vJoy…**. Install vJoy 2.1.9.1 (the build for Windows 10 and 11) first: **Get vJoy**.

Not done yet: **force feedback** from the game back to the wheel (EmuWheel passes it through); with Hush on, Forza
can't reach the wheel itself, so the wheel has none.

## Using it with Forza

1. Install vJoy and HidHide; set the vJoy device up (the card turns all green).
2. Bind each control: **Set**, then press or move the real one.
3. **Mute** with Hush.
4. **Start** the emulator.
5. In Forza: Settings → Controls → Advanced, make a custom layout and bind each action by pressing the real control.
6. **Unmute** when you're done.

## Code

| File | What it is |
|---|---|
| `WheelEmuApplet.cs` | The tool: settings, the emulator and the tab; a hidden window DirectInput reads in the background for; the tray item |
| `WheelEmuView.cs` | The sub-tab: the emulator, vJoy, Hush and file cards, and a row per control with Set, +, ✕, invert, deadzone and its live value |
| `WheelEmulator.cs` | The emulator: reads the bound controllers, works out each control, writes changes to vJoy (axes, buttons, the hat); reads slowly for the live values while the tab shows |
| `WheelConfig.cs` | Every bindable control (`Roles`), a binding (`Binding`, `InputRef`), the bindings (`WheelConfig`, saved and loaded as JSON) and the settings (`WheelEmuSettings`: `WheelEmu.json`) |
| `GameControllers.cs` | The controllers through DirectInput (Vortice.DirectInput): 8 axes, 128 buttons and 4 hats each; vJoy's own devices left out |
| `Capture.cs` | **Set**: waits for the next button, hat or axis used on any controller |
| `VJoy.cs` | vJoyInterface.dll, loaded from where vJoy is installed; reading a device's setup and checking it; feeding it |
| `Hush.cs`, `Elevated.cs` | Hush through HidHide's command line; running commands as administrator with one permission prompt |
