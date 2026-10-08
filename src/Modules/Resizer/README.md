# Resizer

*A module of [Daisy's App](../../../README.md): the Resizer tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\Resizer\` next to `DaisysApp.exe`.*

Saves a window size and position for a program and puts its window there on demand — typically to stretch a game
across several monitors without Nvidia Surround / Eyefinity, or to force a size the game doesn't offer. Ported from
[Resize Rabbit](https://github.com/rosscarlson/resize-rabbit) (itself based on Resize Raccoon by mistenkt).

**Profiles.** **New profile** opens the editor:
- **Program** — pick a running program (tick *Show all processes* to see ones without a window) or type its exe name.
- **Window** — width, height and position in pixels, measured from the top-left of the main display (screens to its
  left are negative). Leave width and height empty to only move the window. **Copy from a preset** fills in triple
  1080p / 1440p / 4K; **Use current window** fills in the program's window as it is now.
- **Remove borders** strips the window frame; **Remove title bar (Store / UWP games)** also moves the leftover title
  strip of games like Forza Horizon 4 above the screen (does nothing for other games).
- **Apply automatically when the program starts**, with an optional wait, and a **Shortcut**.
- **Apply now** tries the values without saving.

**Groups.** **New group** creates one; a profile joins it from the editor's **Group** list. A group's shortcut (and its
apply button) applies every member whose program is running. Groups and profiles are ordered with the ↑ / ↓ buttons.

**Applying.** A green dot marks profiles whose program is running. A profile is applied by its apply button, its
shortcut (system-wide, also with the app in the tray), the tray icon's **Resizer** menu, a script or Stream Deck
command, or automatically when the **process watcher** (Resizer tab) is on. Several profiles can share a shortcut: it
applies whichever of their programs are running.

```
echo apply-profile "Profile name" > \\.\pipe\resize-rabbit
echo apply-group "Group name" > \\.\pipe\resize-rabbit
echo show > \\.\pipe\resize-rabbit
```

The pipe has Resize Rabbit's name so existing scripts keep working (`\\.\pipe\daisysapp-resizer` also works).

**How applying works.** The program's largest visible window is used (a program can own several); if no process has
the exe name, a window whose title contains the profile name is tried (some games' windows belong to a differently
named process). The window is moved and checked; if the program has no window yet it tries again every 5 s (twice),
and for ~35 s afterwards it re-applies if the game moves itself back. A program running as administrator can only be
moved if Daisy's App runs as administrator too.

**Moving from Resize Rabbit.** On first run the Resizer imports Resize Rabbit's profiles, groups and watcher setting
from this PC; Settings → Resizer can import again (or from Resize Raccoon). While Resize Rabbit is still running it
keeps its shortcuts and the pipe, so close it (and stop it starting with Windows) once your profiles are here.
