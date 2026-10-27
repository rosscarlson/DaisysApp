# Daisy's App: version history

What changed in each release, newest first. (The [README](README.md) describes the app as it is now, and each
module's guide describes that module.)

**3.15.3**
- **AI: a session on another PC has the same Claude Account box as one here.** The same list of accounts with their
  usage, and the same **⟳ Usage** button under it. **Usage** opens that PC's full usage details at the top of the tab,
  the same panel as this PC's: each window's bar and pace, the last day's chart, and what's been using it. ⟳ and
  **Check now** ask that PC to check again. On another account there, the button is **⟳ Refresh**, as it is here. A new
  tab's list of another PC's accounts is asked for again every minute while it shows.
- **AI: Usage opens the details again.** The Usage button could only close them.
- **Remote API:** a session's `usage` message has `own`, the PC's own sign-in's usage in full (`plan`, `breakdown`,
  `checked`, `problem`, `history`; the standalone `usage` op too). `{op: "accounts"}` lists the accounts with their
  usage, and the control `refresh_usage` makes it check again. See [API.md](API.md).

**3.15.2
- **AI: another PC's accounts as the same list.** Starting a session on another PC (a new tab, Host on that PC) and a
  session open from another PC now show its Claude accounts as the list this PC's are in, each with its usage there
  (5H, W, days to the week's reset; amber or red near or at a limit, ▸ for its windows): click a name to pick it (the
  drop-down is gone). Both PCs need 3.15.2 for the usage; an older one's accounts show with no usage.
- **Remote API:** each account in `accounts` (in `list`, `attached` and a session's `usage`) has its `usage` there, and
  `attached` and `usage` say which is Claude Code's own sign-in (`ownAccount`); see [API.md](API.md).

**3.15.1
- **AI: the Usage button says Refresh again.** On a session on another account (where the button only reads its usage
  again) it showed just the ⟳ icon; it's **⟳ Refresh** now.

**3.15.0**
- **AI: the list of accounts in columns, and clicked to choose.** In the Usage card, each account's line shows this 5
  hours (*5H*), this week (*W*) and the days until the week resets (*.1D* to *6.9D*) in columns, lined up from line to
  line; near or at a limit, when it's back shows under the name. **Click an account's name** to switch the session to
  it (the drop-down is gone): the one in use is bold, a little larger, on a tint; ▸ opens its usage; **Usage** stays at
  the bottom right.
- **AI: a new tab's panel in two columns.** Name and Host at the left; Claude Account at the right, with Permissions
  under it. Claude Account is the same list as the Usage card's (the same code, so a change to one shows in both), so
  you see each account's usage while you choose: click a name to start on it; **Add an account…** is under the list.
  (For another PC under Host, its accounts stay a drop-down: their usage isn't known here.)

**3.14.1**
- **AI: your requests are logged by the app, not the AI.** Each message you send is added to `REQUESTS.md` in the
  session's folder, word for word under the day's date (without the files read first; not slash commands or the
  carry-on message), by Daisy's App itself: no AI turn, none of your plan. So the log is complete with session notes
  off, too. **Keep my requests in REQUESTS.md** (Settings → AI → Session notes) turns it on or off; it starts on if
  session notes were on. The session notes' message no longer asks the AI to write it (the old message is replaced
  if you still had it).

**3.14.0**
- **AI: session notes cost less of your plan.** The message that asks for the session notes now has a module's notes
  kept in a `CLAUDE.md` in that module's folder (Claude Code reads one only while it works there), keeps the working
  folder's `CLAUDE.md` to what every session needs (Claude Code reads that one on every turn of every session), and
  has your requests appended to `REQUESTS.md` without reading the whole file first. If you still had the old message,
  it's replaced by the new one; one of your own is left as it is.
- **AI: CLAUDE.md isn't sent twice.** Ticked under Project Docs in a new Claude Code tab, it was sent ahead of your
  first message although Claude Code has it already; now it's left out of what's sent (other AIs still get it).
- Daisy's App's own notes: the root `CLAUDE.md` (115 KB, about 30,000 tokens on every turn) split into each module's
  folder; it's 6 KB now.

**3.13.1**
- **SimEffects → AmbiLight: each LED's slice shown, and cut the right way.** In the Displays picture, a zone with
  several LEDs shows its slices, each in the colour its LED gets, so you see exactly the spot each LED is sampled from
  (only inside the zone; a bigger zone gives bigger slices). The slices now go across the way the light runs (worked
  out from the next zone), whatever the zone's shape: a zone taller than wide in a row along the top was cut into rows
  before, so every LED averaged a whole row of it. (The code already went out inside 3.13.0, with that release's
  change; this release says what it is.)

**3.13.0**
- **Title bar bubbles are clickable:** clicking one (ForzaWheel, Button2Key, Button2Key Remote, AmbiLight, Webcam, the
  AI's Coding and Claude usage) takes you to its module, on the right sub-tab (the Webcam's: Video → Overlay). Dragging
  a bubble still moves the window, and pointing at one shows what it's about. Modules say where theirs goes through
  Core's `TitleBadges` (API 0.39).

**3.12.9**
- **Logs moved to the Tools tab** (Tools → Logs, from Network → Logs). Your open log tabs and filters come with it; if it
  was switched off, it stays off, and if it was showing when you last closed the app, the app opens on it.
- **Logs: Clear** deletes the tab's own log files (Daisy's App's: every module's, or one module's), today's too, after
  the usual **Confirm?**. Windows' and WSL's logs aren't cleared from here.
- **Logs: Export… saves only what's shown.** The zip (to send for help) now holds just the entries the tab shows, with
  its time range, level and search, a file per log, with Windows' crash records as before. Before, it held every log
  file of the last 7 days whatever was shown. (Text and CSV already saved only what's shown.)
- **Crashes (still being tracked down):** the rig still crashed on 3.12.6 and 3.12.8, within seconds of its Xbox pad
  dropping off USB and coming back. Every call the app makes to Windows' old joystick API (shortcut buttons on
  controllers, Button2Key, Calibration) now goes one at a time, never from two threads at once while Windows rebuilds
  its list of controllers. This may not be the cause; if it crashes again, the crash records will tell.

**3.12.8**
- **Shortcuts:** a module's shortcut window no longer closes the moment a key is pressed: it shows the one picked
  until **Done**. A shortcut already set shows there and stays until it's cleared with the **×**, which then listens
  for the new one.

**3.12.7**
- **SimEffects → AmbiLight:** lights stayed dark after 3.12.5. A light drops a picture with more colours than it
  takes, and 3.12.5 sent 84 to every light (the Neon Rope Light took its 10 segments' worth but not 84). **LEDs** now
  starts at the light's **Segments** and follows them until you set it; each still takes its colour from exactly its
  own spot of its zone. Set it higher only if the light takes it (Show LEDs; if the light goes dark, lower it).

**3.12.6**
- **Shortcuts:** setting a shortcut works again. Since 3.5.0, pressing a key or button in a shortcut box (any module's
  shortcut window, Settings → Shortcuts) went back to "Click to set a shortcut" and nothing was saved; shortcuts
  already set kept working.

**3.12.5**
- **SimEffects → AmbiLight:** colour LED by LED, not segment by segment. A light now gets **LEDs** (84 to begin with,
  up to 255): that many colours are sent along it, each taken from exactly its own spot in its zone, and the light
  spreads them over all its LEDs (Stretch). So a zone covering 10 of them, with a small blue window in its middle and
  black around it, lights only the LEDs behind the blue. Before, only the light's segments were sent (10 by
  default), so each one's whole stretch showed one average and the light blended between them. **Show LEDs** shows
  how finely the light takes them (each white, then red, green, blue); Stretch off still sends one colour per
  segment.

**3.12.4**
- **Crash fix (Gaming → Button2Key):** with an Xbox-style pad plugged in, Daisy's App could crash now and then
  (a few times a day on one PC, often soon after the pad was plugged in, woke up or Button2Key switched on, and
  sometimes on exit), with errors in places that had nothing to do with it (heap corruption, Mini Mirror, the GPU
  readings, the window). Reading the pad's Xbox button wrote 4 bytes past the memory it was given; it no longer does.
  Also, Button2Key's tab and its engine can no longer read a controller the other has just let go of.

**3.12.3**
- **SimEffects → AmbiLight:** each segment takes its colour from its own part of its zone (a zone of 10 segments is
  cut in 10 along its length), so 10 LEDs under a zone show what's under each of them, not one average. A zone takes
  only what's inside it: the pixels on its edges count only as far as they're inside (before, a zone along a black
  band could take the white just below it), and the screen is read 2–3 times finer.

**3.12.2**
- **Video → Mini Mirror:** an **ON / OFF** switch at the top left, with a shortcut button beside it (keys or a
  controller button; also in Settings → Shortcuts and the tray menu). Off, no mirror shows or captures and Mini
  Mirror's other shortcuts are left to other programs; making a new mirror switches it on.

**3.12.1**
- **HA:** media players that aren't in an area are left off the tab (Plex makes one for every person a library is
  shared with); ones in an area still show. Settings → HA → **Show media players without an area** brings them back.

**3.12.0**
- **HA** (new tab): your Home Assistant's devices, laid out as Network → Zabbix's hosts: a box per device in a box
  per area (or floor, or type; groups dragged into your order), with coloured counts of what's on, off and
  unavailable, a search, area and type filters and On only. Each box has a quick control for its main entity (ON /
  OFF, Activate, Run, Press), what it's doing and its sensors' values, live as Home Assistant changes. Click one for
  its window: every control it has (brightness, warmth, colour and effects; speed; position and Open / Stop / Close;
  target temperature and modes; volume, play / pause, source; lock / unlock; a vacuum's buttons; numbers and
  options), its readings, and its settings under More; **Hide** takes it off the tab, and Settings → HA lists what's
  hidden to show again. Anything that switches or has a level can have shortcuts (On / off, Up, Down: keys or
  controller buttons, through Settings → Shortcuts too). The setup wizard asks for Home Assistant's address and a
  long-lived access token the first time (or Skip, or Switch off HA). Controls devices only; Home Assistant itself
  isn't changed.

**3.11.1**
- **AI:** the Claude Account box (in a session's Usage card) lists every Claude account kept here, a line each, above
  the drop-down: green while its usage is fine (its 5-hour and week percentages), amber past the warning level (and
  when that window resets), red at its limit (and how long until it's back); the session's account is in bold. Click
  a name for its windows: a bar each, when it resets, the pace (Claude Code's own sign-in) and when it was read.
  Other accounts are read with `/usage` in their own folders (no model is asked) when the tab first shows, every few
  minutes while it shows, and by the Usage button's ⟳.

**3.11.0**
- **Gaming → RGB** (new): the lights on your motherboard, the fans and strips on its ARGB headers, your RAM and your
  graphics card, without the makers' programs. Each device gets an effect (Static, Breathing, Flashing, Colour Cycle,
  Rainbow or Off) with its colour and speed, **All Devices** sets them all at once, and **Lights ON / OFF** turns
  every light off and back on. Settings are kept on the devices, so they stay after a restart. Supported:
  - ASUS motherboards with Aura on USB (most since 2019), the board's lights and each ARGB header, with no rights or
    driver needed.
  - ASUS graphics cards (RTX 30, 40 and 50 series) and EVGA GTX 10 cards, through NVIDIA's driver.
  - G.Skill Trident Z RGB / Neo and other RAM with an ENE chip, and older ASUS boards, through PawnIO (a signed
    driver that isn't any maker's, installed with LibreHardwareMonitor). Windows asks for administrator rights for
    each change.

  See the [RGB guide](src/Modules/Rgb/README.md) for what's known and what isn't yet.

**3.10.2**
- **Gaming → Calibration:** fixed: only some axes moved (on SimJack pedals, Z did and X Rotation didn't): each axis
  was read at the device's own place for it rather than its place in what DirectInput hands over, so only an axis
  where the two match worked. In the wizard, click an axis's number to go straight to it; the one showing is green and
  an axis being moved turns blue, so you can tell which pedal is which.
- **SimEffects → Shaker:** Crashes and Impacts are a proper hit now: full strength at once and held for the first
  third of their length (they faded from the first cycle before), the pitch dropping from the shaker's strongest
  frequency to the effect's own, a fuller wave, and stronger from the threshold up (Crashes 75% at the threshold,
  Impacts over half, both full at twice it). While one plays, the other effects on that device are turned down so it
  isn't lost under the engine and road. Measured through loopback, a crash carries about 3.6 times the energy at the
  same volume. An effect's Test plays one hit (it played one every 0.45 s: the light thud, thud, thud).

**3.10.1**
- **AI:** a new tab's Project Docs and Module Docs boxes are 6 rows high (4 before), and Module Docs names each tab as
  it shows: a renamed tab by your name for it (the AI tab as *Claude*).
- **SimEffects → AmbiLight:** an on / off shortcut (the controller button beside ON / OFF: a key combination or a
  controller button, also in Settings → Shortcuts), and a green **AmbiLight** bubble in the title bar while it's on
  (**Titlebar Status** leaves it out).
- **Gaming → ForzaWheel:** the input overlay's left fifth runs twice as fast as the rest, easing in, so the right moves
  as before and it holds 20% more history; each bar (throttle, brake, clutch, handbrake in orange) has how far it's
  pressed above it, 0–100.

**3.10.0**
- **Gaming → Calibration** (new): your pedals', wheel's, sticks' and handbrake's axes calibrated in Windows, as
  Heusinkveld's DIView (and Windows' Game Controllers page) does it, so every game gets them calibrated. Pick a
  controller on the left; each axis shows its raw reading (with Windows' min, centre and max as ticks) and what games
  get. Click an axis to set its min, centre and max (sliders, numbers, **Here** for where it is now, **Midpoint** for a
  straight line) on a live graph, then **Save**; **Default** and **Original** put back Windows' own or what it had
  before. **Wizard** goes through each axis: move it end to end and its range is found, the centre halfway; **Reset**,
  **Skip**, **Ignore** (left out from then on) and **Back** on each, and a summary to **Save**.

**3.9.2**
- **SimEffects → AmbiLight:** lights found are listed by name (*Neon Rope Light 5M*, *Smart Ceiling Light*: Govee's
  product names, not *H6641*), and you can rename them. **Test** (on each light found, and beside Find for the one
  picked) turns the whole light red for 2 seconds, then puts it back as it was: its own scene, its brightness, and
  off again if it was off (or didn't say). Each display has a tick in its middle: untick the ones the lights shouldn't
  follow (say, two below three), and new lights are laid out on the ticked ones. While you hold a zone, its segments
  light red on the light, so you can see which part you're placing. The segment pattern (Test before) is now **Show
  Segments**, in the Segments card.

**3.9.1**
- **AI → Search → My prompts** (new, the button after 💬): the session as a chat of only your prompts, each with the
  AI's final reply to it, to see how the work went. The session notes' turns are left out, and a reply after carrying
  on from a limit counts as the prompt's.
- **AI:** changing a session's Claude account while it waits for a limit no longer carries on at once: only its usage
  changes. It carries on on the new account when you send something (or **Send now** on a waiting message), or by
  itself at the time it was waiting for, as before. Sending anything while it waits now also ends the wait.

**3.9.0**
- **SimEffects → AmbiLight** (new): what's on your displays, on Govee lights behind them, as Philips' Ambilight.
  **Find** your Govee lights (switch on LAN Control for them in Govee Home first) or add one by its address; set how
  many **Segments** it has (**Test** lights them white, red, green, blue to count them) and how many share a zone
  (**Per Zone**: 1 each, or 10 of 100 for 10 zones of 10); **Lay Out** the zones along the top, bottom, sides or all
  around your displays, then drag and size each one over your displays (all of them, triples too), shown with what's
  on them and each zone's colour now. Frame rate, smoothing, saturation, a black level and the light's brightness.

**3.8.6**
- **Video → Fire TV is gone** (it's back to 3.7's Video): Fire OS 8 put most of what it did back. A TV you used it on
  keeps what was turned off; to turn things back on, use the TV's own Settings → Applications.
- **AI:** the Claude Account box is at the bottom of the Usage card (no card of its own). Above a long conversation,
  *n earlier messages aren't shown* has **Show Earlier** and **Show All**, to scroll back to the session's start.
  **Module Docs** shows each as its tab and sub-tab (*Audio → Volume*, *Video → Mini Mirror*), in the order the tabs
  show.
- **SimEffects → Shaker:** making the meters widget again after deleting it works (it failed with "already the
  logical child of another element"); a **Widgets** card at the bottom left lists the open widgets with Edit and
  Delete.
- **Tools → Notepad:** Ctrl+W closes the tab (a second Ctrl+W when it asks Confirm?).
- **Gaming → ForzaWheel:** the input overlay shows only while ForzaWheel is on or one of its games is running.

**3.8.5**
- **Video → Fire TV:** Ads/Tracking now lists the TV's viewing recognition (ACR, which reports what's on your screen)
  and advertising ID, advised off, plus Special offers, Ad links, Media recommendations, Sneak Peek and Ambient
  Experience; Amazon lists Live Shopping, miniTV, Game hub and Kids+. They were hidden as parts of Fire OS. The guide
  explains what can't be done: the home screen's ads, Sponsored rows and placeholder tiles come from Amazon's home
  screen itself, and turning every ad package off didn't change them.

**3.8.4**
- **Video → Fire TV: Keep apps off** (on by default). Fire OS 8 turns some of its apps back on at every start (Alexa,
  and at times Silk Browser, Fubo and Push notifications), from inside the system, which nothing can stop without
  rooting the TV. Now, while Daisy's App runs, it checks your TVs every 2 minutes and turns off again whatever came
  back on (right after Apply's restart too), and Status says so. It never pops anything up on a TV that hasn't
  allowed this PC. Turned-off apps are also suspended, so they can't open or show notifications even when Fire OS
  turns them on.

**3.8.3**
- **Video → Fire TV:** the **Ad-blocking DNS** tweak is gone: it cut the TV off the internet. Connecting to a TV that
  has it puts the TV's Private DNS back as it was (a Pi-hole on your network does the same job for the TV).
- Apps are now **turned off** rather than removed: Fire OS 8 put removed apps back at the next restart, while
  turned-off ones stay off (and your own apps are never deleted). After **Apply** the TV restarts so the changes take
  effect, the tab connects again by itself, and it says which changes Fire OS undid (it turns Push notifications and
  Silk Browser back on at every start).
- **Status** (was *Done*): a line for each thing it does, with its time, a mark and what the TV answered, a heading
  and summary for each Apply; right-click to copy.
- The groups are in the order **Amazon**, **Ads/Tracking**, **Installed Apps**, **System Updates**; Fire OS's
  overlay packages no longer show among your apps.

**3.8.2**
- **Video → Fire TV:** a tick now means *on*: ticked apps stay or come back, unticked ones go, and **Apply** (in
  place of Remove Ticked and each app's Restore) makes the TV as ticked, saying how many it changes; it asks
  Confirm? before deleting one of your own apps. The ticks show the TV as it is (on a TV not changed yet, what's
  advised to go starts unticked).
- **No prompt on the TV?** When the TV doesn't answer for a minute, a small window walks through turning ADB
  Debugging off and on again on the TV, a step at a time, and its last step connects again so the prompt shows.

**3.8.0**
- **New: Video → Fire TV.** Debloats an Amazon Fire TV (any Fire OS model: Fire TV Stick 4K, 4K Max, Lite, Cube…; not
  the Vega OS Fire TV Stick 4K Select) over the network, without installing anything: **Find** looks for Fire TVs on
  your network (or type its IP address), the TV asks to allow this PC once, then its apps are listed by group (your
  apps, ads and tracking, Amazon's apps, system updates) with what's advised ticked: everything but Plex, F1 TV and
  YouTube TV (and the Appstore, which updates them). **Remove Ticked**, a **Restore** on each removed app, **Restore
  All**, and tweaks: an ad-blocking DNS (AdGuard) and faster animations. ADB Debugging must be on on the TV (its guide
  says how). Friends can use it the same way.

**3.7.0**
- **One widget code everywhere.** Button2Key Remote's pop-outs are now widgets like Performance's, Zabbix's,
  ForzaWheel's overlay and the Shaker's meters (Core's `Widget`): right-click one (or hold a finger on the space
  between its buttons) to drag it, stretch it from any edge (it keeps its ButtScreen's shape), set how solid it is,
  On top and Locked, Edit ButtScreen, Done, or close it. Their places carry over. Right-click menus say **Make Widget**
  everywhere, from one shared piece of code.
- **SimEffects → Shaker:** right-click the meters → **Make Widget** (as the pop-out icon). A new Settings → SimEffects
  → Shaker page sets the **Test level**, how strong the Test buttons play: 100% by default (they played at 60–80%).
- **AI:** a **Coding** bubble in the title bar while a session works, in its status colour (purple thinking, blue
  writing, orange working); a checkbox beside the usage bubble's leaves it out. **Module Docs** are sorted by name.
  Every module's title bar bubble uses Core's shared colours now.
- **Video → Recording:** a virtual webcam that was running when the app closed or restarted starts again by itself.
  Started that way, or by its shortcut or the tray, it has no sound (Discord keeps your microphone); the Start window
  still offers the game's sound, from Windows' main playback device (never the Communications one).

**3.6.1**
- **Logs say what the app pressed.** Every key or mouse button Button2Key (or Button2Key Remote) sends is logged in its
  log ("Sent key: Up"; at most 30 every 10 seconds, then a count), and every shortcut pressed, in its module's log
  ("Shortcut Controller 8 Button 1: ForzaWheel · Start / stop the emulator"), at the normal log level. So when force
  feedback drops in a game, the logs show whether Daisy's App typed or pressed anything just before.
- Closing the app no longer logs an error from the tray icon (a colour change queued as it closed).

**3.6.0**
- **A test build for the force feedback report.** No code changes since 3.5.6. The shortcut code was checked against
  3.4: it registers the same keys and buttons and does the same things, and nothing in the app types keys except
  Button2Key. Button2Key switches on by itself when a game on one of its profiles starts (with game switching on),
  and its "Forza Horizon" profile sends keys (arrows, Enter…) from the wheel and the Gladiator: switch it off after
  Forza has started, or untick its game switching, to test without it.

**3.5.6**
- **SimEffects → Arduino works with SimHub's sketch.** An Arduino set up with SimHub's Arduino setup now works as it
  is, with nothing to upload: Daisy's App speaks SimHub's protocol, the board says how many RGB LEDs it has and
  whether it has an 8×8 matrix, and a Shift strip's new **First LED** says where the shift lights start on its chain.
  The app finds out which sketch is on a board (SimHub's or DaisysSim) and shows only the settings that sketch uses.
  Close SimHub (or switch its Arduino off) while Daisy's App drives the board: only one program can use a COM port.
- **Status lights:** each board has a green / amber / red / grey light in the Arduinos window's list, beside its
  status, and on the tab's card. **Test** is greyed out while the board isn't connected (before, it silently did
  nothing).
- **Arduinos window:** the Data Pin is a drop-down; **Remove** sits beside **Add Arduino**, the same size, and works
  only with a board picked.

**3.5.5**
- **AI: tab keys as in a browser.** **Ctrl+T** opens a new tab, **Ctrl+W** closes the tab showing, and
  **Ctrl+Shift+T** opens the tab closed last again (in a new tab, or in the tab showing if that's a new one not started
  yet). They work wherever the focus is while the AI tab shows, and so does **Ctrl+F** (search) now.
- **AI remote tab's Claude Account box** (it came in 3.5.2): it shows when the PC at the other end is on 3.5.2 or
  later and has Claude accounts. Update both PCs if one is older.

**3.5.4**
- **Remote API in one place:** [API.md](API.md) describes everything other devices say to Daisy's App: remote
  control (AI sessions, their Claude account, Project and Module Docs, usage), module channels (Tools → Notepad) and
  Button2Key Remote. The guides link there instead of each describing their part. A new remote feature is an op or a
  channel there, on the shared code.
- **AI remote control:** `{op: "usage"}` gives the PC's plan usage without opening a session (for Daisy's App
  Mobile's Usage page).
- **Button2Key Remote's Android app** can be built into another app: Daisy's App Mobile now includes it as its
  Butt2Key module, from the same code. Inside it, the app's own updates are left out. Daisy's App Mobile 0.2 is on
  this release (`DaisysApp-Android-0.2.apk`).

**3.5.3**
- **SimEffects → Arduino** (new): your Arduinos, each on its COM port (listed by Windows' name for it), set up in
  the **Arduinos** window: add as many as you have, and for each one what's wired to it (its features), with their
  pins and settings, **Test** and **Remove**. Every board runs the same sketch, **DaisysSim** (**Open Sketch Folder**;
  it needs the FastLED library): the app tells it what's on it when it connects. Built so new kinds of feature (motor
  controllers, fans…) can be added as plugins later.
- **SimEffects → Notify** (new; shown while an Arduino has its lights): an **8×8 RGB matrix** shows the gear (R, N,
  1–9), blue at the shift point and flashing over-rev; a row of **8 or 16 RGB LEDs** are shift lights (green, yellow,
  red, all blue at the shift point, flashing over-rev; from one end or both ends to the middle). Colours, shift points
  (% of the redline) and a **Demo**; live previews of every light. Each car's **redline is found by itself**, as
  SimHub does: iRacing's own shift lights per car, else the highest RPM the car has held at full throttle once it's
  near the game's maximum, else the game's maximum; or your own for a car.
- **SimEffects → Fanatec** (new): the same shift lights on a Fanatec wheel's rev LEDs (the ClubSport Steering Wheel
  RS's nine), and the gear on the rim's display if it has one.
- **SimEffects:** the Shaker, Notify and Fanatec can follow the same game on the same port at once (one reader for
  them all).

**3.5.2**
- **AI:** a session open from another PC has the **Claude Account** box in its dashboard again: it lists that PC's
  Claude accounts, the one the session runs on there chosen, and picking another switches that session there, as if
  you were on that PC (only that session moves). A switch made on that PC shows here too. Both PCs need 3.5.2. A
  session on another PC that hadn't started yet no longer shows this PC's new-tab panel when it opens.

**3.5.1**
- **AI:** a new tab's **Module Docs** are named by their folder (*Delay*, *Notepad*…; *AudioTools/Tests* where two
  share a name), beside Project Docs. **Recently Closed** is a list like Previous Sessions (click one for Open or
  Remove), on one row with **Previous Sessions** (or a remote PC's **Remote Sessions**), each three rows high.
- **SimEffects → Shaker:** Forza Horizon's port is 8000 by default (a Forza Horizon profile still on 5300 moves to
  8000; set the same in the game's Data Out). The meters **pop out** as a widget (the icon at their top right), like
  Hardware → Performance's: placed and sized by dragging, with opacity and On top, kept open at the next start.
- **Widgets:** every widget in the app (Performance's graphs, Zabbix's, ForzaWheel's input overlay, the Shaker's
  meters) is now one piece of code in Core, so they all move, stretch and save the same way.

**3.5.0**
- **Settings → Shortcuts** (new, after General): every shortcut in the app in one list, a card per module: click one
  to change it, **Reset** puts back the one it came with, **Delete** unbinds it. One that another program has says so
  in red; one used twice (by two modules, say) says what else it does (both act). Behind it, every module's
  shortcuts are now one piece of code in Core, so they all work the same way: a key combination or a controller /
  wheel / button box button everywhere (the **Resizer** gets controller buttons too), every shortcut paused while
  you pick one in any module, and the same small window for setting them. The Shaker's **Level Wizard** keys are
  listed too, and only taken from other programs while the wizard is open.
- **Confirm? everywhere:** every button for something that can't be undone works the same way now (one piece of code
  in Core): the first click turns it into a red **Confirm?**, a second click within 4 seconds does it. The questions
  that popped up before go: deleting a Button2Key profile or removing a controller from one, a Forza Analysis session,
  ForzaWheel's Clear all, iRacing's kept races, benchmarks, Recording's Reset to the defaults, and in AI removing an
  account, a PC or a provider and deleting a previous session. The ones that said **Click to confirm** or **Click
  again to delete** (Audio's Reset delays, Reset levels, Reset speaker positions and Remove EQ, Hardware's Delete the
  log, the Resizer's and Mini Mirror's Delete) say Confirm? like the rest. Warnings worth reading stay questions
  (ending a Windows process, Realtime priority, closing an AI tab that's working, Storage's Delete now…).

**3.4.0**
- **AI:** a new tab's files to read first are now two boxes of tick boxes: **Project Docs**, the working folder's
  `.md` files (as before), and beside it **Module Docs**, the `README.md` files in its subfolders, each by its title
  (*Audio: Delay*, *Tools → Notepad*…), so a session can read just the guide of the module it's about. Module Docs is
  left out when the folder has none. A session started on another PC lists that PC's (it needs 3.4.0 too).

**3.3.4**
- **SimEffects → Shaker:** every shortcut here (volumes, on / off, the Level Wizard) can be a key by itself (an F-key,
  a number pad key…), as well as a key combination or a controller button. While the Level Wizard is open, the tab
  follows it as it happens: the levels and settings it changes show on the tab's sliders, and the effect it's on is
  outlined and bold in the list, with its meter and its column in the meters brighter.

**3.3.3**
- **Tools → Notepad:** **Add Server…** at the bottom of the host box (while this PC is the host) adds another PC
  with its address, port and passphrase, and connects to it straight away (it's added to Settings → AI → Remote
  control's list too, which the notepad uses).

**3.3.2**
- **SimEffects → Shaker:** effects are worked out wheel by wheel, and each device has a **Position**: all wheels,
  front, rear, a side, or one corner, so it feels only its wheels (a kerb under the front left wheel shakes the front,
  not the rear; a crash shakes the side it came from). New **Split** channels play a position's left wheels on the
  left channel and its right wheels on the right. Front / rear, side to side and four-corner rigs can all be set up
  (examples in the guide).

**3.3.1**
- **SimEffects → Shaker:** a device's volume slider and mute are now the device's own volume in Windows, as on the
  Audio tab: moving it changes the device straight away, and a change made in Windows shows on the slider (before,
  the slider only scaled the app's own sound, so a device turned down to 0 in Windows stayed silent while the app
  showed 80%). The slider shows Windows' volume when the app starts.

**3.3.0**
- **SimEffects → Shaker** (was Bass Shaker; its settings are kept): **Level Wizard**, beside Add Effect. A window
  above the game that goes through the profile's effects one at a time, to set each while you drive by shortcuts
  (keys or controller buttons, set on its first step): Previous / Next step, Level up / down, and if you like
  Frequency, Sensitivity, Threshold and Length up / down. A setting without a shortcut says so and keeps its slider.
  **Solo / All** plays only that effect, or every effect as it was with your changes, to feel it in context; closing
  it puts the effects' mutes back as they were.

**3.2.5**
- **Gaming → Button2Key:** a game starting while it's off now switches it on with the game's profile working straight
  away, also when that profile was already the chosen one (before, it did nothing until the Button2Key tab was opened).

**3.2.4**
- **SimEffects → Bass Shaker:** each device's sound is filtered to its own Low–High range and kept in by a limiter
  instead of being clipped, and knocks that come close together no longer jump, so no higher tones (heard as a pitch
  on speakers) go out, only the low ones a shaker moves with. The log says, for each device, the sound format Windows
  asks for and any gap in it.

**3.2.3**
- **SimEffects → Bass Shaker:** each effect has a level per device under **Plays On** (each device shown by its name
  only), with **Test** and **Remove** at the bottom right. A controller-icon button beside ON / OFF sets a shortcut
  for switching it on and off; the "Off" text beside it is gone. The profile picker and **New** moved to the top of the
  Profile card. **Dayton Audio BST-2** added to the transducers.

**3.2.2**
- **AI:** the Claude Account box follows the session showing: picking an account there changes only that session, and
  on a session on another account its meters show that account's usage (read with `/usage` in its folder when it's
  picked or starts) while this PC's usage details are hidden. A **⟳** on the Usage button reads the usage again.

**3.2.1**
- **AI:** each Claude Code session picks its own **Claude Account** (a new tab's drop-down between Host and
  Permissions, Claude Code's own sign-in chosen to start with, Add an account… last), so sessions on different accounts
  run side by side; the dashboard's Claude Account box switches only its session. A session on another account runs
  in that account's own folder (sessions, history, settings and skills still shared), so Claude Code's own sign-in is
  never changed, and its plan meters are that account's. Add an account… no longer switches Claude Code's sign-in.
  A new tab shows no dashboard until it starts. With another PC under Host, the drop-down lists that PC's accounts and
  the session starts there on the one picked; its Remote Sessions show each one's account (both PCs need 3.2.1; the
  protocol, with the account fields, is in the AI guide's "For client makers").

**3.2.0**
- **SimEffects** (new tab): SimHub's features, as they're needed. First, **Bass Shaker**: the game's telemetry
  played to bass shakers on your audio devices. Add a device and pick its transducer (ButtKicker, Dayton Audio,
  AuraSound, Clark Synthesis, a subwoofer…), which sets the frequencies it's sent; Left / Right / LFE for shakers
  sharing an amplifier. Effects (Crashes, Impacts, Curbs, Road Feel, Off-Road, Gear Shift, Wheel Slip, ABS, Rev
  Limiter, Engine) each with a volume, mute and settings, folded or open, dragged to reorder, a meter column each
  above them. A profile per game, with its own UDP port: Forza Horizon (5300), Forza Motorsport (5301), iRacing and
  ACC (shared memory, no port), picked by the game running. Master volume top right; every volume has shortcuts
  (keys or controller buttons) for up, down and mute.

**3.1.8**
- **Network → Zabbix:** the host groups share one grid again: each group is as wide as its hosts, so small groups sit
  side by side, and one with more hosts than a row has left fills the row and carries on in the next. **Alerts** stays
  first; drag a group by its name to move it. **Display Grid Size** (Settings → Network → Zabbix, beside Server):
  columns and rows (0 = automatic).
- **Tools → File Sync:** problems (in Recent Syncs, the sync's window and its card in the background) can be selected
  and copied, every one is listed, and each is written to the log as an error (as are folders Compare can't read).
  Recent Syncs lists a sync's problems under it. Comparing counts the folders and the files found on each side.
  "What a sync would do" is now **Sync Details**; "Recent syncs" is **Recent Syncs**.
- **Gaming → ForzaWheel:** the "Using it with Forza" block is gone (the steps are in its guide).

**3.1.7**
- **Tools → Terminal** (new): CMD, PowerShell and WSL sessions in tabs, each the real shell in Windows Terminal's
  own control, with Windows' console colours and font. **+** opens the default kind (Settings → Tools → Terminal),
  the arrow beside it any kind; right-click a tab to rename it; Ctrl+C / Ctrl+V or right-click copy and paste, as in
  the console. The tabs come back at the next start.

**3.1.6**
- **Gaming → Button2Key:** a watched game closing now switches Button2Key off however it was switched on (by hand,
  the shortcut, or on already when the app started), not only when the game had switched it on.

**3.1.5**
- **The tray icon** keeps its white flower and changes its background instead: red while a red bubble shows in the
  title bar, else green while a green one does, else the usual blue.
- **Tools → Notepad:** **Ctrl+Shift+Z** redoes what Ctrl+Z undid, as many steps back as were undone (as Ctrl+Y);
  typing something new ends it.

**3.1.4**
- **Tools → Notepad:** right-clicking a tab edits its name in place, as the app's own tabs do (Enter keeps it, Esc
  or clicking away doesn't); no window opens.

**3.1.3**
- **The tray icon** turns its flower green while a green bubble shows in the title bar (Button2Key, ForzaWheel,
  Button2Key Remote, Webcam), and white again when none does.
- **Tools → Notepad:** tabs are named **Notes 1**, **Notes 2**… rather than after their first line (a file's tab keeps
  the file's name). Right-click a tab → **Rename**: it's renamed on every PC.

**3.1.2**
- **Gaming → Button2Key:** a game starting now switches Button2Key **on** (with that game's profile) when it was off,
  and the game closing switches it off again, but only if the game switched it on: on or off by hand stays as set. Any
  profile can be picked by hand at any time. **Default** is an ordinary profile now: rename or delete it like any other.
- **Button2Key → on / off shortcut:** the controller button beside **On** (as in ForzaWheel) sets keys or a
  controller / button box button that switches Button2Key on and off from anywhere.

**3.1.1**
- **AI → Context** (above Pause, while it works): a small box to tell it more about what it's doing; it's read after
  the step it's on, and it carries on with it, without stopping.
- **Gaming → iRacing → Export** (between Sync and Import): the page as a web page to share, as it looks, with every
  table. **Import…** and **Settings…** are now **Import** and **Settings**.
- **Network → Zabbix:** hosts are boxed by host group, each headed by coloured counts of its hosts' statuses;
  **Alerts** comes first, with every host that has a problem or can't be reached, worst first; a host's page has
  **Hide Device** at the top right.

**3.1.0**
- **AI → a new tab:** **Host** (was Which AI?) lists **Local** and **PC (Remote)**; **Working Directory** (with its
  files to read first) comes right under the name; **Recently Closed** is a drop-down, beside **Previous Sessions**
  (with opening by id and Import under it); on another PC, **Remote Sessions** and **Remote Session Details**. Fewer
  explanations. The panel scrolls, so the box stays on screen on a short window.
- **AI → times** are whole seconds up to 60, then minutes (`5:05` rather than `305 s`), with no "s".
- **AI → the dashboard:** the status card's word (*Done*, *Ready*) sits level with the orb; Session has no **Allow
  Everything** (it's in Settings → AI; the Permissions box's *Don't ask* is the session's own) or **Process**; Usage's
  API price line is off unless **Show the API price in Usage** is on (Settings → AI). The chat box has no tooltip.

**3.0.6**
- **Tools → Notepad: tabs.** **+** (or Ctrl+T) adds a tab and **✕** closes one, on every PC: all the tabs are shared
  between your PCs like the text, and any PC can add or close them (3.0.5's note is the first tab).
- **Tools → Notepad: `---`** on a line of its own is drawn as a line across the page.
- **Tools → Notepad as Windows' app for text files:** Settings → Tools → Notepad, **Make default…** for the types
  ticked (`.txt`, `.log`, `.md`, `.ini`… ), finished in Windows' Default apps (Windows lets no program set it by
  itself); **Restore** takes it off them and helps put back the app each type had before. A file opened with it opens
  in a new tab, popped out; **Save** (or Ctrl+S) writes it back on that PC.

**3.0.5**
- **A notepad button** at the top right, after Update: it pops out Tools → Notepad, or brings its window to the front
  if it's out already. **Update** is now just its icon.

**3.0.4**
- **Video → Resizer:** the "Ways to apply a profile" card is gone (the pipe commands still work; they're in its guide).

**3.0.3**
- **Button2Key Remote for Android 1.1.4** (`Button2Key-Remote-1.1.4.apk` on this release; it updates itself from 1.1.0
  on): full screen's **✕** fades away a few seconds after you touch the screen; press and hold anywhere for 3 seconds
  and it fades back in for 5 seconds. A new PC's passphrase is hidden as it's typed (it showed until **Show the
  passphrase** was ticked and unticked).

**3.0.2**
- **AI → Activity:** the times by kind are in two columns (three each).
- **Tools → Notepad:** Copy and Cut copy plain text only, so pasting it elsewhere (the AI's box, say) brings no
  formatting or background.

**3.0.1**
- **Tools → Notepad:** no colours any more (other PCs' text is plain, and the Colours checkbox is gone; the status
  lists the PCs connected). **Pop out** (top right) puts it in a window of its own, in Alt+Tab; it opens popped out
  again at the next start if it was out when the app closed.
- **Fewer explanations:** Video → Resizer (the process watcher's note and the two lines about ways to apply a profile)
  and Video → Recording (the benchmark's and the encoder's lines; the encoder's still shows when there's none).

**3.0.0**
- **Tools → Notepad** (new): one note shared between your PCs. One Daisy's App hosts it (**This PC**) and the others
  pick it as their host; what's typed on any shows on all of them at once, and another PC's text is in its colour
  (**Colours** turns that off). Bold, Italic and Underline, the font and its size, Word wrap. It uses AI → Remote
  control's connection (its port, passphrase and list of PCs), so it needs no port of its own; typing while the host
  is away is kept and sent when it's back. See [its guide](src/Modules/Tools/Notepad/README.md).
- **Fewer explanations:** Hardware → Performance (under Processes, and Widgets' "None yet"), Video → Mini Mirror (the
  empty list's text, and the steps on the right: its header is now **Current Mini Mirrors**), Button2Key (the line
  under the on / off switch, a profile's empty games list, the games note; "No controllers detected"), ForzaWheel
  (Hush, Nothing muted, Start with a game, force feedback, the input overlay, the bindings' file), and Settings → AI's
  first six cards.
- **Settings pages:** Audio two cards by two (Signal options removed), Video → Mini Mirror two by two, Overlay with the
  overlay and the shortcuts side by side and Games under them, Network → Zabbix with Use Zabbix and Hosts side by side
  and the server under them.

**2.9.0**
- **Audio → Volume: Bluetooth.** A card above Apps shows the Bluetooth devices you pick with its gear (it lists every
  paired device), blue when connected and white when not, each with **Connect** / **Disconnect**.
- **Button2Key:** pick any profile at any time, on or off. A game now switches to its profile once, when it's first
  seen running (again only after it closes, or Button2Key is switched off and on); a profile picked by hand stays, and
  nothing switches to Default any more, at start or when a game closes. The option is now "Switch to a game's profile
  when the game starts".
- **AI: everything I've said.** A button in the search bar opens a window with this session's messages at the top and
  the whole project's at the bottom, with message and word counts, and **Ctrl+F**: every match marked and counted,
  next / previous, Match case, Whole word.
- **AI: a sound** when Claude asks you a question (or asks to go on); Settings → AI turns it off.
- **AI's Session box:** Export, ⧉ Sess ID and a red ✕ Process. **Activity**'s times are a line each; "Waiting for
  Claude" is Waiting, "The web" is Web Access, and Other Tools.

**2.8.2**
- **Button2Key Remote's tab:** the top card is **Status**, with the **ON / OFF** button before it and a joystick
  button on the right that opens a small window for the on / off shortcut (keys, or a controller or button box
  button); **Game Switching** (was Auto Launch: a game's BigButt while it runs) and **Titlebar Status** share a line.
  **From a template…** is now **Butt Wizard** (beside New ButtScreen only; it can add the game to the BigButt in use),
  **Link a ButtScreen…** is **Link2Butt**, Devices is **Remote Devices** with **Save** beside the passphrase, and the
  "A device shows its … ButtScreen" line is gone.
- Button2Key Remote's sizes: the **Dragon Touch NotePad X10** (800 × 1280) and the older **Dragon Touch X10** (10.6",
  768 × 1366).

**2.8.1**
- **Button2Key Remote: the template wizard** is now **ButtScreen Template**, a step at a time: the device first (so the
  layout suits it, held portrait or landscape), the template, its buttons as tiles to tick (in groups that open and
  close), then the theme with a picture of the ButtScreens as they'll be, and **Create**.
- **Button2Key Remote's editor**: **Show Text** under a button's label (off: only its icon shows); Colours is now
  **Style**, with **Titles**: **Show** or **Hide** every button's text at once, whatever each one says (green while it
  does). The themes have plain names: Slate, Coral, Magenta, Night Sky, Bronze, Racing Orange, Royal Purple, Steel
  Blue, Teal and Neon. Size lists the iPhones (a line per screen size), and a connected Pixel 10 Pro shows once, as
  Google Pixel 10 Pro. Some explanations are gone from the editor.

**2.8.0**
- **Button2Key Remote: templates.** **From a template…** (beside New ButtScreen, and under BigButts for a whole new
  BigButt with the game as its game) makes ButtScreens with a game's buttons already on them, each with its label, icon
  and keys: Forza Horizon 5, Forza Horizon 6, iRacing, Star Citizen, Elite Dangerous and Assetto Corsa Competizione
  are built in. A short wizard picks the template, then the groups and buttons you want (ticks, each group opening and
  closing); groups that fit share a ButtScreen, the rest go on the next, and you change them as you like after.
  Your own templates are JSON files: drop one in the templates folder or **Import…** it in Settings → Gaming →
  Button2Key Remote (where **Make it yours** copies a built-in one to change).
- Button2Key Remote: the "None in … yet" line under an empty BigButt's ButtScreens is gone.

**2.7.8**
- **Button2Key Remote: less text in the tab.** The checkboxes are now **Auto Launch** (a game's BigButt while it runs),
  **Titlebar Status** (the title bar bubble) and **Auto Enable B2KR** (switching it on with a game); pointing at one
  says what it does. The lines under the on / off switch, the BigButt in use and the games, and the ButtScreens'
  description, are gone.
- **Button2Key Remote for Android 1.1.3** (`Button2Key-Remote-1.1.3.apk` on this release; it updates itself from 1.1.0
  on): Find PCs on the network is now **Network Scan**.

**2.7.7**
- **Button2Key Remote: no BigButt is special any more.** Default was: it couldn't have games, or be renamed or
  deleted. Now any BigButt can, and when a game's BigButt closes, the one you picked before comes back. With no
  BigButts at all, devices are told there are none.
- **Button2Key Remote: clicking a ButtScreen's picture opens it in the editor** (a double-click before).
- **Button2Key Remote's editor: a Theme in Colours** colours the whole ButtScreen at once: Daisy's Derrière, Cheeky
  Peach, Bubble Butt, Full Moon, Junk in the Trunk, Badonkadonk, Gluteus Maximus, Plumber's Crack, Tushie Teal and Booty
  Call.
- **Button2Key Remote for Android 1.1.2** (`Button2Key-Remote-1.1.2.apk` on this release; 1.1.0 and later update
  themselves): the BigButts screen after connecting no longer turns the screen to landscape; it follows how the device
  is held until a ButtScreen shows.

**2.7.6**
- **Button2Key Remote: each BigButt has its own ButtScreens**, as a profile has its inputs: the right side shows the
  ButtScreens of the BigButt in use, and picking another shows its own. New ButtScreen (and Copy) put the new one in
  the BigButt in use. **Link a ButtScreen…** adds one of another BigButt's: the same ButtScreen, so a change to it shows
  in every BigButt it's in; a linked one has **Remove** (off this BigButt only) in place of Delete.
- **Button2Key Remote: Rename and Delete are beside New and Copy** under the BigButts list (for the one picked), and the
  note about Default is gone.
- **Button2Key Remote's editor: turning a ButtScreen between portrait and landscape swaps its columns and rows**, and
  turns its buttons with them.

**2.7.5**
- **Button2Key Remote: the BigButts come first**, above the BigButt in use (headed by its name), and **each BigButt's
  games are in the BigButts card**: pick one in the list to see and change its games.
- **Button2Key Remote switches itself on with a game**: while a BigButt's game is running it's switched on (if it was
  off), and off again when the game closes, if the game switched it on, as ForzaWheel's Start with a game. **Switch
  Button2Key Remote on while one of these games is running** (in BigButts) turns that off.
- **Button2Key Remote for Android 1.1.1** (`Button2Key-Remote-1.1.1.apk` on this release; 1.1.0 updates itself to it):
  right after connecting it shows the **BigButts** screen, every BigButt with its ButtScreens and the one in use
  marked, to pick the one to use now; **BigButts** at the top opens it any time. (Settings → Show the BigButts to pick
  from after connecting leaves it out.)

**2.7.4**
- **Button2Key Remote: the editor has Save & Close** at the top right (the line of tips is gone).
- **Button2Key Remote: every BigButt has Rename and Delete on its own row**, not only the one in use.
- **Button2Key Remote: Devices has a Save button** for the port and passphrase, and below it just **Active Devices**, a
  line each (name, Android version, address).
- **Button2Key Remote for Android 1.1.0** (`Button2Key-Remote-1.1.0.apk` on this release): the BigButts as a row of
  chips while connected, the one in use lit, a tap picks another; full screen keeps a border round the buttons, clear
  of the screen's rounded corners and camera (and a margin you choose); and it **updates itself**: Check for updates /
  Update on its main screen, and a check when it starts. (1.0.0 has to be updated by hand this once.)

**2.7.3**
- **Button2Key Remote for Android**, the first client app: your ButtScreens on a phone or tablet. Find PCs on the
  network or add one by **Name, IP Address, Port and Passphrase** (with **Test**); connected, it shows the PC's BigButt
  in use and follows it, swipes through its ButtScreens, presses with several fingers at once, picks another BigButt,
  and has **full screen** with a small Exit button in the corner you choose (Settings). It reconnects by itself. The APK
  is attached to this release (`Button2Key-Remote-1.0.0.apk`); see [its guide](clients/android/README.md).
- **Button2Key Remote: a Google Pixel 10 Pro size** in the editor's Size list (1280 × 2856), beside the phone and
  tablet shapes. (A device that connects is listed by itself too, with its own size.)

**2.7.2**
- **A blank white window fixes itself**: when the graphics card stops drawing the app's window (it can happen to every
  WPF app after Windows has run for a long time, or after a driver hiccup), the app notices within seconds and starts
  again drawn without the graphics card, saying so and how to fix the card (Win+Ctrl+Shift+B resets the graphics driver,
  or restart Windows). **Settings → General → Appearance → Draw without the graphics card** keeps it that way, and
  `--software` does it for one start.

**2.7.1**
- **Button2Key Remote: off to start with, and its on / off switch is the only one**: on, devices can connect and
  buttons do what they're set to; off, neither (Let devices connect is gone). A **shortcut** beside it (keys, or a
  controller / button box button) switches it on and off from anywhere, and a green **Button2Key Remote** bubble shows
  in the title bar while it's on.
- **Button2Key Remote: BigButts work as Button2Key's profiles** and the tab is laid out like Button2Key's: the BigButt
  in use with its ButtScreens (numbered, in the order a device goes through them) and its games, then the list of
  BigButts (New, Copy, Rename, Delete). Devices are told the BigButt in use with how many ButtScreens it has and which,
  in order, so they can move from one to the next.
- **Deleting a ButtScreen or a BigButt**: click **Delete**, then **Confirm?** (no question window). Every button that
  needs confirming works this way from now on (the README says so).

**2.7.0**
- **Gaming: Button2Key Remote, a new module** (a sub-tab of Gaming). **ButtScreens** are screens of buttons for a phone,
  tablet or PC, laid out on a grid: pressing a button does what a Button2Key input does (keys held, tapped, repeated or
  toggled, a long press, a macro, mouse movement, a program), or shows another ButtScreen. **BigButts** group
  ButtScreens (usually one per game) and the one in use follows the game running, as Button2Key's profiles do.
  - **The editor**: the ButtScreen's size (a device that has connected, common phone and tablet shapes, or pixels
    typed in; portrait or landscape), its grid and spacing, background and icon colours. Double-click a cell to add a
    button, drag it to move it, drag its corner to make it cover more cells (1×2, 2×2…); each button's label, icon,
    rounded or square corners, background, outline, font, size, bold and text colour, and what it does (Button2Key's
    own window). Saved as it changes, with Undo / Redo.
  - **Over 7,000 icons** (Material Design Icons) in one style, drawn in the ButtScreen's icon colour, with search; or
    a picture of your own on a button.
  - **Pop out** a ButtScreen to use it with the mouse or a touchscreen: it never takes the keyboard from the game, and
    can be made see-through, kept on top and locked in place; open ones come back at the next start.
  - **Devices connect** over the network with a passphrase (encrypted), find the PC by themselves, and get the
    ButtScreens, BigButts and icons, and again as they change. The Android app is next; the protocol is in the
    module's guide, so any client can be written.
- **Button2Key**: what an input does as it's pressed, held and let go is now its own part (`ActionRunner`), shared
  with Button2Key Remote; nothing changes in how it works.

**2.6.2**
- **Video - Mini Mirror: mirrors no longer show black on SDR monitors with Windows' colour management or Auto HDR.**
  Windows runs such a monitor in a 16-bit mode, and Mini Mirror took that to mean HDR pictures, while the capture still
  sent ordinary 8-bit ones: the HDR conversion then turned every picture black. Whether a picture is HDR now comes from
  the picture itself.
- **Video - Recording: the same fix** for recordings of such a monitor (it recorded through the HDR conversion too).
- **Video - Mini Mirror: more in the log**: the format pictures come in, the SDR white level when converting HDR, the
  brightness of each capture's first pictures, and 15 s after a mirror opens, the brightness of its picture and of each
  monitor's, so a black mirror shows whether the picture was black before it was drawn.

**2.6.1**
- **Video - Mini Mirror: a mirror stuck on the picture it started with comes to life**, on PCs where Windows' desktop
  duplication sends one picture and then nothing more. While a capture is quiet, Mini Mirror peeks at the monitor
  through GDI once a second: if the screen is changing but the capture sent nothing, it's set up again, and if that
  keeps happening the monitor is captured another way: an HDR monitor with Windows' 8-bit picture (as the SimHub plugin
  does), then, if that gets stuck too, through GDI (slower, at most 60 frames a second) until the app restarts. A
  really still screen is left alone.
- **Video - Mini Mirror: the Video log describes the PC** the first time it captures (Windows build, each GPU with its
  driver version, each monitor with its refresh rate and HDR, GPU scheduling, multiplane overlay and Windows' DirectX
  game settings), the first pictures each capture gets, what's in front when one gets stuck, and, 15 seconds after a
  mirror opens, how many pictures it drew and how many the capture brought, all with Normal logging.

**2.6.0**
- **AI: the plan's usage line at the top of the tab is gone**: it said again what the dashboard's Usage card shows
  (this 5 hours and this week, each with its countdown). **Usage** in the Claude Account box still opens the details
  there.
- **AI: a session on another PC shows that PC's plan usage**, not this one's: its dashboard's plan meters are the
  server's (with its account, and when its limit is reached), sent when the session opens and again as they change.
  The Claude Account box and the usage details (this PC's) don't show on its tab. Both PCs need 2.6.0 for the meters;
  with an older server they're left out, as before.
- **AI: the Claude Account box** lists the accounts by their names (the email in the tooltip), with **Usage** beside
  the list; the "Signed in as" line shows only when there's a problem.
- **AI: Add a PC / Edit…** (Settings → AI → Remote control): its boxes are **Name**, **IP Address**, **Port** and
  **Passphrase**, labels right against them, and **Test** is beside **Save**.

**2.5.1**
- **AI: the Claude Code box is now the Claude Account box**, and simpler: picking another account in its list switches
  to it at once (no Switch account button or question), **Usage details** is **Usage**, and Sign out, Add an account…
  and Check again are gone (accounts are added in Settings → AI → Accounts; after Sign in, or when the tab shows
  again with a problem, it checks by itself).

**2.5.0**
- **AI: more than one Claude account, and Switch account.** The Claude Code box (bottom right of the AI tab) has a
  list of your Claude accounts with the one in use, and **Switch account**, which asks which one Claude Code should
  use. Sessions here start on it with their next message (one that's working finishes its turn first), one waiting
  for a limit carries on at once, and the plan's usage is read again for it.
- **Settings → AI → Accounts:** the accounts kept here (each with a name of your own, its email and plan; ✕ to
  remove) and **Add an account…**, which asks what kind of account (Claude for now, ready for more), then signs in to
  another Claude account (a subscription or the Anthropic Console) and adds it. The account Claude Code is signed in
  to is added by itself. Each one's sign-in is kept encrypted for your Windows user; sessions, settings and history
  are shared by all of them.

**2.4.0**
- **AI → Remote control: more than one other PC.** Settings → AI → Remote control keeps a list of PCs to use the
  sessions of (**Add a PC…**, **Edit…**, ✕ to remove), each with a name of its own here, its address, port and
  passphrase, and **Test**. A new tab's **Which AI?** has a line for each, so you pick which PC to open a session on
  or start one on. The PC set up before carries on as the first in the list, and its tabs with it.

**2.3.1**
- **Gaming → ForzaWheel comes back as it was left.** Running when the app closed (for an update, say) starts it
  again at the next launch, and stopped stays stopped. (Button2Key's on / off already did: it's kept as it's switched.)
- **ForzaWheel: Start with a game.** A new block under Hush: a list of programs (typed, or picked with **Add a running
  program…**), and while one of them runs the emulator runs too: it starts when the game starts and stops when it
  closes, unless you started it yourself.

**2.3.0**
- **Video → Recording: a green Webcam bubble in the title bar while the virtual webcam runs** (red, *Webcam muted*,
  while it's muted; *Webcam: screen only* with the cameras off). The shortcuts window has a setting to leave it out.
- **The virtual webcam's shortcuts in one window** (the keyboard button beside Mute virtual webcam): start / stop the
  webcam, mute / unmute it, and **cameras off / on**, each a key combination or a controller / button box button
  (none to start for the first and last). They're in Settings → Video → Overlay too, and the tray menu.
- **Cameras off**: a button on the Record card (and a tray item and a shortcut) takes every camera off the picture,
  so the virtual webcam (and a recording) carries on with just the screen; press again to put them back. Cameras
  hidden by their own shortcut stay hidden; all are back on when the app restarts.

**2.2.1**
- **Video → Overlay: Medium's numbers sit right against their graphs.** The graphs stay in line with each other
  against the outer edge, and each row's numbers now end where its graph begins, rather than at the other edge.

**2.2.0**
- **Gaming → Joy2Key is now Button2Key**: its sub-tab, its Settings → General → Modules entry, its title bar bubble,
  tray menu and messages. Its profiles and settings carry on as they were (they keep their Joy2Key file names).
- **Button2Key: a profile's games are a list.** One profile can work with several games: each game is a line of its
  own with **×** to take it off, and a game is added by typing its program name and **Add** (or Enter), or with **Add a
  running program…**. It used to be one box of names separated by commas.

**2.1.4**
- **Video → Overlay: the Medium overlay lines up.** The numbers sit against one edge and the graphs against the
  other, so every row is in line on both sides however long its numbers are (the video memory's pushed its graph
  over). Video memory's and RAM's totals are in whole GB: 11.5 / 16 GB rather than 11.5 / 15.9 GB.

**2.1.3**
- **Video → Mini Mirror: a mirror that froze on its first picture comes back to life.** On some PCs Windows' screen
  capture can go quiet for good after its first frame. When no new frame has come for a couple of seconds, the capture
  is now set up again (waiting longer each time while the screen really is still), and the log says so. With
  Settings → General → Logging on Debug, the Video log also counts each minute's frames per monitor and per mirror.

**2.1.2**
- **AI: text pasted from Notepad is pasted as text.** Notepad copies rich text in its monospaced font, which the box
  took for code: a code block, in black on the dark code background. Now it's read as writing (and a code block
  pasted in has the box's colours).
- **AI: Claude's replies show when they were written**, in small type at their bottom right (with the date if it
  wasn't today).
- **Settings → AI: the cards in two columns**, three rows, with the preview under them the whole width.

**2.1.1**
- **Hardware → Performance: the Network column fills in.** It showed – everywhere until you signed out and back in
  after **Allow…**. Now **Allow…** (one administrator prompt) gives your account the rights to Windows' network trace
  directly, and the column works straight away, with no sign-out.

**2.1.0**
- **Hardware → Performance: a Network column in the process list**, like Task Manager's: each program's share of all
  the PC's network traffic, in %, with its Mbit/s when you point at it (a program's processes added up on its row).
  It reads Windows' network event trace, which needs the *Performance Log Users* group (as the Video tab's frame
  counter): **Allow…** under the list adds you once (an administrator prompt), and it works from your next sign-in.

**2.0.2**
- **AI: the plan bubble in the title bar** ("Claude 94%", or red at the limit) only shows while a Claude Code session
  is working; when none is, it goes.

**2.0.1**
- **AI: the session notes are one line.** The notes' turn after each request (CLAUDE.md, README.md) shows as
  *📝 Session notes brought up to date: CLAUDE.md, README.md ▸*; click it for what it did. It goes above the reply
  it follows, so a long reply (after a release, say) stays at the bottom instead of scrolling away.
- **AI: your requests are kept word for word** in `REQUESTS.md` in the session's folder, as part of the session
  notes, to search later.
- **AI: Allow Everything** in the dashboard turns red while it's on, in place of the red line under it.

**2.0.0**
- **Settings → General → Modules** (was Applets; the app says *module* throughout now): each module can be switched
  on or off **with its sub-modules**: Hardware's Performance, USB Monitor, Devices and Sensors; Audio's Volume, Levels
  and Delay; Video's Resizer, Mini Mirror, Overlay and Recording; Gaming's Joy2Key, ForzaWheel, Forza Analysis and
  iRacing; Network's Zabbix and Logs; Tools' File Sync, Disk Usage and Renamer. A switched-off sub-module isn't loaded
  at all (no sub-tab, no settings, nothing running). Switching off a module's last sub-module switches the module
  off; Recording needs the Overlay. Applies at the next start (**Restart now**).
- **Settings → General is laid out in rows:** **Startup and System Tray** beside **Updates** at the top, then
  Modules, then **Appearance**, **Notifications** and **Language** side by side. **Tab names** is gone: right-click a
  tab to rename it.
- **AI: a card's title is one line again.** A command without a description showed all its lines in the card's
  title, so the cards looked open; now its first line shows (with …), and the whole command is in the details.
- **For working on the code:** a map of every module and sub-module to its folder and guide (`CLAUDE.md`), and file
  lists in the Forza Analysis, File Sync and Renamer guides.

**1.11.10**
- **AI: Claude's questions show up.** When Claude asks you something (its multiple-choice questions), its card shows
  the questions with their options, a box for your own answer, and **Send** / **Skip**. Before, with **Allow
  everything** on, the question was answered for you, empty, and Claude said the question didn't come through.
- **AI: a notification when a session needs you.** A question, or a permission request, while you aren't looking at
  that tab (another tab, another program in front, or the window in the tray) shows a Windows notification with the
  tab's name; clicking it opens the window.

**1.11.9**
- **AI: Hide details at the bottom of the usage details**, so they close from where you finish reading them (as
  well as with **Hide usage** in the Claude Code box).

**1.11.8**
- **The app notes when its window stops responding.** If the window doesn't answer for 5 seconds, the log says so (and
  when it answers again); after 10 seconds it saves a dump of the app in the logs folder (`DaisysApp-hang-….dmp`,
  the newest two kept), which shows what it was busy with. For the white window some PCs got after the app had run a
  long time.
- **Hardware → Performance: the process list takes less of the app's time** on a PC with a program that reads
  windows aloud or controls them (UI Automation: screen readers, Voice Access and others): the list no longer
  describes every row and cell to it each second. That was half of what the window did while the list showed.
- **AI: remote control sends less.** A session's reply, sent on to the other PC as it's written, goes out in batches
  (a few a second) rather than as two small packets for every few words. The AI log notes how much each connection
  sent and received.

**1.11.7**
- **AI: the top of the tab takes far less room.** The Claude Code buttons (Sign in / out, How to install, Find
  claude.exe…, Check again) and **Usage details** are in a new **Claude Code** box at the bottom of the right column,
  with who's signed in. A problem with Claude Code shows as one line at the top that **✕** clears. The plan's line
  only shows when it has meters. Search stays beside the tabs.

**1.11.6**
- **AI: browse the other PC's folders** when starting a session there (remote control): **Browse…** beside *The
  folder there* shows its drives and your folders there, goes in and up, and **New folder…** makes one there. (Both
  PCs need this version.)

**1.11.5**
- **AI: the command cards start closed.** A command (the orange cards) shows only its title, what it's for, with ▸;
  click it to see the command line and what came back, and again to close it. Every card has the ▸ / ▾. A command
  it asks permission to run opens, so you can see it; a failed one stays closed, with its ✗.

**1.11.4**
- **AI: Pause, Stop and Send are stacked** at the right of the box (Send at the bottom), not side by side.

**1.11.3**
- **AI: closing a remote tab leaves the session running.** Closing the tab of a session on another PC only
  disconnects: it doesn't ask to stop it, and it carries on there. It's listed under **Recently closed** on a new tab
  (*on PC name*): **Open** connects to it again, if it's still open there, with its conversation and any question it's
  asking. (Or open it from **Its sessions**, as before.)

**1.11.2**
- **AI: reopen a closed tab.** A new tab lists **Recently closed** under its first line: the tabs you've closed, the
  newest at the top, from any folder. Click one and **Open**: the tab becomes it again, as it was (name, AI, folder,
  model, permissions, totals and the conversation), ready to carry on where you left off. **Remove** takes one off
  the list.

**1.11.1**
- **AI: carrying on after a limit works.** Claude Code reports your plan's limit as a message of its own
  (*You've hit your session limit · resets 9:20am (America/New_York)*), not an error, so it wasn't noticed. Now it is:
  the tab waits, and at the reset time it gave (a minute after, in this PC's time) it sends the carry-on message.
  Also when Claude Code ends at the limit.
- **AI: Pause.** Beside Stop while it works: it finishes the step it's on, then stops there; **Resume** tells it to
  carry on.
- **AI: a queue.** What you send while it's busy waits above the box and goes when it's done, one at a time (Send
  now or ✕ for each; Ctrl+Enter sends at once, as before).
- **AI: Permissions when a tab starts**, on the line with Name and Which AI?.
- **AI: how long each kind of step took**, under the activity ribbon (thinking, writing, reading, editing,
  running…).

**1.11.0**
- **AI: the box you write in is rich text.** The text starts at its top left (not the middle). Formatting pasted in
  is kept (Word, Outlook, web pages, editors, the conversation): headings, bold, italic, code, code blocks, lists,
  links and tables, and Ctrl+B / Ctrl+I as you type; it's sent as Markdown. **Images** can be pasted in (screenshots,
  copied images, image files) or dropped on it: they go with the message, and show in your bubble.
- **AI: Claude's text stands out.** It's at the left edge with an accent bar and a tint behind it; the tool cards and
  notes are indented under it (the thinking chips stay at the left). Your bubbles show their formatting.
- **AI: a new tab's start.** A **Name** box (Session 1, Session 2…) on the line with **Which AI?**; **Read first**,
  the folder's `.md` files to tick, sent ahead of the first message so the AI reads them before anything else; and
  **Previous Sessions** (the folder's, for Claude Code or a provider), three showing and scrolling for more: click one
  to **Open** or **Delete…** it.
- **AI: remote control (new).** Use the sessions on one PC from another: Settings → AI → Remote control, on both (one
  the server, with a port; the other the client, with that PC's address), with the same passphrase; everything between
  them is encrypted with it (AES-GCM). Then **Which AI? → Another PC** in a new tab: open a session running there, or
  start a new one there (which AI, the folder, read first). What it does shows on both PCs, and either can write to
  it, answer its questions or stop it.

**1.10.1**
- **Gaming → iRacing: import your history from the browser.** No OAuth client needed: Settings → Gaming → iRacing
  has the export script (**Copy the export script**) and its steps (paste it into the console on members-ng.iracing.com,
  signed in); **Import…** (on the tab or in Settings) reads what it downloads, `iracing_raw.json` (or `races_all.csv`
  with its history files). Every session comes in, practice and qualifying too, with iRating and safety rating before
  and after each race. Importing again updates what's kept; a sync carries on from it.
- **iRacing: more to look at.**
  - Filters for one **Track** (its retired versions too) or one **Car**, and for the kind of **Sessions** (races,
    qualifying, practice, every session).
  - **Time on track**: every session's laps (race, qualifying, practice), hours, tracks and cars, and laps at each
    track and in each car, stacked by kind.
  - The rating graphs are steps (a rating holds until it changes), start at your first race, follow the time chosen,
    and the safety rating's scale is in classes (R, D, C, B, A), read as *A 3.10*.
  - Syncing fetches every session too, not only races; hosted sessions get their category.

**1.10.0**
- **Gaming → iRacing (new module).** Every race of yours from iRacing's Data API, kept on this PC and summed up.
  - **Settings → Gaming → iRacing:** your iRacing email and password and your own OAuth client's ID and secret
    (iRacing's *password limited* sign-in; the secrets sent masked as iRacing asks and kept encrypted), with **Test
    sign-in**; hosted and league races too; syncing when the tab opens.
  - **Sync** fetches everything the first time (every race since you joined) and only what's new after that, waiting
    when iRacing's rate limit runs out; **Stop** keeps what was fetched.
  - **The tab:** your licences; filters (category, time, official only); tiles (races, wins, podiums, top 5s, poles,
    laps, laps led, km, hours, average start and finish, places gained, incidents, clean races, tracks, cars, series,
    iRating won, SoF…); a summary in words; graphs (races by category, how races ended, iRating and safety rating
    over time, races a month, a year of race days, when in the week you race, where you start and finish, places
    gained, incidents, most laps by track, most races by car and series, wins and podiums by track, your best tracks,
    iRating by car); records; more numbers (streaks, poles converted, median finish…); and tables of tracks, layouts,
    cars, classes, series, seasons, years, every race (with Find) and iRacing's own totals.
  - Settings → General → **Storage** lists its races (*iRacing races*).

**1.9.2**
- **AI: session notes.** Settings → AI → **Keep session notes**: when a request is done, the AI brings `CLAUDE.md`
  (and `README.md`, if it's out of date) in the session's folder up to date: what was asked, what it did, where things
  stand and what's next. So there's a record to pick the work up from if a session ends; Claude Code reads it when it
  starts, and the other providers are told to. A new session's folder, the first time, is `Documents\AI`.
- **AI: the status box keeps still.** The dashboard's "Working" / "Thinking" box keeps its height: what it's doing
  shows in two lines at most (the rest in its tooltip).

**1.9.1**
- **AI: search the conversations.** **🔍 Search** beside the session tabs (or **Ctrl+F**) finds words in what you
  said, the replies, or both, in this session or in every session on this PC (Claude Code's and the other providers'),
  older messages too. Click a result to go to it in its tab (opened if it isn't).
- **AI: allow everything.** Settings → AI's never-ask switch is now **Allow everything**, without a confirmation, and
  it can be switched on right where you're asked (**Always allow everything**, beside Allow all this session) or from
  a session's dashboard. Once it's on, nothing is asked again, in any session, with any AI.

**1.9.0**
- **Hardware → Performance: a program's processes are one row.** A browser's processes (one a tab) show as one row,
  for example **msedge (32)**, with their CPU, RAM, GPU, VRAM, disk and threads added up.
  - The arrow at its left (or a double-click, or → and ←) opens it, listing each process under it with its own
    figures. The main one comes first, in bold; it's also the PID the row shows.
  - It stays open until you close it.
  - On a group, End task ends all of them.
- **Settings → General → Storage:** what the app and its tools keep on disk, and cleaning it up.
  - Each kind is listed with its files, size and oldest date, and its folder: performance and sensor history, logs,
    USB monitor logs, driving sessions, AI conversations, EQ wizard runs, and downloaded updates.
  - Tick some and **Delete now…** deletes their files older than 7–365 days. **Do this by itself each day** keeps it
    that way.
  - Settings are never touched.

**1.8.0**
- **Claude Code is now AI, with more than Claude.** Each tab picks **which AI** when it starts:
  - **Claude Code** as before;
  - **any model with an OpenAI-compatible API**, through the module's own coding agent.

  Run them side by side, so when Claude's limit is reached you can carry on in another tab.
  - **Providers…** sets them up from presets: Google Gemini (free key from AI Studio), OpenAI, OpenRouter, Ollama or
    LM Studio on this PC, or a custom one. Each has its key (kept encrypted), model (Fetch models), context window,
    longest reply, temperature and extra instructions, and a Test button.
  - **The agent:**
    - reads, writes and edits files, lists and searches them, and runs PowerShell commands in the tab's folder;
    - asks first as the tab's permissions say (or not, with Never ask);
    - shows thinking;
    - `/compact` and `/clear`;
    - keeps the conversation, so the tab picks up after a restart.

    Everything shows as with Claude Code: the cards, permission questions, the context ring, tokens, the activity
    ribbon, the files changed. The tab shows the provider's name.
- **Carrying on after a limit** (Settings → AI, on to start with):
  - **Claude Code:** a session stopped by your plan's limit waits, with a countdown, then sends "Please continue
    where you left off." (which you can change) once the limit resets. Stop cancels the wait, and the wait survives a
    restart.
  - **Other providers:** rate limits are waited out and the request sent again.
- Your Claude Code tabs, sessions and settings carry over (`ClaudeCode.json` becomes `AI.json`), as does the tab's
  place. The old `modules\ClaudeCode` is removed.

**1.7.0**
- **Claude Code: never ask** (Settings → Claude Code → Permissions). "Never ask before Claude uses a tool
  (dangerously skip permissions)" runs every session in Claude Code's bypass mode, and allows anything that would
  still ask. It asks you to confirm when you switch it on, takes effect at once (running sessions too, and a question
  already showing is answered), and each session's dashboard says so in red while it's on.
- **Claude Code: how the conversation looks** (Settings → Claude Code), with a preview, and the open tabs follow at
  once:
  - **Colours**, each picked from a palette or typed as #RRGGBB, or left to follow the theme: the background, Claude's
    text, your messages and their text, code blocks and their text, and the box you write in and its text.
  - **Text**: the font, its size, line spacing, how wide it goes, the code font and its size, and the size of the tool
    cards and notes.
  - **The box you write in**: how many lines tall (it grows to twice that) and its text size.
  - **Back to the defaults**.

**1.6.0**
- **Claude Code: your plan's usage, and warnings before you run out.** The app keeps track of your Claude plan's
  limits while it runs, whether or not the tab is open. It asks Claude Code every 10 minutes (`/usage`, which uses
  none of your plan; every 3 minutes when you're close), and running sessions update it with each request.
  - **At the top of the tab:** each window (this 5 hours, this week) with a bar, how much is used, and a countdown to
    its reset.
  - **Usage details** open:
    - each window's bar, with a mark for how far through the window you are in time ("you're 5% of the way through it
      and have used 70%: faster than it lasts");
    - when it resets, as a day, a time and a countdown;
    - the pace: at this rate, when you'd reach 100% and how long that is before the reset, or how much you'd have used
      by the reset;
    - a chart of the last day, with the pace carried on to the reset;
    - what's been using it, as Claude Code reports it;
    - the warnings' settings.
  - **Windows notifications:**
    - when a window passes 80% and 95% (you can change both);
    - when the pace will reach the limit within the hour;
    - when the limit's reached, saying when it resets;
    - when a window you were close on has reset, so you know you can carry on.
  - **A title bar bubble:** amber ("Claude 87%") when you're close, red ("Claude: limit, back 2:40 AM") when you're
    out.
  - **The tray menu** shows both windows and their countdowns. The session dashboard's plan meters show the time to
    each reset.
- Modules can raise Windows notifications and colour their title bar bubbles. Core's API version is now 0.27, so
  modules from this release need this release's app.

**1.5.2**
- **Claude Code: easier to read.**
  - The box you write in is three times as tall: it grows to about twice that before it scrolls.
  - The text is bigger throughout, with more space between lines and paragraphs: Claude's replies 15.5 (were 13.5),
    code 14 (were 12–12.5), headings 20 and 17.5, and the tool cards, notes, permission questions and dashboard
    about a point and a half bigger.
  - Code blocks have more padding.

**1.5.1**
- **Hardware → Performance: right-click a process** for what Task Manager offers:
  - Switch to;
  - End task (or Del) and End process tree, trying as administrator when Windows won't let it be ended, and asking
    first for a part of Windows;
  - Set priority;
  - Open file location, Properties, Search online;
  - Copy name, PID or path;
  - History.
- **Claude Code: carry on with a session from anywhere.** In a new tab:
  - **By its id:** paste what Claude Code gave you when it closed (`claude --resume <id>`) and click Open. It's found
    on this PC, in the folder it worked in, with its conversation.
  - **From the web or your phone:** paste a claude.ai/code link or session id, and Claude Code brings it here
    (teleport).
  - **From another PC:** Session → Export… there, then Import a session file… here.

**1.5.0**
- **New: the Claude Code tab** (a first draft). It runs Claude Code sessions in tabs, signed in as Claude Code already
  is on this PC (your own account), so you can chat and let Claude read, edit and write files and run commands, as the
  `claude` command does.
  - **Tabs:** one per session, all able to run at once. Each tab has a dot showing what it's doing, blinking amber
    when it needs you. Click the name of the tab showing to rename it; the Claude Code session is renamed too. A new
    tab picks its folder, or carries on with a session saved there. A tab's conversation so far comes back when it's
    reopened.
  - **The conversation:** Claude's text streams in, then shows as Markdown. Each tool call is a card: edits as red and
    green lines, commands, results, to-do lists as checklists, agents' steps. Permission requests are answered with
    Allow, Allow all this session or Deny.
  - **A live dashboard:**
    - a status orb that breathes, ripples, spins or blinks by what Claude is doing;
    - its writing speed;
    - the turn's activity ribbon;
    - a context-window ring;
    - tokens by kind, and their cost at API prices;
    - your plan's 5-hour and weekly usage;
    - the files changed;
    - the session's model and permissions, which can be changed while it runs.
- The wrapper offers Claude Code too.

**1.4.0**
- **Two downloads: Daisy's App and the wrapper.** The wrapper (`Wrapper-Setup-1.4.0.exe`, about 2.5 MB, against Daisy's
  App's 46 MB) is the same program with no modules. The first time it starts it asks which tools you want (**Choose
  your tools**: every module, with what it is and its size) and downloads only those. **Settings → General → Add or
  remove tools…** changes them later. It runs on Microsoft's .NET 8 Desktop Runtime, which its installer gets if it's
  missing (that runtime is most of Daisy's App's size). When the wrapper updates, its tools are fetched for the new
  version too.
- Each release now also carries every module as a zip (`Module-<name>-1.4.0.zip`) and their list
  (`Modules-1.4.0.json`), built from the same source as Daisy's App. Which module goes in Daisy's App and which the
  wrapper offers is `src/Modules/modules.json`.
- **No more separate Joy2Key and Resizer installers** (1.1.68–1.3.0): those copies won't find updates. Install the
  wrapper and pick the tool instead (settings start fresh; Joy2Key's Export / Import moves profiles).

**1.3.0**
- **Title bar bubbles:** a green **ForzaWheel** bubble after the app's name and version while the ForzaWheel emulator
  runs, and a green **Joy2Key** bubble while Joy2Key is on with a profile in use. Each
  has a setting to leave it out: **Show ForzaWheel in the title bar while it's running** (under Start) and **Show
  Joy2Key in the title bar while a profile is in use** (under Joy2Key's on / off switch).
- **Video → Mini Mirror: a locked mirror stays put.** When a monitor went or the resolution changed (a 5120-wide
  screen switched to 2560 while the game wasn't running), mirrors past the narrower screen's edge were brought back on
  screen, and that new place was saved. Now a locked mirror is never moved: it keeps its saved place even off screen,
  Windows' own moves of it are undone, and it's where you put it when the wide screen is back. Unlocked mirrors are
  still brought back on screen.
- Core's API version is now 0.26 (TitleBadges), so modules from this release need this release's app.

**1.2.0**
- **New: Gaming → Forza Analysis** (a first draft): your driving sessions, analysed for how smoothly you use the
  brake and throttle, both going on and coming off.
  - Scores for brake application, brake release, throttle application, throttle release, steering, consistency and
    overall.
  - The time split: full throttle, part throttle, braking, both together, coasting.
  - The trace, with the brake zones shaded and problems marked. You can zoom and drag along it.
  - Every brake zone, and every throttle application, drawn over the others to compare. Brake zones can be lined up
    at the brake point or at the release.
  - Tables of each zone and throttle movement, with their times, smoothness, trail braking, coasting and notes. For
    example: let go in one go, pressed again while releasing, a spike then backing off, a slow build-up, a throttle
    stab, lifting on the way.
  - Tips from all of that.
  - Sessions can be named and deleted.
- **ForzaWheel's input overlay logs a session** while it shows (**Record a session log while it shows**, on to start
  with).
  - About 100 rows a second of the steering and pedals.
  - A session starts when something moves and ends when the overlay closes or after two minutes of nothing.
  - Saved as CSV files in `%LOCALAPPDATA%\DaisysApp\ForzaSessions\`.

**1.1.68**
- **Update button** at the right of the tab row: checks for a newer version and, if there is one, installs it and
  starts the app again (the quick update). Double-clicking the Settings tab no longer does this.
- **One-tool apps: Joy2Key and Resizer** can be shared on their own. Each release now also has
  `Joy2Key-Setup-1.1.68.exe` and `Resizer-Setup-1.1.68.exe`: the same program, named after the tool, with only that
  module beside it, so they carry none of the other modules' code. With one module the window is just its tab and
  Settings, no sub-tabs. Each has its own settings, logs, tray icon, install folder and updates, and runs alongside
  Daisy's App.
- **The app builds its tabs from the modules it finds.** Each module's DLL says what it is (`[Applet]`) and which tab
  it shares (`Group`); a shared tab is described once (`[assembly: TabGroup]`). Two or more modules of a group make one
  tab of sub-tabs; one on its own is a tab of its own. Daisy's App looks and works as before.
- **Modules split:** Joy2Key and ForzaWheel are each a module (`modules\Joy2Key`, `modules\ForzaWheel`; they were
  `modules\Gaming`), still the Gaming tab's two sub-tabs; the Resizer is a module again (`modules\Resizer`), still
  Video's first sub-tab. Settings, tab order and the sub-tab shown last are kept. Their logs are now `Joy2Key-…`,
  `ForzaWheel-…` and `Resizer-…`.
- **Smaller core:** the audio, Voicemeeter and hardware code (and NAudio) moved out of DaisysApp.Core into
  DaisysApp.Shared, which only the Audio, Hardware and Video modules bring. Core's API version is now 0.25, so modules
  from this release need this release's app.

**1.0.0**
- **Tools → File Sync:** a running sync is called **Sync** (its window and its heading), not Mirror or Mirroring. Its
  progress has a **speed graph** like Windows' copy dialog but more technical: the speed against the time since it
  started, with scales, the average as a dashed line, and readouts under it (speed, average, peak, time taken, bytes
  and items done, time left, problems). **Background** hides the window and shows the sync on the tab, just above
  Recent syncs: one line with its progress; click it for the graph, Pause and Stop; **Pop out** at its top right
  brings its window back.
- **Tools → Renamer:** the panels are in three columns, each closed to start: click a title to open it (it pushes the
  ones under it down). Hold a title and drag to move a panel anywhere, in any column; where they are is kept.

**0.66.0**
- **Gaming → Joy2Key:** controllers whose buttons, axes or hats the Windows joystick API misses now show and work: a
  Saitek / Logitech X-56 stick showed 1 button and 2 axes, and its throttle stopped at 32 of its 36 buttons. Each
  controller is also read through DirectInput where that sees more: up to 128 buttons, its axes when the joystick API
  found fewer than six, and up to four hats (Hat 2–4 are new inputs). Existing profiles and JoyToKey imports keep
  their inputs.

**0.65.0**
- **Gaming → ForzaWheel:** a **start / stop shortcut** for the emulator: the controller button beside **Start** opens a
  window to set it, a key combination or a controller / button box button. It works anywhere, in the game too, and does
  what Start does (Auto Hush included).

**0.64.0**
- **The setup wizard** runs in full after installing, and after that opens by itself only when a module is missing a
  setting it can't do without (flagged as required: today, Zabbix switched on but never set up or skipped), showing
  only those modules' steps; if nothing is missing it doesn't open, so it no longer runs after every update.
  **Settings → General → Run the setup wizard…** goes through every step from the beginning, whatever is set. (The
  Gaming permission to count frames is optional, so it's in the full wizard but never opens it by itself.)
- For module authors: `IApplet.SetupRequired` and `RequiredSetupPage` (DaisysApp.Core's API version is 0.24).

**0.63.0**
- **Gaming → ForzaWheel: input overlay.** The pedals' and wheel's last few seconds as racing telemetry traces
  (throttle, brake, clutch, handbrake and steering), with live bars and a turning wheel, on a see-through, resizable
  window that stays over the game and lets clicks through. Its block (above vJoy device): show it, Move / resize,
  Reset position, how many seconds of history, how dark the background, and which traces.

**0.62.0**
- **Gaming → ForzaWheel:** **Hush** is second in the left column, with Mute / Unmute above the devices, and
  **Auto Hush** (on to start with): starting the emulator mutes the bound devices for Forza and stopping it unmutes
  them, so Start is the only button. Every block but Emulator folds up and stays as left. The force feedback tuning
  line and its buttons are gone (Strength and Reverse stay; the tuning still comes with Import EmuWheel).
- **Gaming → Joy2Key:** switches to a game's profile while the game is **running** (not just in front), and to the
  **Default** profile when it closes, for the desktop (made blank if missing; it can't be deleted or renamed). A
  profile picked by hand stays until a game starts or closes.

**0.61.0**
- **Gaming → ForzaWheel, force feedback (important fix):** the wheel was thrown from side to side at full force. vJoy
  hands over a force as a 16-bit signed number in a 32-bit field without carrying its sign into the top half, and it
  was read as 32 bits, so every small push one way (say −100) became full force the other way (+65436, capped at the
  maximum). The forces (and the spring and damper coefficients and offsets, and the vibration offset) are now read as
  16 bits, as EmuWheel's vJoy wrapper reads them, and a force outside the game's range is dropped rather than sent.

**0.60.0**
- **Gaming → ForzaWheel:** **Import EmuWheel…** (Bindings file) reads Forza EmuWheel's `configuration.json` and makes
  its bindings ForzaWheel's: every controller's axes (with Inverted and Deadzone), buttons and d-pad, across as many
  controllers as it uses, and its force feedback tuning.
- Fixed the throttle, brake and clutch reaching Forza backwards: they were sent to vJoy reversed; now they're sent as
  how far they're pressed, as EmuWheel does. A pedal whose **Invert** was ticked to make up for it is flipped back once
  by itself, so Forza gets the same and the bars on the tab are right again.

**0.59.0**
- **Gaming → ForzaWheel: force feedback.** Forza's forces now reach the wheel (until now ForzaWheel didn't pass them
  on, so the wheel had only its own built-in effects, which felt weak): ported from Forza EmuWheel so it feels the
  same. The steering wheel is opened exclusively with auto-centre off while the emulator runs, and Forza's constant
  force, vibrations (engine and gear shifts), spring and damper are played on it with EmuWheel's tuning. A **Force
  feedback** card: on or off, **Strength** (100% = EmuWheel), **Reverse**, **Use EmuWheel's tuning…** (reads your
  EmuWheel configuration.json, for exactly the feel you had), **Default tuning**, **Test** and whether forces arrive.

**0.58.0**
- **Gaming → Joy2Key:** **On** / **Off** on each controller in a profile: off, its input is ignored and its
  assignments are kept, for testing without it. **Joy2Key on or off** (nothing sent, whatever the profile) is now at
  the top of the left column. **Export** saves every profile and Joy2Key's settings to a .zip, as a backup.
- **Gaming → WheelEmu is now ForzaWheel** (for Forza Horizon only; its settings move to `ForzaWheel.json` by
  themselves). The wheel buttons after X, Y, LB, RB and the stick clicks are numbered (Wheel button 11–16), as what
  each does is chosen in Forza; the Extra axes section is gone (Forza doesn't see them).

**0.57.0**
- **Gaming → WheelEmu, binding Steering** (it took axis Y at a touch and sat at +225°): DirectInput reports a device's
  axes centred until it sends its first report, and a Fanatec base sends one only when something changes, so touching
  the wheel made its unused pedal input (Y) jump from the middle to full, which **Set** took. Binding an axis now
  treats a device's first report as it waking, and once something moves it watches for **1.5 seconds** and takes the
  axis that travelled furthest (turn the wheel left and right; press a pedal and let it go), so a jump or a stuck
  axis can't win and a sensitive 1080° wheel isn't taken at a touch.
- Axis controls take only axes, and button and d-pad controls only buttons and hats.
- Steering **Sensitivity** (100–400%): Forza's wheel is 450°, so a 1080° wheel at 240% turns the car as far as the
  hands. **Set Steering again** after updating.

**0.56.0**
- **Gaming → WheelEmu, binding:** **Set** passes over every control already bound to something else (touching the
  wheel while binding a button no longer takes it), and Steering and the combined pedals take only an axis. Fixed
  Steering binding an axis that never moves (it showed *Steering +225°* and ignored the wheel): where each control
  rests is now taken from good readings only, where an unread device's axes counted as 0, so an axis that sits at
  full looked like it had just moved. Each axis's range is also read from the device rather than assumed. **Set
  Steering again** to replace a binding made before this.

**0.55.0**
- **Updates:** fixed the update that sometimes stopped at 0% or partway (51%…) and had to be killed. The updater
  wrote its progress from many threads at once, to a file the app was reading, and crashed when they collided
  ("Access to the path is denied", in Windows' event log); its progress is now written from one thread, retried if
  the file is busy and never fatal, and the app reads the file without blocking it. A download that stops receiving
  for 30 seconds (or drops) now carries on from where it got to, up to four tries. (The fix is in the updater, so it
  helps from the update after this one.)
- **Translations:** only English ships now; the Spanish, French and Portuguese files are removed (also from an
  existing install). Settings → General → Language still lists any language whose files are added.

**0.54.0**
- **Gaming → WheelEmu:** **Hush** works as EmuWheel's Hush.exe does, with nothing to install (HidHide is no longer
  used): it mutes each device for Forza only, through Windows' GameInput device settings, and shows which are muted;
  other games still see them, so there's no need to unmute.
- The vJoy device is told to Windows as a **racing wheel** with EmuWheel's layout (its `vjoy.reg`), and the bindings
  follow that layout, so Forza takes it for a wheel: steering X; throttle, brake and clutch Z, Rx and Ry; handbrake Rz;
  Menu, View, A and B buttons 1–4; wheel buttons 5–16; shift down and up 17 and 18; reverse and gears 1–7 19–26; the
  d-pad. The vJoy check includes it and **Set up vJoy device** writes it. Gears 8–10 and the separate handbrake and
  clutch buttons, telemetry, ANNA and photo mode rows are gone (Forza doesn't see them on a wheel: bind those
  actions to wheel buttons in Forza); buttons 27–128 and the extra axes stay, for other games.
- The bindings have a **picture** at the top for testing: the wheel turns with the steering, buttons, paddles and the
  d-pad light up, the pedals and handbrake fill and the shifter shows the gear. Steering and Pedals stay full width,
  the other groups are in two columns.

**0.53.0**
- **Gaming tab** (new): **Joy2Key** moved in as a sub-tab (its profiles and settings unchanged; updating moves its
  place among the tabs, and whether it was off, to Gaming), and **WheelEmu**, new, a wheel emulator like Forza
  EmuWheel: your wheel, pedals, shifter, handbrake and button boxes as one vJoy controller.
  - **Start** the emulator (or start it with Daisy's App); it reads the devices through DirectInput (8 axes,
    128 buttons and 4 hats each) and feeds the vJoy device.
  - **vJoy device** check: all 8 axes, 128 buttons, one 4-direction hat (vJoy's default has 8 buttons and a
    continuous hat) and force feedback; **Set up vJoy device** sets it all with vJoyConfig as administrator.
  - **Hush**: hides the real devices from the game with HidHide, Daisy's App still reading them; **Unmute** shows them.
  - **Bindings**: every control of the game's controller (steering, pedals or combined pedals, clutch, handbrake,
    shift up / down, gears 1–10 and reverse, A / B / X / Y, LB / RB, Start, View, stick clicks, page up / down, the
    d-pad, horn, look back, camera, rewind, telemetry, ANNA, photo mode, three extra axes and buttons 35–128): **Set**
    and press the real control, **+** for another, invert and deadzone for axes, live values; saved as JSON, with
    Save to file / Load from file. Force feedback to the wheel isn't passed through yet.

**0.52.0**
- **Hardware → USB Monitor / Devices:** the details page's explanation is now the first row of the property list,
  **Probable Device:** with a word or two for what it is (*UPS (CyberPower)*, *Keyboard and mouse (Logitech)*,
  *Unknown, most likely …*). Click the row to open the full explanation across the page's width; the page is back to
  its old size and header.

**0.51.0**
- **Hardware → USB Monitor / Devices:** a device's details page has **What it probably is** in its top right corner,
  in plain English: what kind of device it is and whose (from its USB class, the name it reports itself and its
  vendor ID); where it's plugged in, hub by hub back to the PC's port and which USB controller that is (on the
  motherboard, an extra controller chip, a USB card or a graphics card's USB-C); what else is on the same hub, so it
  can be found among many; and for a problem, what the failure means, its usual causes and which device that worked
  there it most likely is. It spots a big hub made of two hub chips, and a hub built into another device.

**0.50.0**
- **Video → Recording:** each camera in **Add Camera** has its own **Hide / show shortcut** (a key combination or a
  controller / button box button) that takes it off the recording and the virtual webcam and puts it back; cameras
  sharing a shortcut hide together. The webcam's mute shortcut can be set from a button beside **Mute virtual
  webcam**, and every Video shortcut (record, overlay, mode, benchmark, mute) now takes a controller button too.

**0.49.0**
- **Documentation:** each module's guide describes that module, and each tool's guide ends with a **Code** section
  (its files and what each does: added for Volume, Levels, Delay, Performance, USB Monitor, Sensors, Zabbix, Logs,
  Resizer, Mini Mirror and Overlay / Recording). The main README is about the app itself (install, using it,
  Settings → General, translations, files, updates, building) and links to the modules' guides instead of repeating
  them; the Voicemeeter notes moved to the Audio guide, and the version history to this file.

**0.48.0**
- The **notification bar** fades away and closes by itself after a message such as *Updated to version …* or *You're up
  to date*, after the time set in Settings → General → **Notifications** (8 seconds by default; or never). Problems,
  an update on offer and things in progress (checking, downloading, installing) stay until you close them.

**0.47.0**
- **Audio → Levels** and the **EQ Wizard**'s live view: in a quiet room the analyser no longer keeps turning itself up
  (it went on raising its gain by 0.5 dB a second, until the mic input's own hiss, often a hump near 17–20 kHz,
  filled the graph); it stops at about −50 dBFS, so quiet shows as quiet.

**0.46.0**
- **Audio → Levels:** with the Microphone card ticked, the analyser is live all the time the Levels page is on screen,
  test signal or not (the mic closes when you leave the page or hide the window).

**0.45.0**
- **Audio → Levels:** the analyser in the middle of the speaker map is always shown: greyed out with *No Microphone*
  while the Microphone card is unticked, empty (saying what it shows) until the test signal plays, live while it does.

**0.44.0**
- **Quick update:** double-click the **Settings** tab to update now: the newest version is downloaded, installed with
  no windows and no admin question (through a Task Scheduler task the installer sets up), and the app starts again on
  it by itself, without the setup wizard (unless it has something new). The first time, from 0.43 or earlier, Windows
  asks for admin approval once.

**0.43.0**
- **Audio → Levels:** a **Microphone** card under *Output device*: tick it to see what the mic hears while the test
  signal plays: its level, and a live spectrum analyser in the middle of the speaker map (in the listener's place),
  corrected for the mic with its **Calibration file** if ticked (the same file the EQ Wizard uses). Unticked, it shows
  nothing and the mic stays closed; it's off while a wizard is open. The map's middle 3 × 3 cells are kept free of
  speakers for it (the 16 around them are the speakers'; any saved in the middle move to the nearest free cell).

**0.42.0**
- **Tools → File Sync:** **Compare** and **Sync** (was Synchronize…) are at the top right, beside the saved syncs.
  Compare can be clicked once both folders are filled in, and Sync once a comparison has found something to do (the
  sync uses it). The sync's window lists what it's about to do with a tick each (copy new files, make folders, update,
  delete files, delete folders), to leave any of it out at the last moment. The saved syncs list starts on *Saved
  syncs*, with **New…** to name a sync; **Save as…** is available once it's named.

**0.41.0**
- **Tools → Renamer** (new), like Bulk Rename Utility: a folder's files (and folders, subfolders, hidden ones, by a
  mask) with a tick each, and Bulk Rename Utility's panels in its order: RegEx, Name, Replace, Case (with exceptions),
  Remove (first/last, from–to, characters, words, crop, digits, symbols, accents, trim…), Move / Copy, Add (prefix,
  insert, suffix, word space), Auto Date (modified, created, photo taken, today), Append Folder Name, Numbering
  (start, step, digits, decimal, hex, letters, Roman, per folder) and Extension. Every new name shows as you type, in
  red when it clashes or Windows won't take it. Renames swap and change case safely; **Undo** puts the last rename back.
- **Tools → File Sync:** **Compare by** is now ticks: name (always), size, date and time, contents, in any mix, e.g.
  name and size when times are wrong. **Compare** is green.

**0.40.0**
- **Tools** (new tab) with two sub-tabs:
  - **File Sync**, like FreeFileSync: a source and a destination side by side (browse, paste or pick a recent one;
    swap them), **Mirror** (the destination exactly like the source, extra files to the Recycle Bin) or **Update**
    (new and changed files, nothing deleted), compare by time and size or by contents, a Leave alone list.
    **Compare** lists what would happen; **Synchronize…** opens the sync's own window: what's about to happen (with
    warnings, e.g. an empty source), then progress (speed, time left, **Pause**, **Stop**) while the app stays free.
    Files are copied under a temporary name and swapped in, so a stopped sync leaves no half files. Saved syncs, also
    run from the tray.
  - **Disk Usage**, like WizTree: the folder tree largest first (any column sorts), file types, and a cushion-shaded
    treemap. **Fast scan** reads an NTFS drive's file table directly (as administrator: Windows asks), so a whole
    drive takes seconds (2.3 million entries in under 4 s here); other drives are read folder by folder with several
    threads. Open, show in Explorer, copy the path, scan a folder, or delete to the Recycle Bin.
- **Setup wizard:** **Back** is there on the last step too, and a double-click on **Next** no longer skips a step.
- Typing in drop-down boxes that take text (e.g. Resizer's program box) shows what's typed; it didn't before.
- Core: `ModuleTasks` (a module's job run in a second copy of the app, as administrator if needed).

**0.39.0**
- **Network** (new tab) with two sub-tabs: **Zabbix** (was a tab of its own) and **Logs** (was the last page of
  Settings); everything in them works as before, and their settings are kept. Settings → Network has Zabbix's page.
  Updating moves Zabbix's place among the tabs to Network; if Zabbix was switched off, Network is on (for Logs)
  with Zabbix switched off.
- **Zabbix** is switched off and on by itself now (it's part of the Network module): **Switch off Zabbix** in its
  setup step, or **Use Zabbix** in Settings → Network → Zabbix. Off, its sub-tab and widgets go and nothing is read.
- Settings → General → Logging **View** opens Network → Logs.

**0.38.0**
- **Zabbix** (new tab): your Zabbix server's hosts as a grid of boxes (reachable or not, CPU, memory, problems,
  uptime; a host that's only pinged shows its response time). Click one for its page: Performance-style tiles (CPU,
  memory, disks, network, temperatures, ping) opening the same history windows (live, or up to 30 days, with peaks,
  a summary and Export), **New graph** to put several values in one unit on one graph (values in other units are
  greyed out), problems, details and every item. Hosts, tiles, graphs and items can be widgets. The setup wizard
  asks for the server while it isn't set up (Test connection, Skip for now, or Switch off Zabbix). Read-only;
  Zabbix 5.0 and later.

**0.37.0**
- **Hardware → USB Monitor:** problem devices show **bold red**, with what the code means and the device they most
  likely are (matched against the devices known to have worked: the same port, hub, vendor and product, serial or
  container), in a new **Likely device** column, the details page and the logs. Right-click a column header to
  filter it (a tick box per value) or sort it; **Reset filters** clears them.
- **Hardware → Devices** (new): every USB device known, from the USB Monitor and Windows' own records, with last
  connected, disconnected and working (status bold red when disconnected or failing); double-click for details.

**0.36.0**
- **Video → Recording, cameras:** moving and resizing snap to the picture's edges and the other cameras' edges, and a
  resize snaps to another camera's height or width, so side-by-side cameras match exactly.
- **Mute virtual webcam:** a button, a tray item and a shortcut (a key combination or a controller / button box
  button; Ctrl+Alt+F8 to start). The webcam keeps running and shows a VIDEO MUTED card instead of the picture.

**0.35.0**
- **Video → Recording, cameras:** resizing keeps a camera's shape (right-click it to untick **Lock aspect**);
  right-click also has **Rotate 90° / 180° / 270°**, **Flip horizontal** and **Flip vertical**. Rounded corners start
  at 20%, and a new camera's box takes its picture's shape.
- **Shape:** record (and show on the virtual webcam) as captured or the middle of it in 32:9, 21:9, 16:9, 4:3 or
  9:16 portrait, e.g. 5120 × 1440 at 16:9 records 2560 × 1440; the camera layout shows that shape.
- **Overlay while recording:** Simple shows a solid deep-red light; Medium the time and file size; Advanced also
  the bit rate, frames written and dropped, and the recording's cost (video encoder, GPU for the conversion, CPU).

**0.34.0**
- **EQ Wizard, Hear the difference:** after a run, play your own music (chosen once and kept; **Reset** to pick
  another) on all the speakers while the new EQ switches on and off every few seconds (5 to start, on a slider),
  with the speaker levels left alone. Then **Keep EQ**, **Start again** with other options, or **Discard EQ**.

**0.33.0**
- **Audio → Levels:** the speaker that's playing counts down the seconds until the next one, on its tile (Auto-cycle)
  and on its row in the Level and EQ Wizards.
- **Live spectrum:** the Level Wizard has a graph of what the mic hears, and the EQ Wizard draws it under the
  speaker's curves while it measures (pink noise reads flat; it's only to watch).

**0.32.0**
- **Audio → Levels:** the speaker that's making sound turns green, fading smoothly from one speaker to the next as
  Auto-cycle steps through them (and as the wizards measure).
- **Mic AGC:** a mic whose automatic gain control is on and won't switch off is flagged as soon as it's picked, and
  the wizards won't run with it; the start-loud-then-drop check is stricter (6 dB).

**0.31.0**
- **Audio → Levels:** the wizards switch off the mic's automatic gain control (AGC) while measuring, where the
  driver allows, and put it back after. If a speaker still starts loud and then drops and stays down (AGC's
  signature, common in USB headset adapters), the run stops and says so instead of "Couldn't hear" or failing
  on a squeezed, wrong level.

**0.30.0**
- **Video → Recording, cameras:** each camera box has handles on its corners and sides to resize it from there, and
  **Alt** + drag on a handle crops the camera's picture at that edge (the picture stays put; drag back out, or
  **Reset the crop**, to undo). Recordings and the virtual webcam show the crop.

**0.29.0**
- **Video → Recording, cameras:** **Add Camera** on the Record card lays one or more webcams (up to four) over the
  picture: placed and sized on a canvas of the capture monitor at its own resolution, with rounded corners and
  mirroring. Recordings include them.
- **Virtual webcam:** **Start virtual webcam** shows the same picture, cameras and all, as *Daisy's App Camera*, for
  Discord, Teams, OBS or a browser (30 fps, fitted inside 1920×1080). Its sound: the game's playback device can be
  sent to a virtual cable (Voicemeeter's, VB-CABLE), whose other end Discord takes as its microphone. The installer
  registers the camera with Windows (softcam, MIT).
- **History:** every session's average graph has its own row; under it, the sessions listed beside the picked
  session's graph, with its average on one line and its other figures on the next.

**0.28.0**
- **Video → Recording:** **Performance** (was GPU, CPU and memory) stays on the left, as tall as **Record** and
  **Benchmark**, now stacked on the right. **History** (was Performance history) lists an app's sessions on the left,
  short (*10/9 7:19 PM - 13 min - 95 FPS*), beside the graphs; clicking one shows its graph with its other figures (1%
  and 0.1% lows, GPU load, top temperature) centred above it. Hovering over either graph shows the values there.

**0.27.0**
- **Level Wizard:** new **Match by** choice (in the wizard and Settings → Audio), **Full range** by default: full-range
  pink noise read across the whole spectrum, as the ear and an SPL meter hear it, so a speaker with weak bass (a small
  centre) is turned up to match. Before, it always matched on 500 Hz–2 kHz only (still there as **Voice band**, the
  cinema standard), which left such a centre sounding several dB quiet. After a successful run, **Close** reads
  **Close & Save**.

**0.26.0**
- **Audio:** the sub-tabs are now **Volume** (was Levels), **Levels** (was Tests) and **Delay**.
- **Level Wizard:** no more false "the microphone's level jumped" on the subwoofer (and rears) with longer measuring
  times. A steady sub's noise, mostly a few octaves at the very bottom that the room's resonances narrow further,
  naturally swings by 5–9 dB from one half second to the next (an SPL meter's one-second averaging hides it), more
  the longer it's measured. Each speaker is now judged in the band that's read (sounds outside it don't count); the
  sub on one-second slices, leaving out its loudest and softest. The wizard also catches noise suppression, which
  learns a steady sound and fades it by 10 dB or more after a second or two, and says so. The run log shows both.

**0.25.0**
- **Tray menu:** the tools of a tab with sub-tabs each get their own item at the top level (Resizer, Mini Mirror,
  Overlay, Recording), rather than a submenu inside Video's.
- **Video:** Gaming is now two sub-tabs: **Overlay** (Now and FPS overlay) and **Recording** (GPU, CPU and memory,
  Record, Benchmark, and Performance history); Settings → Video splits the same way. The squares use the whole width
  of the window, two to a row.

**0.24.0**
- **Audio → Tests, Level Wizard:** the louder speakers still come down to the softest, but when one would have to be cut
  by more than 10 dB, the softest is raised instead (and every speaker with it, so they stay matched) by up to a limit
  you set in Settings → Audio → **Level Wizard** (6 dB by default), before the others are cut further. Before, it raised
  them as far as it took. Speakers still too far apart are listed as out of range, with what to do.

**0.23.0**
- **Logs** moved from a tab of its own to the last page of Settings (after Video).
- **Settings:** every page uses the whole width of the window.
- **Performance:** the tiles are **RAM**, **VRAM**, **CPU Cores** and **Temps**. **Network Tests**' graph goes from 0 to
  50 ms; each host has a **Max** (50 ms) and a **Red for** time (15 s): an answer slower than its Max turns its line
  bold and red until it has been back under it that long. All of it, and the graph's top, is set in the card's gear.
  **Speed Test**'s scale reads 2.5 Gb, 1.8 Gb, 1.2 Gb, .6 Gb; the server and the data used moved from the card to an
  **Information** group at the bottom of its gear.

**0.22.0**
- **Video** (new tab) holds **Resizer**, **Mini Mirror** and **Gaming** as sub-tabs, and **Hardware** (new tab)
  **Performance**, **USB Monitor** and **Sensors**, each tool as it was; their settings pages are sub-tabs of Settings →
  Video and Settings → Hardware. Each tool keeps its settings, and the tabs' order, on/off state and last tab open move
  over by themselves. The installer removes the six old module folders.
- **Tabs:** drag one to move it, as in a browser: it follows the pointer and the others slide out of its way (no more
  press and hold).
- **Setup wizard:** runs after each update too (saying which version you're on, with **What's new**), and tools can
  add a step. **Gaming**'s, until Windows lets the app count frames: **Allow…**, then sign out and in (or restart),
  and whether to show the FPS overlay.
- **Gaming:** the FPS overlay is off on a new install. The tab is now squares, two to a row: Now, a new **GPU, CPU and
  memory** square (GPU load and video memory moved here from Now, with CPU and memory, and graphs of the last two
  minutes), FPS overlay, Record and Benchmark; **Performance history** (was Game history, now by **App**) stays under
  them, the whole width.
- **Logs:** only the Level column is coloured: errors and crashes red, warnings yellow, everything else green. Where
  the app started again without having logged a clean exit, a red row marks the spot, with when that run started and
  its last entry just below; **N improper shutdowns** selects each.
- **Audio Tools** is now just **Audio**, and **Joy 2 Key** is **Joy2Key**.
- **Speed Test** is off on a new install; when switched on it runs once a day by default, for 5 seconds each way.

**0.21.0**
- **Logging:** the app and every applet now keep a log, one file a day each (`DaisysApp-10-10-26.log`,
  `AudioTools-10-10-26.log`…) in `%LOCALAPPDATA%\DaisysApp\logs\`, deleted after 7 days. **Normal** records what they
  do (startup with each applet's time, devices, profiles, mirrors, recordings, wizards, settings changed, update
  checks) and every error; **Debug** (Settings → General → Logging) adds much more, including every exception thrown
  and every settings file saved. They replace `errors.log`. After an unexpected close, the next start notes it and
  copies in what Windows recorded about the crash (for a .NET crash, the error and where it happened).
- **Logs** (new tab): Daisy's App's logs (all applets merged, or one), Windows' Application, System and Setup logs, what
  Windows recorded about the app's crashes, any other Windows event log, and each WSL distribution's journal, each in
  its own tab (**Add**), remembered. Time range, level filter and search; the selected entry in full below. **Copy**
  (to the clipboard) and **Export…** (text, CSV, or a zip of every log file to send in).

**0.20.1**
- **Audio Tools:** a single jump in the mic's level (a noise in the room, a glitch) no longer stops the Level or EQ
  Wizard: that speaker is measured again, and the run stops only if it jumps all 3 times. The log has every half-second
  level of a rejected recording.
- **EQ Wizard:** a speaker that gets no filters says why: the range it was corrected over, and, for a small speaker,
  below which frequency it falls short of the target's bass (which the EQ doesn't boost). Fixed the EQ adding narrow
  cuts at the bottom of a speaker's range to undo the spill of a bass boost just above it.

**0.20.0**
- **Audio Tools:** the Level and EQ Wizards check that the mic's signal is real before trusting it, and stop with an
  explanation (putting the levels or EQ back) when it isn't: a quiet room recorded as digital silence, a steady test
  noise swinging by more than 10 dB, speakers still more than 3 dB apart after the last pass, or an EQ check that
  doesn't match the EQ. The diagnostics from 0.19.4 showed all of these: the mic's sound processing (noise
  suppression and echo cancellation) was removing the test noise, which 0.19.5's raw mode turns off.
- Both wizards show a warning when the mic can't be read raw.

**0.19.5**
- **Audio Tools:** the mic is opened in Windows' raw mode, without the sound card's noise suppression, automatic gain
  and echo cancellation, which made the Level and EQ Wizards' readings wrong on analog mic inputs (echo cancellation
  removes part of the test signal). If a mic can't give its raw signal, the status line says how to turn the
  processing off. On a stereo input the mic is read from the louder channel.
- **Level Wizard:** each speaker is measured for as long as Auto-cycle gives it (its seconds on the main screen)
  instead of always 2 seconds; every run is logged, and it has **Export diagnostics…** too.

**0.19.4**
- **Audio Tools, EQ Wizard:** every run is logged (settings, mic, the EQ as written, each recording and a table per
  speaker of what was heard, the response and the filters), and **Export diagnostics…** zips the last runs to send in.

**0.19.3**
- **Audio Tools, EQ Wizard:** no longer leaves speakers sounding thin. The target is now a proper in-room curve by
  default (the bass rising to +6 dB below about 150 Hz, the treble falling gently above 1 kHz) instead of flat, and
  only the narrow peaks in the bass are cut fully: the broad shape is cut by at most 3 dB, as a single mic position
  overstates the bass the room adds. Run the wizard again to replace an EQ made with 0.19.2 or earlier.

**0.19.2**
- **Performance:** the Network Tests and Speed Test graphs have a value scale and times, and show each line's value
  under the mouse; the "Last 5 minutes · top of the graph…" line under the pings is gone.
- **Joy 2 Key:** the input tiles are two lines (the input and what it does) and a little narrower.

**0.19.1**
- **Gaming:** the Advanced overlay (and the tab) shows the game's loaded upscaling and frame generation libraries and
  their versions (DLSS, DLSS Frame Gen, Ray Reconstruction, FSR, XeSS, Reflex). Settings → Gaming can switch on NVIDIA's
  own DLSS indicator, which shows the DLSS mode and render resolution in games.

**0.19**
- **Gaming → Benchmark:** measure a run of the game in front, for a set time or until it's stopped, started and stopped
  with a shortcut (Ctrl+Alt+F12) or from the tab. Each run is saved with an editable name; **Benchmarks…** shows a
  report for each, and compares runs of the same game and length side by side, with the differences and their frame
  rates on one graph.

**0.18**
- **Setup wizard:** runs once for each user, the first time the window opens after installing or updating: a welcome,
  then every tool with an on / off switch and a one-sentence description, dragged up and down to set the order of the
  tabs, then a summary (restarting if anything changed). Settings → General → Applets has **Run the setup wizard…**.
- Modules can now have their own first-run setup, run after the app's (none use it yet; see the Template's README).

**0.17.2**
- **Joy 2 Key:** the profile selected in the list is now the one in use (as in JoyToKey). Before, selecting a profile
  only showed it and the **Use this profile** button had to be clicked too, so a profile that was set up but not
  "chosen" sent nothing. On updating, the profile that was showing becomes the one in use.
- **Joy 2 Key:** the tab shows the last key it sent and when, and says when it's paused (while the tab is in front).
- **Joy 2 Key:** an error no longer stops it until the app restarts: it's logged and it carries on.

**0.17.1**
- **Tabs** can be put in any order: press and hold one, then drag it. (Settings stays last.)
- **Audio Tools → Levels:** how much a shortcut press changes the volume moved to Settings → Audio Tools.
- **Joy 2 Key:**
  - The left column stays put while the controllers on the right scroll: at its top the profile you're looking at
    (its games and options), then the profiles (with **Import**, which opens the JoyToKey import, next to **Delete**),
    then on / off and auto-switching.
  - Each controller's box can be folded up (the arrow next to the bin), and removing one always asks first.
  - The input tiles are half as wide.
  - **+ Add another key after it (makes a macro)** under the keys of an input, the quick way into a macro.
  - The JoyToKey import has a new step: which controller each of JoyToKey's numbered joysticks is, to check and
    change before anything's brought in.

**0.17**
- **Joy 2 Key:** macros. An input can play any number of keys one after another (**+ Add a key**, reorder with the
  arrows), each a single key or a combination, with the same pause between all of them (e.g. 0.5 seconds) or a pause
  set after each, once per press or over and over while it's held.

**0.16**
- New **Joy 2 Key** tab, a first draft of a JoyToKey replacement: profiles per game (switching with the game in
  front), a tile per button / axis direction / POV direction of each controller that lights up when it's used, and
  for each input keys (up to four together; held, tapped for a set time, repeated, toggled, or other keys on a long
  press), mouse movement, clicks and the wheel, running a program or switching profile. Right-click a keys box for
  every key by group (number pad, F13–F24, left / right modifiers, media, browser, mouse…). Xbox-style pads are read
  through XInput, numbered as JoyToKey numbers them. **Import from JoyToKey…** brings its `.cfg` profiles in, and
  lists what couldn't come across.

**0.15**
- **Audio Tools** now holds three tools on sub-tabs: **Levels** (was the Audio Levels tab), **Tests** (what was Audio
  Tools) and **Delay** (was the Audio Delay tab). Their settings and shortcuts are kept. Levels no longer has the
  explanation under its columns.

**0.14**
- **Performance widgets:** a history window's **Widget** button pops its graph out into a small window to keep anywhere
  on the screen: drag and stretch it, set its opacity, stay on top and time span; right-click to change it. A new
  **Widgets** card under Processes lists them, to change or delete.
- **Performance:** press and hold a tile to drag it to a new place; click a graph's scale to set its top (clocks now
  fit closer to the readings too: a 6.1 GHz CPU tops out at 7, not 10); on a PC with several graphics cards, pick which
  the GPU window graphs, each in its own colour; the history window's Summary is folded away until you click it.

**0.13**
- **Audio Leveler** is now called **Audio Tools** (its settings are kept).
- **Audio Levels:** playback devices, recording devices and a new **Apps** column (each program's own volume, as in
  the volume mixer) side by side. Click any name to give it volume up, volume down and mute shortcuts — keys or
  controller / wheel buttons — with a step you set (10 by default).
- **Mini Mirror groups:** name a set of mirrors (e.g. one game's), give the group a show / hide shortcut, and drag
  mirrors into it. New mirrors go at the top.
- **Performance:** the log is kept a year by default now (it's about 50 MB a year), or 2 years or forever; the tiles
  and live graphs pick up the last 10 minutes from it after a restart. The Speed Test graph has a fixed top (2,500
  Mbit/s by default, in its settings), Data used explains itself when you hover over it, and the Network Tests and Speed
  Test graphs are a third shorter.
- **Gaming:** **Browse Folder** (was Open the folder) and a **Settings** button beside it that opens Settings → Gaming at
  the recording options. Applets can now open their own Settings page.

**0.12**
- The tabs (and the Settings page's tabs) stay on one row however many there are, and scroll sideways when they don't
  fit: drag the bar under them or use the mouse wheel over them. The open tab is always scrolled into view.
- Settings save as soon as you change them, and now say so: a green **✓ Saved** appears beside the checkbox, option,
  list, slider or text box you changed, then fades.

**0.11**
- **Sensors**, a new tab: LibreHardwareMonitor's sensors (until now at the bottom of Performance) with their history
  windows, the setup guide and the sensor log, now with a legend in the history graphs. Settings → Sensors has the web
  server's address and the log. The connection to LibreHardwareMonitor is shared, so Performance's Temperatures tile
  still shows the CPU. Existing settings and sensor history carry over.
- **Rename any tab:** right-click it and type (Enter saves, Esc cancels), or Settings → General → Tab names.
- Performance: **Speed Test** shows download, upload and latency side by side, with the last test's time, server, data
  used and the next test aligned underneath; the caption under the graph is gone. **Storage** moved below Speed Test.

**0.10**
- **Gaming**, a new module:
  - **FPS overlay** over games: Simple (the frame rate), Medium (frame rate, frame time and video memory with small
    graphs) or Advanced (adds the 1% / 0.1% lows, GPU, CPU and memory, bigger graphs). Drag to move, drag a corner to
    scale, lock it so clicks go through; left or right justified; background colour and opacity; text colour.
  - **Recording** of a monitor, a region or a game to MP4 with the graphics card's encoder (NVENC, like the NVIDIA app),
    H.264, HEVC or AV1, in editable High / Medium / Low profiles (up to 500 Mbit/s); the PC's sound and optionally the
    microphone; SDR or HDR10 on HDR monitors.
  - **History:** each game's frame rates and hardware logged a second at a time, with per-game session graphs.
    Games are recognised by themselves and can be given friendly names.
  - Shortcuts for recording, showing the overlay and changing its mode.
  - Frame rates come from Windows' event tracing, like PresentMon: nothing is injected into games. Needs one-time
    membership of Windows' Performance Log Users group (the tab offers to add you).
- Building one module on its own (not the whole solution) now puts it in the app's Debug `modules` folder.

**0.9**
- **Modules:** each applet is now its own module, installed in its own folder under `modules\` next to the app with its
  translations and its README. The app loads every module it finds there (switch them on or off in Settings → General
  → Applets as before), so modules can be added without changing the rest. Shared code is in DaisysApp.Core.
- **Template** module: a hello-world example, installed but off, whose README is the guide to writing a module.
- Each applet's documentation moved from this README into its module's README (linked from its section here).

**0.8**
- **Languages:** the whole app is translatable, with Spanish, French and Portuguese included. Pick one in Settings →
  General → Language. Translations are plain JSON files in each module's `lang` folder, so anyone can add a language.
- Update banner: "Running version" instead of "You have".
- Performance: **Speed Test** uses the nearest speedtest.net server (Cloudflare's had started refusing with "too many
  requests"), falling back to Cloudflare. Click a host in **Network Tests** to highlight its line in the graph.
- The tab strips wrap onto a second row when the tab names don't fit.

**0.7.3**
- Performance: fixed games stuttering while the Performance tab was open (even minimized, and with the network cards
  off). Since 0.7.1 the process list grew to match the taller side column, showing about three times as many rows,
  all redrawn every second, which doubled the app's CPU use. It's back to its 0.7 height.

**0.7.2**
- Performance: **Network Tests** and **Speed Test** each have an on/off checkbox; off, the card dims and sends nothing
  at all.
- **Speed Test** has its own gear: schedule (every 5 minutes to once a day), test length from 1 second (timed from when
  data starts arriving) and orange / red warning levels for download, upload and latency. A test fails, red, after 10
  seconds without data or an answer, and failures are kept in the history.

**0.7.1**
- Audio Levels: click a volume's number to set it to 100.
- Performance: **Network tests** (ping each host once a second, graphed; hosts set with the gear) and **Speed test**
  (download and upload on a schedule, every 10 minutes by default, graphed over 24 hours) between Storage and System.

**0.7**
- Audio Leveler: **EQ Wizard**. Measures each speaker's response with a mic (with its calibration file, e.g. the
  iMM-6's) and sets a per-speaker EQ, in Voicemeeter's bus EQ or through Equalizer APO. Levels files now keep the EQ
  too.

**0.6.4**
- Mini Mirror: fixed a crash a few seconds after creating a mirror on an HDR display (the HDR capture call was made
  with bad arguments). Mirrors that were saved also crashed the app at every launch.
- Mini Mirror can no longer stop the app from opening: if screen capture crashed the last run, the mirrors aren't
  started (and HDR conversion is switched off) until you click **Start mirrors** on the Mini Mirror page.
- Error log: Settings → General → Files → **Error log** opens it. It now also records errors on background threads,
  and a note when the app closed unexpectedly last time.

**0.6.3**
- Performance is the first tab and USB Monitor the second.

**0.6.2**
- Performance: warning levels — tiles turn orange / red, over-limit processes are highlighted, levels set from each
  history window's **Warnings** button and the process list's gear (with the list's refresh rate, down to 0.5 s);
  levels drawn on the graphs; CPU cores tile moved to the start of the second row; legends back on the graph's title
  line; process groups indented; fixed the process list not keeping its sort order (it looked frozen).

**0.6.1**
- Performance: processes split into Apps and Background processes; RAM / VRAM columns; GPU engine column; centred
  headers; Storage above System; the Disk tile shows the busiest disk (an average over all disks hid one disk being
  flat out); history windows: clickable legend highlights a line, CPU and GPU power on one graph, the summary always
  in view, a Close button.
- Performance: every LibreHardwareMonitor sensor in a **Hardware sensors** section (grouped by hardware, filtered by
  kind, value / min / max, history per sensor or per piece of hardware); temperatures, fans and power logged; a **Get
  more sensor data…** banner and setup guide when it isn't running; its address in Settings → Performance.

**0.6.0**
- New **Performance** applet: live tiles for CPU, GPU, memory, video memory, disk, network, temperatures and CPU
  cores; process list; system and storage details; a background log with history windows (date ranges, peaks,
  summary, busiest processes, CSV export); CPU temperature from LibreHardwareMonitor, GPU sensors from NVIDIA's driver.
- Mini Mirror: fixed an error that could appear while picking a region.

**0.5.0**
- New **Audio Levels** applet: a live volume slider and mute for every playback and recording device.

**0.4.0**
- Audio Leveler: the speaker map is a grid (5 × 5 by default, set in Settings → Audio Leveler) and speakers can be
  dragged anywhere on it to match the room, saved per device; the microphone is chosen in the wizard (the Microphone
  card is gone from the page); **Auto-level…** is now **Level Wizard**; Load / Save / Reset levels are icon buttons.
- Mini Mirror: **Ctrl+Shift+F8** starts a new mirror without leaving the game (the selection never takes the focus,
  so games don't pause); HDR monitors are converted to SDR so mirrors aren't washed out.
- The window title shows the version (Daisy's App v0.4).
- Global shortcuts no longer use a hidden window (which could take the focus from a game).

**0.3.0**
- New **Resizer** applet, ported from Resize Rabbit: window size/position profiles and groups, global shortcuts,
  tray menu, script / Stream Deck pipe (same name as Resize Rabbit's), process watcher, import from Resize Rabbit and
  Resize Raccoon.
- New **Mini Mirror** applet, ported from the MiniMirror SimHub plugin (no SimHub needed): live always-on-top mirrors of
  any screen region (rectangle or circle, across monitors), window size and zoom, opacity, frame rate, lock,
  click-through, duplicate, Alt-drag snapping, show / hide shortcuts that can be a keyboard combination or a wheel /
  controller button, tray menu, optional hiding from screen capture, import from the SimHub plugin.
- Applets can add their own submenu to the tray icon's menu.
- Global shortcut handling (and the shortcut box, now with controller buttons) moved to `Shared/Hotkeys/`.

**0.2.2**
- Applets: each applet lives in its own folder under `src/DaisysApp/Applets/`, is found automatically, and can be
  switched on or off in Settings → General → **Applets** (with **Restart now**). Code shared between applets moved to
  `src/DaisysApp/Shared/`.

**0.2.1**
- Settings: **General** is the first page, with **Updates** second; tool pages follow in tab order.
- Audio Leveler: **Save levels… / Load levels…**.
- Audio Delay: **Save delays… / Load delays…**.

**0.2.0** — first release of the combined app
- Shell: tabs (Audio Leveler, Audio Delay, USB Monitor, Settings), system tray, start at sign-in, dark/light/system
  theme, GitHub auto-update.
- Audio Leveler (from MCAL): levels stored in Voicemeeter's bus EQ (persist without the app), auto-level wizard
  (Baseline / Leveling / Verifying, 10 dB cut cap, subwoofer handling, live readings, mic clipping recovery), mic level
  control, full speaker names with Voicemeeter **Out** channels, Voicemeeter check banner.
- Audio Delay (new): syncs two Voicemeeter outputs with output delay, including virtual/VBAN outputs.
- USB Monitor (from USB Mon): newest first, Status column third, Connected/Disconnected, one-page device details,
  resizable columns.
