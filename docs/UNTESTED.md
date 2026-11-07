# For the user to try

The full list of what is waiting on the user to try or report, newest first, moved out of the root CLAUDE.md on 2026-10-26 (it isn't loaded by itself; read it when a release's follow-up comes up).

- **4.10.4–4.10.6 (Tools → Screen → Black screens):** switch it On (it no longer blacks the monitors itself), set its shortcut on its card, untick All screens and leave one monitor out (4.10.6: it stays lit; tested here only on one monitor: picker drawn, click unpicks), press it in a game and on the
  desktop: every monitor black (no taskbar, no pointer), and back at the next press; a double-click on one ends it too;
  a monitor of another DPI fully covered. Tested by a harness on this PC's one monitor (window = monitor, on and off);
  not several monitors, a game in front, nor the shortcut.
- **4.10.3 (Compare's timeline and playback):** open a race with a video in Compare: zoom with the wheel (the frame
  under the chart shrinks round that part), drag the frame and click outside it; click the chart (the white cursor
  moves there, the readout, video and map back to it when the mouse leaves); play at each speed (cursor, video and
  map move with it, the chart pages along, sound only at 1×; reverse video is choppier). A zone's number still picks
  it. Tested in DaisysDev3 with a fake recorded race: play, 2×, ½× back by UIA (cursor, clock, video, map moved);
  the clicks, drags and wheel on the chart and frame weren't tried (no mouse input in tests).
- **4.10.2 (a race right after another):** with Analysis' Video on, drive to a race's start and race it: the race
  itself has its video (SimEffects' log: "recording the race …" at its start, no "busy"), shown in Compare's Video.
  Tested: a fake race restarted at once in DaisysDev3 (both recorded, the second kept and linked, stopped at the end);
  not a real race.
- **4.10.1 (race videos while the webcam runs; Compare's channels at the right):** with the virtual webcam on and
  Analysis' Video on, drive a race: the video starts (Video's log: "Recording started (for another module)"), and
  Compare's Video shows it. The webcam keeps going meanwhile. Tested: two recorders of one monitor at once in a
  harness (both files had the picture) and a fake race recorded and linked in DaisysDev3; not with the webcam itself
  (the real app holds it) nor a real race. Mini Mirror on the monitor being recorded still has its own capture.
- **4.10.0 (Analysis in every game):** with Analysis on, drive a few laps (out of the pits, a cut one, a whole one)
  and a short race to the flag in each of iRacing, ACC, Assetto Corsa and AC EVO (Assetto Corsa and AC EVO need a
  profile: Profiles → New): the track appears in Discovered Tracks with its name, the whole laps (not the out lap or the
  cut one) in All Laps with the game's times, the race in Races with its start and finish, the car's name and class
  right. Open a race in Session Analysis: the Track Map's line looks like the track (iRacing's isn't mirrored and
  doesn't drift much through a lap), Compare and Shifting work. In FM (2023) a race: the track's added once, by its
  number. Tested in a DaisysDev copy with fake shared memory for ACC, Assetto Corsa, AC EVO and iRacing (a car on a
  circle: laps kept or dropped, the race's result, names); no real game. AC EVO's offsets are worked out from Kunos'
  header (not tried on the game); iRacing's line direction is a guess.
- **4.9.0 (Compare's Video: Export):** pop out Compare's Video on a race with a video, line it up, click the Export
  icon (right of Play), save: the percentage counts up, Explorer shows the file; play it: the Dash dial, the Inputs
  trace and the lap time move with the race, the sound's there. Try the icon again mid-way (it stops; no file left).
  Tested by a harness only (a real FH log over an NVIDIA 5120×1440 clip: 3840×1080 H.264 + AAC, 19 s in 18 s, frames
  looked at); the button, the save dialog and stopping not tried in the app.
- **4.8.1 (the race video's sound):** pop out Compare's Video and play: the game's sound with it, in step with the
  picture. Tested in DaisysDev3 with an NVIDIA clip (the session's meter moved); how well it keeps in step not judged.
- **4.8.0 (Analysis: race videos, Best Times' Game):** switch on **Video** beside ON, drive a race in FH: a video
  appears in Video → Recording's folder; open the race, hover Compare's chart: the Video slot follows. Pop it out:
  play, the slider, the sync buttons and **Race starts here** (pause where the race clock starts on screen); check
  the race clock shown against the game's. Quit a race before a lap: its video goes. Best Times' Game box. Tested in
  DaisysDev3 with fake races (recording, keep / drop, playing the AV1 file, the sync buttons by UIA); a real race, how
  far off the automatic sync is, and the chart's line while playing not looked at.
- **4.7.0 (Analysis: Trace in Compare, Session 1 / 2, Shifting in rpm):** open a race in Analysis: Compare with
  Session 2 None is the trace (zones shaded, markers at the top with tooltips); click a zone in Brake Point, a row of
  Brake Analysis, a Shifting dot: the chart goes there with a line. Pick a Session 2: its channel boxes and Delta
  appear, a corner row draws the line. Pop out the Track Map and Video (icon top right), move and size them, close:
  they're back. Shifting reads in rpm with a Limiter band. A blue chevron on the right of a row folds the row. Contact
  tips orange for little contact. Tested in DaisysDev3 with fake races (all of the above by UIA but the orange tip:
  no fixture with contact); dragging the windows, the mouse over markers and a real race not tried.
- **4.6.10 (Shifting):** drive a race in FH with a manual gearbox, then Analysis → Shifting: a dot per gear change,
  the green band where the car's best upshift is (from its own power), early and limiter shifts in amber and red, the
  tips in Tips. Worth checking: does Forza's limiter sit at 98% of its max rpm (else limiter time reads 0), and do the
  bands look right for the car. Tested in DaisysDev3 with a fake race (gears added to a seed log); hover and click by
  hand, and a real race being logged, not tried.
- **4.6.9 (Compare's map and scroll bar):** Compare: the Track Map right of the corners; zoom the chart with the
  wheel: the scroll bar's thumb shrinks; drag it. Tested in DaisysDev3 with fake races (a corner picked zoomed, the bar
  moved it); the wheel and dragging by hand not tried.
- **4.6.8 (widgets):** make a widget in any module (Performance, Zabbix, Devices…): Click through is ticked, the strip
  is Background / On top + Click through / Done + Delete; after Done clicks go through, Ctrl+Shift + right-click edits it.
  Tested in DaisysDev with a Devices widget (strip seen); click-through after Done not tried.
- **4.6.7 (Calibration in Devices):** on the rig, Gaming → Devices → the SimJack pedals: Calibration under the axes;
  click each axis above (picked below); Find Ranges, press each pedal to the floor (its name turns blue, then green;
  Range found), Apply (green, top right), then Output reads 0–100% end to end; a game sees it. Same for the PXN-HB
  handbrake (4.6.2's fix: its axis is found). Cancel: click, ✕, changes gone. The Calibration sub-tab is gone after
  the update (`modules\Calibration` removed). Tested in DaisysDev with the pretend pedals only (Find Ranges, Apply
  logged, Cancel by ✕, Here by hand); no real device.
- **4.6.6 (Notepad images):** Tools → Notepad: Win+Shift+S a screenshot, Ctrl+V: it shows; double-click opens it;
  Ctrl+Z / Ctrl+Y; select only the image, Ctrl+C, paste into Paint; the image shows on another PC with the same notes.
  Tested by a harness only (insert, redraw, undo); no real paste or second PC.
- **4.6.5 (Compare's picks):** Compare: This set to an older race and to an imported one, With only that track's
  races (another driver's lap too), the laps' times and the fastest marked. Tested in DaisysDev3 with fake races (one
  on another track left out, an imported "Sam" found by its start).
- **4.6.4 (Analysis tweaks; Inputs overlay):** Inputs overlay: finish a race, start the next: the graph starts empty;
  Current Location's icons (Save, Add New, Export, Name Car) and greyed *Unknown*; click a Last races circle: that race
  opens in Session Analysis. Tested in DaisysDev3 (layout seen); the overlay gap and the click not tried.
- **4.6.3 (Analysis: Racing Statistics, Track Map, Compare's colours):** after a real FH race or two, SimEffects →
  Analysis: Racing Statistics' numbers; Compare a lap with another (8 boxes and highlights; the other dotted); the
  Track Map card's two lines line up on the track; the Track Map block (Widgets, and as an overlay from Overlays) draws
  the best lap's line and your car while driving; Current Location greyed *Unknown* out of a race; Brake Analysis'
  double-click and Close. Tested in DaisysDev3 (a copy, `%APPDATA%\DaisysDev3`) with two fake 3-lap races
  (`%TEMP%\da462\seed.py`): the layout, Racing Statistics, Compare with both runs and the Track Map card seen; the
  live block not (no game sending).
- **4.6.2 (Calibration finds axes; Device Widget):** on the rig, Gaming → Calibration → the PXN-HB handbrake: its axis is
  listed and moves; Wizard: pull it end to end, Next, Save; same for the SimJack pedals (each pedal's dot turns blue
  when pressed, the right one). Devices → the handbrake's Device Widget: the lever moves with it and shows its axis; a
  stick's tiles say Z Rotation / Slider 1…; tiles go 2.5 s after letting go. Tested: the widgets in DaisysDev with the
  fakes (handbrake, stick, pedals drawn); Calibration not tried with a real device (PLEX has none).
- **4.6.1 (Analysis layout):** SimEffects → Analysis: Current Location (with Name Track) left of Widgets; the shorter
  charts and Smoothness/Tips 2/3–1/3 look right with a real race; fold Laps, restart: it stays folded; Ctrl-click two
  Discovered Tracks, Delete (both go). Tested in DaisysDev without laps (layout seen; two tracks picked by UIA enabled
  Delete; none deleted).
- **4.6.0 (Gaming → Devices → Device Widget):** on the rig, pick the wheel, press the controller icon (left of
  Calibrate), Done; turn the wheel (it always shows), press throttle and brake (two bars), a button 2 s later: the
  pedals fade after 5 s, then the button. Same for the stick and throttle; pedals show nothing until pressed. Tested in
  DaisysDev with the fake devices (wheel, stick, HOTAS, throttle, pedals drawn right); the fakes never stop moving, so
  the fading wasn't seen.
- **4.4.2 (ForzaWheel → Apply FH5 Menu Fix, Steam only):** the user's friend with Steam's FH5: close the game,
  press it (under Hush), start FH5, Settings → Controls → Wheel: pick the default profile; in menus, do the paddles
  change tabs and the d-pad move? Does driving still work (remap anything)? Tested on a copy of the Store copy's zip
  (entry added, valid XML, applied twice); not tried in the game (the rig has the Store copy). If the game ignores
  a new file name, the next try is replacing a known file's contents.
- **4.5.0 (Forza Analysis merged into Analysis, races only):** with Analysis on, drive a Forza race to the finish and
  some free roam: SimEffects → Analysis → Race Analysis lists the race (track · car · time); click a lap in Laps (it
  opens there, that lap in This), right-click another → Compare With; a race quit before a lap leaves no log. Open
  Widgets (it stays open after a restart); Position blank outside a race. Gaming has no Forza Analysis. Tested in
  DaisysDev with fake FH packets (a 3-lap race kept and linked, a quit one dropped; a lap picked by UIA); mouse clicks,
  the right-click menu and a real race not tried.
- **4.4.0 (Forza Analysis: Compare, Smoothness, contact, timed tips):** drive a few laps of Forza with the Input
  Overlay showing (sessions now carry telemetry), then in Forza Analysis: point at a red arrow (its tooltip); Compare
  two laps, and Export one / Import it back (or a friend's); hide and highlight each run; click a Brake / Throttle
  Analysis row (its window); bump a wall or a car: the Tips' contact line and its time. Tested in DaisysDev with
  synthetic sessions; hover, row clicks and a real Forza session not tried.
- **4.3.3 (Network Scan laid out as HA):** on the rig, resize the window: the groups pack with little free space and
  the tiles fill each row; drag a group's title onto another (the order stays after a restart); Settings → Network
  Scan: Display Grid Size. Tested in DaisysDev (a scan of 192.168.1.0/24); dragging not tried there.
- **4.3.1 (the controller crash: Windows' DirectInput mended):** on the rig, switch the Xbox pad off and on a few times
  with the app open: no freeze, no crash. The log has "DirectInput fix, dinput.dll: patched" (and dinput8.dll). Tested
  on the rig in a harness (code checked in a debugger) and DaisysDev; the crash itself not reproduced on purpose.
- **4.3.0 (Network Scan: Zabbix and HA columns):** on the rig, click devices in Network Scan: are the right Zabbix
  hosts and HA devices beside them, and none wrong (a name match on a common name)? Open in Zabbix / Open in Home
  Assistant. Tried in DaisysDev with your settings: GAMER11 (both), d3-printer (Zabbix), Hue Bridge (HA), pi and the
  LG TV (in neither).
- **4.2.4 (Devices: Alert on Change):** on the rig, Set as Normal, unplug and replug the Xbox pad: no DirectInput order
  alerts on the others; untick its Alert on Change: no missing / new alert for it. Built, not run with a device.
- **4.2.3 (Inputs overlay's trace):** with a game, the trace speeds up from halfway across. Built, not run.
- **4.2.2 (Network Scan's details window):** click a tile: the window, a port in it (Port Scan's window), Scan Host
  (opens Host Scan, scanning). Tried in DaisysDev (an Echo: details, ports 55442/55443, Scan Host → Host Scan).
- **4.2.1 (Network Scan, was Device Scan):** on the rig, Scan: the Echos in **Alexa**, the three phones and three
  tablets in Phones & Tablets (asleep ones dimmed, "not answering"); search "Galaxy". Tried in DaisysDev on PLEX's LAN:
  53 devices in 45 s, 4 Echos in Alexa, all 6 phones/tablets (5 asleep, from DNS). Still Unknown: 8 with "Private"
  MACs (98:17:3C…, D0:C9:07…), TTL 255, no ports: likely plugs or bulbs; `Livingroom`, `U1` (a Snapmaker printer).

- **4.2.0 (Support → Network Tools → Device Scan):** on the rig, Device Scan → Scan (GAMER11's subnet): are the groups
  right (anything in the wrong group, or Unknown that has a name: say which, for `HostScanner.NamePatterns`)? Search
  for a device; click one: Host Scan should open and scan it. Type another subnet (a /24 you have, or a remote one
  over a VPN: ping and ports only). Tried in DaisysDev on PLEX's LAN: 45 devices in 43 s, groups, search, Full Scan.
  Not tried: a subnet off this PC's network, a /20, Host Scan switched off (tiles then don't click).

- **4.1.0 (Support → Process Analyzer):** on the rig, open Support → Process Analyzer: the Suggestions list should be
  short and make sense (anything flagged High that's fine: Edit it, or Fine Here, and say which); click a few programs
  for their details and history graph; Edit one and see it stay after a restart; Export…, then Import… the file on
  another PC (or the same one). With a VirusTotal key (Settings → Support → Process Analyzer), VirusTotal on an
  unsigned program. Leave the tab on another one for 5 minutes with something risky running for the notification.
  Tried in DaisysDev (PLEX, not elevated): 118 programs, paths and figures for all but the kernel's own; seeds loaded
  over earlier automatic entries; Edit form, settings page; merge rules and export/import in a harness. Not tried: the
  Save/Open dialogs by hand, VirusTotal (no key), the notification, Daisy's Server (its `processes` module isn't
  written yet: the brief is in `src/Modules/Support/CLAUDE.md`'s notes and the module's README).
- **4.0.0 (the audit: less background work, shared code):** on the rig, use it as usual for a while; it should look
  and behave the same. Worth a look: a game starting and ending still switches Button2Key, B2K Remote, ForzaWheel,
  SimEffects' profile, Mini Mirror and the Resizer's automatic profiles; plugging a wheel or button box in shows up in
  every module by itself (no Refresh), and its shortcuts work again after a replug; the Overlays and the Dash move with
  a game and stand still with none; the Shaker's device volumes follow the sliders, shortcuts and Windows' mixer; an
  Arduino reconnects when plugged back in; Video's overlay, recording and FPS history as before. Tried in DaisysDev:
  starts clean; a fake ForzaHorizon5.exe (renamed ping) switched B2K Remote on and back off and played/stopped
  SimEffects' Forza Horizon profile within ~2 s; tools/buttclient.py connected and read the state (B2K Remote's bytes
  unchanged); the Shaker read its devices' volumes. Not tried: any controller, Arduino or game (none on PLEX), the
  overlays drawing, the Shaker's volumes changed by hand.
- **3.38.0 (Resizer: Desktop Layouts):** Video → Resizer → New profile, Source **Desktop Layout**, name it, Save; set its
  joystick shortcut. Switch the displays off and on (or drag windows about), press the shortcut: every window in it goes
  back, maximized ones maximized on their display; new windows stay put. Tried: a harness's own 3 windows (normal ↔
  maximized, minimized kept minimized) put back; the editor in DaisysDev (snapshot listed). Not tried: 5 displays
  powered off and on, displays of different scales, a program running as administrator.
- **3.37.16 (every shortcut button is Core's):** the joystick buttons in Resizer (rows and editors), Audio → Volume's
  rows, Button2Key, ForzaWheel, B2K Remote, Screen, AI's Hold to talk, HA's things, Recording, and SimEffects' tabs: each
  opens the shortcut window and turns accent once set. Tried in DaisysDev via UIA: every one found with its tooltip;
  Resizer's (fixture profile + group, restored) and Recording's (with its checkbox) windows opened. Not tried: HA's
  (no HA there), setting one by a real key or button press, Mini Mirror's region keys after the change.
- **3.37.15 (controller buttons kept by their controller; Mini Mirror's joystick buttons):** on the rig, bind a
  mirror's (or any) shortcut to a button box button: Settings → Shortcuts shows "<box name> Button N"; replug the
  devices so Windows renumbers them (or Gaming → Devices' Apply Order): the button still works, and the wheel's button
  with that number doesn't. An old binding ("Controller 2 Button 5") becomes the named one at the first start with
  that controller in (the module's log: "Shortcut … is now …"). Tried: matching, the swap and the upgrade with fake
  controllers in a harness on the built Core (PLEX has none plugged in); in DaisysDev the mirror's and group's
  joystick buttons and the shortcut window. Not tried: a real controller's name and instance (DirectInput's joystick
  configuration).
- **3.37.14 (Shortcuts: Also: rows):** an Audio app's shortcut on a button another app has: the note under it reads
  **Also:** then a row per other binding (`Audio · Rpcs3`), the action only when it differs. Built only.
- **3.37.12 (AI: Ctrl+Shift+number, Pause/Stop icons, Compact):** with several AI tabs, Ctrl+Shift+2 shows the second
  (Ctrl+Shift+1…9); during a turn, ⏸ and ■ side by side over **Compact**, ▶ when paused; Compact sends `/compact`
  (queued while it works). Built only (not run: DaisysDev was in use).
- **3.37.11 (AI box: undo and redo):** type, paste, drop an image, then Ctrl+Z / Ctrl+Y (and the right-click
  menu's Undo / Redo) step back and forth through them; send, then Ctrl+Z brings the message back. Tried by a harness
  on the built DLL only (typing, bold, an image, select all + delete, after a send), not by keys in the app.
- **3.37.10 (Video → Recording: a region asked for each time):** pick **A region**, press Record (and the region
  shortcut): the screen dims to drag a new region each time, then it records that. Seen in DaisysDev: the picker opened
  on each Record and closing it cancelled; no region was dragged nor recorded.
- **3.37.9 (summary folded):** open a tab whose conversation was compacted (this one): where the long "This session is
  being continued…" bubble was, one grey line *✂ Carried on from an earlier conversation ▸*; a click opens the
  summary, another closes it. Built only.
- **3.37.8 (compacting counter):** AI tab, `/compact`: the ✂ line reads *Compacting the conversation 0:01*, counting
  up each second, and stops when it says it was compacted. Built only.
- **3.37.5 (Conversation Mode's "doing" line):** AI tab, 💬 on, send something that runs commands: under the last
  reply a grey line with a coloured dot says the step it's on (*PowerShell: …*), changing as it goes, and goes when the
  reply is done. 💬 off mid-turn: the line goes and the cards are back. Built only: not seen in a real turn.
- **3.37.3 (Apply Order's Force):** on the rig, with the program 3.37.2 named running (SimHub, say): tick **Force**,
  drag a block, **Apply Order**: the program closes, the order is applied, and the note says "Closed: … Start them
  again if you need them"; the `Reorder:` lines in the Devices log have the Closed lines. Not run here (no controllers):
  only the task's arguments were tried, with an unknown device.
- **3.37.2 (Devices' Apply Order lets go first):** on the rig, Gaming → Devices: drag a block, **Apply Order**. Either
  the numbers move, or the note names the device and the program holding it ("… has it open"); the `Reorder:` lines in
  `GameDevices-<date>.log` say which device refused and with what code. Tested here only through the task with an
  unknown device (no controllers on PLEX): the letting go, a real refusal and Windows' veto event never ran.

- **3.37.0 (Video → Recording's buttons and shortcuts):** the Record card's new buttons (seen in DaisysDev). Set the
  three new recording shortcuts (keyboard button) and press each: the monitor, the region and the game record whatever
  Record has chosen, and a second press stops it. Record a **region** and stop it: Explorer opens on the file. Only the
  buttons and the shortcuts window were seen (DaisysDev); no recording was made.
- **3.37.1 (Screen's Always on top):** Tools → Screen → Always on top **On**, right-click a window's title bar (Notepad,
  Explorer, a game launcher): **Always on top** above Close; click it, the window stays above others, ticked next time;
  click again to let it go. Alt+Space, then T, does the same. Set the joystick's shortcut and press it on Chrome. Off:
  the item's gone from the menus. Tested by a harness only (a WinForms window in another process: item put in once,
  above the line over Close; the real menu opened by a posted Alt+Space, seen as ours, item lit by Down, Enter → on top;
  off → item removed, window stays on top): not by a real mouse click, not on Chrome/UWP/elevated windows, the shortcut
  and the card on screen not seen.
- **3.36.13 (ForzaWheel's Test FFB):** on the rig: Gaming → ForzaWheel, **Start**, then Force Feedback → **Test FFB**
  (Forza closed): the wheel pulls left, then right, then shakes, and it says "Done: the forces went through vJoy device 1
  to …". Does left come first (if it's right first, the labels are the wrong way round; Reverse flips it)? Are the
  pulls and shake strong enough? Only the button and "Start the emulator first." were seen, in DaisysDev (no vJoy or
  wheel on this PC): the forces themselves weren't sent.
- **3.36.12 (Hold to talk, Talkativeness):** AI tab: hold 🎤, say something, let go: it's sent and the reply is read out.
  Is Windows' recognizer good enough on your voice? Settings → AI: set **Hold to talk** to a key, hold it from another
  program; a controller button; **Standard** (a longer reply read out; "give me the details" while on Brief). A session on
  another PC. Tried by a held F24 and by UIA on DaisysDev, with whatever the room's microphone heard, reply muted: the real
  mouse hold, a clear voice, a controller button, hearing it and a remote session weren't.
- **3.36.11 (Auto-Compact only after publishing):** with 🗜 on, ask a question: no "Compacting…" after it; ask for a
  change that gets pushed (git push or a release): "Compacting…" once it's done (after its notes). Only built and the
  matcher checked; not in a real turn.
- **3.36.12 (Install Narrator's Voices):** on a PC without the adapter: Settings → AI → **Install Narrator's Voices**, yes to
  Windows: Voice shows Narrator's voice and says the line. GAMER11 already has it (installed by the job, by real UAC; its
  sound not heard by the session). Not tried: the button itself, a PC with no natural voice picked (opens Narrator's settings).
- **3.36.9 (Narrator's voices):** after updating on PLEX, ask a session to say something: it should be Ava, and Settings
  → AI → Voice should show Narrator's voice (the app moves off Mark by itself the first time). Does it sound like
  Narrator's Ava? The adapter says HD isn't fully supported, so it may be the lighter model. Press the voice button
  mid-sentence to check the mute cuts it off. Tested: Ava through the real `Voice` class in a harness (nothing logged
  wrong; whether the mute cut it off wasn't measured), not in the app's window. GAMER11 needs Ava and the adapter too.
- **3.36.4 (AI: brief speech):** with 🔊 on, a new session should speak only a few words (taking over the PC, done,
  or what you asked it to say), never a summary. Not tried in a real session (built only).
- **3.36.3 (AI: Auto-Compact; Traceroute's hidden rows):** turn 🗜 on, ask a session something: once the reply (and
  the session notes) are done, one "Compacting the conversation…" line should appear and change to "compacted" when
  it's finished, with no /compact bubble and no "Compacted in" line. It shouldn't compact again by itself after that.
  Tested in DaisysDev: the button order and that the setting is saved; Traceroute to amazon.com ("6–30 · Hiding 25
  Lines" during the trace, "6 · Hiding 1 Line" at the end). Not tried: a real turn with Auto-Compact on.
- **3.36.7 (Devices: drag, Apply Order):** with no game running, drag a device to another place, Apply Order. Say
  whether it asked for administrator rights, whether the devices came back, whether the numbers really changed (also in
  a game or joy.cpl), and whether they last a reboot. If one stays gone: replug it. Log: `GameDevices-<date>.log`
  (`SetConfig` lines). Tested: pretend devices and the calls with no device; never a real number, and the drag not with a physical mouse.
- **3.35.3 (AI: Conversation Mode, the voice button):** 💬 left of Search during a real turn: only your messages and
  the replies; a permission question and an error still show, and the card goes once it's answered. 🔊 right of Search:
  mute mid-sentence stops it. A new session says a few words before taking over the PC and when done. Tested in
  DaisysDev: both buttons, the feed filtering a read-back conversation and coming back, the mute dropping `--say`;
  SAPI's cut-off checked at zero volume. Not tried: a live turn in Conversation Mode, a real question in it, the
  spoken take-over lines.
- **3.35.2 (Devices' bars, Invert, Calibrate; with 3.35.1's pictures):** Warthog throttle forward: lever up, Z and Z
  Rotation bars full. NXT stick: Y and Z bars go the way you expect (they follow the role: Y up / down, Z as the
  throttle; if Z is really its twist, set its role to Twist and it reads straight). Invert flips bar and picture. The
  Calibrate button opens Calibration on that device. Pressed pedal rises. Tested: pretend devices only; Calibrate opened the tab and saved the device, but Calibration's pretend list has only Test Pedals, so landing on the right device is unproven
 .
- **3.35.0 (Gaming → Devices, the Overlays' trash can):** on the rig, Gaming → Devices: every device joy.cpl lists is
  there with vJoy, numbers as you expect them (compare joy.cpl; the Warthog throttle should say the joystick API hasn't
  it). Move each one: green dot, whichever device shows. Set each one's type if the guess is wrong, then its axes' roles
  and Invert (SimJack: Ry, Rx, Z), the first gear button on a shifter. Unplug something: amber bubble within 3 s; back:
  gone. Swap two so their numbers change: red alert, stays after you swap them back, until **Set as Normal**. The gear:
  the device's own Properties page, nothing else on screen, and Game Controllers gone when it's closed. Double-click a
  name. Make Widget on a pedal set. SimEffects → Overlays: the Dash's own trash can deletes it. Tested: all of it with
  13 pretend devices in DaisysDev (double-click by the pencil instead); Dash and a block deleted from their strips. **Not
  tested on any real device** (PLEX has none): reading, the dot's thresholds, real numbers, plugging, Properties picking
  a device (only its fallback, the window shown when the device isn't listed).
- **3.34.1 (Button2Key: the HOTAS Warthog throttle couldn't be added):** Button2Key → **Add a controller…** lists
  **Throttle - HOTAS Warthog**; add it, push the throttles and press its buttons: the tiles light up; assign one and it
  sends in a game. Tested: DaisysDev on the rig lists it (id 208, 32 buttons, 4 axes, DirectInput only); its tiles and
  reading not tried (Elite Dangerous was running).
- **3.34.0 (Trace when down, Past traces, Events chart, tracks, Restart on GPU):** Support → Ping → gear → tick **Trace
  when down** → Save; when a target drops for real (or a fake one, 203.0.113.1), Traceroute traces it by itself and
  Events has "Traceroute to … no answer past hop n"; Traceroute → **Past** lists every trace, pick one → table and map.
  Events → **Over Time**: By hour / Hour of the day… with your real events. SimEffects → Analysis: Discovered Tracks
  lists the FH5/FH6 tracks. Performance → Processes → right-click Discord → **Restart on GPU 1 (…iGPU…)** → Yes →
  Discord restarts, Task Manager's GPU engine column shows GPU 1; after Discord updates, still on GPU 1. Tested:
  Trace when down, Past and the Diagnosis line in DaisysDev; traces and the chart in a harness; tracks merged in
  DaisysDev (178 of 187). **Not tried at all:** Restart on GPU (no right-click by UIA), the gear window's box on screen.
- **3.33.0 (Overlays, Dash, Events Report, Responded in):** SimEffects → **Overlays**: the list has Inputs and any Analysis widget you had open; New ▾ → Dash → drag it where you want it → Done (or Edit/Done in the list); drive FH → the needle, gear (flashing red at the shift point), speed, TCS when the wheels spin, ABS when a front wheel locks. Pick a block in the list → its rows, Title, Background, shortcut; tick/untick to show/hide; the trash can twice deletes; ON/OFF and Stop hide them all. Analysis' blocks no longer have pop-out buttons. Support → Events → **Report ▾** → Last 7 days → a page in your browser → Print. AI: your next request ends "Responded in m:ss". Tested in DaisysDev with fake FH packets (port 8123: SimHub holds 8000): the list, Dash on screen (revs, gear flash, TCS and ABS lights), New, Done, Delete, a block's rows, Inputs ticked, Stop/Play hiding them, the old Timing widget brought over (Overlays on in FH, Inputs off); the Report from made-up events rendered by Edge (headless), its menu in Events. Not tested: a real game, the ON button by click (UIA can't), shortcuts, Report opening your browser, "Responded in" in a real turn.
- **3.32.0 (widgets, SimEffects defaults, logo, Events):** right-click a Performance widget → **Background** slider: the card fades, the graph and numbers stay; tick the box of **Click through** (the box itself, not the words) → Done → clicks go to what's under it; hold Ctrl+Shift → right-click it → edit again. The same strip on Zabbix, Ping, the Shaker's meters, Analysis, the Input Overlay (its Background is the tab's) and a B2KR pop-out (its ButtScreen's background fades, buttons solid). The taskbar and Start menu icon green; the tray blue at rest, green while a module runs. Support → Events: times with seconds; **Export…** → a text file to paste into a chat; when your internet drops → a **Diagnosis** row ("Internet connection dropped: the router answers…") and "Over: … after …" once back. On a new install (or another PC): SimEffects has your four profiles and the Shaker your tuning. Tested in DaisysDev: background at 30% on screen, the strip's boxes and words both get the click (WM_NCHITTEST), Click through sets the window click-through and is saved; a fresh SimEffects settings folder → the four profiles, Impacts at 95%; Export written; an unreachable target → "Only Fake server down; Cloudflare, Google answer…". Not tested: Ctrl+Shift (no keys sent), a real drop, the other modules' widgets on screen, the icon after installing.
- **3.31.1 (SimEffects → Analysis):** the tab: five blocks on one row, no Widgets card; Best Times' search (a track, then a car); Best Laps' Date, filters on one row growing with the window, its table ending level with Laps. In FH: near a known track, type a name, Add New → the box is focused and selected; race there → on the new track, not the old one; a race starting nearer the old one → on the old one. Tested in DaisysDev with fake FH6 packets (Add New 250 m from a track, then races 20 m from each and at a tagged place: each on the nearest; Add New and Name Car focused the box) and the layout on screen; not in the real game; Save's and Export's focus not clicked.
- **3.31.0 (SimEffects → Analysis → Map):** Map image… → pick a big image of the FH6 (or FH5: Game) map; pick a track in the list, Place on map, click its event marker on the image; another far away → "Lined up from 2 tracks", every dot on its event (a third or fourth makes it truer). Wheel zoom, drag, Fit; Search + Enter jumps to it; Type, Form, Raced only, Names; click a dot → its best laps, races and laps under the map; Type → set one by hand. Drive → the white dot follows you. Tested in DaisysDev with your 82 FH6 tracks over a made-up 4000 px map with a ring at each track: two tracks placed in the settings put every dot in its ring; search, picking, filters, the type override, Names, Raced only and the tables on screen. Not tested: the click that places a track (posted clicks don't reach WPF and you were in the game), wheel and drag, a real map image, the car dot.
- **3.30.3 (SimEffects → Analysis → Races):** in FH5 (and FH6) finish a sprint and a circuit race, then go to the next one → Races lists each with start and finish places; the log has "race on … finished". Tested in DaisysDev with fake FH5 packets (a sprint and a 3-lap circuit, a gap after the finish, then frames with the distance at 0 elsewhere: both kept, 6th → 4th); not in the real game.
- **3.30.2 (SimEffects → Analysis):** at the start where another event's start 740 yards away was picked → Current Location names the right one (or Unknown); Settings → SimEffects → Analysis → Tracks → Start radius (m) changes how far it looks. The tab: Tires, Performance, Timing; then Position, Leaderboard; Best laps and Laps side by side; no Vehicle or Laps block (an open widget of either gone after the update); Name Car in Name Track while in a car. Tested in DaisysDev: the layout and the setting on screen; not with FH6 (no race or start detection driven).
- **3.30.0 (shared code, nothing should look different):** the moves were only opened in DaisysDev. By hand: Zabbix and HA with a real server: drag a group, Test connection, resize the window; Mini Mirror: make, move and resize a mirror; Video → Overlay on a game, and its click-through; Button2Key and B2KR: name a profile (it's Core's prompt now), a binding; ForzaWheel and Input Overlay: **Set** an input; Performance → Processes: Switch to, Properties.
- **3.29.0 (AI: Gemini on your Google plan, Antigravity):** Settings → AI → Providers → Add… → Google Antigravity → Test answers "OK"; Fetch models lists Gemini 3.8 Flash and the rest. New tab → Host **Local: Gemini** → ask it to change something in a project → Write/Edit cards, Files changed, the reply; ask it to run a command with Ask before acting → refused, the red note; Permissions → Don't ask → it runs. Stop mid-reply, then send → it remembers. Restart the app → the tab's history is there and it carries on. Tested: a harness on the agent (4 turns, Stop, resume) and a DaisysDev tab driven over the remote API (file written, command after switching to Don't ask, screenshot); the Providers window's buttons and typing in the box not tried.
- **3.28.2 (SimEffects → Analysis → Leaderboard, iRacing):** in the iRacing profile switch Analysis on, pop out the Leaderboard; in a race: the cars around you in order, yours highlighted, each gap the car's to the one ahead (does the order match iRacing's own as cars pass, and the gaps its relative / F3 box?); a car pitting fades; in practice the gaps between best laps; Settings → Analysis → Leaderboard → Cars changes how many show. Tested in DaisysDev with a fake iRacing (shared memory, 13 cars at different paces, one in the pits): order, gaps (equal to the fake's, across the line too), pit fading, practice; not with real iRacing, a real start, multi-class or lapped cars.
- **3.28.1 (SimEffects → Analysis → Races):** in FH6, finish a circuit race and a sprint → Races has each with Start and Finish (e.g. "7 of 12"), Gained, laps and time; the line above counts wins, podiums, average and best finish, average start, places gained. A race quit isn't kept. Tested in DaisysDev with fake FH6 packets (3 laps, grid 7th, finishing 2nd, the clock going to 0 and stopping at the line: both kept, 7 → 2, +5); not with a sprint or a real grid.
- **3.28.0 (SimEffects → Analysis, Profiles, Shaker):** in FH6: a 3-lap race → History has all 3 laps (log: "last lap ended (…)"); near a known event start → Current Location names it, Name Track has its name, type over it + Save → renamed everywhere; somewhere new → Unknown + coordinates, Save tags it; next to a known one → Add New. Fold a few cards, switch tabs / restart → still folded. Best Times: only the classes raced. Discovered Tracks: sort by each header, Game's funnel, rename in the table, pick a row → Delete. Profiles → Profile: Units (System, Temperature) above Processes; changing them changes Analysis. Shaker with Master muted: an effect's Test is felt. Tested in DaisysDev with fake FH6 packets (3 laps with the clock going to 0 at the line, and with LastLap set while the clock stood still: 3 recorded each; rename, Add New, folding, tiles, table, Profile card) and the Shaker Test by loopback recording (muted master: silent before, heard after); sort clicks and Game's filter not clicked.
- **3.27.1 (SimEffects → Analysis):** in FH6: drive around → New track shows "Here: x, z" changing (and a track nearby, how far); at an event's marker type a name, Save → "Saved …", the box empty; start the event → Track shows the name, finish → its lap on it; the same sprint again (its grid a few hundred metres off) → found again, Laps shows "3 - 45%" and Position a Done column climbing; a circuit → "Pos - Lap" as "3 - 2 / 3". A widget's joystick → set a key → pressing it closes the widget, again → back where it was. Tested in DaisysDev with fake FH6 packets (tag, a sprint 500 m from the tag, again 280 m from the first start: found, 55% / 73% shown); the widget shortcut not pressed.
- **3.27.0 (Tools → Screen → Spacebar Drag):** switch it On; hold the spacebar and click anywhere on a window (a
  browser page, Notepad's text) → it follows the mouse, nothing is clicked in it, no space typed; a maximized one comes
  out restored under the pointer. Typing: words with spaces come out right at full speed (also with space rolled into
  the next letter); Win+Space and Alt+Space as always. Set a shortcut with the joystick → it switches On/Off, shown in
  Settings → Shortcuts. Off → the spacebar repeats again. Tested with a harness (grab, move, release, maximized) and the
  card in DaisysDev; not with real keys and mouse.
- **3.26.8 (Settings → General → Audio):** pick an Output other than Windows' default → ask a session to say something (or Settings → AI → Voice) → it plays there; a question's chime too. Pick an Input → 🎤 in a session → your words typed; a recording with the microphone track → that mic. Back to Windows default → as before. Voicemeeter's tools unchanged. Tested: the card in DaisysDev (both picked and saved), `--say` measured on the picked Speakers while Windows' default was Voicemeeter Input, recording from the picked mic, dictation through that kind of stream (a spoken clip recognized); 🎤 and a recording not run in the app.
- **3.26.7 (AI: files dropped; 🎤 beside Send):** drop a text file (and a PDF) on the box → 📎 chips above it, ✕ removes one; send → your bubble ends *📎 name*, Claude reads the file. On a tab of another PC's session (e.g. GAMER11): drop a file, send → the session there reads it (it's in `AI attachments` on that PC). Send and 🎤 side by side, same width as before. Tested: the files' round trip (JSON → saved, same bytes) with a harness; not on screen, no real turn.
- **3.26.6 (AI: Speak):** in a session, **🎤 Speak** above Send → it turns red **■ Stop**; talk → your words appear in the box a phrase at a time; Stop → back to Speak; fix and Send. Tested Windows' recognizer on a spoken test clip (word for word) and the default microphone opening; the button not seen on screen (DaisysDev's AI tab is off).
- **3.26.5 (Daisy's Server in Settings → General; Notepad on the server only):** Settings → General: no explanations, the Daisy's Server card (before Storage), connected as before; Settings → Tools → Notepad has only the default app; the Notepad page has no host box, its status says Connected to (server); your tabs that were on a PC host are on the server. Daisy's App Mobile's notepad can't connect to a PC any more. Tested with two throwaway copies and a test server (tabs up, live edit and rename, server restart), General on screen; not typed by hand.
- **3.30.1 (AI: tables shown):** a reply with a Markdown table shows it as a table (header bold, lines between rows, narrow columns for short text) instead of a blank block; code blocks show their text with "Code text" left on Theme. Tested: a render harness on the built DLL (`%TEMP%\mdharness`, the 3.30.0 audit table); not yet in a real session.
- **3.26.2 (one pinger):** Hardware → Performance's Network tests graph and Support → Ping both still draw every
  second, same times as before; a host down shows in both. Tested with a harness and Support's log in DaisysDev.
- **3.26.1 (games watched with one process list):** start and quit a game with Button2Key, Button2Key Remote,
  ForzaWheel, SimEffects (Auto Start), Mini Mirror (Process Watcher) and the Resizer set up for it: each still
  switches on and off as before; the pickers (Select App, Add a running program) list the same programs. Tested in
  DaisysDev with a stand-in game (not ForzaWheel's emulator or Mini Mirror's watcher).
- **3.25.2 (AI voice):** Settings → AI → **Voice**: pick one (it says a line); new session, ask "tell me out loud when you're done" → it speaks when done. Tested `--say` in DaisysDev; the combo not seen on screen.
- **3.26.4 (Tools → Notepad on Daisy's Server):** install `Downloads\DaisysServer-Setup-0.1.0.exe`, add a user and a token there; Settings → Tools → Notepad → Daisy's Server: address, port 38900, token, user, password → Connect; pick Daisy's Server in the Notepad's host box → your tabs go up; a second PC (or the phone, once built) on the same user: typing, new tabs, renames and closes show on both; Change password → still connected, notes there. Tested with two throwaway copies through a test server (tabs up, a third client's bold text and rename, an offline edit merged), the settings card on screen; not typed by hand.
- **3.25.0 (Settings → General → Settings):** the card sits right of Updates; **Save…** → a JSON file in Documents\Daisy's App with every settings file; change a setting, **Load…** that file → the app restarts with a "Settings loaded: …" notice and the old setting back. Tested with a harness (16 checks), not on screen.
- **3.24.9 (AI, Support):** with a session running on an account, a new tab's Claude Account list: a green dot between that account's arrow and its usage dot, an empty ring on the others; close the session → empty. After updating: Support has Logs and Tools (last two sub-tabs), Tools hasn't; Settings → General → Logging **View** opens Support → Logs; Logs' open tabs kept. Build only (DaisysDev was in use by another session).
- **3.24.8 (AI → Usage):** open an account: "Resets …" and "+…% an hour" sit at the right, under each bar's end. Build only.
- **3.24.7 (SimEffects → Analysis):** in FH6: press **Name** at an event's marker, name it, start the event → Track shows that name the whole race; finish the sprint → a lap 1 in Laps on that track ("sprint finished (the data stopped)" in the SimEffects log); quit a known sprint halfway from the pause menu → nothing recorded; a circuit race → its last lap recorded too; **Export** → a JSON file with the tracks. Tested in DaisysDev with fake FH6 packets (data stopping at the line, a quit, a series, a restart, a circuit with a pause).
- **3.24.5 (SimEffects → Analysis):** in FH6: a sprint to the finish → a lap 1 in Laps with the race's time and the New track box (if not, send the log's "race ended" line: it says how the clock behaved); the same sprint again → the same track, PB; at an event's start press **Name** (flag, top right), name it, start the event → Track shows that name during the race (circuit or sprint); a circuit still records every lap. Tested in DaisysDev with fake FH6 packets (a sprint whose clock stops at the finish, one whose LastLap is set).
- **3.24.6 (rig, controllers):** start the app with the Fanatec wheel and the other controllers on, ForzaWheel / Button2Key / Input Overlay as usual, force feedback in Forza: no crash (Fanatec driver, network check), FFB not stuttering every 5 s, Calibration still reads and saves. Send `DaisysApp-hang-*.dmp` (logs folder) if 3.24.4's hang at 20:22 left one. Build only.
- **3.24.4 (logs):** if the app hangs again (the rig, the friend's PC), wait ~5 s before closing it, then send `DaisysApp-hang-*.dmp` from the logs folder (Settings → General → Files) with the logs zip. The friend's PC: Mini Mirror's refusals now one Info line each spell (Video log), no ERRORs. Build only, not run.
- **3.24.3 (SimEffects):** the Play button white with FH6 closed, green once it starts (within ~3 s), blue after Play, red if FH6 is closed while playing; no "Waiting for…" line on any tool; Analysis' shortcut (a wheel button) turning it on / off from the seat, its widgets hiding when off; a widget's edit strip (Background, On top + Title, Done + Delete). Tested in DaisysDev with PING.EXE as the game.
- **3.24.2 (SimEffects):** in FH6 with the widgets over the game: Title off and Background 0% (readable? the shadow enough?), less padding; Stop hides them all (Analysis', Shaker meters), Play brings them back; Timing's PB (this car) and PBC (any car of the class) after a few laps; Position with Lap beside it in a circuit, alone in a sprint; Tires green / orange / red: are 60 / 99 / 116 °C right for FH6? An R class car shows "R" (I read FH6's class 6 as R, a guess); the Tracks tiles. Tested in DaisysDev with fake FH6 packets and seeded laps.
- **3.24.0 (SimEffects):** Profiles: the tiles (click one: the tool switches on / off), Version on the Forza Horizon profile (Auto → FH6 named; set FH5 → names as in FH5). In FH6: Make shows the car's name (one that says "Car n" is newer than the list: tell me its number); pause mid-race → every block, widget, the Input Overlay and the lights hold, then carry on; Tires readable from the seat, °C; Performance's graphs during a few laps; Timing, Position and Lap as widgets over the game (big enough? Position's "of" right? Lap's "of" appears from the second race on a track). Tested in DaisysDev with fake FH6 packets only (pause = zero packets, as FH sends them; if FH6 stops sending instead, the blocks still hold but the lights go off).
- **3.23.4 (SimEffects):** Profiles: the Name drop-down switches profile; Edit → rename → Save; New → name it → Save; Delete's Confirm?; Auto Start Yes on Forza Horizon's profile → start FH6 → the profile plays; quit FH6 → it stops (No: nothing happens). Analysis in FH6: the Tires car (temperatures readable on the tab and in a widget, stretched), Vehicle's columns ("A 800", "…HP …LB/FT V8": is V right for your car?), Laps' pairs, Pos - Lap during a race and Pos alone outside one. Tested in DaisysDev with fake FH packets and PING.EXE as the game; Auto Start "No" not clicked through.
- **3.23.3 (AI):** in a session's box, Enter makes a new line, Alt+Enter and Send send, Ctrl+Enter mid-turn still sends at once; the Usage block's amber line "Resets in 2h 48m @ 4:35PM" and red "Back in 1h 01m @ 4:30PM" (a day or more away: as before). Built only, not run.
- **3.23.1 (SimEffects → Analysis):** in Forza Horizon 6 with the Forza Horizon profile: the blocks while driving (HP, TQ, boost and tyre units right; Group a sensible number); a circuit race of a few laps → each lap in Laps with the right time, the New track box, named; the same race again → the same track, PB shown; another route from the same start → a new track; a sprint (whether Forza counts its finish as a lap: if no lap appears, tell me); a rewind and a restart mid-race; a widget made, rows unticked, Done, moved, deleted; Name Car. Tested here only with made-up packets (`%TEMP%\da323\race_send.py`), and the track Delete not pressed.
- **3.22.2 (Support → Wi-Fi):** on a PC with Wi-Fi: the three cards; 5 GHz (and 6) with several access points on one channel → each hump and name readable, the channel numbers right; pick a row → its details under the table, highlighted on both graphs, the band switching to it. Tested here only with made-up access points (no Wi-Fi on this PC).
- **3.22.1 (AI):** with Keep session notes on, ask a question that changes no file → no notes' turn after it; then a request that edits something → the notes' turn runs (covering both). A new tab's docs boxes show sizes ("README.md 8.5k"); a module's CLAUDE.md shrinks below ~8 KB when its notes are next written.
- **3.22.0 (Support → Stress Tests):** a Benchmark run on the rig (scores near or above 1,000; the reference is this dev PC, i7-8086K / GTX 1080 Ti); a long run (10 min a test, or Until stopped) with All at Once and Maximum logging, then Tools → Logs → Support; Export… there and Compare… with a run from another PC (Import…), Save Report… opened in the browser; the page by mouse (chips, options, Runs' Delete); an AMD or Intel GPU (VRAM's free memory from Windows' counter, the decoder on the GPU); an HDD or USB drive for Storage.
- **3.21.0 (Tools → DiskMark):** a full Default run (5 × 1 GiB) on the rig's drives, against CrystalDiskMark's own numbers if it's installed; the page by mouse (the drive list, Tests… for a Custom profile, the history table's Delete, pointing at the graph); a weekly schedule left on; a USB stick or SD card; the results with the Light theme. Tested here: C: (NVMe), E: (HDD), a scheduled run, Stop, the Settings page.
- **3.20.2 (Tools → Tools):** Reset Graphics Driver on a real screen (blinks?); Restart Explorer; the *as administrator* ones (Renew IP, Restart Audio, Sync Clock, Repair Windows, Admin Terminal: Windows' question, then it works); Empty Recycle Bin with Confirm?; the Power buttons when convenient (Restart to BIOS, Advanced Startup).
- **3.20.0 (Support → Networking Tools):** by mouse (UI Automation can't double-click or right-click): double-click a Traceroute hop → its details window (map, network, registration); click a marker on the map → its hop picked; double-click a Port Scan row → the port window; right-click an address anywhere → Copy IP / Copy Host, and selecting part of one + Ctrl+C. Host Scan of a phone, a TV / Chromecast, a printer and a Mac: the kind and model right? With nmap installed: its card (and OS guesses when run as administrator).
- **3.19.0 (Support → Networking Tools):** the Wi-Fi tab on a PC with Wi-Fi (the rig or a laptop): networks on the channel graph (2.4 / 5 / 6 GHz chips), widths right, the connected one bold, the table's security; if Windows wants location access, Location Settings opens it. Connection's Wi-Fi card on Wi-Fi. A Speed Test run (Run Now) and on a schedule (Settings → Support), its widget; right-click Ping's / Speed Test's card → Make Widget (here only made from a fixture). Traceroute's map dragged and zoomed by hand, Repeat for a while. Events with a real drop (unplug the cable or switch Wi-Fi off for 10 s). Hardware → Performance's Speed Test still runs as before (its code moved to Core).
- **3.18.8:** AI: paste an image between some text, select it all (or a part with the image) and press Backspace / Delete / Ctrl+X, or type over it → deleted, no "Something went wrong".
- **3.18.7:** AI: paste an image, click its ✕ (top right) → gone; Backspace right after one → gone. Resizer: a game windowed (or maximized), its profile's shortcut → applied; the shortcut again → back as it was (borders too); a group's shortcut the same.
- **3.18.5 (Mini Mirror, the friend's PC DESKTOP-QTNJDH2):** update there and use it as before; lock the PC (Win+L) with a mirror showing and unlock: the mirror comes back within ~5 s, no crash or "not responding". If it still hangs, send the hang dump: `DaisysApp-hang-*.dmp` in its logs folder, or WER's `memory.hdmp` under `C:\ProgramData\Microsoft\Windows\WER\ReportQueue\Critical_DaisysApp.exe_*` (or ReportArchive).
- **3.18.4 (Mini Mirror):** a mirror's or group's Process Watcher set to a game (Select App, or typed and Add): start the game → Mini Mirror ON (no mirror shown by it), its shortcuts work in the game; close it → OFF. Switched off by hand while the game runs → stays off when it closes.
- **3.18.3:** right-click the Input Overlay over a game → its edit strip; drag / stretch, Done; the trash can switches it off. The speeds between the bars and the wheel (green / white / orange), the grey wheel. The Shaker's Test beside each device's volume. ForzaWheel's bubble with the two checkboxes gone.
- **3.17.2:** a widget's trash can (its edit strip, Performance's / Zabbix's / the Shaker's Widgets card): red ✕ first, a second click deletes. Right-click a Performance tile → History, Make Widget. AI Usage at a limit: "Back @ 2:10PM". Input Overlay / Arduino / AmbiLight without Titlebar Status, bubbles still showing.
- **3.18.1 (SimEffects):** Play / Stop beside the Profile drop-down (and its shortcut, a key or a wheel button): stopped, the shaker, lights, overlay and AmbiLight all go dark and their ON shows grey; Play brings back the ones that were on. The overlay's three speeds the same size over a game.
- **3.18.0 (SimEffects profiles):** start a game: the Profile drop-down shows its profile and every sub-module switches (on / off as the Sub-modules grid has it, the Shaker's effects, shift points, overlay settings); close it: back to Desktop. A change on a tool's tab stays with the profile in use. Rename / delete / New on the Profiles tab. The Shaker, lights and overlay all following one game at once.
- **3.17.0 (SimEffects → Input Overlay, profiles):** the overlay over Forza Horizon (and iRacing / ACC) in Game mode: traces follow your pedals and wheel (steering direction right in iRacing and ACC? the clutch the right way round in ACC?), the three speeds (top / bottom hold for Window Size, then catch up); Device mode with Set on the rig's pedals; it came on with Forza if ForzaWheel's overlay was showing (Switch On With Game); a session log appears in Forza Analysis after 20 s of driving (none was ever written before). Switch On With Game on the Shaker, Arduino and AmbiLight with a game; Arduino OFF frees the COM port, ON reconnects; the Arduino bubble.
- **3.16.5 (AI):** Brief Replies ticked on a new tab: the reply is brief, your bubble shows only what you wrote. Compact after Release ticked: after a release (tag pushed) and its notes, the tab compacts by itself. Both on a session started from another PC or the phone (the phone app doesn't send them yet: it gets the PC's defaults).
- **3.16.4 (AI):** Compact on a session with some context → "Compacting" in the dashboard and "✂ Compacting the conversation…" in the chat while it runs; then "… → … tokens of context", the context ring smaller, "Compacted in …".
- **3.16.3 (Button2Key, Shaker):** the bubbles with a real game: "B2K: <profile>" changing as a game switches the profile; "Shaker: <profile>" while the Shaker is on; Titlebar Status off hides it.
- **3.16.2 (AI):** rename a session tab on one PC → the other PC's tab (and Remote Sessions) shows the name; rename a remote tab → renamed on the host.
- **3.16.1 (AI):** two sessions working at once → a title bubble each (named as its tab); click each → the AI tab at that session. An account at its limit: "Back in …" on the right. The remote-only PC: no install text in the Claude Account box.
- **Next:** the user tries 3.16.0's Tools → Logs header menus with a real mouse click (sort, Level/Source tick boxes, the check on the current sort). the user updates both PCs to 3.15.3 and tries a remote session's Claude Account box (the same list with usage, ⟳ Usage opening that PC's details, Check now, click to switch) and a new tab with Host on the other PC; and the Usage button opening the details locally (it couldn't before). the user tries 3.15.0's AI account list: the columns (5H, W, days) lining up, clicking a name to switch a running session (and in a new tab), the tint on the one in use, amber/red reasons under the name; the new tab's two columns. the user tries 3.13.1's AmbiLight on the rope: the slices in the Displays picture, then LEDs raised above Segments (Show LEDs; lower again if the rope goes dark) so a zone gives more than one colour. the user tries 3.13.0's clickable bubbles (each opens its module/sub-tab, the Webcam's Video → Overlay, the AI's the AI tab; drag still moves the window; tooltips). the user updates the rig to 3.12.9; if it still crashes, set up a full crash dump there (as administrator: `reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\DaisysApp.exe" /v DumpType /t REG_DWORD /d 2 /f`, optionally page heap via Image File Execution Options GlobalFlag 0x02000000 + PageHeapFlags 3 so the crash stops at the bad write) and read it with `dotnet-dump`/WinDbg. the user updates to 3.12.7: the rope lights again (10 colours); then Show Segments / Show LEDs with higher counts to find the most it takes (it went dark at 84). the user updates to 3.12.8 and sets a shortcut by hand in a module's shortcut window (shows the pick until Done; a set one needs × first; a controller button too) and in Settings → Shortcuts. the user tries 3.12.5's AmbiLight: Show LEDs on the rope (a fine stripe all along = it takes 84; then try more, up to 255, until the stripe blurs), then the blue-window case. the user runs 3.12.4 on the rig with the Xbox pad plugged in: no more random crashes (if one comes, the export's crash records and the timeline around it). the user tries 3.12.3's AmbiLight on the ropes (a zone over black stays dark; segments in one zone follow what is under each). the user tries 3.12.2's Mini Mirror ON / OFF and its shortcut. the user tries 3.12.x's HA tab (Plex players gone, ones in areas kept) with their Home Assistant (homeassistant.local:8123 + a long-lived token in the setup step): areas/devices as expected, quick toggles, a device window, a shortcut, Hide/Show; if Home Assistant refuses the registries (non-admin user) everything is ungrouped (HomeAssistant log says which). the user tries 3.11.1's account lines in the Usage card (a real second account read by `/usage`, amber/red states, expanding a name; whether the pace line and the line colours at WarnAt rather than the bars' 60/85% suit them). the user tries 3.11.0's RGB on the rig: the Z790's board + ARGB headers listed (how many headers, which carry the 9 fans), effects/colours on each, Lights OFF; the TUF 5070 Ti found (its ENE version in the Rgb log if not); Find RAM (needs PawnIO on the rig: LibreHardwareMonitor installs it; Windows asks) then the RAM card's Apply; Armoury Crate's LightingService fighting it; whether per-fan control or more effects are wanted. the user tries 3.10.2: the SimJack’s X Rotation (and Y Rotation) moving in Calibration, then the wizard (Ry’s present calibration -14800 · 0 · 0 has centre = max); whether Crashes/Impacts now hit hard enough on the rig (if too much: Length, the hold share 0.35, the duck 0.7). the user tries 3.10.1 (the AmbiLight shortcut and bubble, the overlay with real pedals, Module Docs "Claude"). the user tries 3.10.0's Calibration on the SimJack Pro pedals (the axes listed with names and ranges; the wizard's found range; Save, then the Output bar and a game showing the calibrated pedal; whether DIView shows the same; if Save says Windows didn't take it, the Calibration log has the code). the user tries 3.9.2's AmbiLight: Find → Test each Neon Rope Light (on, red 2 s, back off), untick the two lower displays, Add, Show Segments to count, drag zones (red on the rope), then ON; whether Stretch on/off and the colours/latency feel right; its segment count for a built-in table. The user tries 3.9.1 (My prompts on a long session; switching account while waiting for a limit: stays waiting, usage changes, sending carries on on the new account). The user tries 3.9.0's AmbiLight on their Govee Neon Rope Light (LAN Control on in Govee Home; Find; Test to count segments; Stretch on/off; zones on the triples; whether colours/latency feel right; the model and its segment count for a built-in table). The user tries 3.8.6 (Module Docs order and naming, Show Earlier / Show All on a long session, the account box in Usage, the Shaker's Widgets card, Notepad Ctrl+W, the ForzaWheel overlay hiding when it's off). The user tries 3.7.0 (the Coding bubble during a real turn; the webcam coming back after a restart, without sound; a pop-out edited by right-click / a held finger; Shaker Test level on the rig; whether "main system audio" meant what was done). The user tests 3.6.1 on the rig and reports; when FFB drops, read the rig's Joy2Key/Butt2KeyRemote logs for "Sent key:" and the modules' "Shortcut …:" lines just before it (no lines = the app typed nothing). the user tries 3.5.6 with their SimHub-flashed Arduinos as they are (SimHub closed or its Arduino off: one program per COM port): the light goes green with "(SimHub sketch) · n RGB LEDs", set First LED/LEDs for the shift lights, Test; if it stays red, the SimEffects log's "Arduino COMx:" lines say why (a Debug log shows the sketch's own debug text). Before 3.5.6 the plan was: the user flashes DaisysSim on their Arduino(s) (FastLED; pins), adds them in SimEffects → Arduino → Arduinos…, tries Notify's matrix/strip (rotation/zigzag/colour order right?) and Demo, then a game (redline learning); tries Fanatec on the RS (if LEDs don't change: the report or the driver; check the SimEffects log "Fanatec: … reports n/m bytes", Right to Left); the user tries 3.5.2 (both PCs on it: a remote tab's Claude Account box switching that session there); the user updates (from 3.3.3) and tries 3.5.1 (Module Docs by folder name; Recently Closed list beside Previous/Remote Sessions; Shaker on port 8000 with FH's Data Out set to 8000; meters pop-out on the rig's second screen; whether all .md files in subfolders, not only READMEs, are wanted in Module Docs); the user tries 3.5.0 (Settings → Shortcuts: change/Reset/Delete, the notes; every module's shortcut window; Resizer with a wheel button; Confirm? on the converted buttons; whether the kept warning dialogs should go too); the user tries 3.4.0 (Module Docs ticked in a new AI tab: the first message carries them; a remote PC's boxes once it's on 3.4.0); the user tries 3.3.4 (plain keys in the Shaker's shortcut boxes, the tab following the Level Wizard on another screen); the user tries 3.3.3's Notepad Add Server… with the other PC's address, port and passphrase; the user sets up 3.3.2's positions on the rig (front/rear, sides or corners) and says how the per-wheel effects feel; the user tries 3.3.1 on the rig (device sliders now show and set Windows' volume; the shakers should now play) and 3.3.0's Level Wizard while driving (shortcuts on their wheel/button box, Solo/All) and the Shaker name; the user tries 3.2.5 (a game starting switches Button2Key on and it works without opening the tab, with a real game and controller); how the Shaker's effects feel on the rig (3.2.3's per-device levels and on/off shortcut, 3.2.4's filtering, thresholds per game); the user tries 3.2.2/3.2.1 (the box per session, ⟳ Usage, a second Claude account added from a new tab, two sessions on different accounts at once, switching a session in its dashboard; the mobile app using `list`/`new` with `account`); the user tries 3.2.0's Bass Shaker with their shakers and games (Data Out set in Forza to 127.0.0.1 and the profile's port; whether effects feel right, thresholds per game, transducer ranges, the meter style they meant); the user tries 3.1.8 (Zabbix packing/drag/Display Grid Size: whether Columns × Rows is what they meant by the 2 fields; File Sync copy/paste of problems, the folders/files counter on a big compare); the user tries 3.1.7's Terminal (typing, copy/paste, WSL, rename, the default kind; whether the colours look like their default terminal); the user tries 3.1.6/3.1.2 (Forza starting switches Button2Key on with its profile, closing switches it off however it was on; the shortcut; renaming/deleting Default); the user tries 3.1.5 (tray background green/red with the title bar's bubbles; Ctrl+Shift+Z in the Notepad) and 3.1.4/3.1.3 (Notepad names and in-place rename between PCs); the user tries 3.1.1 (Context mid-turn in a real session, iRacing Export shared, Zabbix groups/Alerts/Hide Device); the user tries 3.1.0's AI tab on the laptop (new tab scrolls, Recently Closed drop-down, a remote Host); the user tries 3.0.6 (tabs between PCs, `---`, Make default… then pick it in Windows, a file opening popped out, Save, Restore + Choose…); the user tries 1.1.4 on a device (✕ fade and 3 s hold; a held ButtScreen button also brings the ✕ up: offered to limit it to empty space if wanted; passphrase hidden while typed; self-update from 1.1.x); the user tries the Notepad between two real PCs (host's AI remote control on; the other PC in its list) and its pop-out; the user tries 2.9.0 (Bluetooth connect/disconnect on a PC with Bluetooth: if a device won't switch, the AudioTools log has "One-shot … 0x…" at Debug; Button2Key switching once with a game; the question sound; Everything I've said); the user tries 2.8.2 (Status card, joystick shortcut, Butt Wizard, Dragon Touch size: confirm which X10) and 2.8.1 (wizard steps, Show Text, Titles, iPhone sizes) and 2.8.0's templates (each game's keys in the real game; FH6's program name); the user tries 2.7.7/2.7.6 (Default as an ordinary BigButt with games, themes, click to edit, Link a ButtScreen…, Remove, Held turning the grid); installs 1.1.1 on the Galaxy tablet and the Pixel 10 Pro (B2KR On + passphrase + Save on the PC first; over USB: USB debugging, then `%USERPROFILE%\Android\sdk\platform-tools\adb.exe install -r <apk>`), checks the full-screen border on the Pixel, the BigButts screen, games switching B2KR on; add the tablet's model to `EditorWindow.Shapes` once known. A new APK: raise `versionCode`/`versionName`, build release with `-PB2KR_KEYSTORE=…`, attach to that DA release. **Not started** (interrupted earlier): AI tab, a block in the right column above "Claude Account" listing active sessions on every remote PC set up; clicking one opens it as a new/current tab to connect and control; publish as a new sub version (needs a server op listing running sessions, and the client asking each of `RemoteSettings.Pcs`). Still for the user to check: B2KR in DA (On + shortcut, bubble, editor, icons, pop-outs), 2.6.0 Add a PC / remote usage, 2.5.x accounts, 2.4.0 remote PCs list, 2.3.x ForzaWheel games and Webcam, 2.2.x Button2Key / overlay, the Network column. Mini Mirror: the friend updates to 2.6.2; if still black, the Video log's "pictures come as …", "brightness" and 15 s lines say which side. On a hang, `dotnet-dump analyze` the .dmp (`clrstack` on thread 0).
