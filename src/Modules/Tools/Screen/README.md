# Screen

*A tool of the [Tools](../README.md) module (since 3.27.0): its sub-tab, switched on or off in Settings → General →
Modules (the Screen tick under Tools). Its code is in this folder.*

Tools for the desktop's windows. For now one: **Spacebar Drag**.

## Spacebar Drag

As some X window managers on Linux did: **hold the spacebar and click anywhere on a window** to grab it as if by its
title bar; it follows the mouse until the button is let go. Off to start with.

- **On / Off** beside its name switches it; the **joystick** beside that sets a shortcut (a key combination or a
  controller / wheel / button box button) that switches it from anywhere, also listed in Settings → Shortcuts
  (Screen · Spacebar Drag on / off). Each switch is logged in the Tools log.
- **Typing isn't changed:** while it's on, a space is typed when the spacebar is let go rather than when it's
  pressed, unless a window was grabbed (then no space is typed). Typing on (a key pressed while the spacebar is down)
  types the space at once. A space with Ctrl, Alt, Shift or Win (Win+Space, Alt+Space…) works as always. Holding the
  spacebar doesn't repeat it while it's on, and a game's spacebar is held back the same way: switch it off for games
  (the shortcut is for that).
- **Which windows:** any program's, maximized ones too (restored under the pointer as soon as it moves, as Windows
  does), but not the desktop or the taskbar, nor a program running as administrator while Daisy's App isn't (Windows
  doesn't let it move those; the click then goes to the window as usual).
- Only the left button grabs; the click that grabs (and its release) doesn't reach the window, which is brought to
  the front.

The settings are in `%APPDATA%\DaisysApp\Screen.json` (on or off, and the shortcut).

## Code

| File | |
|---|---|
| `ScreenApplet.cs` | The sub-tab: the Windows card with Spacebar Drag's On / Off and joystick (Core's `Hotkeys.Add` and `HotkeyWindow`), switching `SpaceDrag` |
| `SpaceDrag.cs` | Low-level keyboard and mouse hooks on a thread of their own with its message loop (installed only while it's on): the spacebar's states (held back, passed on, used for a grab; a held space counts only while its repeats keep coming, so one whose release was missed can't grab), the window under the pointer (`WindowFromPoint` → its top-level window, the desktop and taskbar left out, a window we may not move found by a first posted `SetWindowPos`), and the move (`SetWindowPos` with `SWP_ASYNCWINDOWPOS`, so a hung window can't hold the hooks up; a maximized one restored with `ShowWindowAsync` at its normal size, at the same share of its width under the pointer) |
| `ScreenSettings.cs` | `Screen.json` |
