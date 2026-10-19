# Gaming

*The Gaming tab of [Daisy's App](../../../README.md). Switch it on or off in Settings → General → Applets. It isn't a
module itself: it's the tab the gaming modules share when two or more are installed, each on its own sub-tab (the one you used last
opens next time). This folder holds only this guide.*

| Tool | What it does | Module and guide |
|---|---|---|
| **Joy2Key** | Controller buttons, sticks and hats pressing keys, clicking and moving the mouse, a profile per game (like JoyToKey, whose profiles it imports) | [src/Modules/Joy2Key](../Joy2Key/README.md), installed to `modules\Joy2Key\` |
| **ForzaWheel** | Your wheel, pedals, shifter, handbrake and button boxes as one vJoy controller for Forza (like Forza EmuWheel): the emulator, Hush to hide the real devices from the game, the binding tool, and the input overlay (which logs your sessions) | [src/Modules/ForzaWheel](../ForzaWheel/README.md), installed to `modules\ForzaWheel\` |
| **Forza Analysis** | The sessions ForzaWheel logs, analysed for smooth braking and throttle (application and release), with the trace, the zones compared, tables, scores and tips (since 1.2.0) | [src/Modules/ForzaAnalysis](../ForzaAnalysis/README.md), installed to `modules\ForzaAnalysis\` |
| **iRacing** | Every race of yours from iRacing's Data API (your own sign-in and OAuth client), summed up: tiles, a summary, ratings over time, race days, places, tracks, cars and series, records and tables (since 1.10.0) | [src/Modules/iRacing](../iRacing/README.md), installed to `modules\iRacing\` |

(Not to be confused with Video → **Overlay** and **Recording**, the old Gaming tool, whose id is still Gaming.)

Until 1.0 both were one module, `modules\Gaming\`; since 1.1.68 each is a module of its own, so each can be downloaded on
its own by the wrapper (see [The wrapper](../../../README.md#the-wrapper)). The tab is described once, in
`Joy2Key/GamingTab.cs` (`[assembly: TabGroup("GamingTools", "Gaming", …)]`), which each of their projects compiles in; each
tool's `[Applet]` names it (`Group = "GamingTools"`). With only one of them installed there's no Gaming tab: that tool
is a tab of its own.

Each tool keeps its own settings in `%APPDATA%\DaisysApp\`: `Joy2Key.json` and the `Joy2Key\Profiles` folder, and
`ForzaWheel.json`, `ForzaAnalysis.json` and `iRacing.json`; `GamingTools.json` remembers which tool was showing (the tab's id is GamingTools, as Video's Overlay
tool has had the id Gaming, and `Gaming.json`, since before it was a sub-tab). Their logs are `Joy2Key-<date>.log` and
`ForzaWheel-<date>.log` (`Gaming-<date>.log` before 1.1.68).
