# Audio Levels

*A module of [Daisy's App](../../../README.md): the Audio Levels tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\AudioLevels\` next to `DaisysApp.exe`.*

Every connected playback and recording device with its Windows volume (0–100, the same number as Sound settings) and a
mute button. The default devices are listed first, marked **Default ·**. The sliders follow the devices live: if
something turns a device down (a Bluetooth headset or speaker resetting its volume, another app, the device's own
buttons) the slider moves, so you can see it and put it back. Drag a slider or scroll over it (Shift = 1 at a time);
click the speaker to mute, or the number to set it straight to 100.
Devices appear and disappear as they're connected; the refresh button looks again.
