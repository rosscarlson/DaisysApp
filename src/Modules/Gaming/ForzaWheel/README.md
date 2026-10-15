# Gaming: ForzaWheel

*A tool in [Daisy's App](../../../../README.md)'s [Gaming](../README.md) tab: its **ForzaWheel** sub-tab. Switch Gaming on or off in Settings → General → Applets. Its code is in this folder, part of the Gaming module (installed to `modules\Gaming\`).*

(Called WheelEmu before 0.58: its settings move from `WheelEmu.json` to `ForzaWheel.json` by themselves.) For
Forza Horizon only. Makes a wheel, pedals, an H-pattern shifter, a handbrake and any number of button boxes into
**one racing wheel** for Forza, the way [Forza EmuWheel](https://forzatools.weebly.com/forza-emuwheel.html) does: a
[vJoy](https://github.com/jshafer817/vJoy/releases) device the game sees, driven from the real devices. EmuWheel's
three parts are here in one place:

- **The emulator** (**Start**): reads the bound devices about a thousand times a second and drives the vJoy device:
  its axes from the steering and pedals, its buttons from everything bound to them, its hat from the d-pad. Devices
  unplugged while it runs are looked for every 2 seconds. **Start it when Daisy's App starts** starts it at launch,
  even with the window hidden in the tray; the tray menu has **Gaming → Wheel emulator running**.
- **Hush**, as EmuWheel's Hush.exe, with nothing to install: **Mute** mutes the ticked devices (the bound ones are
  ticked) **for Forza only**, so it sees just the vJoy wheel and binding isn't confused by the same press arriving
  from two devices. Each device gets `Disabled` = 1 under
  `HKLM\SYSTEM\CurrentControlSet\Control\GameInput\Devices\<VID><PID><usage page><usage>`, which Forza (reading
  controllers through Windows.Gaming.Input) honours; other games read them through DirectInput and still see them,
  as does Daisy's App. **Unmute** removes it. Each device shows whether it's muted. Both ask Windows for permission
  (administrator), and Mute also tells Windows the vJoy device is a racing wheel if it isn't yet (Hush imports
  EmuWheel's vjoy.reg each time it runs). Xbox (XInput) pads can't be muted this way, as with EmuWheel's Hush.
- **The binding tool** (on the right): what drives each control of the vJoy wheel. **Set**, then press the real button
  or move the real axis, on any device. An axis control (steering, pedals, handbrake) takes only an axis, and a button
  or d-pad control only a button or hat. A control already bound to something else is passed over. For an axis, Set
  ignores a device's first report (a Fanatec base reports every axis centred until it's touched, and its own pedal
  input, unused with separate pedals, then jumps to full) and, once something starts moving, watches for 1.5 seconds
  and takes the axis that travelled furthest: turn the wheel left and right, or press a pedal all the way and let it
  go. A small turn is enough for the wheel, so a 1080° wheel needn't go far; **+** adds another control for the same button (any of them
  presses it: paddles and a button box both shifting, say); **✕** clears it. The bindings are kept in `ForzaWheel.json`
  and used as soon as they change; **Save to file…** and **Load from file…** keep or move them as a .json file of
  their own, and **Clear all** starts again. **Import EmuWheel…** reads Forza EmuWheel's `configuration.json` (beside
  Forza EmuWheel.exe) and makes its bindings these: every controller's axes (Steering, Throttle, Brake, Clutch,
  Handbrake, Combined, with their Inverted and Deadzone), its buttons (each EmuWheel vJoy button to the control here
  on the same vJoy button) and its d-pad (the controller's first hat), and its force feedback tuning. It asks before
  replacing bindings you have, and lists anything it couldn't map.

**The picture** at the top of the bindings is for testing them: the wheel turns with the steering (over Forza's
450°, with the angle under it), its paddles, A / B / X / Y, View, Menu, bumpers, stick clicks and d-pad light up, the
clutch, brake, throttle and handbrake bars fill, the H-shifter lights the gear, and a button box holds the other wheel
buttons. Anything not bound is faint. Each row also shows its live value.

## What can be bound

The layout is EmuWheel's, the one Windows is told about (below), so Forza takes the vJoy device for a racing wheel:

| Group | Controls | On the vJoy device |
|---|---|---|
| Steering and Pedals | Steering, Throttle, Brake, Clutch, Handbrake; or **Combined throttle / brake** for pedals that share one axis (up throttle, down brake; pressing both cancels out, as in EmuWheel) | Axes X, Z, Rx, Ry, Rz (combined: Z and Rx) |
| Shifting | Shift down, Shift up | Buttons 17, 18 |
| H-pattern shifter | Reverse, Gear 1–7 | Buttons 19–26 |
| Menu buttons | Start / Menu, Back / View, A, B | Buttons 1–4 |
| D-pad | Up, right, down, left | The hat (one direction at a time; a continuous hat gets diagonals) |
| Wheel buttons | X, Y, LB, RB, left and right stick clicks, then Wheel button 11–16 (numbered: what each does is set in Forza) | Buttons 5–16 |
| Extra buttons | Button 27–128 (Forza sees buttons 1–26), folded away until opened | Buttons 27–128 |

So everything Forza can take from a wheel can be bound: 16 wheel buttons (bind each to any action in Forza's own
controls; the names are a guide), the paddles, seven gears and reverse, the d-pad, Menu, View, steering, three
pedals and the handbrake. Steering and Pedals are full width; the other groups are in two columns, the spare buttons
last. A control bound to two things is marked (**Also bound to …**), as
EmuWheel warns of duplicate bindings.

Steering has **Sensitivity** (100–400%): Forza's wheel turns 450°, so a wheel set to more turns the car less than your
hands; set it to your wheel's rotation ÷ 450 (1080° → 240%, 900° → 200%) for the car to turn as far as you do. Each
axis has **Invert** (for a pedal that reads full when released; **Set** ticks it when it sees one rest at the top)
and a **Deadzone**: ignored at the released end of a pedal, or around the centre of steering and the combined pedal.
The pedals are sent to vJoy as how far they're pressed (0 released), as EmuWheel sends them (0.53–0.59 sent them
reversed, so Forza read them backwards unless Invert was ticked; 0.60 flips each pedal's Invert once, so what Forza
gets stays the same and the bars here are right). EmuWheel's troubleshooting
applies: a menu that moves on its own is an axis not at 0%; give it a deadzone, or invert it.

## The vJoy device

The **vJoy device** card checks the device (1 unless you pick another) has what Forza needs, as EmuWheel's setup
guide shows: **all 8 axes**, **128 buttons** (vJoy's own default device has 8), **one 4-direction POV hat and no
continuous one** (the default has a continuous hat), and **force feedback switched on** (Enable Effects); and that
Windows is told it's a **racing wheel**: EmuWheel's `registry\vjoy.reg`, which Hush imports, writes the vJoy device's
layout (which axis is the wheel, throttle, brake, clutch and handbrake, which buttons are the gears, paddles, Menu,
View, A, B and wheel buttons 1–16, the hat as the d-pad, a 450° wheel) under
`HKLM\SYSTEM\CurrentControlSet\Control\GameInput\Devices\1234BEAD00010004`. It also says if another program
(EmuWheel itself, a feeder or Joystick Gremlin) is using the device. **Set up vJoy device** does what's missing, as
administrator: vJoyConfig (`enable on`, then `1 -f -a x y z rx ry rz sl0 sl1 -b 128 -p 1 -s 0 -e …`) and the racing
wheel layout (imported with reg.exe); if anything is still missing it says how to do it in **Configure vJoy…**.
Install vJoy 2.1.9.1 (the build for Windows 10 and 11) first: **Get vJoy**.

## Force feedback

Forza sends its forces to the vJoy wheel; ForzaWheel passes them to the wheel **Steering is bound to**, the way
EmuWheel does it (ported from EmuWheel, so it feels the same). While the emulator runs, that wheel is opened
exclusively with its auto-centre spring off (force feedback needs it; another program holding it, EmuWheel included,
stops it), and four effects are made on its force-feedback axis: a **constant force** (the steering force), a **sine**
wave (vibrations: Forza's engine and gear-shift jolts are told apart by the gain it gives them), a **spring** and a
**damper**. Each packet Forza sends vJoy updates them, scaled by EmuWheel's tuning. vJoy's signed values (the force,
the condition offsets and coefficients) are read as 16 bits, as EmuWheel's vJoy wrapper reads them: read as 32 bits
(0.59–0.60) a small push one way became full force the other, and the wheel was thrown about. A force outside the
game's range (±10000) is dropped, never sent to the wheel.

The **Force feedback** card: **Pass force feedback to the wheel**; **Strength** (100% = as EmuWheel); **Reverse**
(if the wheel pulls away from the centre: or use Forza's Invert Force Feedback, not both); the tuning in use, with
**Use EmuWheel's tuning…** (reads the FFBParameters from EmuWheel's `configuration.json`, beside Forza EmuWheel.exe, so
it feels exactly as your EmuWheel did) and **Default tuning** (EmuWheel's Configurator's defaults: steering force 100%,
engine vibration 3%, gear-shift 10%, spring and damper off); **Test** (a gentle push left then right, while running);
and whether the game's forces are arriving. The settings are in `ForzaWheel.json` (`Ffb`).

## Using it with Forza

1. Install vJoy and set its device up (the card turns all green).
2. Bind each control: **Set**, then press or move the real one; the picture shows it working.
3. **Mute** the real devices with Hush.
4. **Start** the emulator.
5. In Forza the vJoy wheel is your wheel: choose it in Settings → Controls, and bind the wheel buttons (1–16) to
   actions there by pressing the real ones.

Muting only affects Forza, so there's no need to unmute for other games.

## Code

| File | What it is |
|---|---|
| `ForzaWheelApplet.cs` | The tool: settings, the emulator and the tab; a hidden window DirectInput reads in the background for; the tray item |
| `ForzaWheelView.cs` | The sub-tab: the emulator, vJoy, Hush and file cards; the picture, then a row per control with Set, +, ✕, invert, deadzone and its live value (Steering and Pedals full width, the rest in two columns) |
| `WheelDiagram.cs` | The picture: wheel, paddles, buttons, d-pad, pedals, handbrake, shifter and button box, lit from the live values |
| `ForzaWheellator.cs` | The emulator: reads the bound controllers, works out each control, writes changes to vJoy (axes, the pedals reversed, buttons, the hat); reads slowly for the live values while the tab shows |
| `WheelConfig.cs` | Every bindable control and its vJoy axis or button (`Roles`), a binding (`Binding`, `InputRef`), the bindings (`WheelConfig`, saved and loaded as JSON) and the settings (`ForzaWheelSettings`: `ForzaWheel.json`) |
| `GameInputRegistry.cs` | Windows' GameInput device keys: Hush (mute and unmute a device for Forza) and the vJoy device as a racing wheel (checked, and written as a .reg file to import) |
| `GameControllers.cs` | The controllers through DirectInput (Vortice.DirectInput): 8 axes, 128 buttons and 4 hats each, and their GameInput key; vJoy's own devices left out |
| `Capture.cs` | **Set**: waits for the next button, hat or axis used on any controller |
| `VJoy.cs` | vJoyInterface.dll, loaded from where vJoy is installed; reading a device's setup and checking it; feeding it |
| `Elevated.cs` | Running commands as administrator (vJoyConfig, reg.exe) with one permission prompt |
| `ForceFeedback.cs` | Force feedback, ported from EmuWheel: vJoy's packets from the game to constant, sine, spring and damper effects on the wheel, scaled by EmuWheel's tuning (`EmuWheelFfb` in `WheelConfig.cs`) |
