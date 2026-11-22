# SimEffects → Recordings

*A tool of [SimEffects](../README.md) (since 4.20.0). Its settings file is `SimRecordings.json`.*

Records a session as you play (the game's telemetry and a video of the screen) and plays it back later as if the game
were running, so the Shaker and the other effects can be tuned while watching instead of driving. Separate from
[Analysis](../Analysis/README.md), which logs races.

## Recording

- **Record** (the red dot right of Play, at the right of the sub-tabs on every SimEffects tab) starts and stops one;
  its shortcut is the second controller button there (also in Settings → Shortcuts: SimEffects · Record / Stop).
- The video is Video → Recording's, as its Record card and settings have it, saved in its folder; the telemetry is in
  `%LOCALAPPDATA%\DaisysApp\SimRecordings\` (a `.frames` and a `.json` each; Settings → General → Storage has them). Every
  frame the game sends is kept, for any game SimEffects reads. The profile doesn't have to be playing.
- Greyed (its tooltip says why): no game in the profile, Video → Recording switched off, a race's video being recorded
  (Analysis' **Record each race**), or a recording playing. A race beginning with Record each race on stops a recording (it's
  kept) and the race's video starts. Another game's profile, or the video stopping, stops it too.
- **Recording** is in SimEffects' title bar bubble, and the tray icon's centre is red, while it records.

## Playing

- The list has every recording (its name, when, how long, game and car), the newest first. The one picked is beside it:
  **Play / Pause** (or a click on the picture, or a double-click in the list), a slider along it, **Stop playback**,
  **Loop**, and the video's volume. The icon at its top right pops it out into a window of its own, to watch while on the
  Shaker's tab; it goes on playing on any tab.
- While one plays, every tool reads it in place of the game: the Shaker, Notify and the Wheel's lights, the Overlays (the
  recording's pedals and wheel, even with Read From set to a device) and Analysis' blocks. The profile it was recorded with (else
  its game's) is used and played, so what's changed is kept in that profile; Stop playback puts back the profile that was
  in use, playing or stopped as it was. Status lines say *Forza Horizon (recording): driving*.
- Paused, it's as the game paused: the Shaker quiet, the rest hold. The profile's Stop pauses it; picking another game's
  profile, or a game starting (Auto Start), stops it.
- Nothing is recorded from it: no laps, race logs or videos, and no redlines learned.
- **Sync**: the telemetry and video line up by when each began; **−0.1 s** to **+0.1 s** move the telemetry in the video,
  kept per recording (**Auto**: as recorded).
- **Rename**, **Show Video** (in Explorer), **Delete** (Confirm?; its video too).

## Code

| File | What it is |
|---|---|
| `RecordingsApplet.cs` | The tab (`RecordingsView`: the list, and the one picked) and Record with its shortcut (`RecordButton`, put in the header by `ProfilesApplet.Picker`) |
| `RecordingPlayer.cs` | The video and its controls: Analysis' `VideoFrames` and `VideoSound`, following `SimPlayback`'s clock (put right when more than 0.25 s off) |
| `SimRecorder.cs` | Recording: Core's `ScreenRecordings` and a thread writing the game's frames; `CantRecord`, and `MakeWay` for a race's video (Analysis' `RaceVideo`) |
| `SimPlayback.cs` | Playing: the clock, the profile used and put back, `SimData.Play` |
| `PlaybackSource.cs` | `PlaybackClock`, and the recording as an `ITelemetrySource`: each frame as the clock comes to it, marked `TelemetryFrame.Playback`; not driving while paused, at a jump and at the end |
| `FrameFile.cs` | The `.frames` file: `FrameColumns` (every public field of `TelemetryFrame` by reflection, so a new field is recorded by itself), `FrameWriter` (gzip; columns named in the file, texts and lists written when they change; one cut short by a crash plays up to its last seconds), `FrameData` (reading) |
| `SimRecording.cs` | One recording (its json) and `RecordingsSettings` |
