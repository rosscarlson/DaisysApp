# Daisy's App: version history

What changed in each release, newest first. (The [README](README.md) describes the app as it is now, and each
module's guide describes that module.)

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
