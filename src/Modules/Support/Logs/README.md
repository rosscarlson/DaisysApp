# Support: Logs

*A tool in [Daisy's App](../../../../README.md)'s [Support](../README.md) tab: its **Logs** sub-tab, for reading logs, Daisy's App's own and Windows', with copy, clear and export (Settings → General → Logging **View** opens it). Its code is in this folder, part of the Support module (installed to `modules\Support\` next to `DaisysApp.exe`). Before 0.39 it was the last page of Settings; from 0.39 until 3.12.9 it was on the Network tab, and until 3.24.9 on the Tools tab (switched off there, it's switched off here; showing last there, the app opens on it here).*

Each log opens in a tab of its own; **Add** opens another, and the × on a tab closes it. The open tabs are remembered.

| Add… | What it shows |
|---|---|
| **Daisy's App (every applet)** | The app's own log and every applet's, merged, newest first. New lines appear as they're written while the tab is showing. |
| **Daisy's App: one applet** | Just one of them (the app's is *DaisysApp*). |
| **Daisy's App crashes (Windows)** | What Windows recorded when DaisysApp.exe crashed or stopped responding: *Application Error*, *.NET Runtime* (the .NET error and where it happened), *Application Hang* and *Windows Error Reporting* in the Application log. |
| **Windows: Application / System / Setup** | Windows' event logs, as in Event Viewer: programs' events and crashes; drivers, devices, services, power and restarts; Windows updates. |
| **Windows: other log…** | Any other event log on the PC that has entries (a driver's, an app's, PowerShell's…), with a search box. Some, such as Security, need the app to run as administrator. |
| **WSL: *distribution*** | A WSL distribution's system journal since it last started (its kernel messages if it has no journal). Listed only when WSL is installed; loading it starts WSL if it isn't running. |

**Each tab:** how far back to show (last hour, 24 hours, 7 days or 30 days), which entries (everything, Info and
above, warnings and errors, errors only), and a search box for text in the message, source or event. Click a column's
header for its menu (since 3.16.0): **Time** newest or oldest first; **Level** errors or Debug first, and a tick box per
level (with how many entries each has; **Show all**, **Hide all**) to hide some; **Source** A to Z or Z to A, and a
tick box per source; **Event** low to high or high to low (by number). The sorted column has an arrow, a filtered one
a filter mark; the tab keeps both. The **Event** column is the Windows event id, so it's shown only on Windows' logs
(the app's and WSL's have none). The Level
column is coloured: errors (and crashes) red, warnings yellow, everything else green. Click an entry to see it in full below the table (a crash's whole stack
trace, for instance); drag the bar between them to resize. **Refresh** loads it again. Windows' logs show their newest
5,000 entries in the time chosen.

**Improper shutdowns.** Every run of Daisy's App logs when it starts and, when it closes properly, *Exited cleanly*.
Where a tab with the app's own log finds a start with no clean exit before the next one, it adds a red row just above
that run's last entry: the run crashed, stopped responding and was ended, or Windows or the PC stopped first, and the
entries just below are the last it wrote — where to look for what broke. **N improper shutdowns** in the toolbar
(shown when there are any) selects each in turn.

**Copy** copies the selected entries (Ctrl+click or Shift+click to select several) or, if one or none is selected,
every entry shown, as text, with a line saying which log and PC it's from. Ctrl+C in the table copies the selection.
**Export…** saves the entries shown, and only those (the time range, level and search decide), as a text or CSV file;
for Daisy's App's logs also as a **zip**: a file per log, named and written as the log files are, with what Windows
recorded about the app's crashes and the app's version — the thing to send when asking for help (since 3.12.9; before,
the zip held every log file whatever was shown).

**Clear** (Daisy's App's logs only) deletes the tab's log files: every module's, or the one module's, today's too
(Core's `Log.Clear`, which closes a file being written first, so its next line starts a new one). It asks **Confirm?**
first, as every button that can't be undone does. Windows' and WSL's logs aren't cleared from here.

## Daisy's App's logs

The app and every applet write a log, one file a day each, in `%LOCALAPPDATA%\DaisysApp\logs\`: the applet's name
(without spaces) and the date, e.g. `DaisysApp-10-10-26.log`, `Video-10-10-26.log`. Files older than 7 days are
deleted when the app starts. Each line is written to disk straight away, so a crash loses nothing logged before it.

Settings → General → **Logging** chooses how much goes in:
- **Normal** — what the app and each applet do: starting and stopping (with how long each applet took), the window
  shown and hidden, devices, profiles and mirrors used, settings changed, recordings, benchmarks and wizards, update
  checks, everything shown as an error, and exceptions. If Daisy's App closed unexpectedly, the next start says so and
  copies in what Windows recorded about it.
- **Debug** — much more: every settings file loaded and saved, every key and hotkey, each move of a window, each
  device's details, the steps of starting up, and every exception thrown, even ones the app handles (most are
  harmless). Use it while tracking down a problem, then switch back.

For developers: `Log.Here.Info("…")` (or `Log.For("Name")`) in any module writes to that module's log; `Debug(() =>
"…")` only builds the text when Debug logging is on. `ErrorLog.Write(context, exception)` writes an error to the
calling module's log. See `DaisysApp.Core/Logging/Log.cs`.

## Code

| File | What it is |
|---|---|
| `LogsApplet.cs`, `LogsView.cs` | The sub-tab: a tab per open log, and Add |
| `LogPage.xaml(.cs)` | One log's tab: the table, filters, the column headers' sort and filter menus, the entry in full, Copy / Clear / Export |
| `LogSources.cs` | What a tab can show (the app's logs, a Windows event log, a WSL journal), and for the app's logs the zip export and Clear: new kinds of log go here |
| `LogRow.cs` | One entry, from any source |
| `ShutdownCheck.cs` | Finds where the app stopped without a clean exit |
| `WindowsLogPicker.cs` | Picks any Windows event log |
| `LogsSettings.cs` | `Logs.json`: the open tabs and their filters and sorts |

The logs themselves are written by Core (`DaisysApp.Core/Logging/Log.cs`).
