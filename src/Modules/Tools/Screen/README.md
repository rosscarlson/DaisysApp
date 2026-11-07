# Screen

*A tool of the [Tools](../README.md) module (since 3.27.0): its sub-tab, switched on or off in Settings → General →
Modules (the Screen tick under Tools). Its code is in this folder.*

Tools for the desktop's windows: **Spacebar Drag** and **Always on top**; and **Black screens**. Each has a card of
its own (since 4.10.6) with On / Off, its shortcut and its settings; the cards sit side by side, up to 5 in a row, then
in as few rows as that takes with the cards shared out evenly (6: two rows of 3).

Every shortcut here is Core's (`Hotkeys`: a key combination by `RegisterHotKey`, a controller button by Core's
joystick polling), so pressing one costs nothing of this sub-tab's own. Spacebar Drag and Always on top also set
low-level hooks of their own while they're on: those aren't for their shortcuts but for what they do (the held
spacebar and the drag; the title bar menu's pick).

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

## Always on top

(Since 3.37.1.) Keeps a window above the others, as Windows' own Task Manager can. Off to start with.

- **On / Off** beside its name puts **Always on top** in every window's title bar menu: the one Windows shows on a
  right-click on the title bar, or Alt+Space. Pick it (by mouse, Enter or its T) and the window stays on top; it's
  ticked while it is, and picking it again lets the window go. Switching it off takes the item out of the menus again
  (windows on top stay so).
- **The joystick** sets a shortcut (a key combination or a controller / wheel / button box button) that puts **the
  window in front** on top, or not, whether the item is on or off; also in Settings → Shortcuts (Screen · Always on
  top: the window in front). A shortcut is the way for programs that draw a title bar menu of their own (Chrome, Edge,
  some new Windows apps): their right-click doesn't show Windows' menu (Alt+Space usually still does).
- Each change is logged in the Tools log, with the window's title. A program running as administrator can't be
  changed while Daisy's App isn't (the log says so).

## Black screens

(Since 4.10.4.) Every monitor black at once, nothing on them (no wallpaper, no taskbar, no pointer): for OLED
displays, as good as off without switching them off.

- **On / Off** beside its name switches the feature on (since 4.10.5; kept across starts): it doesn't black the
  monitors itself, so they never go black with no way back. Off also brings them back if they're black.
- **The joystick** sets its shortcut (a key combination or a controller / wheel / button box button), also in Settings
  → Shortcuts (Screen · Black screens on / off): while it's on, press it to turn every monitor black, and again to
  bring them back. A **double-click** on a black screen also brings them back.
- **All screens** (ticked to start with) blacks every monitor. Untick it and the monitors are drawn as they're laid
  out, each with its number: click one to leave it out (grey) or put it back (black). They're all picked the first
  time; picked or not, it's kept (by Windows' name for the monitor, `DISPLAY2`…) and changes at once while black.
- It stays on top (the taskbar or a game can't come above it) and covers a monitor plugged in while it's on. Black
  monitors aren't kept: the app always starts with the screens as they are. Each switch is logged in the Tools log.

The settings are in `%APPDATA%\DaisysApp\Screen.json` (on or off, the shortcuts and the monitors picked).

## Code

| File | |
|---|---|
| `ScreenApplet.cs` | The sub-tab: a card each (`Card`) for Spacebar Drag, Always on top and Black screens with On / Off and a joystick, in a `UniformGrid` of up to 5 columns (`Grid`), Black screens' All screens box and picker (Core's `Hotkeys.Add` and `HotkeyWindow`), switching `SpaceDrag`, `TopMost` and `Blackout` (which lets the shortcut black the monitors); Always on top's shortcut toggles the window in front |
| `SpaceDrag.cs` | Low-level keyboard and mouse hooks on a thread of their own with its message loop (installed only while it's on): the spacebar's states (held back, passed on, used for a grab; a held space counts only while its repeats keep coming, so one whose release was missed can't grab), the window under the pointer (`WindowFromPoint` → its top-level window, the desktop and taskbar left out, a window we may not move found by a first posted `SetWindowPos`), and the move (`SetWindowPos` with `SWP_ASYNCWINDOWPOS`, so a hung window can't hold the hooks up; a maximized one restored with `ShowWindowAsync` at its normal size, at the same share of its width under the pointer) |
| `TopMost.cs` | Always on top: the item put into a window's system menu from outside (`GetSystemMenu` + `InsertMenu`, above the line over Close, ticked to match `WS_EX_TOPMOST`) when it comes to the front or is right-clicked; the pick seen without a hook in the program: WinEvents for the menu showing (its `MN_GETHMENU` holding our item), then low-level hooks for a click (or Enter) with our item lit (`MF_HILITE`), or its T; `SetWindowPos` `HWND_TOPMOST` / `HWND_NOTOPMOST`. On its own thread, only while it's on; switching off deletes the item from every menu it went into. `Toggle` and `Front` serve the shortcut |
| `Blackout.cs` | Black screens: a black borderless topmost tool window per monitor (`EnumDisplayMonitors`, placed in physical pixels with Core's `WindowTools.SetBounds`, twice for a monitor of another DPI), no pointer, a double-click ends it; every 2 s back on top, and rebuilt if the monitors changed; only over the monitors `Covers` picks (`Displays`: `EnumDisplayMonitors` + `GetMonitorInfo` for the device name), `Refit` when the pick changes |
| `ScreenPicker.cs` | Black screens' monitors drawn to scale on a canvas as they're laid out, numbered; a click (or Space / Enter) picks one or not; redrawn on resize and `SystemEvents.DisplaySettingsChanged` |
| `ScreenSettings.cs` | `Screen.json` |
