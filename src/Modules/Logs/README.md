# Logs

*A module of [Daisy's App](../../../README.md): a tab for reading logs, Daisy's App's own and Windows', with copy and
export. Switch it on or off in Settings → General → Applets. Its code is in this folder (installed to
`modules\Logs\` next to `DaisysApp.exe`).*

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
above, warnings and errors, errors only), and a search box for text in the message, source or event. Errors show in
red, warnings in amber, debug lines dimmed. Click an entry to see it in full below the table (a crash's whole stack
trace, for instance); drag the bar between them to resize. **Refresh** loads it again. Windows' logs show their newest
5,000 entries in the time chosen.

**Copy** copies the selected entries (Ctrl+click or Shift+click to select several) or, if one or none is selected,
every entry shown, as text, with a line saying which log and PC it's from. Ctrl+C in the table copies the selection.
**Export…** saves the entries shown as a text or CSV file; for Daisy's App's logs it can also save a **zip of every log
file** (the last 7 days of every applet's), with what Windows recorded about the app's crashes and the app's version —
the thing to send when asking for help.

## Daisy's App's logs

The app and every applet write a log, one file a day each, in `%LOCALAPPDATA%\DaisysApp\logs\`: the applet's name
(without spaces) and the date, e.g. `DaisysApp-10-10-26.log`, `AudioTools-10-10-26.log`. Files older than 7 days are
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
