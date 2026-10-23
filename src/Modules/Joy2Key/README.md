# Button2Key

*A module of [Daisy's App](../../../README.md): in Daisy's App it's the **Button2Key** sub-tab of the [Gaming](../Gaming/README.md)
tab (with ForzaWheel; its own tab before 0.53). Called Joy2Key before 2.2.0: its folder, files and
settings keep that name. Switch Gaming on or off in Settings → General → Modules. Its code is in
this folder; it's installed to `modules\Joy2Key\`. It's also one of the tools the **wrapper** offers: install the wrapper,
tick only Button2Key, and you have just a Button2Key tab and Settings (see [The wrapper](../../../README.md#the-wrapper)).*

Turns game controller input (buttons, sticks, triggers, the POV hat / d-pad) into key presses, mouse clicks, wheel
turns and mouse movement, like [JoyToKey](https://joytokey.net/en/), with a **profile per game**. It can import
JoyToKey's profiles.

## Using it

The left column stays put: at the top **Button2Key on or off** (Off: nothing is sent, whatever the profile, and nothing
needs deleting) with the **on / off shortcut** button beside it, and auto-switching, then the profile you're looking at (its name, games and options), then the list of
profiles. The controllers, on the right, scroll.

1. **Pick or make a profile** in the list (**New**, **Copy**, **Rename**, **Delete**, **Import**). One profile per game
   is the idea. **Export** saves every profile, and Button2Key's settings, to a .zip as a backup (the profile files as
   they are, under `Profiles\`; put them back in `%APPDATA%\DaisysApp\Joy2Key\Profiles` to restore).
2. **Add its controllers**: **Add a controller…** lists what's plugged in. Each controller gets a box with a tile for
   every axis direction, POV direction and button it has. **On** / **Off** at its top right switches the controller off
   in this profile: its input is ignored and its assignments are kept, to test without it. The arrow folds the box
   up; the bin removes the controller from the profile (it asks first).
3. **Find an input by using it**: press a button, push a stick or pull a trigger, and its tile lights up and is
   selected. (Nothing is sent to other programs while the tab is in front, so this is safe.)
4. **Double-click the tile** to choose what it does (or right-click it: **Clear**, **Copy**, **Paste**).
5. **Tell it the games**: under **Games**, type a game's program name (e.g. `eldenring.exe`; `.exe` is added if it's
   left off) and **Add** (or Enter), or use **Add a running program…** while the game is open. A profile can have any
   number of games, each on a line of its own with **×** to take it off (since 2.2.0: before, one box of names
   separated by commas). With **Switch on with a game's profile when the game starts** on, Button2Key looks at the
   running programs every 2 seconds, whether it's on or off: when a profile's game starts, that profile is picked,
   once (the one in front, if several games run), and Button2Key is switched **on** if it was off. When the game
   closes, it's switched off, however it was switched on (since 3.1.6; in 3.1.2–3.1.5 only if the game had switched it
   on). Switched off by hand while the game runs, it stays off until the game starts again. You can pick any other profile in the
   list at any time and it stays. No profile is special: **Default** (made only when there are no profiles at all)
   can be renamed or deleted like any other; the last profile can't be deleted. (Before 3.1.2 a game didn't switch
   Button2Key on, and Default couldn't be renamed or deleted; before 2.9.0 a game closing switched to Default.)

While Button2Key is on with a profile in use, a green **Button2Key** bubble shows in the window's title bar, after the app's
name and version. **Show Button2Key in the title bar while a profile is in use**, under the
on / off switch, leaves it out (on to start with; since 1.3.0).

The on / off switch is kept as it was left: closing the app (for an update, say) while it's on leaves it on
at the next launch, and off stays off; on only because a game is running, it starts off (and a game still running
switches it on again).

**The on / off shortcut** (the controller button beside **On**, as in ForzaWheel): click the box and press a key
combination (with Ctrl, Alt or Shift) or a controller / button box button; × clears it. It switches Button2Key on and
off from anywhere, the game included, like the button (a watched game closing still switches it off). The tray's **On**
shows it. A controller button that's also assigned in the profile does both, so pick one that isn't.

The tray menu has **Button2Key → On** and the profiles, to switch without opening the window.

Under **On** the tab shows the profile in use and the **last key it sent**, with the time, so it's easy to see that it's
working. Nothing is sent while the Button2Key tab is in front (it says so there): switch to the game or another window.

### What an input can do

| Choice | |
|---|---|
| **Press keys** | Up to four keys pressed together (e.g. Ctrl + Shift + F1), in one of four ways: **Hold** them while the input is held (like a key on the keyboard), **Tap** them once for a set time (**Each press lasts … ms**), **Repeat** them while it's held (**Press again every … ms**, with an optional longer wait before the first repeat), or **Toggle** them (one push holds them down, the next lets go). |
| … with a **long press** | Held for at least the set time, the input presses *other* keys instead (held until it's let go); a shorter press taps the main keys. JoyToKey's "Keyboard (Multi)" short / long press. |
| **Play a macro** | (Or **+ Add another key after it (makes a macro)** under **Press keys**' keys.) Any number of keys pressed **one after another** (add each with **+ Add a key**; reorder with the arrows), each held for a set time. The pause between them is either the **same throughout** (e.g. 0.5 seconds) or set after each key. A key box can hold a combination (Ctrl + C), pressed together as one step. **Each press** plays it once, or **over and over while it's held**. A press while it's still playing is ignored. |
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

Everything else (wheels, pedals, flight sticks, button boxes, other gamepads) is found through the Windows joystick API,
which has up to 32 buttons, six axes (X, Y, Z, R, U, V = axes 1–6) and one POV hat, and misreads some devices (a Saitek
/ Logitech X-56 stick shows 1 button and 2 axes there). So each one is also matched with its DirectInput device, and
whatever DirectInput sees more of is read through it: all its buttons (up to 128, numbered the same), its axes when
the joystick API found fewer than six (X, Y, Z, then R = Z rotation, U = X rotation, V = Y rotation), and its hats
(up to four: the second to fourth are **Hat 2**–**Hat 4**). The input ids stay the same, so profiles and JoyToKey
imports keep working. A profile remembers a controller by its USB ids, so it finds it again whichever port it's in;
with two of the same model, it tells them apart by order.

## Importing JoyToKey profiles

**Import**, next to **Delete** under the profiles:

1. **The folder**: JoyToKey's profiles are `.cfg` files, in `Documents\JoyToKey` unless it was moved (JoyToKey shows
   where under Settings → Preferences). Subfolders are searched too.
2. **Which ones**: each with how many assignments and controllers it has, and how many things can't come across.
   Same-named profiles are kept (the new one gets a number) unless **Replace** is ticked.
3. **Which controller is which**: JoyToKey's profiles don't record which controller they're for, only a number (its
   joystick 1, 2…, in the order Windows lists them). Each number is shown with the controller it is now; change any
   that's wrong, and the profiles use that controller wherever it's plugged in. Leave it as "in order, whichever it is"
   to keep JoyToKey's way.
4. **What came across**, with a list of what didn't.

Brought in: keyboard assignments (up to four keys) with their auto-repeat, Keyboard (Multi) short / long press, mouse
movement, clicks and the wheel, run a program, the axis threshold and 8-way POV setting. Not yet: button combinations,
shift / profile-switch "button functions", stick diagonals, POVs after the first, sequences, and JoyToKey's special key
codes above FF (its mouse-click and Num Enter entries in a keyboard assignment); the wizard lists each one it skipped.
A joystick left "in order" shows as "Joystick 1 (from JoyToKey)"… and uses whichever controller is plugged in at that
place; pick a controller at the top of its box to tie it to that one.

## Files

| File | |
|---|---|
| `%APPDATA%\DaisysApp\Joy2Key.json` (the Joy2Key app: `%APPDATA%\Joy2Key\`) | On / off, the profile in use, auto-switching, the on / off shortcut (`Hotkey`), the last import folder |
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
| `Joy2KeyView.cs` | The tab's content: profiles, profile options (its games as a list: `AddGames`), a box of tiles per controller, live lighting-up |
| `OnOffHotkey.cs` | The on / off shortcut (keys through Core's `HotkeyManager`, or a controller button through `ControllerButtons`) and its window |
| `BindingWindow.cs` | What one input does |
| `KeyPicker.cs` | The keys box: press keys, or right-click for the grouped key menu |
| `KeyCatalog.cs` | Every key (id, name, group, virtual-key code) and `KeySender`, which presses them with SendInput |
| `Joy2KeyEngine.cs` | The background thread: reads the active profile's controllers every 4 ms (1 ms timer resolution only while one's connected), steps each input through `ActionRunner`, looks at the running programs (while off too): a game starting picks its profile once and switches it on, and that game closing switches it off (`OnByGame`: on only by a game, saved Off when the app closes); `Turn` is on / off by hand |
| `ActionRunner.cs` | What an input does as it's pressed, held and let go: hold / tap / repeat / toggle / long-press timing, macros, mouse movement, Run, Switch profile (since 2.7.0; also linked into Button2Key Remote, with `BindingWindow`, `KeyCatalog`, `KeyPicker`, `Profiles` and `TextPrompt`, so its buttons work the same) |
| `Joysticks.cs`, `XInput.cs` | Finding and reading controllers (the ones the joystick API misses through ForzaWheel's `GameControllers.cs`, compiled in here too, so the module needs nothing of ForzaWheel's) |
| `GamingTab.cs` | The Gaming tab it shares with ForzaWheel when both are installed (`[assembly: TabGroup]`; linked into ForzaWheel's project too) |
| `Joy2Key.csproj` | Adds Vortice.DirectInput and links `../ForzaWheel/GameControllers.cs` |
| `JoyToKeyImport.cs`, `ImportWindow.cs` | Reading JoyToKey's `.cfg` files, and the wizard |
| `Profiles.cs` | The settings, the profile format and where profiles are saved |
