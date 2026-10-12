# Audio

*A module of [Daisy's App](../../../README.md): the Audio tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\AudioTools\` next to `DaisysApp.exe`.*

Three tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **Volume** | Volume and mute for every playback and recording device and every app, side by side and live, with volume up / down / mute shortcuts (keys or controller buttons) for any of them | [Levels/README.md](Levels/README.md) |
| **Levels** | Test signals, per-speaker level knobs, microphone leveling and an auto-level wizard, plus an EQ wizard that evens out each speaker's response in the room | [Tests/README.md](Tests/README.md) |
| **Delay** | Brings two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker or VBAN stream) into sync with Voicemeeter's output delay | [Delay/README.md](Delay/README.md) |

Tests and Delay use [Voicemeeter Banana or Potato](https://vb-audio.com/Voicemeeter/) (see the main README). Settings
→ Audio holds the Tests options.

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`, as it did when it was a tab of its own (before 0.15):
`AudioLevels.json` (Levels: the step and the shortcuts), `AudioLevel.json` (Tests) and `AudioDelay.json` (Delay);
`AudioTools.json` remembers which tool was showing.

**Code:** `AudioToolsApplet.cs` (the tab) and `AudioToolsView.cs` (the sub-tabs); each tool's code is in its folder,
`Levels/` (Volume), `Tests/` (Levels) and `Delay/`, with its own namespace (`DaisysApp.Applets.AudioTools.Levels` …);
the folders keep the names the tools had before 0.26. The translations
for all three are in `lang/`.
