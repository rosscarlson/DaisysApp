# Audio Delay

*A module of [Daisy's App](../../../README.md): the Audio Delay tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\AudioDelay\` next to `DaisysApp.exe`.*

For two outputs that play the same audio but reach you at different times — typically a sound card and a Bluetooth
speaker, or a VBAN stream to another PC, which lag. Choose the two Voicemeeter outputs, the device to play through
(usually *Voicemeeter Input*, whose strip must be routed to both outputs) and a microphone at your listening position,
then press **Start**.

- **Device 1 is the base** and keeps no delay; **Device 2 gets the delay**. If the baseline shows Device 2 is actually
  the later one, the app says so, swaps the two selections and carries on.
- Outputs can be hardware (A1 …) or virtual (B1 …, e.g. sent on over VBAN). Voicemeeter can only delay hardware
  outputs, so a virtual output can be the base but never the delayed one. For a VBAN stream that lags, make it
  Device 1 and the sound card (A1) Device 2.
- It plays a short beep (a 30 ms sweep) on each output in turn, three times each, and times when each reaches the mic,
  so it knows which one is late without any guessing. For each beep the other hardware outputs (and the other selected
  output) are muted in Voicemeeter.
- The delay is Voicemeeter's **output delay** (Menu → System Settings, 0–500 ms). Only one output is ever delayed, to
  keep audio as close to the video as possible.
- **Baseline** measures how far apart they are, **Adjusting** checks after setting the delay, and **Verifying** runs
  only if they're still more than 1 ms apart.
- Mutes are always put back afterwards; Cancel (Esc) or an error also puts the delays back. If the mic clips, its level
  is lowered and the run starts over. **Reset delays** sets the hardware outputs back to 0 ms.
- **Save delays… / Load delays…** write every hardware output's delay to a `*.delays.json` file (by default in
  `Documents\Daisy's App`) and set them again from it, matched by output name (A1, A2 …).
- **Microphones that come through Voicemeeter** (e.g. *Voicemeeter Out B2*) work: that bus is never muted, and if the
  playback strip also feeds it, that route is switched off during the test (and restored) so the beep can't reach the
  "mic" electronically. The mic's bus can't be one of the two outputs being synced. For headphones, hold an earcup
  against the mic.
