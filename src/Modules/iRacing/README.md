# Gaming: iRacing

*A module of [Daisy's App](../../../README.md): the **iRacing** sub-tab of the [Gaming](../Gaming/README.md) tab (with Joy2Key, ForzaWheel and Forza Analysis; on a tab of its own when it's the only one installed). Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\iRacing\`. New in 1.10.0.*

Every race of yours from **iRacing's Data API**, kept on this PC and summed up: totals and rates, a summary in words,
your ratings over time, when and where you race, where you start and finish, the tracks, cars and series you race
most, records, and tables of it all.

## Signing in (Settings → Gaming → iRacing)

iRacing's Data API signs in with OAuth. Since December 2025 (2026 Season 1) the old email-and-password sign-in is
gone, so it takes four things:

- **iRacing email** and **Password**: your iRacing account (it needs an active subscription).
- **Client ID** and **Client secret**: an OAuth client of your own, from iRacing, for the *password limited* sign-in
  (meant for scripts and tools like this), with your account on the client's list of users (up to three). See
  [iRacing's OAuth documentation](https://oauth.iracing.com/oauth2/book/). iRacing paused handing out new client IDs in
  2025 while it reviews third-party use; the documentation and iRacing's forums say when it opens again.

The password and secret are sent **masked**, as iRacing asks (Base64 of the SHA-256 of the secret followed by the
lower-case email or client ID), never as they are, and they're kept in `iRacing.json` encrypted for your Windows
account. After the first sign-in the app keeps going with the **refresh token** iRacing gives it (saved, encrypted
too; each is used once), so the password isn't sent each time. Two-factor sign-in isn't asked for this kind of
client. **Test sign-in** signs in and says who you are.

**Fetching:**

- **Hosted and league races too** (on by default): not only official series.
- **Sync when the tab opens**, if the last sync was more than 1, 3, 6 (the default), 12 or 24 hours ago (off: only
  the **Sync** button).

**Races kept on this PC:** **Open the folder**, and **Delete them and fetch everything again…**.

## Syncing

**Sync** (at the top of the tab) fetches:

1. you (`/member/info`): your name, member number, when you joined, your club and your licences (class, safety
   rating, iRating, TT rating, CPI per category);
2. iRacing's own totals, per category (`/stats/member_career`) and per year and category (`/stats/member_yearly`);
3. your iRating and your licence and safety rating over time, per category (`/member/chart_data`: Oval, Road, Dirt
   Oval, Dirt Road, Sports Car, Formula Car);
4. every race (`/results/search_series`, races only, and with hosted races on, `/results/search_hosted`), 89 days
   at a time (iRacing allows 90 per search). The first sync goes back to when you joined, which takes a minute or two;
   later ones fetch from the last one (less two days, in case results came in late);
5. the car names (`/car/get`) and the tracks' lengths (`/track/get`), for distances, when there's one it doesn't
   know.

Each data call answers with a link to the data (on Amazon S3), which is fetched in turn; a search's results come in
chunks, each fetched. iRacing limits how often its API is called: when the limit runs out (its
`x-ratelimit-remaining` header), or it answers *too many requests*, the sync says so and waits until it resets.
**Stop** stops it; what was fetched is kept, and the next sync carries on from there. A problem (wrong password,
client not accepted, too many sign-ins) is shown under the buttons, in plain words.

Everything is kept in `%LOCALAPPDATA%\DaisysApp\iRacing\data.json`, so the tab shows it at once. Settings → General →
Storage lists it as *iRacing races* (deleting it only means the next sync fetches everything again).

**Positions.** iRacing's results can count positions from 0 (the winner is 0): a 0 anywhere in your races means
they count from 0; otherwise iRacing's own count of your wins says which (`RaceData.GuessFromZero`). Everything shown
counts from 1.

## The tab

**The top:** your name, member number, when you joined, your club and how many races are kept; a chip per licence
(its colour at the left: class and safety rating, iRating; point at it for the TT rating and CPI); **Sync**,
**Stop**, **Settings…**, what the sync is doing and when it last synced. Until you've signed in, a card says what's
needed, with **Open the settings**.

**Filters:** the **Category** (all, or one), the **Time** (all time, this year, the last 12 months, 90 days or 30
days) and **Official races only**. Everything below follows them, except the rating graphs (iRacing's record, all of
it, for the category chosen) and iRacing's totals.

**Tiles:** races, wins, podiums, top 5s, poles, win rate, laps, laps led, kilometres, hours (in race sessions),
average start and finish, places gained a race, incidents a race, clean races (no incidents), not finished (more than
a lap short of the winner's distance), tracks, cars, series, iRating won, average strength of field and championship
points. Point at one for more (the share of races, the front row, laps led as a share, incidents every 100 laps…).

**In short:** the same in sentences: how many races over how many days, wins and podiums, your track (most laps), car
and series, the day and hour you race most, your busiest day, the longest run of days with a race, average start and
finish, incidents and clean streaks, your best runs of wins, podiums and top 5s, and the distance (in times around the
Earth).

**Graphs** (point at any of them for its numbers):

- **Races by category**, and **How your races ended** (wins, 2nd and 3rd, 4th and 5th, 6th to 10th, further back).
- **iRating over time** and **Safety rating over time**: a line per category, each named at its end, with a
  crosshair and the values on the day you point at.
- **Races a month**: wins, 2nd and 3rd, and the rest, stacked (a column a quarter when there are more than three
  years).
- **Race days**: a year of days, as GitHub's: the darker, the more races. **When you race**: the days of the week and
  the hours of the day.
- **Where you finish** and **Where you start**: how many times in each place, coloured like the endings above.
- **Places gained or lost** (gained blue, lost red), and **Incidents a race**.
- **Most laps: tracks**, **Most races: cars**, **Most races: series**, **Wins and podiums: tracks**, **Your best
  tracks** (top-5 rate, three races or more, with the average finish) and **iRating won and lost: cars**.

**Records:** first race, first win, toughest win (strongest field), best finish against the strongest field, biggest
charge and drop, most laps led, longest race, biggest and strongest field, best iRating gain and worst loss, most
incidents, latest win and latest race. Double-click one to open its results on iRacing.

**More numbers:** median finish, top 10s, average field, wins from pole and not, poles converted, clean wins, races
leading a lap, official and hosted races, races a week, days with a race, the longest streaks (days, wins, podiums,
clean races) and the clean streak now, safety rating won, car classes, how many different drivers won races you
didn't, and who won most of them.

**Tables** (click a heading to sort): **Tracks**, **Layouts**, **Cars**, **Car classes**, **Series**, **Seasons**,
**Years** (each with races, wins, podiums, top 5s, poles, win %, best, average start and finish, places gained, laps,
laps led, km, incidents a race, clean races, average SoF, iRating won and last raced), **Every race** (with **Find**:
series, car, track or winner; double-click a race for its results on iRacing) and **iRacing's totals** (per category,
then per year).

The graphs are drawn by the module itself (`Charts.cs`): eight series colours in a fixed order, each with its own step
for the dark and the light theme, chosen to stay apart for colour-blind eyes; a category always has the same colour;
one blue for single series and its shades for the heat maps; a neutral grey for "the rest".

## Files

| File | |
|---|---|
| `%APPDATA%\DaisysApp\iRacing.json` | The settings: email, client ID, the password, secret and refresh token (encrypted), what's fetched and the filters |
| `%LOCALAPPDATA%\DaisysApp\iRacing\data.json` | Everything fetched: you, licences, totals, ratings over time, every race, car names and track lengths |
| `iRacing-<date>.log` | Its log (in the logs folder): signing in, syncs, problems |

## Code

| File | What it does |
|---|---|
| `IRacingApplet.cs` | The applet (`[Applet]`, in the Gaming tab's group), with its view and Settings page |
| `IRacingApi.cs` | The Data API: OAuth sign-in (password limited, then refresh token), masking, data calls and their links, searches' chunks, the rate limit, and reading the answers |
| `RaceData.cs` | `Race`, `License`, `CareerStat`, `ChartPoint` and `RaceData`: what's kept, loading and saving it, and the sync |
| `RaceStats.cs` | Everything worked out from the races shown: totals, rates, streaks, records, per track, car, series, season, year, category and class (`RaceGroup`), and each graph's numbers |
| `Charts.cs` | The graphs: `BarList` (bars, stacked), `ColumnChart`, `TimeChart`, `Donut`, `CalendarHeat` and `WeekHeat`, with the colours and the readout they share (`ChartBase`) |
| `IRacingView.cs` | The tab: the top, syncing, filters, tiles, summary, graphs, records and tables |
| `IRacingSettingsView.cs` | Settings → Gaming → iRacing |
| `IRacingSettings.cs` | `iRacing.json`, the secrets encrypted with Windows' DPAPI |
