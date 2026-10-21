# Button2Key Remote

*A module of [Daisy's App](../../../README.md): in Daisy's App it's the **Button2Key Remote** sub-tab of the
[Gaming](../Gaming/README.md) tab (since 2.7.0). Its code is in this folder; it's installed to `modules\Butt2KeyRemote\`.
It's also one of the tools the **wrapper** offers.*

Screens of buttons, called **ButtScreens**, for a phone, tablet or PC, or popped out on this PC. Pressing one does what
a [Button2Key](../Joy2Key/README.md) input does: keys (held, tapped, repeated, toggled, a long press), a macro, mouse
movement, a program, or showing another ButtScreen. ButtScreens are grouped into **BigButts** (usually one per
game), and the BigButt in use follows the game running, as Button2Key's profiles do. Devices connect to this PC over the
network with a passphrase: the **Android app** ([its guide](../../../clients/android/README.md), an APK on each release
since 2.7.3) is the first client, and the protocol is below, so any client can be written.

## Using it

The left column has the switch, the BigButts (with each one's games), the BigButt in use, then the devices; on the
right, the ButtScreens of the BigButt in use (each BigButt has its own, as a profile has its inputs).

- **Button2Key Remote on or off** (off to start with; kept as it was left after a restart). On, devices can connect and
  buttons, theirs and pop-outs', do what they're set to; off, neither. Beside it, a **shortcut** that switches it on and
  off from anywhere: click the box and press keys (with Ctrl, Alt or Shift) or a controller / button box button; the ×
  clears it. While it's on, a green **Button2Key Remote** bubble shows in the title bar after the app's name (pointing
  at it says the BigButt in use and who's connected); **Show Button2Key Remote in the title bar while it's on** leaves
  it out. **Use a game's BigButt while the game is running, and Default when it closes**: as Button2Key's profiles.
- **BigButts** (a BigButt is Button2Key's profile, holding ButtScreens): the list (pick one to use it), then **New**,
  **Copy** (the same ButtScreens, linked, and no games), **Rename** and **Delete** (these two for the one picked;
  Default has neither). A device can pick a BigButt too. Under them, **the picked BigButt's games**: type a program
  name and **Add**, or **Add a running program…**; × takes one off. While one of its games runs (checked every 2
  seconds; the one in front wins when several run) it's the BigButt in use; when it closes, Default is. One picked by
  hand stays until a game starts or closes. **Switch Button2Key Remote on while one of these games is running** (on to
  start with, since 2.7.5): a BigButt's game starting switches it on if it's off, and its closing switches it off
  again if the game switched it on (as ForzaWheel's Start with a game; one switched on by hand stays on, and the next
  start finds it as it was before the game).
- **The BigButt in use**, headed by its name: **Its ButtScreens**, the ones ticked, numbered in the order a device goes
  through them (the arrows change it; a device moves from one to the next itself, told how many there are and which).
- **Devices**: the **Port** (38870 to start with) and the **Passphrase** (8 characters or more, typed the same on each
  device; kept encrypted for your Windows user), applied with **Save** (or Enter); then **Active Devices**, a line each:
  `- Pixel 10 Pro (Android 17, 192.168.1.92)` (pointing at one says since when). Nothing else shows there, unless
  something's wrong (the port in use, no passphrase yet). Devices on the same network find the PC by themselves. The
  first time, Windows may ask to let Daisy's App through its firewall: allow it on private networks.
- **ButtScreens in** the BigButt in use (since 2.7.6; pick another BigButt and they change): a picture of each, its
  size and buttons, with **Edit** (or double-click the picture), **Pop out** / **Close pop-out**, **Copy** (a copy of
  its own, in this BigButt) and **Delete** (click it, then **Confirm?**). **New ButtScreen** asks for a name and opens
  it in the editor, at the size of the device that connected last (or a 20:9 phone); it goes into the BigButt in use.
  **Link a ButtScreen…** adds one of another BigButt's: the same ButtScreen, not a copy, so changing it changes it in
  every BigButt it's in. A linked one says **Linked: also in …**, and has **Remove** in place of Delete: it takes it
  off this BigButt only.

### The editor

A window of its own per ButtScreen. Everything is saved as it changes (**✓ Saved** shows in the toolbar), and
**Save & Close** (top right) saves what's left and closes it;
**Undo** / **Redo** (Ctrl+Z / Ctrl+Y) go back through every change made while it's open.

- **Left, the ButtScreen**: its **Name**; its **Size**: a device that has connected (with its own size, which it sends
  when it connects), a device of ours (Google Pixel 10 Pro, 1280 × 2856), a common shape (phones 20:9, 19.5:9, 18:9 / 2:1 and 16:9; tablets 4:3, 3:2 and 16:10; square) or
  **Your own size**; **Held** portrait or landscape (turning it swaps the
  columns and rows too, and turns every button with them, so the layout keeps its shape); and the **Pixels**, to type any size. The **Grid**: **Columns**,
  **Rows** (1–24; ones with buttons in them can't be taken away) and the **Spacing** between buttons and round the edge,
  in pixels. **Colours**: the **Background**, and the **Icons** colour, which every library icon on the ButtScreen is
  drawn in.
- **Middle, the ButtScreen itself**, drawn to fit, its empty cells dashed. **Double-click an empty cell** to add a
  button there (or **Add a button**, top left: the first free cell). **Click** a button to select it; **drag** it to
  another place (the outline shows green where it fits, red where it doesn't); **drag its corner handle** to make it
  cover more cells or fewer (1×2, 2×2, 4×1…). **Double-click** it (or Enter) to choose what it does. Right-click a
  button for What it does…, Copy, Duplicate and Delete; right-click an empty cell for Add a button here and Paste. The
  keyboard: arrows move the selected button a cell, Delete removes it, Ctrl+C / Ctrl+V / Ctrl+D copy, paste and
  duplicate, Esc deselects.
- **Right, the selected button**: its **Label** (Enter makes a second line); its **Icon**: **Icon…** opens the
  library, **Your own…** a picture of your own (PNG, JPEG…: copied in, at most 512 pixels across), **None**; what it
  **Does**, with **Choose…**; how many cells it **Covers**, wide × high. Its **Look**: **Corners** rounded or square,
  **Background** and **Outline** colours with the outline's width, the **Font**, **Text size**, **Bold** and **Text
  colour**. Sizes are in the ButtScreen's pixels, so they grow and shrink with it. New buttons look like the last one
  changed; **Give every button this look** gives them all the selected one's. Colours have a palette, a box for
  `#RRGGBB` and how solid they are.

**What it does** is Button2Key's own window: **Press keys** (Hold, Tap, Repeat or Toggle, with a long press), **Play a
macro**, **Move the mouse** (for as long as the button is held), **Run a program**, or **Show a ButtScreen**: the device
or pop-out it was pressed on shows that ButtScreen instead (a way to have pages). See [what an input can
do](../Joy2Key/README.md#what-an-input-can-do). A button held on two devices at once stays held until both let go; a
quick touch still holds its keys for 40 ms, so a game sees it.

**The icon library** is [Material Design Icons](https://pictogrammers.com/library/mdi/) (Pictogrammers; icons Apache
2.0): over 7,000 icons in one style, each a single shape that can be drawn in any colour. Search by name or by words
(car, arrow, music, volume, mic…); each shows in the ButtScreen's icon colour on the button's background. Double-click
one, or select it and **OK**; **No icon** takes it off.

### Pop-outs

**Pop out** opens a ButtScreen in a window of its own, to use with the mouse or a touchscreen, just as on a device. It
never takes the keyboard from the game or program in front (so the keys its buttons press go there), isn't in Alt+Tab,
and has no frame: drag it by the space between buttons, and resize it with the grip in the corner (it keeps its
shape). Right-click it for **How solid** (100% down to 20%), **Keep on top**, **Lock in place** (no moving or resizing:
every touch presses), **Size**, **Edit this ButtScreen…** and **Close**. Where each one is, and whether it was open, is
remembered: open ones come back at the next start. The tray menu's **Pop out** opens and closes them too, with **On** and **BigButt in use**.

## Files

| File | |
|---|---|
| `%APPDATA%\DaisysApp\Butt2KeyRemote.json` | On / off and its shortcut, the port and passphrase (encrypted with DPAPI), the BigButts and the one in use, devices that have connected (their sizes), where the pop-outs are |
| `%APPDATA%\DaisysApp\Butt2KeyRemote\Screens\<id>.json` | One file per ButtScreen: its size, grid, colours and buttons (with what each does, in Button2Key's format) |
| `%APPDATA%\DaisysApp\Butt2KeyRemote\Images\` | Pictures of your own on buttons (deleted when no button uses them) |

The log is `Butt2KeyRemote-<date>.log` (devices connecting and going, BigButts switching; each press at Debug).

## For client makers: the protocol (version 1)

Everything a device needs; `tools/buttclient.py` is a working client in Python (finding PCs, signing in, fetching
every ButtScreen, pressing a button) to check a new client against, and `clients/android` a whole one in Kotlin.

**Finding PCs.** Send `{"op":"discover"}` as a UDP broadcast to the port (38870 unless changed). Each PC with Button2Key
Remote on answers, to the address it came from:
`{"op":"here","app":"DaisysApp","service":"Butt2KeyRemote","v":1,"pc":"PLEX","port":38870}`. Or let the user type the
address and port.

**Connecting.** TCP to the port. Every frame is a 4-byte little-endian length, then that many bytes (at most 1 MB).
The PC's first frame is plain JSON:
`{"app":"DaisysApp","service":"Butt2KeyRemote","v":1,"pc":"PLEX","salt":"<base64, 16 bytes>","iterations":150000}`.
The key is PBKDF2-HMAC-SHA256 of the passphrase (UTF-8) with that salt and iteration count, 32 bytes (Android:
`PBKDF2WithHmacSHA256`; iOS: CommonCrypto's `CCKeyDerivationPBKDF`). Every frame after that, both ways, is AES-256-GCM:
a 12-byte nonce, the 16-byte tag, then the ciphertext of the JSON message (no associated data). The nonce is a
little-endian `uint32` direction (1 = from the device, 2 = from the PC) and a little-endian `uint64` count of frames
sent that way (from 0); each side checks the other's counts, so frames can't be replayed or reordered. (Java's and
Swift's GCM put the tag after the ciphertext: move it.)

The device's first message is its hello: `{"op":"hello","device":"Pixel 8","platform":"Android 15","app":"Butt2Key
Remote 1.0","width":1080,"height":2400}` (its screen in pixels, the way it's held). With the wrong passphrase it
doesn't decrypt, and the PC closes the connection (after a pause). With the right one the PC answers
`{"op":"welcome","pc":"PLEX","v":1}`, then the state.

**Messages from the PC**

| Message | |
|---|---|
| `{"op":"state","pc","on","bigButt","current":{"name","count","screens":[{"index","id","name"}]},"bigButts":[{"name","screens":[ids]}],"screens":[{"id","name","width","height","columns","rows","version"}]}` | Sent after welcome and whenever anything changes (a ButtScreen edited: its `version` goes up; the BigButt in use switching, by a game or by hand). `current` is the BigButt in use: its `count` of ButtScreens and which, in order. Show the first and let the user move from one to the next (a swipe, say); when `current` changes, start again at its first. `bigButts` lists every BigButt, for a picker (`use`). |
| `{"op":"screen","screen":{…},"icons":{name: path},"images":{file: base64 PNG}}` | A ButtScreen's layout, the answer to `get`; keep it until its `version` changes. |
| `{"op":"show","screen":id}` | A Show a ButtScreen button was pressed on this device: show that one. |
| `{"op":"pong"}` | The answer to `ping`. |
| `{"op":"error","message"}` | Something it couldn't do (worded for the user). |

**Messages from the device**

| Message | |
|---|---|
| `{"op":"get","id":id}` | A ButtScreen's layout. |
| `{"op":"down","screen":id,"button":id}` / `{"op":"up",…}` | A button pressed and let go (send both, for holds, repeats and long presses). |
| `{"op":"tap","screen":id,"button":id}` | A press and let-go in one, for a client that only knows taps. |
| `{"op":"use","bigButt":name}` | Use that BigButt (as picking it in the tab). |
| `{"op":"state"}` | The state again. |
| `{"op":"ping"}` | Send one at least every 10 seconds: a device silent for 30 seconds is dropped, and the buttons it held are let go (as they are when it disconnects). |

**Drawing a ButtScreen.** `width` × `height` are its pixels; scale the whole thing to fit the device's screen, keeping
its shape, so every size below scales with it. The grid is `columns` × `rows` cells with `gap` pixels between them and
round the edge: a cell is `cw = (width - gap × (columns + 1)) / columns` wide and `ch = (height - gap × (rows + 1)) /
rows` high, and a button at `column`, `row` (from 0) covering `columnSpan` × `rowSpan` cells is at
`x = gap + column × (cw + gap)`, `y = gap + row × (ch + gap)`, `columnSpan × cw + (columnSpan - 1) × gap` wide and
`rowSpan × ch + (rowSpan - 1) × gap` high. The screen's `background` fills it. Each button has `id`, `label` (may have
line breaks), `icon` (a name in `icons`) or `image` (a file in `images`), `shows` (the ButtScreen a Show a ButtScreen
button shows, for drawing it as a link; the PC still sends `show` when it's pressed) and `style`: `rounded` (corner
radius `cornerRadius`, given with the screen; square otherwise), `background`, `outline` and `outlineWidth` (pixels,
drawn inside the button), `font`, `fontFamily` (`sans-serif`, `serif` or `monospace`, for when the device hasn't that
font), `fontSize` (pixels), `fontColor` and `bold`. Colours are `#AARRGGBB`. The icon or picture is centred, 45% of the
button's smaller side with a label under it (a gap of 4% of that side between them) and 55% without one; the label is
centred and wraps. Icons are SVG path data on a 24 × 24 grid (Android: `PathParser.createPathFromPathData`; iOS: a
small SVG path parser), filled with the screen's `iconColor`; pictures keep their own colours. Show a button as
pressed while it's held (the PC's pop-outs lighten it).

## Code

| File | |
|---|---|
| `RemoteApplet.cs` | The applet (a sub-tab of Gaming) and its tray menu |
| `ButtHub.cs` | Holds it together: the ButtScreens and settings, the engine and server (handed fresh copies after each change), the open editors and pop-outs, the title bar bubble, the on / off shortcut, the BigButt in use and the game watching (which also switches it on and off with a game) |
| `RemoteView.cs` | The tab: on / off with its shortcut, the BigButts (New, Copy, Rename, Delete, the picked one's games, switching on with a game), the BigButt in use (its ButtScreens in order), devices, the BigButt in use's ButtScreens' pictures (New, Link a ButtScreen…, Remove or Delete); `ConfirmButton` (the two-click Confirm? button) |
| `EditorWindow.cs` | The layout tool: the ButtScreen's size, grid and colours; the stage (add, select, drag to move, corner handle to resize, keys, right-click menu); the selected button's label, icon, action, cells and look; undo / redo; saving as it changes |
| `ScreenVisual.cs` | Draws a ButtScreen at its own pixel size (the editor, the tab's pictures and pop-outs put it in a Viewbox), and finds the cell under a point |
| `PopOutWindow.cs` | A ButtScreen in a window that never takes the focus: pressing (mouse and multi-touch), moving, resizing, its menu |
| `PressEngine.cs` | Presses and let-gos from anywhere, done on a thread of its own through Button2Key's `ActionRunner` |
| `ButtServer.cs` | `ButtWire` (the encrypted connection) and `ButtServer` (listening, the UDP answer for finding PCs, the messages) |
| `IconLibrary.cs`, `Icons/mdi.txt.gz` | The icon library (name, words, path a line each, built into the DLL) and its search |
| `IconPickerWindow.cs` | Picking an icon (search, rows of icons made only as they scroll into view) |
| `ColorBox.cs` | A colour: palette, #RRGGBB box and how solid |
| `Model.cs` | The settings, BigButts, ButtScreens and buttons, where they're saved, pictures of your own |
| `Butt2KeyRemote.csproj` | Links Button2Key's `ActionRunner.cs`, `BindingWindow.cs`, `KeyCatalog.cs`, `KeyPicker.cs`, `Profiles.cs` and `TextPrompt.cs` (and the Gaming tab's `GamingTab.cs`), so buttons work as Button2Key's inputs and nothing of Button2Key's is needed at run time |

`Icons/mdi.txt.gz` was made from `@mdi/js` and `@mdi/svg`'s `meta.json` (7.4.47; deprecated icons left out): a line per
icon, `name<TAB>aliases and tags<TAB>path`, gzipped.
