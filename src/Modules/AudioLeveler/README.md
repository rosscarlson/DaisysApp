# Audio Tools

*A module of [Daisy's App](../../../README.md): the Audio Tools tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\AudioLeveler\` next to `DaisysApp.exe`.*

Plays a calibrated test signal on any speakers of the selected output device and lets you set each speaker's level,
by hand or automatically with a microphone.

**Speaker map.** Follows the speaker configuration Windows reports for the device (Stereo, Quad, 5.1, 7.1, 7.1.4 …;
numbered speakers if it isn't recognized). Each tile shows the speaker's full name, one word per line ("Front / Left",
"Low / Frequency / Effect"), and, for Voicemeeter devices, the bus channel it's on (**Out 1**, **Out 2** …, matching
Voicemeeter's own channel numbers). The map is a 5 × 5 grid with the listener in the middle: **drag a speaker to any
cell** to match where it really is in your room (e.g. the subwoofer between Front Left and Center, sides in the rear
corners); dropping it on another speaker swaps the two. Positions are saved for each output device. The grid size
(3 × 3 to 9 × 9) and **Reset speaker positions** are in Settings → Audio Tools. To level all six speakers of a 5.1 system, the Windows device you play through
(e.g. *Voicemeeter Input*) must be set to 5.1 in Sound settings → *Configure*.

**Signals.** Pink noise (full range), pink noise 500 Hz–2 kHz (calibration band), white noise, sine 10 Hz–20 kHz. All
play at −20 dBFS RMS (the calibration standard) and are RMS-normalized. The subwoofer (LFE) channel can be low-passed
at 24 dB/octave (30–200 Hz, default 80 Hz).

**Level knobs.** Select a speaker and a knob appears on its tile:
- **Voicemeeter devices:** the level is set in the EQ of the bus you pick under *Output device* (±12 dB). Each bus has
  an 8-channel parametric EQ; the app uses **cells 5 and 6** of a speaker's channel as a low shelf and a high shelf at
  1 kHz with the same gain, which together are a flat volume change. See them in Voicemeeter: right-click the bus's
  **EQ** button. Setting a level turns the bus EQ on; if that bus's EQ was off but has other bands set up, the app
  warns you. This is separate from VB-Audio's 8x8 Matrix (which only works while the Matrix runs); don't level the
  same speakers with both.
- **Other devices:** the level is the Windows per-channel volume (−40 to 0 dB).

**Microphone.** Chosen in the Level Wizard, which shows its live level and **Mic level** (the mic's Windows input
volume; lower it if the meter shows CLIPPING). The Windows default mic is marked **Windows default ·** and is chosen
on first run; any other mic you pick is remembered.

**Load / Save / Reset levels** (the three icon buttons next to **EQ Wizard**; hover for their names). Save and
Load write every speaker's level (and its EQ) to a `*.levels.json` file (by default in
`Documents\Daisy's App`) and set them again from it — handy after resetting Voicemeeter. Levels are matched by channel;
the app tells you if the file came from another device or if a speaker's name has changed.

## Leveling

Before you start: set the device's speaker configuration in Windows, turn off Spatial sound and enhancements, and put
the mic or SPL meter at the listening position, at ear height, pointing at the ceiling.

- **With an SPL meter:** choose *Pink noise — 500 Hz–2 kHz*, solo a speaker, press Play, and turn its knob until the
  meter reads your target (typically 75 dB SPL, C-weighted, slow). Repeat; Auto-cycle steps through them for you.
- **With a mic:** press **Level Wizard**, pick the microphone, tick the speakers, and press **Start**.
  - All ticked speakers start from the same level. The wizard measures the room's background noise, then plays
    band-limited pink noise on each speaker for 2 seconds, showing the live mic reading in that speaker's row.
  - **Baseline** (pass 1): the softest speaker becomes the baseline and the others are turned down to match. No
    speaker is cut by more than 10 dB: if one would need more, all speakers are raised by the difference instead,
    keeping them balanced within ±12 dB. **Leveling** (pass 2) checks every speaker and corrects; **Verifying**
    (pass 3) runs only if one is still more than 0.5 dB out. **Level** shows each speaker's setting.
  - A subwoofer that's too quiet to measure, more than 10 dB quieter than the other speakers, or still unbalanced
    after pass 3 is left out and set back to 0 dB; the result says it may need more power.
  - If the mic clips, its level is lowered by 6 dB and the run starts over (up to six times). Esc or Cancel puts the
    original levels back. It stops with a message if a speaker isn't 10 dB above the noise, or if a level change made
    no measurable difference (usually the wrong Voicemeeter bus).

Mic readings are relative (dB at the mic), not calibrated SPL.

## EQ

**EQ Wizard** measures each speaker's frequency response with the mic and gives that speaker its own EQ: peaking
filters that even out its response at the listening position (mostly cuts for the peaks a room adds in the bass). A
measurement mic such as the Dayton Audio iMM-6 is what this is made for.

- **Where the EQ goes:** for a Voicemeeter device, cells 1–4 of each channel of the bus's EQ (cells 5 and 6 hold the
  speaker's level), so four filters per speaker, kept by Voicemeeter. For any other device Windows has no per-speaker
  EQ, so it needs the free [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) (installed and ticked
  for the device in its Configurator): up to ten filters per speaker, written to `DaisysApp-SpeakerEQ.txt` in its
  config folder and included from `config.txt`, with a preamp to make room for any boost. Either way the EQ stays
  applied without Daisy's App running.
- **Calibration file:** **Load…** the mic's calibration file (for the iMM-6, download it from Dayton Audio's site with
  the serial number on the mic; use the 90° file if there is one, as the mic points at the ceiling). It's kept per
  mic, so it's loaded again next time.
- **Options:** **Target** flat, or a room curve (a few dB more bass, slightly softer treble); **Correct up to** 300 Hz,
  1 kHz (recommended) or 16 kHz; **Most boost** none, 3 dB (default) or 6 dB. Boosts are limited further above
  500 Hz (3 dB) and not used at all at the bottom of a speaker's range; filters above 1 kHz are kept broad.
- **The run:** the EQ on the ticked speakers is cleared, the room's background noise is recorded, then each speaker
  plays full-range pink noise (the subwoofer low-passed) for 6 seconds. Its response is worked out from what the mic
  heard (less the calibration and anything below the background noise), filters are fitted and put in place, and
  every speaker is measured again to check. About 15 seconds per speaker.
- **Results:** **Before** and **After** are how far the response strays from the target over the corrected range
  (RMS); click a speaker for its graph (before, after, the EQ, and the corrected range shaded) and its filters.
  **Remove EQ** takes the EQ off the ticked speakers.
- The EQ changes each speaker's loudness a little, so run the **Level Wizard** again afterwards. Esc or Cancel puts
  the EQ back as it was; the run stops if a big cut made no measurable difference (the wrong Voicemeeter bus, or
  Equalizer APO not enabled for the device).

| Action | How |
|---|---|
| Play / stop | Space or the Play button |
| Select / deselect a speaker | Click its tile |
| Solo a speaker | Right-click its tile, or keys 1–9 |
| Adjust a level | Drag the knob up/down (Shift = fine) or scroll (0.5 dB; Shift = 0.1 dB); double-click = 0 dB |
| Move a speaker on the map | Drag its tile to another cell (onto another speaker to swap) |
| Reset all levels | Reset levels (the ↺ icon), then click again to confirm |

| Problem | Fix |
|---|---|
| Only two speakers on a 5.1 system | Set the playback device (e.g. *Voicemeeter Input*) to 5.1 in Windows Sound settings → *Configure*, then press refresh. |
| Knobs don't change what you hear (Voicemeeter) | Check the bus under *Output device* is the one your speakers are on. |
| Knobs don't change what you hear (other device) | Some drivers ignore per-channel volume; use the physical output device. |
| "Windows blocked microphone access" | Settings → Privacy & security → Microphone → *Let desktop apps access your microphone*. |
| Mic shows CLIPPING | Lower **Mic level**, or turn the speakers down. |
| "Couldn't hear … above the background noise" | Raise the mic level or speaker volume, move the mic closer, or quiet the room. |
