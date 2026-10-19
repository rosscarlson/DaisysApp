# Video: Overlay and Recording

*A tool in [Daisy's App](../../../../README.md)'s [Video](../README.md) tab: its **Overlay** and **Recording** sub-tabs (one sub-tab, Gaming, before 0.25). Switch Video on or off in Settings → General → Modules. Its code is in this folder, part of the Video module (installed to `modules\Video\` next to `DaisysApp.exe`).*

An FPS overlay over your games (frame rate, frame time, video memory and more, in three sizes), screen recording with
the graphics card's own encoder (NVENC on NVIDIA cards, like the NVIDIA app's recorder), and a history of every
game's performance. All of it is built to cost games nothing you could measure: see [Performance](#performance).

## The sub-tabs

The cards use the whole width of the window.

- **Overlay:** **Now** (the game in front, its frame rate, 1% low over 30 seconds and frame time) and **FPS overlay**.
- **Recording:** **Performance** on the left (GPU load, video memory, CPU load and memory in use, with graphs of the
  last two minutes: GPU and CPU load, and memory and video memory as a share of the total), as tall as **Record** and
  **Benchmark** stacked on the right; under them, the whole width, **History**: pick an app for a graph of every
  session's average and 1% low, the whole width; under it, its sessions listed on the left, newest first and short
  (*10/9 7:19 PM - 13 min - 95 FPS*), beside the picked session's graph, with its average frame rate and, on the line
  under that, its other figures (1% and 0.1% lows, GPU load, top temperature) centred above it. Hover over either graph
  to read the values at that point. The Record card also has **Add Camera** and **Start virtual webcam** (below).

Settings → Video splits the same way: **Overlay** has the overlay's look, the shortcuts and the games; **Recording**
the recording options and the history. In the tray menu, **Overlay** shows or hides the overlay and changes its mode,
and **Recording** starts and stops a recording or a benchmark.

## The FPS overlay

A small see-through box, always on top of the game, that never takes the focus (clicking it doesn't pause or minimise
a game). It shows the frame rate of the **game in front**: a program counts as a game the first time it shows frames
full screen (or borderless full screen), and it's then listed in Settings → Video → Gaming → **Games**, where you can give it
a friendly name (`forzahorizon6.exe` → *Forza Horizon 6*) or untick it to ignore it. Browsers, video players and the
like are never taken for games.

The overlay is **off** on a new install: switch it on with **Show the overlay** (or in the setup wizard's Gaming step, or with its shortcut).

**Three modes**, cycled with one shortcut (Simple → Medium → Advanced → Simple):

| Mode | Shows |
|---|---|
| Simple | The frame rate, nothing else |
| Medium | Frame rate, frame time and video memory, each with a small graph beside it (the last minute; frame time: every frame of the last 3 seconds) |
| Advanced | The game's name, session time and clock; frame rate with the **1% and 0.1% lows** over the last 30 seconds; frame time with the worst frame; video memory; each with a bigger graph; GPU load, temperature, clock and power; CPU load (all, and the game's); memory (all, and the game's); the 30-second average |

**While recording**, the overlay shows it too: in Simple, just a solid deep-red light; in Medium, the light, **REC**
with the time, and the file's size; in Advanced, also the bit rate so far, what's recorded (size, frame rate, codec),
the frames **written** in the last second against the target and the frames **dropped**, and what the recording
**costs**: the graphics card's video encoder load, Daisy's App's share of the GPU for converting the picture (and the
cameras), and its CPU. The GPU figures are Windows' GPU engine counters, the ones Task Manager shows.

Each of the three graphs (frame rate, frame time, video memory) can be switched off in Settings → Video → Gaming. While a
recording runs, a red **● REC** and its length show at the top in every mode.

**Moving and sizing:** drag it anywhere; drag any corner to scale everything up or down (the opposite corner stays
put). **Lock in place** (on the tab or in Settings) stops it moving and lets clicks go straight through it to the game.
**Reset position** puts it back in the top-left corner.

**Look** (Settings → Video → Gaming): left- or right-justified (right-justified, it grows to the left, so it can sit in the
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
isn't allowed, the setup wizard (after installing, or from Settings → Run the setup wizard) has a Gaming step for it, and the Now card says so and offers **Allow…**: that adds you to the group (Windows asks for an
administrator's OK first), once. Windows only applies it to new sign-ins, so sign out and back in (or restart), then
**Try again**. After that the app never needs to run as administrator.

## Cameras and the virtual webcam

**Add Camera** (on the Record card; it reads **Cameras (n)…** once there are some) opens the camera layout. The canvas
is the capture monitor at its own resolution, with a snapshot of the screen behind; each camera is a live box on it.
**Add a camera** puts the chosen one on (up to four, any mix of webcams); its box takes the camera's shape. Drag a box
to move it; drag one of the handles on its corners and sides to resize it from there, keeping its shape (**Lock
aspect**, on to start); hold **Alt** and drag a handle to **crop** the camera's picture at that edge (the picture
stays where it is and the box trims to it; Alt and drag back out to uncrop, or **Reset the crop**). **Right-click** a
camera for **Lock aspect**, **Rotate 90° / 180° / 270°** (or none), **Flip horizontal**, **Flip vertical**, Reset the
crop and Remove. **Snapping:** moving or resizing a camera, its edges snap onto the picture's edges and onto the
other cameras' edges (a line shows where), and a resize snaps to another camera's exact height or width, so two
cameras side by side come out the same size. For the camera picked in the list, **Rounded corners** goes from square (0%) to a pill or circle
(50%; 20% to start), **Mirror** flips it left to right, and **Hide / show shortcut** (a key combination or a
controller / button box button) takes that camera off the recording and the virtual webcam and puts it back, while
they run. Give several cameras the same shortcut and they hide and show together; the list shows each camera's, and
the Record card's button reads **Cameras (n, m hidden)…** while some are hidden (they're all back on when the app
restarts). The canvas is the recorded picture in the **Shape** chosen
under Record, so it shows what the recording will look like. A camera fills its box, cropped rather than squashed. **Save** keeps the layout:
every recording then has the cameras over the picture, in the same places (as fractions of the picture, so they sit
the same on a region or a game's window). Remove them all to record without.

**Start virtual webcam** shows the same picture, cameras and all, as a webcam called **Daisy's App Camera**, which
Discord (User Settings → Voice & Video → Camera), Teams, OBS or a browser can pick. It runs at 30 frames a second,
the picture fitted inside 1920×1080 keeping its shape (a 5120×1440 monitor comes out 1920×540). The camera is a
DirectShow filter (softcam, MIT, built by `tools/build-softcam.ps1`), 64- and 32-bit, which the installer registers
with Windows; if it isn't registered, starting it does (Windows asks for an administrator's OK, once).

**Sound for Discord:** a webcam carries no sound, and Discord takes sound only from a microphone. Starting the webcam
asks whether to send the game's sound: pick the playback device the game plays on, and a virtual cable to send it to
(Voicemeeter's AUX or VAIO3 input, or VB-CABLE); then in Discord choose the cable's other end (e.g. *Voicemeeter Out
B2* or *CABLE Output*) as the input device. Only that device's sound goes, so pick one only the game plays on. The
choice is remembered, and the tray menu's **Recording → Start the virtual webcam** uses it.

## Recording

Record a **monitor**, a **region** of one (**Select…**, then drag around it on one monitor; Esc cancels) or a **game or program**
(the game in front when the recording starts, or one you pick; the recording follows its window). Start and stop with
the button on the tab, the tray menu, or the shortcut (Ctrl+Alt+F9 at first). A short note appears on screen when a
recording starts and when it's saved, even with the overlay hidden.

Recordings are MP4 files in `Videos\Daisy's App\` (**Browse Folder** on the tab opens it; **Settings** beside it
jumps to Settings → Video → Gaming's recording options), named after the game and the time, e.g.
`Forza Horizon 6 2026-10-08 21-30-00.mp4`. The PC's sound is recorded, and optionally the microphone as a **second
audio track** (most editors show both; players play the first). The mouse pointer is recorded unless you untick it.

**Mute virtual webcam** (beside Start virtual webcam, in the tray menu, or its shortcut: Ctrl+Alt+F8 to start; the
keyboard button beside Mute sets it, to a key combination or a controller / button box button, as does Settings →
Video → Overlay): the webcam keeps
running, so Discord and the rest don't think it's gone, but shows a black picture with a large red outline and
**VIDEO MUTED / FUCK OFF** in it. Press again to unmute; it stays muted across a restart of the webcam until then.

**Shape** (under Quality on the Record card): **As captured**, or the middle of what's captured in **32:9**, **21:9**,
**16:9**, **4:3** or **9:16** (a phone held upright). The size follows from what's captured: 5120 × 1440 at 16:9 records
2560 × 1440 (the middle), and the line below the box says what it comes to; the quality profile's own size then
applies on top. The virtual webcam shows the same shape.

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

The tab's **Performance history** card shows, for each game, a graph of every session's average and 1% low, the list of
sessions, and a graph of the chosen session second by second (frame rate, 1% low and GPU load), so you can see how a
driver, a patch or a setting changed things.

The files are in `%LOCALAPPDATA%\DaisysApp\logs\gaming\`: `sessions.csv`, and a folder per game with a CSV a month
(`forzahorizon6\2026-10.csv`). About 350 KB an hour of play; months older than the setting (12 by default) are
deleted. They open in Excel.

## Upscaling and frame generation

In **Advanced** mode the overlay (and the tab's **Now** card) shows which upscaling and frame generation libraries the
game has loaded, with their versions: e.g. **DLSS 3.8.10 · DLSS Frame Gen 3.8.10 · Reflex**, or FSR, FSR Frame Gen,
XeSS, XeSS Frame Gen, Ray Reconstruction. It's the list of the game's loaded files, the same as Process Explorer shows,
looked at when the game comes to the front and every 15 seconds; nothing is read from or put into the game. Loaded
isn't always switched on (a game can load DLSS and render without it), and a game whose anti-cheat hides its files shows
nothing. Switch it off in Settings → Video → Gaming → FPS overlay.

Which mode the game is in (DLSS Quality, Balanced, Performance…, the render resolution, whether Frame Generation is on)
only the game itself knows; reading it would mean injecting code into the game, which anti-cheat can punish. NVIDIA's
own **DLSS indicator** shows it: tick **NVIDIA's DLSS indicator in games** in Settings → Video → Gaming (on PCs with an NVIDIA
driver) and the driver writes the DLSS mode, render resolution and version, and Frame Generation's state, in a corner of
every DLSS game. It's a driver setting (`ShowDlssIndicator` and `DLSSG_IndicatorText` under
`HKLM\SOFTWARE\NVIDIA Corporation\Global\NGXCore`), so Windows asks for administrator approval; a game picks it up
the next time it starts.

## Benchmark

A benchmark measures one run of the game in front: every frame, and the GPU, CPU and memory once a second.

- **Start it** with its shortcut in the game (**Ctrl+Alt+F12**), or with **Start a benchmark** on the tab, which waits
  until you switch to the game and starts then. The overlay shows **◆ BENCH 0:23 / 1:00** while it runs, even when the
  overlay itself is hidden.
- **It ends** after the set time (60 seconds by default) or, in "until I stop it" mode, when you press the shortcut or
  **Stop the benchmark** again. It also ends if the game closes.
- **Settings** (the button on the card): the start / stop shortcut, and a set time or until stopped.
- **When it ends** the overlay shows the average and 1% low for a few seconds, and the run is saved with a name like
  "Cyberpunk 2077 run 3". The tab shows the latest run.

**Benchmarks…** opens every run in a table: name (click it to rename), game, date, length, resolution, average FPS and
the 1% / 0.1% lows. Click a run to see its report: average, 1% and 0.1% lows, the slowest and fastest second, average and
worst frame time, GPU load, temperature (average and highest), clock and power, the most video memory, CPU load (all and
the game's), the most memory, the number of frames, and the frame rate second by second. Once one run is ticked, only the
runs it can be compared with are listed (the same game, and the same set length, or, for runs stopped by hand, lengths
within 10% or 5 seconds); click more to compare them side by side, each with how it differs from the first (green when
better, red when worse) and all their frame rates on one graph. **Delete ticked…** removes runs.

The lows work as in the overlay: the frame rate at the slowest 1% and 0.1% of frame times over the whole run. Runs are
saved in `%APPDATA%\DaisysApp\GamingBenchmarks.json`.

## Shortcuts

| Shortcut | Does |
|---|---|
| Ctrl+Alt+F9 | Start / stop recording |
| Ctrl+Alt+F10 | Show / hide the overlay |
| Ctrl+Alt+F11 | Next overlay mode |
| Ctrl+Alt+F12 | Start / stop a benchmark (set it in the Benchmark card's Settings) |
| Ctrl+Alt+F8 | Mute / unmute the virtual webcam (the keyboard button beside Mute sets it too) |
| *(none to start)* | Hide / show a camera: each camera's own, in Add Camera |

Change them in Settings → Video → Overlay: click a box, then press a key combination or a controller / button box
button. They work anywhere, in games too. (The NVIDIA app uses Alt+F9 and Alt+Z; a shortcut
another program already has is flagged in Settings.) The tray menu has the same.

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

## Settings (Settings → Video → Overlay and Recording)

- **FPS overlay:** show only while a game is in front; leave it out of recordings and screenshots; which graphs to
  show; justify left or right; background colour and opacity; text colour; size; lock in place.
- **Recording:** folder; the PC's sound; the microphone as a second track; the mouse pointer; HDR as SDR or HDR10; the
  quality profiles (new, delete, reset to the defaults, and every setting of each).
- **Shortcuts:** the three above.
- **Games:** every program recognised as a game: its friendly name, whether it counts as a game, and remove.
- **History:** on or off, how long to keep it, open the folder.

Saved in `%APPDATA%\DaisysApp\Gaming.json`.

## Code

This folder is the **Overlay** and **Recording** sub-tabs (one service, two pages; it was the Gaming sub-tab before 0.25).

| File | What it is |
|---|---|
| `GamingApplet.cs` | Overlay, and the Gaming service Recording shares (`RecordingApplet` is in the same file) |
| `GamingService.cs` | The game in front, its frame rate from event traces (`FrameMonitor.cs`), the PC's load (`Telemetry.cs`), logging each app's sessions (`GameLog.cs`), benchmarks |
| `GamingView.xaml(.cs)`, `Pages.cs` | The two pages (Overlay: what's running and the overlay; Recording: load, record, benchmark, history) |
| `OverlayWindow.cs` | The FPS overlay over games |
| `Recorder.cs`, `FrameConverter.cs`, `Encoders.cs`, `AudioTrack.cs`, `RecordingImpact.cs` | Recording: desktop duplication, the GPU's crop / scale / pointer / cameras, Media Foundation's encoders, the sound tracks, what a recording costs |
| `CameraFeed.cs`, `CameraOverlay.cs`, `CameraPlacement.cs`, `CameraLayoutWindow.cs` | Webcams over the picture, and placing them |
| `VirtualCamera.cs`, `WebcamStartWindow.cs`, `MutedFrame.cs`, `AudioRelay.cs` | The virtual webcam (softcam), its start window, its muted picture, and its sound to a virtual cable |
| `WebcamMuteWindow.cs` | The mute shortcut, from the button beside Mute virtual webcam |
| `OutputAspect.cs`, `RegionSelector.cs`, `Native.cs` | The picture's shape, choosing a region, monitors and windows |
| `Benchmark.cs`, `BenchmarksWindow.cs`, `BenchmarkSettingsWindow.cs` | Benchmarks and their reports |
| `HistoryGraph.cs`, `SquareGrid.cs` | The history graph; the pages' square layout |
| `GameTech.cs` | Which upscaling and frame generation libraries a game has loaded |
| `GamingSetupPage.cs` | Counting frames needs *Performance Log Users*: the setup wizard step (the check and **Allow** are `TracePermission` in `DaisysApp.Shared/Hardware`) |
| `GamingSettings.cs`, `GamingSettingsView.xaml(.cs)` | `Gaming.json`, and Settings → Video → Overlay and Recording |
