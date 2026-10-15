# Gaming

*A module of [Daisy's App](../../../README.md): the Gaming tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\Gaming\` next to `DaisysApp.exe`.*

Two tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **Joy2Key** | Controller buttons, sticks and hats pressing keys, clicking and moving the mouse, a profile per game (like JoyToKey, whose profiles it imports) | [Joy2Key/README.md](Joy2Key/README.md) |
| **WheelEmu** | Your wheel, pedals, shifter, handbrake and button boxes as one vJoy controller for Forza (like Forza EmuWheel): the emulator, Hush to hide the real devices from the game, and the binding tool | [WheelEmu/README.md](WheelEmu/README.md) |

(Not to be confused with Video → **Gaming**, the recorder and overlay.)

Each tool keeps its own settings in `%APPDATA%\DaisysApp\`: `Joy2Key.json` and the `Joy2Key\Profiles` folder, as when
Joy2Key was a module of its own (before 0.53), and `WheelEmu.json`; `GamingTools.json` remembers which tool was
showing (the tab's id is GamingTools, as Video's Gaming tool has had the id Gaming, and `Gaming.json`, since before it
was a sub-tab). Their log is `Gaming-<date>.log`. Updating from an earlier version moves Joy2Key's place among the
tabs, and whether it was switched off, to Gaming.

**Code:** `GamingApplet.cs` (the tab: an `AppletGroup` from DaisysApp.Core, which makes the sub-tabs and runs the
tools); each tool's code is in its folder, `Joy2Key/` and `WheelEmu/`, with its own namespace
(`DaisysApp.Applets.Gaming.Joy2Key` …). The translations for both are in `lang/`. `Gaming.csproj` adds
Vortice.DirectInput, which WheelEmu reads controllers with.
