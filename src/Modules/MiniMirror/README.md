# Mini Mirror

*A module of [Daisy's App](../../../README.md): the Mini Mirror tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\MiniMirror\` next to `DaisysApp.exe`.*

Draw a rectangle or circle around any part of the screen and see it live in its own borderless, always-on-top window
that you can put anywhere — a game's track map, delta bar or a corner of its HUD on another monitor, a companion app
next to the game, and so on. Ported from the [MiniMirror SimHub plugin](https://github.com/rosscarlson/SimHub-MiniMirror);
it no longer needs SimHub.

**Making a mirror.** Press **Ctrl+Shift+F8** from anywhere — in a game, it stays in front and keeps the focus, so it
doesn't pause — or **New mirror** (or **New mirror…** in the tray icon's Mini Mirror menu), which gets Daisy's App out
of the way. Every screen dims; drag around what you want to mirror — the drag can cross monitors. Press **R** or
**C** (before or during the drag) for a rectangle or a circle. The outline can still be moved and resized; then press
**Confirm** or Enter (**Cancel** or Esc backs out). The mirror appears on top of the region; drag it where you want it.
New mirrors go at the top of the list, outside any group.

**Groups.** **New group** makes a named set of mirrors — say one game's track map, delta bar and rev lights — that
show and hide together. Drag mirrors in the list onto a group (or onto a mirror already in it, to put it just
above) to put them in it, and onto **Not in a group** to take one out; groups can be dragged up and down too. Pick a
group in the list to rename it, show or hide all its mirrors, give it a **show / hide shortcut** (a key combination or
a wheel / controller button: it hides the group if any of its mirrors is showing, otherwise shows them all), or delete
it (its mirrors stay). Each mirror can still have its own shortcut. The tray menu has a submenu per group.

**The mirror window.** Drag it to move it, drag an edge or corner to resize it. Hold **Alt** while dragging to snap its
edges to other mirrors' edges, to line up a row or column. Mirrors aren't in the taskbar or Alt+Tab.

**Settings for each mirror** (pick it in the list on the Mini Mirror tab):

| Setting | What it does |
|---|---|
| Name | Shown in the list and the tray menu |
| Re-select region | Drag around a new area for this mirror |
| Duplicate | Copies the mirror and all its settings (except the shortcut) into a new one, slightly offset, in the same group |
| Delete | Click twice to confirm |
| Show this mirror | Shows / hides it (also its shortcut, the tray menu, **Show all** / **Hide all**) |
| Shape | Rectangle, or a circle cut out of the region |
| Window size | The window's size relative to the region (0.1× – 5×); double-click the slider for 1× |
| Zoom | Magnifies the middle of the region (up to 8×) or takes in more around it (down to 0.2×), without changing the window size |
| Opacity | 10 – 100 % |
| Frame rate | New mirrors start at their monitor's refresh rate (e.g. 120 / 144 Hz); lower it to save CPU |
| Lock position | Stops it being moved or resized by accident |
| Click-through | Clicks go to whatever is underneath |
| Keep proportions | Corner drags keep the window's shape |
| Show / hide shortcut | A key combination (with Ctrl, Alt or Shift) **or a wheel / button box / controller button**, working system-wide, also in games. Mirrors sharing a shortcut toggle together |

**Settings → Mini Mirror:**
- **New mirror shortcut** — Ctrl+Shift+F8 by default; any key combination or a wheel / controller button. While a
  region is being picked, Esc, Enter, R, C and Tab are taken over system-wide (the game keeps the focus, so the keys
  can't go through it), and handed back when it's done.
- **Convert HDR monitors to SDR** (on) — with Windows HDR on, a plain capture of the desktop looks washed out. Mini
  Mirror asks Windows for the HDR picture and converts it on the GPU the way Windows shows SDR content, using the
  monitor's *SDR content brightness* (Settings → Display → HDR), so an SDR game in a mirror looks like the game.
  Very bright HDR highlights are clipped.
- **Hide mirrors from screenshots, recordings and streams** — mirrors are left out of screen captures (needs Windows 10
  2004 or later). This also stops a mirror sitting over the area it mirrors from showing itself over and over. Leave it
  off if you want mirrors in a whole-screen OBS capture.
- **Import mirrors from the SimHub plugin** — copies the mirrors made in SimHub (from
  `<SimHub>\PluginsData\Common\MiniMirrorSettings.json`, which SimHub writes when it closes). SimHub hotkeys can't come
  across, so set shortcuts again. Remove the plugin from SimHub afterwards so you don't get two of each mirror.

**How it works.** Each monitor is captured with DXGI Desktop Duplication (one capture per monitor, shared by all its
mirrors, started only while a mirror needs it, and skipping frames where nothing changed). Each mirror composes its
region (cropped by zoom) at its own frame rate and draws it into its window. Everything is in physical pixels, so
mixed-DPI monitor setups line up. A mirror whose monitor is unplugged is moved back onto a screen.

**Limits.** Controller buttons use the classic Windows joystick interface: up to 16 controllers and buttons 1–32 each.
Content Windows protects from capture (some video players, or apps that exclude themselves) shows as black. A
full-screen *exclusive* game can't be captured — use borderless / windowed full screen.
