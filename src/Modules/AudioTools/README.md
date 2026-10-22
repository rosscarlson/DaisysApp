# Audio

*A module of [Daisy's App](../../../README.md): the Audio tab. Switch it on or off in Settings → General → Modules. Its code is in this folder; it's installed to `modules\AudioTools\` next to `DaisysApp.exe`.*

Three tools, each on its own sub-tab at the top of the tab (the one you used last opens next time):

| Tool | What it does | Guide |
|---|---|---|
| **Volume** | Volume and mute for every playback and recording device and every app, side by side and live, with volume up / down / mute shortcuts (keys or controller buttons) for any of them | [Levels/README.md](Levels/README.md) |
| **Levels** | Test signals, per-speaker level knobs, the microphone's level and a live analyser in the middle of the speaker map, microphone leveling and an auto-level wizard, plus an EQ wizard that evens out each speaker's response in the room | [Tests/README.md](Tests/README.md) |
| **Delay** | Brings two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker or VBAN stream) into sync with Voicemeeter's output delay | [Delay/README.md](Delay/README.md) |

Levels and Delay use [Voicemeeter Banana or Potato](https://vb-audio.com/Voicemeeter/) (see [Voicemeeter](#voicemeeter)
below). Settings → Audio holds the Volume and Levels options, two cards by two (Volume, Voicemeeter; Level Wizard, Speaker map).

Each tool keeps its own settings file in `%APPDATA%\DaisysApp\`, as it did when it was a tab of its own (before 0.15):
`AudioLevels.json` (Volume: the step and the shortcuts), `AudioLevel.json` (Levels) and `AudioDelay.json` (Delay);
`AudioTools.json` remembers which tool was showing. Saved levels and delays go to `Documents\Daisy's App\` by default
(`*.levels.json`, `*.delays.json`); the wizards' run records to `%APPDATA%\DaisysApp\Logs\`.

**Code:** `AudioToolsApplet.cs` (the tab) and `AudioToolsView.cs` (the sub-tabs); each tool's code is in its folder,
`Levels/` (Volume), `Tests/` (Levels) and `Delay/`, with its own namespace (`DaisysApp.Applets.AudioTools.Levels` …);
the folders keep the names the tools had before 0.26. The translations
for all three are in `lang/`.

## Voicemeeter

**Levels** and **Delay** rely on **[Voicemeeter Banana or Potato](https://vb-audio.com/Voicemeeter/)**
(standard Voicemeeter lacks the per-channel bus EQ these tools use). A banner at the top of both appears whenever
Voicemeeter isn't installed (**Get Voicemeeter**), isn't running (**Start Voicemeeter**), or is the standard edition;
it disappears by itself, and the tab refreshes, once Voicemeeter is up.

Everything the tools set — speaker levels and output delays — is stored **in Voicemeeter's own settings**, so it stays
applied whether or not Daisy's App is running. Save / Load buttons on both keep a copy in a file too.

> **Why Voicemeeter?** Windows does have per-channel volume (Sound settings → device → Levels → Balance), and the
> Levels tool uses it for ordinary output devices. But Voicemeeter's virtual devices ignore it, and Voicemeeter
> usually drives the sound card in a way that bypasses it, so once audio goes through Voicemeeter, its per-channel EQ
> is the only place a level actually takes effect. Windows has no per-device delay at all.
