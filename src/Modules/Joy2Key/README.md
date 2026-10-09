# Joy 2 Key

*A module of [Daisy's App](../../../README.md): the Joy 2 Key tab. Switch it on or off in Settings → General → Applets.
Its code is in this folder; it's installed to `modules\Joy2Key\` next to `DaisysApp.exe`.*

Turns game controller input (buttons, sticks, triggers, the POV hat / d-pad) into key presses, mouse clicks, wheel
turns and mouse movement, like [JoyToKey](https://joytokey.net/en/), with a **profile per game**. It can import
JoyToKey's profiles.

## Using it

1. **Pick or make a profile** on the left (**New**, **Copy**, **Rename**, **Delete**). One profile per game is the idea.
2. **Add its controllers**: **Add a controller…** lists what's plugged in. Each controller gets a box with a tile for
   every axis direction, POV direction and button it has.
3. **Find an input by using it**: press a button, push a stick or pull a trigger, and its tile lights up and is
   selected. (Nothing is sent to other programs while the tab is in front, so this is safe.)
4. **Double-click the tile** to choose what it does (or right-click it: **Clear**, **Copy**, **Paste**).
5. **Tell it the game**: type the game's program name under **Games** (e.g. `eldenring.exe`), or use **Add a running
   program…** while the game is open. With **Switch to a profile when its game is in front** on, that profile is used
   whenever the game is the window in front; otherwise the **chosen** profile (**Use this profile**) is used.

The tray menu has **Joy 2 Key → On** and the profiles, to switch without opening the window.

### What an input can do

| Choice | |
|---|---|
| **Press keys** | Up to four keys pressed together (e.g. Ctrl + Shift + F1), in one of four ways: **Hold** them while the input is held (like a key on the keyboard), **Tap** them once for a set time (**Each press lasts … ms**), **Repeat** them while it's held (**Press again every … ms**, with an optional longer wait before the first repeat), or **Toggle** them (one push holds them down, the next lets go). |
| … with a **long press** | Held for at least the set time, the input presses *other* keys instead (held until it's let go); a shorter press taps the main keys. JoyToKey's "Keyboard (Multi)" short / long press. |
| **Play a macro** | Any number of keys pressed **one after another** (add each with **+ Add a key**; reorder with the arrows), each held for a set time. The pause between them is either the **same throughout** (e.g. 0.5 seconds) or set after each key. A key box can hold a combination (Ctrl + C), pressed together as one step. **Each press** plays it once, or **over and over while it's held**. A press while it's still playing is ignored. |
| **Move the mouse** | Pixels a second left / right and up / down (minus = left / up). On a stick, the speed follows how far it's pushed. |
| **Run a program** | A program, file or web address, with arguments. |
| **Switch profile** | Changes to another profile until another profile's game comes to the front. |

**Picking keys:** click the keys box and press the keys, all together like a shortcut. **Right-click** it for every key
there is, in groups: Ctrl / Shift / Alt / Windows (left and right separately), Enter / Esc / Tab…, arrows and
navigation, F1–F12, F13–F24, the number pad (with Num Enter), letters / number row / symbols, lock keys / Print Screen /
Pause, media keys, browser keys, launch keys, **mouse buttons and the wheel** (left, right, middle, back, forward, wheel
up / down / left / right), international and IME keys, and rare old ones (Help, Select, Attn, Zoom…). Keys are sent as
scan codes, which games read; media, browser and launch keys as virtual keys.

**Profile options:** **Axes count as pressed past** (how far a stick or trigger moves before it counts, 50% by default),
**8-way POV** (the hat's diagonals become inputs of their own; otherwise a diagonal presses both neighbours) and **Only
show what's assigned**.

## Controllers

Xbox-style pads (anything that works as an XInput controller) are read through XInput, and numbered the way JoyToKey
numbers them, so imported profiles line up:

| Input | Xbox-style pad |
|---|---|
| Buttons 1–10 | A, B, X, Y, LB, RB, View / Back, Menu / Start, left stick click, right stick click |
| Buttons 11, 12 | left and right trigger (as buttons) |
| Button 13 | the Xbox button |
| Axes 1, 2 | left stick (− = left / up) |
| Axes 3, 4 | right stick |
| Axes 5, 6 | left and right trigger (as axes) |
| POV | the d-pad |

Everything else (wheels, pedals, flight sticks, button boxes, other gamepads) is read through the Windows joystick API:
up to 32 buttons, six axes (X, Y, Z, R, U, V = axes 1–6) and one POV hat. A profile remembers a controller by its USB
ids, so it finds it again whichever port it's in; with two of the same model, it tells them apart by order.

## Importing JoyToKey profiles

**Import from JoyToKey…** at the top of the tab:

1. **The folder**: JoyToKey's profiles are `.cfg` files, in `Documents\JoyToKey` unless it was moved (JoyToKey shows
   where under Settings → Preferences). Subfolders are searched too.
2. **Which ones**: each with how many assignments and controllers it has, and how many things can't come across.
   Same-named profiles are kept (the new one gets a number) unless **Replace** is ticked.
3. **What came across**, with a list of what didn't.

Brought in: keyboard assignments (up to four keys) with their auto-repeat, Keyboard (Multi) short / long press, mouse
movement, clicks and the wheel, run a program, the axis threshold and 8-way POV setting. Not yet: button combinations,
shift / profile-switch "button functions", stick diagonals, POVs after the first, sequences, and JoyToKey's special key
codes above FF (its mouse-click and Num Enter entries in a keyboard assignment); the wizard lists each one it skipped.
JoyToKey numbers controllers 1, 2…, so an imported profile's controllers are "Joystick 1 (from JoyToKey)"… and use
whichever controllers are plugged in, in order; pick a controller at the top of its box to tie it to that one.

## Files

| File | |
|---|---|
| `%APPDATA%\DaisysApp\Joy2Key.json` | On / off, the chosen profile, auto-switching, the last import folder |
| `%APPDATA%\DaisysApp\Joy2Key\Profiles\<name>.json` | One file per profile: its games, options, controllers and assignments |

A profile file is plain JSON, readable and easy to copy between PCs:

```json
{
  "Name": "Elden Ring",
  "Programs": [ "eldenring.exe" ],
  "Threshold": 50,
  "Pov8Way": false,
  "Devices": [
    {
      "Name": "Xbox controller 1",
      "Vid": 1118, "Pid": 65535, "Number": 1,
      "Inputs": {
        "Button1": { "Kind": "Keys", "Keys": [ "Space" ], "Mode": "Hold", "PressMs": 50, "RepeatMs": 100 },
        "Button5": { "Kind": "Keys", "Keys": [ "LCtrl", "F1" ], "Mode": "Tap", "PressMs": 80, "RepeatMs": 100 },
        "Axis2-":  { "Kind": "Keys", "Keys": [ "W" ], "Mode": "Hold", "PressMs": 50, "RepeatMs": 100 },
        "PovUp":   { "Kind": "Keys", "Keys": [ "Up" ], "LongMs": 500, "LongKeys": [ "PageUp" ], "PressMs": 50, "RepeatMs": 100 }
      }
    }
  ]
}
```

A macro looks like this (`PressMs` is how long each key is held; with `"SameGap": false` each step's `PauseMs` is the
pause after it, in milliseconds):

```json
"Button6": {
  "Kind": "Macro", "PressMs": 50, "GapMs": 500, "SameGap": true, "Loop": false,
  "Steps": [ { "Keys": [ "F1" ] }, { "Keys": [ "LCtrl", "C" ] }, { "Keys": [ "Enter" ] } ]
}
```

Inputs are `Button1`–`Button32`, `Axis1-` / `Axis1+` … `Axis6+`, and `PovUp`, `PovUpRight`, `PovRight` … `PovUpLeft`.
Key names are listed in `KeyCatalog.cs` (`A`, `D1`, `F13`, `Num0`, `NumEnter`, `LCtrl`, `RAlt`, `VolumeUp`, `MouseLeft`,
`WheelUp`…).

## Code

| File | |
|---|---|
| `Joy2KeyApplet.cs` | The tab, the tray menu |
| `Joy2KeyView.cs` | The tab's content: profiles, profile options, a box of tiles per controller, live lighting-up |
| `BindingWindow.cs` | What one input does |
| `KeyPicker.cs` | The keys box: press keys, or right-click for the grouped key menu |
| `KeyCatalog.cs` | Every key (id, name, group, virtual-key code) and `KeySender`, which presses them with SendInput |
| `Joy2KeyEngine.cs` | The background thread: reads the active profile's controllers every 4 ms (1 ms timer resolution only while one's connected), runs hold / tap / repeat / toggle / long-press timing and plays macros, picks the profile from the window in front |
| `Joysticks.cs`, `XInput.cs` | Finding and reading controllers |
| `JoyToKeyImport.cs`, `ImportWindow.cs` | Reading JoyToKey's `.cfg` files, and the wizard |
| `Profiles.cs` | The settings, the profile format and where profiles are saved |
