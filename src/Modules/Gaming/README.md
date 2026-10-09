# Gaming

*A module of [Daisy's App](../../../README.md): the Gaming tab. Switch it on or off in Settings → General → Applets.
Its code is in this folder; it's installed to `modules\Gaming\` next to `DaisysApp.exe`.*

An FPS overlay over your games (frame rate, frame time, video memory and more, in three sizes), screen recording with
the graphics card's own encoder (NVENC on NVIDIA cards, like the NVIDIA app's recorder), and a history of every
game's performance. All of it is built to cost games nothing you could measure: see [Performance](#performance).

## The FPS overlay

A small see-through box, always on top of the game, that never takes the focus (clicking it doesn't pause or minimise
a game). It shows the frame rate of the **game in front**: a program counts as a game the first time it shows frames
full screen (or borderless full screen), and it's then listed in Settings → Gaming → **Games**, where you can give it
a friendly name (`forzahorizon6.exe` → *Forza Horizon 6*) or untick it to ignore it. Browsers, video players and the
like are never taken for games.

**Three modes**, cycled with one shortcut (Simple → Medium → Advanced → Simple):

| Mode | Shows |
|---|---|
| Simple | The frame rate, nothing else |
| Medium | Frame rate, frame time and video memory, each with a small graph beside it (the last minute; frame time: every frame of the last 3 seconds) |
| Advanced | The game's name, session time and clock; frame rate with the **1% and 0.1% lows** over the last 30 seconds; frame time with the worst frame; video memory; each with a bigger graph; GPU load, temperature, clock and power; CPU load (all, and the game's); memory (all, and the game's); the 30-second average |

Each of the three graphs (frame rate, frame time, video memory) can be switched off in Settings → Gaming. While a
recording runs, a red **● REC** and its length show at the top in every mode.

**Moving and sizing:** drag it anywhere; drag any corner to scale everything up or down (the opposite corner stays
put). **Lock in place** (on the tab or in Settings) stops it moving and lets clicks go straight through it to the game.
**Reset position** puts it back in the top-left corner.

**Look** (Settings → Gaming): left- or right-justified (right-justified, it grows to the left, so it can sit in the
top-right corner), background colour and opacity (0% = just the text, with a soft shadow to keep it readable), text
colour, size from 50% to 400%, and whether it shows only while a game is in front (default) or all the time. It's
left out of recordings and screenshots unless you untick that.

**The numbers** come from Windows itself: every time a program hands a finished frame to Windows, Windows' event
tracing reports it, with its exact time (the same source as PresentMon, Intel's and NVIDIA's FrameView, and
CapFrameX). Frame time is the time between frames; the lows are the frame rate at the slowest 1% and 0.1% of frames
(the 99th and 99.9th percentile frame time), which is where stutter shows. DirectX 9 to 12 are counted by DirectX's own
events, Vulkan and OpenGL through the graphics kernel. The GPU's figures come from NVIDIA's management library
(installed with the driver); on other graphics cards only video memory is shown.

### The permission

Windows only lets **administrators or members of the "Performance Log Users" group** read these events. If the app
isn't allowed, the Now card says so and offers **Allow…**: that adds you to the group (Windows asks for an
administrator's OK first), once. Windows only applies it to new sign-ins, so sign out and back in (or restart), then
**Try again**. After that the app never needs to run as administrator.

## Recording

Record a **monitor**, a **region** of one (**Select…**, then drag around it on one monitor; Esc cancels) or a **game or program**
(the game in front when the recording starts, or one you pick; the recording follows its window). Start and stop with
the button on the tab, the tray menu, or the shortcut (Ctrl+Alt+F9 at first). A short note appears on screen when a
recording starts and when it's saved, even with the overlay hidden.

Recordings are MP4 files in `Videos\Daisy's App\` (**Browse Folder** on the tab opens it; **Settings** beside it
jumps to Settings → Gaming's recording options), named after the game and the time, e.g.
`Forza Horizon 6 2026-10-08 21-30-00.mp4`. The PC's sound is recorded, and optionally the microphone as a **second
audio track** (most editors show both; players play the first). The mouse pointer is recorded unless you untick it.

**Quality profiles:** High, Medium and Low to start with, all editable, and you can add your own:

| | Codec | Frame rate | Size | Bit rate |
|---|---|---|---|---|
| High | HEVC | 60 fps | as recorded | VBR, 80 Mbit/s average, 100 peak |
| Medium | HEVC | 60 fps | 1440p | VBR, 40 average, 50 peak |
| Low | H.264 | 30 fps | 1080p | VBR, 15 average, 20 peak |

For each: codec (**H.264**, **HEVC** or **AV1**, whichever the graphics card can encode: GeForce RTX 40 and 50 cards do
all three; older cards H.264 and HEVC), frame rate (30 to 144), size (as recorded, or scaled down to 2160p, 1440p,
1080p or 720p), constant or variable bit rate (average and peak, up to 500 Mbit/s), the encoder preset (Fastest to
Best quality: the encoder chip does the work either way, but the best presets use more of it), the keyframe interval
and the audio bit rate. Settings shows roughly how much disk an hour takes (100 Mbit/s is about 45 GB an hour). H.264
stops at 4096 pixels wide, so super-ultrawide screens are scaled down to fit; HEVC and AV1 take up to 8192.

**HDR monitors:** recordings are SDR by default, converted the way Windows shows SDR content on an HDR monitor (using
its "SDR content brightness"), with HDR highlights rolled off rather than clipped, so they look right anywhere. Or
choose **HDR10** (HEVC or AV1, 10-bit, BT.2020 PQ), like the NVIDIA app's HDR recordings, for HDR screens and
YouTube HDR.

**How it works:** Windows' desktop duplication hands over each new frame of the monitor as a GPU texture; shaders on
the GPU crop and scale it, draw the pointer and convert it to the encoder's YUV format; and the GPU's video encoder
compresses it, through Windows' Media Foundation, into the MP4. The picture never comes back to the processor, and the
encoder is a separate part of the chip from the one that draws the game. If the encoder won't take a setting (too big
a picture, a codec the card can't do), the recording **stops with a message** instead of quietly using Windows'
software encoder, which would take the processor away from the game. If the PC can't keep up, frames are skipped and
the tab says how many.

Frames are taken from the monitor, as the NVIDIA app does in its desktop mode. So a **game or program** recording is its
window's area of the screen: anything on top of it (a notification, another window) is recorded too. Games in true
exclusive full screen (rare now; most use borderless or "full screen optimizations") may not show the overlay, and
recording them can stop when they switch modes.

## History

While a game is in front, a line a second goes into its log: frame rate, frame time, 1% low, the worst frame, GPU load,
temperature, clock and power, video memory, CPU load (all and the game's), memory, and whether it was being recorded.
When you stop playing (the game closes, another game comes to the front, or it's been in the background for 5 minutes)
the session's summary is added too: when, how long, average FPS, 1% and 0.1% lows, average frame time, GPU load,
hottest GPU temperature and most video memory. Sessions shorter than 30 seconds are left out.

The tab's **Game history** card shows, for each game, a graph of every session's average and 1% low, the list of
sessions, and a graph of the chosen session second by second (frame rate, 1% low and GPU load), so you can see how a
driver, a patch or a setting changed things.

The files are in `%LOCALAPPDATA%\DaisysApp\logs\gaming\`: `sessions.csv`, and a folder per game with a CSV a month
(`forzahorizon6\2026-10.csv`). About 350 KB an hour of play; months older than the setting (12 by default) are
deleted. They open in Excel.

## Shortcuts

| Shortcut | Does |
|---|---|
| Ctrl+Alt+F9 | Start / stop recording |
| Ctrl+Alt+F10 | Show / hide the overlay |
| Ctrl+Alt+F11 | Next overlay mode |

Change them in Settings → Gaming. They work anywhere, in games too. (The NVIDIA app uses Alt+F9 and Alt+Z; a shortcut
another program already has is flagged in Settings.) The tray menu has the same three.

## Performance

The app never touches the game: nothing is injected into it, and the numbers are Windows' own.

- **Frame counting** is Windows' event tracing, delivered in batches to one background thread: a few microseconds a
  frame. It only runs while the overlay is switched on or the history is.
- **The overlay** is one small window redrawn twice a second; the hardware is read once a second (a handful of quick
  calls), only while a game is in front, the overlay is up or the tab is open.
- **Recording** keeps the picture on the graphics card from screen to file, and the encoding is done by the card's
  video encoder, not the part that draws the game.

Measured with a test "game" at 144 fps: the module adds about 2% of one CPU core with the Advanced overlay showing
(about 0.2% of a 12-thread processor) and under 1% of a core when only logging. Recording the 5120×1440 monitor in
HEVC at 60 fps cost a test program running flat out 1.6% of its frame rate.

The overlay is a window over the game, like the Xbox Game Bar's widgets, so Windows composes the two. With a game in
"independent flip" (exclusive-like full screen) that can add a little display latency. For competitive play where that
matters, hide the overlay with its shortcut.

## Settings (Settings → Gaming)

- **FPS overlay:** show only while a game is in front; leave it out of recordings and screenshots; which graphs to
  show; justify left or right; background colour and opacity; text colour; size; lock in place.
- **Recording:** folder; the PC's sound; the microphone as a second track; the mouse pointer; HDR as SDR or HDR10; the
  quality profiles (new, delete, reset to the defaults, and every setting of each).
- **Shortcuts:** the three above.
- **Games:** every program recognised as a game: its friendly name, whether it counts as a game, and remove.
- **History:** on or off, how long to keep it, open the folder.

Saved in `%APPDATA%\DaisysApp\Gaming.json`.
