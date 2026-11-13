# SimEffects → Analysis

*A tool of [SimEffects](../README.md) (since 3.23.1). Its settings file is `Analysis.json`; its laps are in
`AnalysisLaps.jsonl` beside it, its races (since 3.28.1) in `AnalysisRaces.jsonl`; the Race Analysis' choices (since
4.5.0, Forza Analysis' before) in `ForzaAnalysis.json`; the races' videos (since 4.8.0) in `AnalysisVideos.json`.*

Your car, tyres and race from the game's telemetry, as blocks (each an overlay over the game: SimEffects →
[Overlays](../InputOverlay/README.md), since 3.33.0), and every lap you drive kept
by game, track, car and class, from the SimEffects profile in use's game: since 4.10.0 every game it reads (Forza
Horizon 4, 5 and 6, Forza Motorsport 7 and 2023, iRacing, Assetto Corsa, Assetto Corsa Competizione and Assetto Corsa
EVO; **[Games](#games)**, below; Forza Horizon's only before, with iRacing's field in the **Leaderboard** overlay since
3.28.2). Since 4.5.0 each race is logged and analysed here (the **Session** card, below; Session Analysis until 4.13.1, Race Analysis until 4.6.3,
Gaming → Forza Analysis before).

The tab, top to bottom: the **Session** card and the session analysis' others; Best Times; **All Sessions** (Laps and
Races until 4.13.2; one table since 4.13.0); **Known Tracks** (Discovered Tracks until 4.13.1) beside **Racing Statistics**
(there since 4.13.1). The Map card is hidden since 4.13.2 (`AnalysisView.MapShown` brings it back). Current Location is a
small window since 4.13.1, from the compass on the Session card's title row (its words until 4.13.2). The blocks
(Widgets, a card here until 4.13.0) are only on SimEffects → [Overlays](../InputOverlay/README.md) now.

## Using it

- **ON / OFF** (top left): records laps (until 3.33.0 it showed the widgets too; they're Overlays' now). It's kept per SimEffects profile, and starts on in every game's (since 4.10.0; Forza Horizon's before). The blocks on the tab show the
  game's data either way. The **shortcut** button beside it (since 3.24.3) sets a key or a controller button for it
  (also in Settings → Shortcuts).
- **Video** (since 4.8.0; right of ON, the shortcut button after it; the same in every profile; since 4.16.1 dimmed as
  ON is while the profile is stopped): records each race
  with Video → [Recording](../../Video/Gaming/README.md)'s settings (what its Record card has chosen: monitor, region
  or game, quality, sound; a region is the one picked last), from the race clock starting until the race's log ends
  (the finish, a restart, free roam, the data stopping). A race not kept (no lap or result) has its video deleted
  with its log. Greyed out while Video → Recording is switched off; nothing's recorded while you're recording
  yourself. The videos stay in Recording's folder (`Videos\Daisy's App`), shown in Compare's **Video**.
- **Units** (on the Profiles tab's **Profile** card since 3.28.0, the tab's top right before; the same in every
  profile): **System**, Imperial / Metric: torque in lb·ft or N·m, boost in psi or bar, distances in miles or km (power
  is always hp); **Temperature**, °F / °C: the tyres (since 3.24.0; before, they followed the units). Both are also in
  Settings → SimEffects → Analysis, with **Tyre colours** (since 3.24.2): where green, orange and red start, in °F or °C
  (60, 99 and 116 °C at first; below green, no colour), and (since 3.30.2) **Start radius (m)** under Tracks: how far
  from a track's start (or a tagged place) a race can start and be on it, 500 m at first (750 before 3.30.2, which
  found another event's start 700 m away). Since 4.10.14 **Delete Incomplete Race** under it (on at first): a race
  left before its finish (quit, restarted) has its session and video deleted; off, it's kept and its session's name in
  the Session card ends "INCOMPLETE" (in AnalysisIncomplete.jsonl, in no table or stat). Only a race against others
  (Rivals, time trials and practice aren't) past its first 5 s, and not one that drove all of its track's known laps
  (a finish missed); in iRacing and the Assetto Corsas, a race session left (not a tow).
  Since 4.13.3 **Left Foot Braking** under it (off at first): throttle and brake together only count (the time split's
  Both, a brake zone's "throttle on too", the Both pedals mark) when both are over 40%; less is braking with the left
  foot. Switching it analyses the session shown again.
- **Folding** (since 3.28.0): every card has a chevron at its top right that folds it to its title, or opens it; kept.
  Since 4.7.0 a row
  of cards side by side has a second, blue chevron after the last one's own: it folds every card in the row, or (all
  folded) opens them.
- **Paused** (since 3.24.0): in the pause menu, a menu or a replay Forza sends zeros; the blocks and widgets keep what
  they showed last instead, and the graphs stop, until you drive again.
- **Cars by name** (since 3.24.0): Forza sends only a car's number; Analysis looks it up in the list of the game's cars
  (Horizon 4, 5 and 6, Motorsport 7 and 2023: the profile's **Version**, or the one running), so the tables read "2017 Nissan
  GT-R". A car newer than the list shows "Car n" until you name it (Name Car). The other games name their cars
  (iRacing's "Global Mazda MX-5 Cup"; the Assetto Corsas' model, spelt out: "bmw_m4_gt3" reads "BMW M4 GT3"), kept as
  they're seen (since 4.10.0).
- **The blocks** (overlays only since 4.13.0; on this tab from 3.24.0, in a Widgets card from 4.5.0), with Forza's
  names for each row:

  | Block | Layout |
  |---|---|
  | Tires | A sports car from above, each tyre's temperature beside its wheel (TireTemp…: FL, FR, RL, RR); the tyre and its temperature green, orange or red by how hot it is (since 3.24.2) |
  | Performance (3.24.0) | Power, Torque and Boost, each a graph of the last minute with its value now and its peak since the car changed (a dashed line); boost below 0 (vacuum) under a line at 0 |
  | Timing (3.24.0) | Current, Last, Best (this session's: since the app started, or Forza's best of the race), PB (your best on this track in this car) and PBC (since 3.24.2: in any car of its class, from every lap kept), large, for reading while driving; "—" until there's one; Last in purple when it's your PB, green when it's the session's best |
  | Leaderboard (3.28.2) | iRacing only (Forza sends only your own place: "No field from this game"). The cars around you, yours in the middle on the accent (at the front or back, the first or last ones), a line each: Pos, # (car number), Driver, **Gap**: each car's gap to the car directly ahead of it, so the gaps are between the cars, not to you (the leader has none). In a race once it's started the order is live, from how far round each car is (laps done + its place on the lap), and a gap is iRacing's estimate of the time between the two (CarIdxEstTime, plus a lap of the class's estimated lap time per lap between them), "+1.4", or "+1 L" a lap or more; before the start and in practice or qualifying, iRacing's places with the gaps between best laps ("+0.352"). A car in the pits is faded; the pace car and spectators are left out. How many cars: Settings → SimEffects → Analysis → Leaderboard → **Cars** (10 at first, 3 to 20). Its columns can be hidden like any widget's rows |
  | Track Map (4.6.3) | The track as your best lap on it went (grey, read from that race's log: Forza's X and Z, the game's own metres, so it's the track's real shape), the line you're driving now (the last few minutes, in the accent colour) and the car (a white dot), north up; on a track without a lap kept yet, just your line as you drive it. Rows: **Best lap** and **Your line**. Its overlay, as every block's, is made on Overlays |
  | Position (3.24.0) | "3 / 8": your place, of the most cars seen in this race or on this track (Forza doesn't send how many); beside it (since 3.24.2; the Lap block before) "2 / 3", the lap, of the most laps a race on this track has had (just "2" until a race there is recorded), in a race with laps, else Position alone (a sprint's **Done**, from 3.27.1, went in 4.6.4: it didn't work). Since 4.5.0, with no place (not in a race) the block (and its overlay) is blank |

  Tires (since 3.24.0): the car smaller and faint, the temperatures larger. Timing and Position use Bahnschrift
  (Windows' own DIN, close to the type Forza's screens use) and grow with their widget.

  Forza doesn't send a car's name, only its number: **Name Car** in Current Location (its second row since 4.6.4; under
  the Vehicle block before 3.30.2) names the car you're in.

  The Vehicle and Laps blocks, and their widgets, went in 3.30.2 (Timing, Position and Current Location show what they
  did); a widget of either that was open isn't shown again.

- **Widgets** (overlays since 3.33.0): a block's widget is made, ticked, edited and deleted on SimEffects →
  [Overlays](../InputOverlay/README.md), with its rows, title, background and shortcut there (the pop-out and shortcut
  buttons on the blocks went). They read from here (Analysis switched off in Settings → General → Modules: no block
  overlays). A widget open before 3.33.0 is in Overlays' list, and shows while Overlays is on (switched on in the
  profiles Analysis was on in). Still Core's widget in its own card (`OverlayFrame`): drag, stretch, Background, On top,
  Title, Click through; while it's edited each cell shows its rows as checkboxes (Tires: one beside each temperature).
- **Current Location** (since 4.13.1 a small window, from the compass right of the Session card's trash can; a card at the
  top left of the tab since 3.28.0 before; since 4.6.4 two rows and a line): the name box has the track the car
  is at (in a race, its track; else the one starting nearest within the start radius), or **Unknown** (greyed out while
  the game sends no place; in the game, picked whole to type over), with Save, Add New and Export as icons beside it;
  the second row is the car's name with Name Car's icon (greyed out until you drive one); under them where the car is
  ("x, z") and how far from the track's start. Since 4.10.8 in Forza Horizon 4 and 5 a **Map** box under the name box:
  the map the car is on (FH5: Mexico, Sierra Nueva, Hot Wheels Park; FH4: Britain, Fortune Island, LEGO Valley).
  Their maps share coordinates, so a track is on one map and only the map picked is searched. Forza doesn't say which
  it is: pick it, and each map's ground (the lowest the car's been in each 25 m square) is learnt as you drive; on
  ground that's another map's (16 squares, or 8 where the picked map's ground doesn't fit) it switches by itself, with
  a notification. A map picked by hand takes the ground learnt since the last switch (driven before it was picked).
  Since 4.13.4 the tracks say which map it is, before the ground: a race starting where none of the picked map's
  tracks (or tagged places) do is matched on the game's other maps, the nearest found, and that map is switched to
  (two tracks starting there are told apart by the lap, on that map); in free roam, within 30 m of another map's track
  or tagged place (its height too) with none of the picked map's within the start radius, it's that map. While a
  race's track is known its map isn't switched by the ground (it switched to Mexico halfway round Sierra Nueva's
  Cadcada Fuerte, and every race after was a new track); Current Location shows the nearest of another map's tracks
  when the picked map has none near.
  (Before 4.6.4 the track's name was also above the box, in large type.)
- **Name Track** (the name box; "New track" in 3.27.1; since 4.5.1 in Current Location's card; since 4.6.3 the box,
  **Save** and **Export** on one row; since 4.6.4 the buttons icons): at a known track the box has its name: type over it and **Save**
  (or Enter) to rename it. Elsewhere, type a name and **Save** to tag the place the car is as a track ("Saved …", and
  it's then where you are). **Add New** (shown at a known track) tags the place as another track all the same (two
  events starting near each other): where the start radii of two overlap, a race is on the nearest (since 3.31.1).
  Every button in the card (Save, Add New, Export, Name Car) leaves the name box focused and selected for the next
  name (since 3.31.1). Tag where an event starts (its marker): the next race started within 15 minutes is
  on it wherever its grid is (since 3.24.7), and later a race starting within the start radius of it (the nearest such place),
  shown in Track from its first second; its start and lap length are learnt from that race. Known Tracks lists it
  with "no race yet" until then. If that race is a track already known, the track takes the name (if it had none) and
  the place goes. (Before 3.27.1 a **Name** button did this, and the box asked for a name for each new track.)
- **A race on a new place** (no tag) adds "Track n" when its first lap ends or the sprint finishes, with a
  notification; name it in Name Track while there, or in Known Tracks.
- **Export** (in Name Track since 3.28.0; the top right before, since 3.24.7): saves every track and named place to a
  JSON file (game, name, start X / Y / Z, length in metres, laps, cars, when found), to share or keep. No list of the
  games' race coordinates is published, so it's built by driving.
- **Map** (hidden since 4.13.2, may come back; since 3.31.0; since 4.6.3 just above Known Tracks): the game's tracks as dots where their races start (the Game box picks
  which game's), coloured by type: road (blue), dirt (orange), cross country (green), street (purple), specials ("The
  Goliath"…, red), other (grey; unnamed). The type is guessed from the name (… Cross Country; … Scramble or … Trail;
  … Circuit or … Sprint; "The …"; any other name a street race), or set by hand in **Type** under the track picked.
  Form: a circuit if a race on it had laps or its name says Circuit or Scramble, else a sprint.
  - **Map image…** picks an image of the game's map (PNG, JPG, BMP or TIFF; copied to `AnalysisMaps\<game>.<ext>`
    beside the settings; over 8192 pixels across it's read at 8192). Then line it up: pick a track, **Place on map**,
    click where its event is on the image (Esc cancels); placing it again moves it. Two tracks far apart (200 m at
    least each way) line it up ("Lined up from 2 tracks"), each axis fitted on its own (the games' maps are north up);
    more make it truer. **Clear line-up** forgets them. Without an image (or until it's lined up) the dots are spread
    over a plain ground, north up.
  - The wheel zooms round the pointer, a drag pans, **Fit** shows the whole map. Beside it: **Search** (the list and
    the dots; Enter jumps to the first), **Type**, **Form**, **Raced only** (laps or races kept), **Names** (each dot's
    name), how many show, and the list. Clicking a dot or a name picks the track: it's larger with its name, and its
    type, form, length, place, laps, races and best lap show beside the map, with **Best laps here** (by car and class),
    **Races here** and **Laps here** under it. A search or the list centres the map on it.
  - Your car is a white dot (the accent round it) while the game sends where it is.
- **Classes** (since 4.13.1) run **X, R, S2, S1, A, B, C, D** top to bottom (Motorsport's P after X, its S after S1),
  other games' A to Z after them: the Class column's sort and filter list, Best Times' Class box.
- **All Sessions** (Laps and Races until 4.13.2; since 4.13.0; Best Laps, All Laps and Races, a card each with their own boxes, before): one
  table; the drop-down at its top right picks **All Laps**, **Best Laps** or **Races**, kept. A right-click on the
  **Game**, **Track**, **Car** or **Class** header drops down that column's values, a box each: tick one or more (a
  funnel in the header while it's filtered; **Clear** takes them off; the filters stay when the drop-down changes).
  Each list shows what the other columns' filters leave.
- **Best Laps:** the best lap of each game, track, car and class, with its PI, its **Date** (since 3.31.1: the day that lap was set, "Oct 31, 2026"), how many laps and when last driven.
- **Races** (since 3.28.1): every race finished (a circuit's last lap, or a sprint's finish; a race quit isn't kept),
  newest first: when, game, track, car, class, laps, its time, **Start** and **Finish** (the place the car was in on
  the grid and at the line, of how many: the most cars the race or track has shown) and **Gained** (places gained, or
  lost: −). Its numbers are Racing Statistics' (over it before 4.6.3).
- **Racing Statistics** (since 4.6.3; a line over Races before; right of Known Tracks since 4.13.1, of Current Location
  from 4.7.0): over every race finished, a tile each: Races (and on
  how many tracks), Wins, Podiums and Top 5 (each with its share of the races and a bar: gold, silver, bronze), Average
  finish (and the best), Average start (and the best), Places gained a race (green up, red down; how many races went
  up and down) and Finished ahead (the share of races finished ahead of where you started). Beside them **Finishes**:
  a bar per place, 1 to 10 and 11+, the podium's gold, silver and bronze; under them **Last races**: the last races'
  places (since 4.13.2 one line of as many whole circles as fit the block's width, a full line reaching its right edge; 15, wrapping, before), newest right (a tooltip says when, and where you started); since 4.6.4 a click opens that race in Session
  Analysis. Finishes is as tall as the two rows of tiles (since 4.6.4).
  Forza sends only the player's own position (no other cars, no gaps), so the Leaderboard shows only iRacing's field.
- **Laps:** every lap, newest first: when, game, track, car, class, PI, the lap's number in its race (1 is from a standing
  start), its time and the gap to the best of its track, car and class. Columns sort with a click.
- **Best Times** ("Tracks" before 3.28.0, since 3.24.2): a tile per track, four across: its name and how many laps it
  has, its best lap overall, then the best of each class raced there with its car, the fastest class first (since
  3.28.0 only the classes raced; none: Overall alone). The tiles of a row are as tall as its tallest. The **search box** (top right, since 3.31.1) filters as you type: tracks whose name has the text, and on the others only the laps of cars whose name has it (a tile's times are then that car's).
  Beside it (since 4.6.3) **Track**, **Car** and **Class**: one track's tile, or only that car's or class's laps (and
  only the tracks it has laps on); before them (since 4.8.0) **Game**: All games, or only one game's tracks.
- **Known Tracks** ("Track names" before 3.28.0, Discovered Tracks until 4.13.1): a table, newest first (since 4.6.4) until sorted by any column with a click on its header: Name
  (type over it to rename the track), Game (its funnel filters to one game), Laps, Lap (its length), Location (the
  start's x, z; sorted, places near each other come together), Nearest (the track starting nearest it, and how far:
  sorted, the ones near each other come first; on the same map) and Found; since 4.10.8 **Map** (Horizon 4 and 5: the
  map it's on, picked in its row). **Delete** (top right) removes the track picked and its laps and races (since 4.5.1 several, picked with Ctrl or Shift).
- **Classes:** Horizon 6 added R above S2; its class 6 reads R (7 X), the other games' 6 X (since 3.24.2, until a real
  FH6 says otherwise). Motorsport's are D, C, B, A, S, R, P and X (since 4.10.0; Horizon's letters before); iRacing's
  are its car classes ("MX5 Cup"), ACC's GT3, GT4, GT2, Cup, CHL, ST and TCX (from the car's model); Assetto Corsa and
  AC EVO have none (no class, no PI).

## Session (Session Analysis until 4.13.1; since 4.5.0, Race Analysis until 4.6.3; Gaming → Forza Analysis before, a module of its own)

How smoothly you drive each race, as a driving coach (or a tool like Racing Labs) would look at it: mostly the
**brake and throttle, going on and coming off**, laps compared with your own or someone else's, contact with walls
and cars, and about what each habit costs a lap. Its cards come under the Widgets, each folding; the page has only
labels and short tooltips.

### Where the races come from

While Analysis is on, each race is logged (`RaceLog.cs`) from its game's telemetry while the race clock runs: a row
for every frame (Forza's 60 a second): steering, throttle, brake, clutch and handbrake, speed, lateral and
longitudinal g, the lap and the time into it, distance, where the car is and the fastest suspension movement (since
4.6.10 the engine's rpm, its maximum, the gear and the power, for Shifting), and a `# car,` line naming the car. A restart, or the next race of a series, starts another log; free roam, or the data
stopping for 5 s, ends it. (Since 4.10.10) Forza Horizon's race clock also runs in free roam (where a fast travel
lands, and after a race), its distance driven 0 there: a race is only one from behind its line (below 0) or once
driven, and over once the distance is 0 again for half a second.

A log is kept only when the race has a lap or its result kept (the tracker's laps and races then carry its file
name, `Session`, in `AnalysisLaps.jsonl` and `AnalysisRaces.jsonl`): driving around and a race quit before a lap
are deleted, 15 s after the next frames come without the race being kept (or when the app closes). The logs are CSV
files in `%LOCALAPPDATA%\DaisysApp\ForzaSessions\` (imported ones in its `Imported\`), the format in Core's
`Shared/SessionLog.cs`. Until 4.5.0 SimEffects → Overlays' Inputs overlay logged sessions while it showed, races or
not; those older files aren't listed (they name no race), and stay in the folder.

### Picking a race

- **Session** (the Session card's title row): every race with a log, newest first: track, car, when, best lap (since
  4.7.0 other drivers' imported ones too, after them; the same list and pick as Compare's Session 1). The export icon (since 4.7.0; Compare's **Export…** before)
  saves Session 1's lap, or the race, as a file to send to someone. The trash can (since 4.10.12) deletes
  the session picked and its race video: a second click on the red ✕ confirms.
- **Analysis Manager** (since 4.12.0, the Session Manager until 4.13.3; the wrench at the right of the Session card's title row): a window of every
  session: date, game, track, car, kind (Race, Incomplete or Imported), laps, best, and what it takes on disk (its log,
  its race video, both); at the top right, the totals as tiles (sessions, data, videos, total, what the filters show,
  free space on the videos' drive with its percentage and a bar: amber under 20%, red under 10%). A click on a header sorts by it (newest first at first); since 4.13.1 a right-click on **Date**
  (by day), **Game**, **Track**, **Car**, **Kind** or **Video** ticks what to show, as on the laps table (boxes in the
  headers before); since 4.13.2 Track's list says how many sessions a track has when it's more than one. **Ctrl** or **Shift** picks several (since 4.13.1): **Delete** takes them all (one Confirm?), the
  rest need one. The buttons are icons since 4.13.2, their names in their tooltips. **Recording Options** (since 4.13.1) goes to Video → Recording. The
  controller icon left of it (since 4.13.3) binds **Previous Event** and **Next Event** to a key or a controller button
  (Core's shortcuts; also in Settings → Shortcuts). **Load** (or a double-click, or Enter)
  analyses the session behind, the window staying open; **Show in Folder** and **Show Video in Folder** open
  Explorer with its log or video picked; **Delete** deletes it and its video (a second click on Confirm?; its copy on
  YouTube stays). "Missing": its video was linked but the file's gone.
  - **YouTube** (since 4.15.0): the YouTube mark left of the controller icon opens the YouTube window: the app's
    YouTube account (Core's, for any module: your own Google Cloud project's **OAuth client ID** and **Client secret**,
    a Desktop app client with the YouTube Data API v3 switched on; a link to where one's made; **Sign In** in the
    browser, **Sign Out**), then **Upload each race's video** (when its recording is saved, once the race is kept),
    **Upload as** Unlisted (at first), Private or Public, and **Delete the video file once it's uploaded** (watched from
    YouTube; no room taken). Kept in `AnalysisVideos.json`. Named from the session: track · car · best lap · game, the
    details in its description; category Gaming. One at a time, in 8 MB pieces (a dropped connection picks up where it
    was); one waiting when the app closes goes at the next start. A **YouTube** column says *On YouTube*, *Uploading
    n%*, *Waiting* or *Failed* (why in its tooltip; the link when it's up). With rows picked: **Upload to YouTube**
    (those with a file not up yet; not signed in, it opens the window), **Delete Video Files (kept on YouTube)** (those
    on YouTube with a file; Confirm?), **Open on YouTube** (one). A Google project that YouTube hasn't audited has its
    uploads made private, which can't play in the app; Google's sign-in for a project in testing runs out after a week
    (sign in again).
- **A click** on a row of the All Sessions table, or a time on a Best Times tile, opens its race here with that lap
  in Compare's **Session 1** (a race: its best lap). Since 4.13.0 the table's rows leave the page where it is, and since 4.13.2 the table
  is held in its place on screen while the cards above change height as the session loads (a Best Times tile still
  scrolls up to it). What's in **Session 2**
  stays (if it's on the same track), so a click changes your run against the one you're comparing with.
- **Right-click** one of those rows: **Analyse**, or **Compare With** (it goes in Session 2).

### The cards

- **Session** (Session Analysis until 4.13.1): the date, car, length, laps and best, the brake zones and throttle applications; then the
  **scores**, 0 to 100 (green 85 and over, amber 65 and over): Overall (the others together, the brake release
  counting most), Brake application, Brake release, Throttle application, Throttle release, Steering, Consistency;
  the **time split** (full throttle, part throttle, braking, both together, coasting); and **key numbers**: the
  average brake peak, application time, release time and trail braking; coasting from brake to throttle; throttle
  to brake; throttle application time; throttle stabs; steering corrections a minute; impacts and crashes.
- **Analysis on screen** ("Compare" until 4.13.1, and still Compare in this guide and the code; since 4.7.0 also the Trace, a card of its own before: with Session 2 at None the chart is the session
  alone): two rows of picks (This and With before 4.7.0). **Session 1**: the race analysed (since 4.7.0 the same pick
  as the Session card's box; 4.6.5–4.6.10 a race of its own): any race, yours or another driver's (imported), then the
  whole race or one of its laps, each with its time, the fastest in bold with a green *★ fastest*. **Session 2**:
  None, or only races on Session 1's track (any car; yours, then other drivers'; before 4.6.5 every race, the same
  track's first): the same track, or for one with no track (someone else's) a start within the start radius of
  Session 1's start or a lap's; then its lap. Since 4.13.0 never Session 1's own session: *-- No Matches --* when
  that leaves none; since 4.15.0 Session 1's own session is there when it has two laps or more (another of its laps to
  compare with), so a track with one session can still be compared. **Compare With** from the table puts a race in Session 2 even if it's elsewhere, or another lap
  of Session 1's own. The session boxes are 140 px wide (their lines whole when open, the title as a tooltip; since
  4.13.0 an icon after a session with a race video, the date without the year), the lap boxes a fixed width, and each
  row's channels are at its right end (since 4.9.1), so both rows line up.
  - **Find a session** (since 4.13.0): a click on **S1** or **S2** (the words Session 1 and Session 2 until 4.13.2, now a blue outline) opens a small window of the
    sessions that slot can have: Date, Game, Track, Car, Kind, Laps, Best and Video, sorted by any header (newest first
    at first), Game, Kind and Video filtered from a right-click on their headers, **Search** by track or car. A row
    picked goes in the slot straight away, behind; **Select & Close** (or a double-click, or Enter) and **Close** close
    the window, keeping what's picked.
  - **Combine** (since 4.13.0; left of Speed, Session 1's row only, kept): one timeline per session, every channel on it
    (Delta on Session 1's), instead of a lane each. The lanes are 12 px apart (6 before 4.13.0).
  - Session 1's brake zones are shaded and numbered (a click on one picks it; since 4.10.3 on its number); its laps' starts are marked; marks at
    the top show what's worth a look (impacts violet, crashes pink), each with a tooltip. Since 4.13.3 three small
    buttons in that row at its top right, no taller than it: a filter (a drop-down of the marks' kinds to tick, kept;
    its icon blue while some are hidden, here and in the Video's timeline), **Previous Event** and **Next Event**: the
    cursor (from where it is, else the start or end) to the next mark shown, the video and Track Map with it, the
    mark's tooltip shown until the cursor moves or the mouse comes onto the chart. The axis' values are twice as far
    apart since 4.13.3 (up to five across). Since 4.15.0, along the distance, Session 1's time at each value is under
    it (to the hundredth zoomed in under 200 m), and the readout has the time at the mouse (both sessions'); the
    popped-out Video's timeline the same. A zone, throttle move or
    gear change picked anywhere (Brake Point, Brake Release, Throttle, Shifting) shows here with a line at it, Session 1 switched to the lap it's in if another was picked (the
    Trace did this before 4.7.0).
  - A lane each, along the lap's distance: **Delta** (the other's time less this one's at each point: above the
    line, this one is ahead; amber; a chip shows or hides it), speed, throttle, brake and steering.
  - Since 4.6.3 each channel has its colour (speed blue, throttle green, brake red, steering white; since 4.7.0
    clutch violet, hidden at first), Session 2's lines dotted (before, this run blue and the other orange). On each
    Session row (since 4.7.0; a row of their own under the picks, with the run's name, before; since 4.16.1 in a
    **Channels** drop-down with a dot in the colour of each shown, RPM and Gear too) a chip per channel: its
    box shows or hides that line (a lane goes when both its lines are hidden; Session 2's chips and **Delta** only
    while there's a Session 2), its
    name (or a click on the line, until 4.10.3) highlights it, bold, the others faded; again, back to all. Kept. The
    wheel zooms, dragging moves along, a double-click shows it all; the readout gives both runs' values at the mouse.
    Since 4.10.3 a click puts the **cursor** there (a white line; off the chart the readout, the video and the Track
    Map's dot are at it). Under the chart (since 4.10.3; a scroll bar since 4.6.9) the whole run drawn small
    (Session 1's speed, its laps' starts, the cursor) with a frame round the part the chart shows, the rest dimmed:
    drag the frame to move along, click elsewhere to bring it there; the wheel zooms, a double-click shows it all.
  - **Playback** (since 4.10.3, under that): **Rewind** (8×, 4×, 2×; 8× and 4× since 4.10.10), **Slow motion back** (½×), **Play** / **Pause**,
    **Slow motion** (½×) and **Fast forward** (2×, 4×, 8×): Session 1's run plays from the cursor, the cursor, readout, Track
    Map and Video with it, the chart moving on a page when the cursor nears its edge; its time and the run's at the
    right. A button again pauses; it stops at the run's end. The video's sound only at 1×; going back, the video
    shows fewer frames (each is decoded from the keyframe before it).
  - **Loop** (since 4.10.15): right-click the chart, the whole run under it or the Video's timeline for **Set Start**
    and **Set End** (a start after the end swaps them) and **Clear Loop**; the section is tinted between two lines.
    The Loop button left of Play (greyed out until both ends are set) plays it round from its start, lit while it
    does; the other speeds keep to it (going back, from its end again); Loop again stops. A new Session 1 clears it.
  - **Pauses** (since 4.14.1): a gap of a second or more in Session 1's log (the game paused, or a rewind) has an
    amber icon over it, above the chart, with how long it was. Hidden at first: cut out of the chart's time (a dashed
    line where it was), the clock and Delta, and playback jumps over it, the video with it. A click on its icon shows
    it (shaded, the eye open; the video plays through it), again hides it. Not kept: a session opened again starts
    with its pauses hidden. The Video's timeline has the same icons.
  - After Session 1's channels: the lap times and the gap, and *Not the same track?* when their starts are over 500 m
    apart or the laps' lengths differ by over 6%.
  - Under the chart (since 4.10.9 450 px high): the race's **Video** (since 4.8.0, below; half the row), the **Track
    Map** (an eighth, taller than wide) and the **friction circle** (since 4.13.0, the rest, with its own **Brake** and
    **Throttle**; a table of Corners, 4.6.9–4.12.0, was there; since 4.14.1 a large white dot on it at the cursor,
    moving as it plays, or at the mouse on the chart). Since 4.13.3 the gaps between them are grips: drag one
    to widen one side and narrow the other (120 px at least), kept.
    The Video and the Track Map each have a pop-out icon at their top right: a window of their own, moved and sized
    anywhere (a second one opens a little lower right); closing it, or the icon again, puts it back. Since 4.15.0, when
    Session 1 and Session 2 both have a video, Session 2's is beside Session 1's (S1 on the left, S2 on the right, each
    named over it, each with its own volume and **Mute**; Session 2's kept apart, muted at first) and the Track Map and
    friction circle make way, here and popped out; Session 2's follows the cursor at the same place along the chart (the
    same distance, or time). The same session with two laps shows both laps' moments.
  - **Video** (since 4.8.0; a placeholder in 4.7.0): Session 1's race as recorded (*No video of this race* without
    one: an imported race, or one raced with Video off), at the moment the mouse is at on the chart, or the zone or move picked (since 4.10.3 the cursor; not the mouse while it plays). It lines up by itself: the video keeps when its first frame was, the log when the race
    clock started. Popped out, its window has the tools under the picture: **Play** / **Pause** (playing, a white
    line follows on the chart and a dot on the Track Map), a slider along the video and its time; the race's own
    clock and lap time at the frame shown (to match against the game's on screen), how far it's been moved
    (**Sync**), buttons that move the race earlier or later in the video by a second, a tenth or a frame (the frame
    shown stays the same moment of the race), **Race starts here** (the frame shown is where the race clock starts)
    and **Auto** (as recorded). Kept per race. The video is read with Windows' Media Foundation (on the graphics card
    where it can, scaled to 1280 across), so AV1 and HEVC recordings play; since 4.8.1 with its sound while it plays,
    on the app's output (Settings → General → Audio). Since 4.10.9 a **volume** slider and **Mute** under the picture
    (popped out, right of its time), kept; since 4.13.3 they scale the video's own sound (the slider's square, so half
    way is about half as loud): 4.10.9–4.13.2 set the output's volume, which didn't change what was heard.
    Since 4.15.0 a race whose file's gone but is on YouTube (the Analysis Manager's YouTube, below), or an imported
    session that came with its YouTube link, plays from YouTube: YouTube's own player in Edge's WebView2 (Windows' own),
    lined up the same way (the sync tools too), playing along with Compare at 1×, ½× and 2× (put right when it's half a
    second out), else shown frame by frame; under Compare it leaves room at its top for the pop-out icon. Export needs
    the file. A private video, or one its owner doesn't let play outside YouTube, says so instead.
    - **Timeline** (since 4.10.9, popped out, under the tools; the window opens 1000 px wide): Session 1's chart as
      Compare's, alone: Speed, Throttle, Brake and Steering, each with its box (kept) and name (highlights it); the
      whole run under it with the frame (drag, click, wheel, double-click) and the playback buttons. Both are the same
      playback as Compare's: a click or play in either moves the cursor in both, the video and the Track Map; it plays
      on while the window's open, whichever tab the app's on.
    - **Export** (since 4.9.0; the icon right of Play, popped out): saves the video (asked where; Recording's folder and
      the video's name + *telemetry* at first) with the race drawn on it as it's synced, as an onboard video: one
      block at the bottom in the middle, Overlays' Dash dial (rpm, gear, speed) and its Inputs trace as Overlays →
      Inputs is set (the pedals, their bars, the speeds, the wheel), and the lap and its time at the top right. Before
      and after the race the video is as recorded. Written as H.264 MP4 with the sound (AAC), at most 3840 across; the
      icon stops it while it goes (the file's then deleted), the percentage beside it; done, Explorer shows the file.
  - The export icon in the Session card's title row saves Session 1 (a lap or the race) as a file with your name (the
    one set before 4.7.0 in the Driver box, which went, else Windows' user name) and the race's. **Import** (the icon in
    Compare's title row; **Import…** before 4.7.0) copies files like it into `ForzaSessions\Imported\`, where they're
    in Session 2, and puts the first in Session 2. Since 4.15.0 an export of a session on YouTube carries its link and
    where in the video it starts (`# video,link,seconds`), so whoever imports it watches it in step.
  - Speeds are in mph where the region isn't metric, km/h elsewhere.
- **Track Map** (since 4.6.3; a card a third of Compare's row until 4.6.9, now in Compare, right of the Video, a quarter of the row): the lines Session 1 and Session 2 took, from where the game says the
  car was (X and Z, 60 times a second, in the game's own metres): this run blue and solid, the other orange and
  dotted, north up, the start a white square. Both are in the same coordinates, so two sessions on a track lie over
  each other as driven, a lane apart or a metre. The mouse on Compare's chart puts a dot on each line at that point; a
  corner picked zooms there; the wheel zooms, dragging moves, a double-click shows it all.
- **Brake Point**, **Brake Release**, **Throttle** and **Shifting**, on one row with **Tips** at the right (since
  4.13.0; four across since 4.6.10; "Braking" before, with a drop-down for the two line-ups; "Brake zones compared"
  before 4.5.1), as tall as the Tips: every zone drawn over the others, the average dashed and the zone picked bold, lined up at the brake point,
  and at the release; Throttle the same for each time the throttle goes on.
- **Shifting** (since 4.6.10): a row per upshift (1→2, 2→3…) and one for downshifts (↓), each gear change a dot at the
  rpm it was made at: green at the right rpm, amber early, red held on the limiter, hollow at part throttle (not
  judged); a downshift grey, red when it sent the revs to the limiter. Since 4.7.0 the axis is in rpm (a share of the
  maximum before), from just under the lowest change (or shift window) to the car's maximum; each row's window to
  shift in is a green band from its best point (a line, its rpm beside it) to just short of the limiter, and the
  limiter a red band named **Limiter**. The mouse over a dot says what it was ("3→4 at 7,240 rpm · early"); a click
  shows it on Compare's chart. (4.6.10 had the share of upshifts at the right rpm and the time on the limiter beside
  the title.) Its tips are in **Tips** with the others. Races logged before 4.6.10 have no gears: *No gears in this
  race's log*.
- **Smoothness**, a card of every application and release's score over the race, went in 4.13.1. The **friction circle**
  (the car's g: sideways across, braking down, accelerating up; red braking, green on the throttle, grey coasting; the
  peaks under it) was left of it until 4.13.0.
- **Tips**: what the numbers suggest, each with about how much time it would save a lap, the most first; contact
  with walls and cars in red when it's too much: since 4.7.0 two crashes (hard hits: walls, mostly) or four impacts
  (cars, mostly) or more, else orange (the telemetry can't tell a wall from a car; before 4.7.0 red also for contact
  once a lap or over a second a lap). Since 4.6.3 in a box per colour, no titles: red first, then (since 4.7.0)
  orange, white, then green; and without the tip about small
  steering corrections (the number stays in the key numbers).

### How it's worked out (`Sessions/PedalAnalysis.cs`)

The log is resampled to an even 100 Hz (`Sessions/SessionData.cs`) and lightly smoothed (30 ms).

**Brake zones.** A zone is the brake above 5%, from the foot leaving the floor until the pedal is let go. If the brake
is let go for under 0.15 s and pressed again, it's the same zone. Zones that peak under 8% are ignored.

- **Application**: the time from 10% to 90% of the peak. Over 0.3 s for a firm stop (50% or more) counts as slow.
- **Spike**: the peak in the first half-second is more than 8% over the level then held (the 80th percentile of the
  rest of the zone).
- **Release**: starts where the pressure last falls below 90% of the level held. Its smoothness is the net travel
  divided by the total travel (jitter under 1% ignored), so 100% means one movement; pressing again by 3% or more is
  a re-application; a brake still at 30% or more 0.1 s before it's let go counts as *let go in one go*.
- **Trail braking**: the share of the release with the wheel turned 5% or more of its travel.
- **The scores**: the application score is smoothness², less for a slow build-up or a spike; the release score is
  smoothness², less for letting go in one go or a release under 0.15 s. Zones under 15% don't count, and the race's
  score weighs each zone by its peak.

**Throttle.** The pedal's turning points are found, ignoring wobbles of under 8%; each rise or fall of 25% or more is
an application or a release. Its time is 10% to 90% of the way; its smoothness is measured as for the brake, and
*lifted on the way* is a drop of 3% or more during the rise. A **stab** is on by 30% or more in 0.15 s, then off again
by 20% or more within 0.3 s.

**Laps** (`Sessions/SessionData.cs`): a run of the same lap number; complete when the next one follows and it started
at the line. Its time is the game's time into it at its end, plus what was left across the line.

**Contact** (`Sessions/Incidents.cs`), as the Shaker's Crashes and Impacts feel it: a crash is 3.5 g or more sideways
and lengthways together; an impact is a jolt, that g changing by 1.3 g or more within 50 ms, or a suspension hit of
1.5 m/s with a jolt of 0.8 g. Only above about 18 km/h.

**What it costs.** Contact: with two clean laps or more, the laps with contact against the clean laps' median;
otherwise the speed it took off and the time to get it back at about 0.4 g (Δv² / 2av), plus any time stopped after
a crash. The pedal tips' times are rules of thumb: a re-application 0.1 s, a stab 0.1 s, a snapped release, a spike
or a lift on the way 0.05 s, half of a slow build's time over 0.3 s, 0.3 s a second of coasting (over 0.3 s a zone)
or of both pedals, and 0.15 s a second at 100%. A lap's share is the race's over the laps its driving is worth.

**Shifting** (`Sessions/Shifting.cs`, since 4.6.10; the chart's rpm since 4.7.0 from the median maximum rpm the game sent). Each gear's ratio is the median rpm over speed while driving
steadily in it (over 8 m/s, the throttle on, no clutch); the engine's power curve is the power at full throttle by rpm
(1% steps of the maximum, near the top of what each step saw). The best point to upshift from a gear is the first rpm
(from 50%) where the next gear's power, at the rpm the change drops to, is at least the power now; if the power still
climbs, 97%. An upshift's rpm is the highest in the quarter second before it; it's judged only at full throttle (90%
on average in the half second before the lift): **early** 5 points or more under the best point (under 70% when the
power curve isn't known), **on the limiter** when it was held at 98% of the maximum for 0.15 s of the half second
before. A downshift **over-revs** when the rpm reaches 99% within 0.6 s. The time **on the limiter** counts stretches
over 0.1 s at 98% or more with the throttle at 80% or more (less the first tenth); in the highest gear used it's a
tip of its own (the gearing too short). The costs: a second on the limiter ≈ 0.4 s, an early upshift ≈ 0.01 s a point
under the best (0.15 s at most), an over-rev ≈ 0.05 s.

**Also:** steering corrections are turns back of less than 13° on Forza's 450° wheel, within a second; consistency
comes from how much the firm zones' peak, application time and release time vary (4 zones or more); the time split
counts only time within 3 s of something moving.

Not yet: sectors; wheelspin.

## Games

Since 4.10.0 Analysis follows every game SimEffects reads (the profile in use's **Game** and, for Forza, **Version**);
each game's tracks, laps and races are its own (the **Game** column and filters: FH4, FH5, FH6, FM7, FM8, iRacing, AC,
ACC, ACEvo).

| Game | What it sends | The track | Laps and races |
|---|---|---|---|
| Forza Horizon 4, 5, 6 | Data Out over UDP: the car's number, class and PI, the race clock, lap, place, tyres, power | Found by where its races start and how long a lap is (below) | The race clock (below) |
| Forza Motorsport 7 | The same as Horizon (no CarGroup) | As Horizon | As Horizon |
| Forza Motorsport (2023) | The same, and the track's number (TrackOrdinal, at the end of its packet) | By its number: added the first time, to be named (a notification) | As Horizon |
| iRacing | Shared memory: the track and layout (WeekendInfo), the session (practice, qualifying, race) and its laps, the laps done and their times, the place, the flags, the car and its class | By its id and layout, named as iRacing names it, its length iRacing's; on the Map by where it is on Earth | The game's (below) |
| Assetto Corsa | Shared memory: the track and layout, the session, laps and times, the place, the flag, the car's place in the world, tyres | By its id and layout (spelt out: "ks_vallelunga" reads "Vallelunga (Club Circuit)"), its length the game's | The game's |
| Assetto Corsa Competizione | The same, and a lap cut; no track length (learnt from the first lap) | By its id ("monza") | The game's |
| Assetto Corsa EVO | Its own pages (`acevo_pmf_…`): the same, and power and torque | By its id and layout, its length the game's | The game's |

**The games that run sessions** (iRacing and the Assetto Corsas) aren't followed by a race clock, as Forza is: a
**run** is the time the car is on track in one session (it starts as the car comes out of the garage or the pits, or
the session starts, and ends as it goes back, outside a race, or the session changes); each run is one race log (and
one video). A lap is kept when the game's count of laps goes up, with the game's time for it, if:

- it's a **whole lap**: it started at the line in this run (not the out lap, the grid to the line, or a lap the app came
  in on) and was most of the track's length (85% to 150%; with no length known, over 500 m);
- it **wasn't cut** (ACC and AC EVO say; iRacing and Assetto Corsa don't, so their cut laps are kept);
- the game gave it a **time** (within 3 s).

The log says why one wasn't kept ("not a whole lap…", "lap 2 cut…"). In a **race** the place it started in is the first
the game gives, and its result is kept when a lap ends under the **chequered flag** (the laps and time are the whole
laps'); a race left before it isn't kept. The Leaderboard's field is iRacing's only; the Position block's "of" is the
cars in the session, its laps the session's.

**What the analysis gets** (the Session card): every game's pedals, steering, speed, g and gear. The Track Map and
Compare's line: Forza's and the Assetto Corsas' place in the world; iRacing sends none, so its line is worked out from
the car's speed and heading, starting again at the line each lap (every lap of a track lies over the others; it drifts
a little through a lap). Shifting's best upshift points need the power curve: Forza and AC EVO send the power, the
others don't, so their upshifts are judged without it. Tyre temperatures: Forza and the Assetto Corsas (iRacing gives
them only in the pits: "—").

## The tracks it comes with

Since 3.34.0 Analysis comes with every FH5 and FH6 track (`Tracks.json`, Export's file, built in): added to your
tracks once, at the start after an update brings them (each time the file changes); one you already have (the same
game, and the same name or start) isn't added again, and one you delete stays gone until the file changes. Since 4.10.11 one an earlier file added is brought up to date
(its name, map and start; its races kept), and FH5's on Sierra Nueva and Hot Wheels Park come on their maps.

## How a track is known

(Forza Horizon and Motorsport 7. The other games say which track it is: [Games](#games).) Forza's Data Out says nothing
about the track. Analysis knows one by two things it does send:

1. **Where its races start:** the car's place (PositionX/Y/Z) when the race clock (CurrentRaceTime) starts.
2. **How long a lap is:** DistanceTraveled over the first lap.

Each game has its own tracks (laps and tracks from before 3.24.0 are Horizon 6's). A race starting within the start radius (Settings, 500 m at first; since 3.30.2, 750 m from 3.27.1, 150 before) of a known track's start, with a first lap within 6% (or 250 m)
of its length, is on that track (the nearest start that fits; since 3.31.1 not the closest in length), unless a place tagged for this race starts nearer (since 3.31.1); failing that, the place just tagged (within 15 minutes,
wherever the race starts), or one within the start radius (since 3.30.2; 750 m from 3.27.1, 300 before; the nearest); otherwise it's a new one. So two routes from the same place (common in Horizon) are two tracks, and a sprint is a
one-lap track. Since 4.10.12 the place the car was in free roam just before a race (where its event was entered;
Horizon then puts it on the grid, often hundreds of metres off) comes first: within the start radius of where a track's
event is entered (a tagged place; a track keeps its place as that after its first race, and a new track where it
was), the race is on that track if its lap fits. A tagged place with a track's name (the track renamed to it) becomes
that track's entry. Entries are compared on the map, height left out (since 4.10.13: Horizon lifts the car as an
event loads). A rewind (the data stopping, then the race clock a few seconds back, the car on the road it drove) is
the same race (since 4.10.13; it was taken for a new one); since 4.10.14 also back over the line (the lap before
again; its lap kept once), and its session log and video go on in one file, the part driven again dropped when it's
read (a gap left where it was; the video lines up by the clock). A track's length is measured again on its first flying lap. While a race's first lap runs, Track shows
the track only if one starts there.

A lap is recorded when LapNumber goes up, with LastLap as its time. A sprint (since 3.24.5) ends no lap: its finish is
the race clock (more than 5 s in, over 100 m driven) standing still for a second while the car still sends, or LastLap
set to the race's time, or (since 3.24.7) the data stopping: Forza sends nothing after the finish line (its Data Out
guide says so), so a race whose data stops for 1.5 s and comes back in free roam (or in another race starting
elsewhere: a series) finished at its last race time, if over 100 m were driven and, once the track's length is known,
at least 85% of it (less is a race quit from the pause menu). It's recorded as lap 1 with the race's time and the
distance driven as its length. A circuit's last lap ends the same way (since 3.24.7), recorded with its current lap time
when most of a lap was driven; since 3.28.0 also when the race clock goes straight back to 0 at the line, stops, or
LastLap takes the lap's time without the lap number moving on (a race of 3 laps recorded only 2). Since 3.30.3 a
finish is judged on the frame furthest into the lap, not the last one: after the finish Forza sends a few frames (the
next race loading) with the race clock as it was, the distance back near 0 and the car elsewhere, which made every race
look undriven, so none was kept. Such frames after a gap end the race there. A circuit whose lap number goes up at the
line (Horizon 5) finished at that line when the data stops with hardly any of the next lap driven. The log says why a
race wasn't kept ("not a finish (…)"). A rewind (the race clock going back) is followed; a restart (the clock back near 0, or
back at all after the data stopped) is a new race. Free roam (no race clock) records nothing.

## Code

| File | What it is |
|---|---|
| `AnalysisApplet.cs` | The tool and its on / off shortcut (`Analysis.OnOff`): reads the profile's game (`SimData.Open()`) on its own thread while it records, the tab shows or Overlays wants it (`Wanted`); a 10/s tick feeds the tracker, the tab and Overlays' block widgets (`Ticked`; the last frame driving held while the game's paused, and the `PerfHistory`); `Current` for Overlays (the widgets and their `Analysis.Widget.<block>` shortcuts are `InputOverlay/InputOverlayApplet.cs`'s since 3.33.0); its Settings page; registered with `SimProfiles` (Enabled per profile, `OnAtFirst`: Forza Horizon's); feeds the race log (`RaceLog`) every frame, which the tracker's `Claim` keeps |
| `HeaderFilter.cs` | (since 4.13.0) A column's filter from a right-click on its header (a popup of checkboxes), for the laps table and `Sessions/SessionPicker.cs` |
| `AnalysisView.cs` | The tab (since 4.13.0 one table, `TableColumns` and `RenderTable`, Game, Track, Car and Class with `HeaderFilter`s; Races since 3.28.1: `RenderRaces`; since 4.7.0 `RowFold`, a row's blue chevron over the cards' `Fold`s): on / off and its shortcut button, status, Name Track (`TagHere`: rename the track the car's at, or `NameHere`), Current Location (`Here`), the blocks, Best Times tiles (`RenderTiles`, a Grid: rows as tall as their tallest; `tileSearch`), Best Laps, Laps (filters, gaps; `Foldable`'s `fill`), Discovered Tracks (`RenderTracks`, `TrackRow`, Game's filter, Location's Z-order `Cell`); each card `Foldable` (`AnalysisSettings.Folded`); since 4.5.0 the Widgets card (`Foldable(…, foldedAtFirst)`), the race analysis' cards, and `ClickToOpen` (a row's lap or race opened there, right-click Compare With); since 4.6.3 `Filters` (Track, Car and Class, one set per card) and the three tables on one row |
| `Blocks.cs` | The blocks, their rows and layout (`Block` and its `BlockKind`, `BlockRow`, `BlockCell` of `CellPart`s, `Reading`), the Leaderboard's cars around yours (`Around`) and gaps (`GapText`), units, lap times, class names; `PerfHistory` (power, torque and boost, the last 600 samples, peaks) |
| `BlockView.cs` | A block's cells in columns (label and value, rows joined, empty ones left out), the car drawing with a temperature at each wheel, Performance's graphs (`Graph`, drawn in `OnRender`), Timing's times, tyre colours, Position's and Lap's "3 / 8"s, or the Leaderboard (`BoardBody`: a column a row, its lines filled every update) (the driving ones in a `Viewbox`, `RaceFont`); in a widget being edited, a checkbox per row (the widget's `Hidden`) |
| `AnalysisSettingsView.cs` | Settings → SimEffects → Analysis, and the Units and °F / °C boxes it shares with the Profile card (`ProfileCard.UnitBoxes`, since 3.28.0; the tab before) |
| `MapView.cs` | The Map card (since 3.31.0): the map image (`LoadPicture`, off the UI thread), the line-up (`Lineup`: a least-squares line per axis from `GameMap.Anchors`), the dots and labels (`Rebuild`, placed by `Place` as the view zooms and pans), the car's dot (`Tick`), search and filters, the track picked (`ShowPicked`: its tables), `Kind` / `Form` (a track's type, guessed from its name unless `Track.Type` is set) |
| `CarDrawing.cs` | The Tires block's car from above (paths in the theme's brushes; the tyres under it, `Paint`ed by temperature), in a `Viewbox` so it scales with the widget |
| `LapTracker.cs` | `LapTracker` (races, laps, a sprint's finish and `Progress`, tracks by game, start and length, places tagged with `NameHere`, the `Nearest` to the car; each track's most laps and cars), `LapRecord`, `RaceRecord` (a race finished: start and finish places, `Result`), `LapStore` (`AnalysisLaps.jsonl`, a JSON line per lap; `AnalysisRaces.jsonl`, one per race; best by track, car and class); since 4.5.0 each `LapRecord` and `RaceRecord` names its race's log (`Session`, from `Claim`); since 4.10.0 the games that run sessions (`TelemetryFrame.Sessions`: `FeedSessions`, a run each, `StartRun` / `EndRun`, a lap `Waiting` for its time, `Settle`, `RecordSession`) and the tracks a game names (`ByKey`, `AddKeyed`; Motorsport 2023's in the Forza path too) |
| `RaceStatsView.cs` | (since 4.6.3) Racing Statistics: the tiles (`Tile`, a share's bar), Finishes' bars and Last races' places |
| `TrackMapView.cs` | (since 4.6.3) The Track Map block: `TrackLines` (the best lap's line on a track, read from its race's log off the UI thread, kept per track and best lap) and `TrackMapView` (that line, your line of the last few minutes and the car, fitted north up) |
| `RaceUploads.cs` | (since 4.15.0) Race videos to YouTube with Core's `YouTube`, one at a time: `Queue`, `Resume` (those marked `RaceVideoLink.Upload`), `Progress`, `Failure`, `DeleteFiles`; `Describe` (the title and description, from `SessionsView`) |
| `RaceVideo.cs` | (since 4.8.0) `RaceVideo`: each race recorded with Core's `ScreenRecordings` (`RaceLog`'s `Began`, `Ended` and `Dropped`), `RaceVideos` (`AnalysisVideos.json`: a `RaceVideoLink` per race's log: the file, its first frame's time, `Offset`) |
| `RaceLog.cs` | (since 4.5.0) A session log a race (since 4.10.0 a run, in the games that run sessions): Core's `SessionRecorder` (Manual) from the reading thread while the race clock runs; `Claim` keeps one for a lap or result, the rest deleted (`SettleLocked`, 15 s after the next frames) |
| `Sessions/` | (since 4.5.0; Forza Analysis' module before) The Race Analysis: `SessionsView.cs` (its cards, the Session box, `Open` / `CompareWith` from the tables, Compare with its export and import; since 4.7.0 `ShowMoment` (a zone or move on Compare's chart), `PopOut` (the Video and Track Map in windows of their own), Session 1 the analysed session), `SessionManager.cs` (since 4.12.0: the Session Manager window, from `SessionsView`'s `Items`, `Listed`, `Load` and `Delete`), `SessionData.cs` (a log read back at 100 samples a second, and its laps), `PedalAnalysis.cs` (brake zones, throttle applications, scores, markers, timed tips), `Incidents.cs` (contact found and costed; speeds in mph or km/h), `SessionChart.cs` (since 4.7.0 only the drawing helpers, `Envelope` and `Clock`: the Trace went into Compare), `CompareChart.cs` (two runs by distance, and the delta; since 4.6.3 a colour per channel, the other run dotted, each line hidden or highlighted: `Hidden`, `Highlight`, `LinePen`; since 4.7.0 the Trace's zones, laps and markers, `ShowSamples` and `Mark`: the moment picked as a line), `LineMap.cs` (since 4.6.3, the Track Map (in Compare since 4.6.9): both runs' lines from X and Z, `HoverAt` from Compare's chart), `FrictionCircle.cs` (scores over the race, the friction circle), `Shifting.cs` (since 4.6.10: `ShiftAnalysis`, the gear changes judged against the car's best upshift points, the limiter, its tips; `Shift`), `ShiftChart.cs` (the Shifting card's chart), `SessionPicker.cs` (since 4.13.0: Compare's window to find a session), `YouTubeOptions.cs` (since 4.15.0: the Analysis Manager's YouTube window and the YouTube mark), `RaceVideoView.cs` (since 4.8.0: Compare's Video, its tools popped out, `Shown` for the chart's `PlayAt`; since 4.15.0 from YouTube with Shared's `YouTubePlayer`, and a second one for Session 2), `VideoFrames.cs` (a video's frames with Media Foundation, on its own thread: `Want`, `Play`, `Take`), `VideoSound.cs` (since 4.8.1: its sound while it plays, NAudio's `MediaFoundationReader` to WASAPI), `VideoExport.cs` (since 4.9.0: the video re-encoded with the telemetry drawn on: Media Foundation's sink writer, H.264 and AAC; Overlays' `DashView` and `OverlayWindow.Trace` drawn on a thread of its own), `OverlayChart.cs` (zones or applications drawn over each other), `SessionSettings.cs` (`ForzaAnalysis.json`) |
| `Terrain.cs` | (since 4.10.8) Which of a game's maps the car is on (`AnalysisSettings.Worlds`, `World`, `Track.Map`): each map's ground learnt (`AnalysisTerrain.bin`), the last 16 squares as evidence, the switch (`AnalysisApplet.SetMap`, `MapChanged`; since 4.13.4 not while `LapTracker.MapKnown`, and a track's map first: `LapTracker.MapSeen`, its race's `raceMap`, `OnTop`) |
| `KnownTracks.cs`, `Tracks.json` | The tracks the app comes with, embedded, merged into `AnalysisSettings.Tracks` once per version of the file (`AnalysisSettings.KnownTracks`, a hash of it) |
| `AnalysisSettings.cs` | `AnalysisSettings` (`Analysis.json`: Enabled, `OnOffKey`, units, Temperature, `BoardRows`, `StartRadius` (since 3.30.2), `BlockWidget`s, `Track`s (since 3.31.0 with a `Type`), `Maps` (since 3.31.0: a `GameMap` per game, its image and `MapAnchor`s), car names; since 4.10.0 a `Track`'s `Key` (the game's id for it), `GameCars` and `GameClasses` (the names the games give, by `CarNames.Number`), `Learn`) |

The race data comes from each game's reader (`TelemetryFrame.HasRace` and the fields after it; since 4.10.0 also
`TrackKey`, `Sessions`, `Session`, `Checkered`, `LapInvalid`, `SessionLaps`, `FieldSize`, `CarClassName`):
`Telemetry/IRacingSource.cs` (`Race`, `ReadTrack`), `Telemetry/AccSource.cs` (Assetto Corsa, ACC and AC EVO: `RaceAc`,
`RaceEvo`, `ReadStatic`), `Telemetry/Stint.cs` (their run's clock and distance) and `Telemetry/ForzaSource.cs`: the dash's
place, power, torque, tyres, boost, fuel, distance, laps and position, and Horizon's CarGroup (offset 232, the first of
the 12 bytes Horizon puts before the dash). The Leaderboard's field is `TelemetryFrame.Field` (`Standings` of `Racer`s,
`Telemetry/Telemetry.cs`), made by `Telemetry/IRacingSource.cs` (`Field`, 4 times a second: the drivers and sessions from
the session YAML, `ReadField`; CarIdxPosition, CarIdxLapCompleted, CarIdxLapDistPct, CarIdxEstTime, CarIdxBestLapTime,
CarIdxOnPitRoad; a lap count a moment late at the line is mended).
