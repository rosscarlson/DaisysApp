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

- **Status**: the **ON / OFF** button (off to start with; kept as it was left after a restart). On, devices can connect
  and buttons, theirs and pop-outs', do what they're set to; off, neither. On the right, the **joystick button** (since
  2.8.2; blue once set, pointing at it says which) opens a small window for the **shortcut** that switches it on and off
  from anywhere: with none set it listens straight away for keys (with Ctrl, Alt or Shift) or a controller / button
  box button and shows the one pressed until Done; one already set stays until the × clears it, which listens again. While it's on, a green **Button2Key Remote** bubble shows in the title
  bar after the app's name (pointing at it says the BigButt in use and who's connected). Under it, on one line:
  **Game Switching** (Auto Launch in 2.7.8–2.8.1): use a game's BigButt while the game is running, and the one picked
  before when it closes, as Button2Key's profiles; **Titlebar Status** (off) leaves the bubble out. (Pointing at either
  checkbox says what it does.)
- **BigButts** (a BigButt is Button2Key's profile, holding ButtScreens): the list (pick one to use it), then **New**,
  **Copy** (the same ButtScreens, linked, and no games), **Rename** and **Delete** (for the one picked). No BigButt is
  special (since 2.7.7; "Default" is an ordinary name): any can be renamed, deleted or given games, and with none at
  all devices are told there are none (New ButtScreen waits for one). A device can pick a BigButt too. Under them, **the picked BigButt's games**: type a program
  name and **Add**, or **Add a running program…**; × takes one off. While one of its games runs (checked every 2
  seconds; the one in front wins when several run) it's the BigButt in use; when it closes, the one picked by hand
  before it is again. One picked by hand stays until a game starts or closes. **Auto Enable B2KR** (on to start with, since
  2.7.5): a BigButt's game starting switches it on if it's off, and its closing switches it off
  again if the game switched it on (as ForzaWheel's Start with a game; one switched on by hand stays on, and the next
  start finds it as it was before the game).
- **The BigButt in use**, headed by its name: **Its ButtScreens**, the ones ticked, numbered in the order a device goes
  through them (the arrows change it; a device moves from one to the next itself, told how many there are and which).
- **Remote Devices**: the **Port** (38870 to start with) and the **Passphrase** (8 characters or more, typed the same on
  each device; kept encrypted for your Windows user), applied with **Save** beside it (or Enter); then **Active Devices**, a line each:
  `- Pixel 10 Pro (Android 17, 192.168.1.92)` (pointing at one says since when). Nothing else shows there, unless
  something's wrong (the port in use, no passphrase yet). Devices on the same network find the PC by themselves. The
  first time, Windows may ask to let Daisy's App through its firewall: allow it on private networks.
- **ButtScreens in** the BigButt in use (since 2.7.6; pick another BigButt and they change): a picture of each, its
  size and buttons, with **Edit** (or click the picture), **Pop out** / **Close pop-out**, **Copy** (a copy of
  its own, in this BigButt) and **Delete** (click it, then **Confirm?**). **New ButtScreen** asks for a name and opens
  it in the editor, at the size of the device that connected last (or a 20:9 phone); it goes into the BigButt in use.
  **Butt Wizard** (From a template… before 2.8.2) makes ButtScreens with a game's buttons on them (see
  [Templates](#templates)). **Link2Butt** (Link a ButtScreen… before 2.8.2) adds one of another BigButt's: the same ButtScreen, not a copy, so changing it changes it in
  every BigButt it's in. A linked one says **Linked: also in …**, and has **Remove** in place of Delete: it takes it
  off this BigButt only.

### Templates

A template is a game's buttons, in groups (for Elite Dangerous: Ship, Navigation, Combat, Heat, Power…), each with its
label, icon and the game's own keys. Built in (since 2.8.0): **Forza Horizon 5**, **Forza Horizon 6**, **iRacing**,
**Star Citizen**, **Elite Dangerous** and **Assetto Corsa Competizione**, with each game's keyboard keys as it comes
(change a button's keys if you've changed the game's; Star Citizen's change from patch to patch).

**Butt Wizard** opens a short wizard, **ButtScreen Template** (since 2.8.1), a step at a time with **Next** and
**Back**:

1. **The device**, so the layout suits it: one that has connected (with its own size), a phone or tablet by name (Google
   Pixel 10 Pro, the iPhones by size, the Dragon Touch tablets), or a shape (phones, tablets, square); and **Held** portrait or landscape. It starts at the
   device that connected last.
2. **The template.**
3. **Its buttons**, a tile each (icon, label, keys and how it presses them: tapped, held, repeating), ticked or not by
   clicking it, in groups that open and close with their arrow; a group's tick (ticked while any of its buttons is)
   ticks or unticks them all. Some start unticked (the less used ones). It says how many buttons on how many
   ButtScreens.
4. **The theme** (the template's own to start with), with a picture of every ButtScreen as it will be, and **Add its games to …**
   (the template's programs as the BigButt in use's games, offered when it hasn't them yet). **Create** makes them, in
   the BigButt in use.

The ButtScreens are made on the template's grid (turned for a device held landscape): a group at a time, each starting
on a new row, so groups that fit share a ButtScreen and one that doesn't goes on the next (one too big for a whole
ButtScreen carries on to the next). They're named for their groups (Elite Dangerous: Ship, Navigation, Combat, Heat).
Then change them as any ButtScreen.

**Settings → Gaming → Button2Key Remote** lists them, built in and yours. Yours are JSON files in
`%APPDATA%\DaisysApp\Butt2KeyRemote\Templates\`: put one there, or **Import…** it (it's checked and copied in; one
with the same name as one of yours replaces it). One of yours with a built-in one's name takes its place; **Make it
yours** copies a built-in one there to start from. A file that can't be used says why, in red. **Delete** (then
**Confirm?**) deletes one of yours.

The format (any text editor; comments and trailing commas are allowed):

```json
{
  "name": "Elite Dangerous",
  "description": "What it has, for the wizard's list.",
  "programs": [ "EliteDangerous64.exe" ],
  "columns": 4, "rows": 7,
  "theme": "Bronze",
  "groups": [
    { "name": "Ship", "buttons": [
      { "label": "Landing Gear", "icon": "airplane-landing", "keys": [ "L" ], "wide": 2 },
      { "label": "Jettison Cargo", "icon": "package-down", "keys": [ "End" ], "selected": false }
    ] },
    { "name": "Panels", "selected": false, "buttons": [ … ] }
  ]
}
```

- `programs`: the game's programs, given to a BigButt made from it as its games. `columns` × `rows`: each ButtScreen's
  grid held portrait (1–24). `theme`: one of the editor's themes, by name (left out: the usual colours).
- A button: `label`; `icon`, an icon library name (as the icon picker shows; an unknown one is left off); `keys`,
  Button2Key's key names pressed together in order (`A`–`Z`, `D0`–`D9` for the number row, `F1`–`F24`, `Num0`–`Num9`,
  `LCtrl`, `LShift`, `LAlt`, `Space`, `Enter`, `Esc`, `Tab`, `Up`, `Down`, `Left`, `Right`, `Home`, `End`, `PageUp`,
  `PageDown`, `Insert`, `Delete`, `Minus`, `Equals`, `Comma`, `Period`, `Slash`, `LBracket`, `RBracket`… the full list
  is `KeyCatalog.cs` in `src/DaisysApp.Controllers/Actions/`; a file with a name it doesn't know isn't used); `mode`: `Tap` (the
  usual), `Hold`, `Repeat` or `Toggle`; `wide` and `high`, the cells it covers (1 unless given); `selected`: `false`
  to start unticked. A group's `selected: false` starts all its buttons unticked.

### The editor

A window of its own per ButtScreen. Everything is saved as it changes (**✓ Saved** shows in the toolbar), and
**Save & Close** (top right) saves what's left and closes it;
**Undo** / **Redo** (Ctrl+Z / Ctrl+Y) go back through every change made while it's open.

- **Left, the ButtScreen**: its **Name**; its **Size**: a device that has connected (with its own size, which it sends
  when it connects; one the size of a device listed by name shows as that name), a device by name (Google Pixel 10 Pro,
  1280 × 2856; the iPhones, a line per size since 2.8.1: 16 Pro / 17 / 17 Pro / 18 Pro, the Pro Maxes, Air, 15 /
  16, the Pluses, 12–14 / 16e, the older Pro Maxes; since 2.8.2 the Dragon Touch NotePad X10, 800 × 1280, and the older
  10.6" Dragon Touch X10, 768 × 1366), a common shape (phones 20:9, 19.5:9, 18:9 / 2:1 and 16:9; tablets 4:3, 3:2 and 16:10; square) or
  **Your own size**; **Held** portrait or landscape (turning it swaps the
  columns and rows too, and turns every button with them, so the layout keeps its shape); and the **Pixels**, to type any size. The **Grid**: **Columns**,
  **Rows** (1–24; ones with buttons in them can't be taken away) and the **Spacing** between buttons and round the edge,
  in pixels. **Style** (Colours before 2.8.1): a **Theme** (since 2.7.7) colours it all at once, the background, the
  icons and every button's background, outline and text (and new buttons'), in one step to undo: Slate, Coral, Magenta,
  Night Sky, Bronze, Racing Orange, Royal Purple, Steel Blue, Teal and Neon (it says Your own colours once any is
  changed; templates may still use the names they had before 2.8.1); then the **Background**, the **Icons** colour,
  which every library icon on the ButtScreen is drawn in, and **Titles**: **Show** or **Hide** every button's text
  whatever each one's Show Text says (green while it does; click it again to give each button its own back).
- **Middle, the ButtScreen itself**, drawn to fit, its empty cells dashed. **Double-click an empty cell** to add a
  button there (or **Add a button**, top left: the first free cell). **Click** a button to select it; **drag** it to
  another place (the outline shows green where it fits, red where it doesn't); **drag its corner handle** to make it
  cover more cells or fewer (1×2, 2×2, 4×1…). **Double-click** it (or Enter) to choose what it does. Right-click a
  button for What it does…, Copy, Duplicate and Delete; right-click an empty cell for Add a button here and Paste. The
  keyboard: arrows move the selected button a cell, Delete removes it, Ctrl+C / Ctrl+V / Ctrl+D copy, paste and
  duplicate, Esc deselects.
- **Right, the selected button**: its **Label** (Enter makes a second line), with **Show Text** under it (off: only
  the icon shows, bigger, here and on devices); its **Icon**: **Icon…** opens the
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
and has no frame: drag it by the space between buttons (with the mouse). Since 3.7.0 it's a widget like every other
in the app (Core's `Widget`): right-click it (or hold a finger on the space between buttons) to edit it: drag it
anywhere, stretch it from any edge or corner (it keeps the ButtScreen's shape), with a strip for how solid it is,
**On top**, **Locked** (the space between buttons doesn't move it), **Edit ButtScreen**, **Done**, and the bin to
close it. Places saved before 3.7.0 are carried over. Where each one is, and whether it was open, is
remembered: open ones come back at the next start. The tray menu's **Pop out** opens and closes them too, with **On** and **BigButt in use**.

## Files

| File | |
|---|---|
| `%APPDATA%\DaisysApp\Butt2KeyRemote.json` | On / off and its shortcut, the port and passphrase (encrypted with DPAPI), the BigButts and the one in use, devices that have connected (their sizes), where the pop-outs are |
| `%APPDATA%\DaisysApp\Butt2KeyRemote\Screens\<id>.json` | One file per ButtScreen: its size, grid, colours and buttons (with what each does, in Button2Key's format) |
| `%APPDATA%\DaisysApp\Butt2KeyRemote\Images\` | Pictures of your own on buttons (deleted when no button uses them) |
| `%APPDATA%\DaisysApp\Butt2KeyRemote\Templates\` | Your templates (JSON files; see [Templates](#templates)) |

The log is `Butt2KeyRemote-<date>.log` (devices connecting and going, BigButts switching; each press at Debug).

## For client makers

The protocol (version 1: finding PCs, connecting, the messages, drawing a ButtScreen) is in
[API.md](../../../API.md#button2key-remote). `tools/buttclient.py` is a working client in Python, and
`clients/android` a whole one in Kotlin.

**The Android app's code is shared.** Daisy's App Mobile (`D:\OneDrive\Code\Android\DaisysApp`) builds
`clients/android/app/src/main` (code and resources) straight into its own APK, as its Button2Key module (its
`b2kremote/` Gradle module points here; nothing is copied). So a change to the Android client changes both apps:
build and test both, and publish both (`Button2Key-Remote-<version>.apk` and `DaisysApp-Android-<version>.apk`). Keep
it working as a library:
- Use its own `R` (package `com.jasbone.butt2keyremote`).
- Don't rely on its manifest: Daisy's App Mobile declares its activities itself, so a new activity or receiver must
  be added to `b2kremote/src/main/AndroidManifest.xml` there too.
- Leave updates to `Updater.embedded()`, which hides this app's own updates inside the other app.

## Code

| File | |
|---|---|
| `RemoteApplet.cs` | The applet (a sub-tab of Gaming) and its tray menu |
| `ButtHub.cs` | Holds it together: the ButtScreens and settings, the engine and server (handed fresh copies after each change), the open editors and pop-outs, the title bar bubble, the on / off shortcut, the BigButt in use and the game watching (which also switches it on and off with a game) |
| `RemoteView.cs` | The tab: on / off with its shortcut, the BigButts (New, Copy, Rename, Delete, the picked one's games, switching on with a game), the BigButt in use (its ButtScreens in order), devices, the BigButt in use's ButtScreens' pictures (New, Butt Wizard, Link2Butt, Remove or Delete); `ConfirmButton` (the two-click Confirm? button) |
| `EditorWindow.cs` | The layout tool: the ButtScreen's size, grid and colours; the stage (add, select, drag to move, corner handle to resize, keys, right-click menu); the selected button's label, icon, action, cells and look; undo / redo; saving as it changes |
| `Templates.cs`, `Templates/*.json` | Templates: `ButtTemplate` (groups of buttons), `TemplateStore` (the built-in ones in the DLL, yours from the folder, checking, Import, Make it yours, Delete) and `TemplateBuilder` (laying the ticked buttons out on ButtScreens, group by group) |
| `TemplateWindow.cs` | The Butt Wizard: the device, the template, its buttons as tiles to tick (in groups opening and closing), the theme with a picture of the result, and its games for the BigButt in use |
| `RemoteSettingsView.cs` | Settings → Gaming → Button2Key Remote: the templates, Import…, Open the folder, Make it yours, Delete |
| `ScreenVisual.cs` | Draws a ButtScreen at its own pixel size (the editor, the tab's pictures and pop-outs put it in a Viewbox), and finds the cell under a point |
| `PopOutWindow.cs` | A ButtScreen as a widget (Core's `Widget`, keeping its shape through `Aspect`): pressing (mouse and multi-touch), dragging by the gaps, a held finger to edit, its strip's Locked and Edit ButtScreen; `PopOutPlace` (Model.cs) is its `IWidgetPlace` |
| `PressEngine.cs` | Presses and let-gos from anywhere, done on a thread of its own through Button2Key's `ActionRunner` |
| `ButtServer.cs` | `ButtWire` (the encrypted connection) and `ButtServer` (listening, the UDP answer for finding PCs, the messages) |
| `IconLibrary.cs`, `Icons/mdi.txt.gz` | The icon library (name, words, path a line each, built into the DLL) and its search |
| `IconPickerWindow.cs` | Picking an icon (search, rows of icons made only as they scroll into view) |
| `ColorBox.cs` | A colour: palette, #RRGGBB box and how solid |
| `Model.cs` | The settings, BigButts, ButtScreens and buttons, where they're saved, pictures of your own |
| `Butt2KeyRemote.csproj` | References `DaisysApp.Controllers`, whose `Actions/` are Button2Key's actions, keys, profiles and binding window, so the two work the same |

`Icons/mdi.txt.gz` was made from `@mdi/js` and `@mdi/svg`'s `meta.json` (7.4.47; deprecated icons left out): a line per
icon, `name<TAB>aliases and tags<TAB>path`, gzipped.
