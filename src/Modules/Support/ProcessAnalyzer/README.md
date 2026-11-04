# Process Analyzer

*A tool of [Support](../README.md) (since 4.1.0): Support → Process Analyzer.*

Every program running on the PC (its processes together, one row each) checked against a database of programs
known to be fine, risky or dangerous, with **suggestions** for the ones that are risky, likely unnecessary or busier
than they should be. A program the database doesn't know is classified from its file (signature, maker, where it
runs from) and stored, so the database grows as it sees new programs. The database can be exported and merged on
another PC, and kept in step with [Daisy's Server](https://github.com/rosscarlson/DaisysServer)'s master database.

## The table

One row per program: **Threat** (Safe, Low, Medium, High, Dangerous), the program's name, **What it is**,
**Publisher**, **Category**, **Needed** (Required, Recommended, Optional, Unnecessary, Unknown), its CPU and memory
now (all its processes), how many processes, **✓** when it's reviewed (classified by a person, or in the list the app
comes with; blank: classified automatically), and the **Suggestion**. The list shows the suggestions first. It looks
again every 2 seconds while the tab is showing.

The filter at the top: **Everything**, **Suggestions** (the default), **Not reviewed**, **Risky** (Medium and up).
The search box matches name, title, publisher and category.

A **suggestion** comes from:
- a **High** or **Dangerous** threat: the database says so, or something was found (below);
- **Likely unnecessary**: the database says it isn't needed (updaters, telemetry, widgets, preinstalled extras…);
- **Using more than expected**: over its expected use now (Light: 3% CPU or 300 MB; Moderate: 15% or 1.5 GB;
  Heavy: 60% or 8 GB), or **Often busy** in its history (among the busiest by CPU more than 5% of the time, at more
  than its CPU level on average then);
- **Unknown: worth a look**: a Medium threat (not signed, not reviewed).

**Fine Here** (in the details) hides a program's suggestions on this PC.

### What's found

- **Where it runs from**: a reviewed entry lists the folders a program belongs in; a Windows program running from
  anywhere else is **Dangerous** (malware named svchost.exe, say), another program **High**. A temporary folder is
  High, the Recycle Bin Dangerous, Downloads Medium.
- **Its signature** (Windows' WinVerifyTrust, its own or Windows' catalogs, offline): not valid is High; not signed is
  Medium outside Program Files and Windows (Low for a reviewed program), and Medium again if it listens on a port.
  Signed by someone other than the publisher a reviewed entry names is **High**.
- **A renamed copy** of a known risky program (its file's original name) is High; a hidden file Medium.
- **VirusTotal** (with an API key): 1–2 engines flagging it is High, 3 or more Dangerous.

## The details

Click a program for everything about it:
- **What It Is**: publisher, needed, expected use, about it, who classified it and when (and the server's version),
  VirusTotal's verdict.
- **Findings**, worst first, in their colours.
- **Now**: processes, CPU, memory, threads, handles, running since, process IDs, network connections and the ports it
  listens on.
- **History**: from Hardware → Performance's log (it logs the 5 busiest programs by CPU and by memory every 10
  seconds): how often it was among them over the last days (7 by default), its CPU then on average and at most, its
  memory, and a graph of its CPU with its expected level; then this PC's own figures since it was first seen, and the
  average across the server's PCs when the server sends it.
- **Its File**: path, signature, company, product, description, version, original name, size, date, command line,
  the process that started it, and what starts it by itself (services, Run keys, Startup folder).

Buttons: **Edit** (classify it yourself: title, publisher, category, threat, needed, expected use, about it, the
folders it runs from; **Forget** puts the automatic one back), **Fine Here**, **VirusTotal** (with a key: looks its
hash up and stores the verdict; without one: opens its hash's page), **Search** (the web), **Open Folder**, and **End**
(every process of it; Confirm? first).

## The database

`%LOCALAPPDATA%\DaisysApp\ProcessAnalyzer\processes.json`: every program known, by name (the file's, lower case,
without .exe), the server's version it's in step with, the changes not yet sent, and this PC's figures for each
program (first and last seen, average and peak CPU and memory). It starts with the list Daisy's App comes with
(`known.json`, about 270 programs: Windows' own, Defender, drivers' helpers, browsers, launchers, remote-control
tools, known adware and miners); a new version of the list replaces only its own entries.

An entry: `name`, `title`, `publisher` (alternatives split by `|`), `category`, `threat`, `necessity`, `resource`
(Light, Moderate, Heavy), `description`, `paths`, `source` (seed, auto, user, import, server), `reviewed`, `updated`
(UTC), `by` (the PC), `rev` (the server's version it changed in), and VirusTotal's `sha256`, `vtMalicious`,
`vtEngines`, `vtChecked`; the server can add `fleetCpu`, `fleetMemoryMB`, `fleetPcs`.

**Export…** writes every entry but the unchanged list ones to a file (`{format: "DaisysApp.Processes",
formatVersion: 1, exported, client, serverVersion, entries}`); **Import…** merges one in: a reviewed entry beats an
automatic one, then the newer wins. What's imported is sent to the server too.

**While the tab isn't showing** it looks every 5 minutes (Settings) and notifies about a High or Dangerous program,
once per program per run.

## Daisy's Server

With the server set up in Settings → General and **Use Daisy's Server's database** on (Settings → Support → Process
Analyzer), the database is kept in step with the server's **master database**; without it everything stays here.
The entries aren't sealed (they're programs' names and classifications, nothing of the user's): the server reads and
merges them, one database for all its users.

The channel is the server's `processes` module (`from`: `ProcessAnalyzer`). Messages inside `msg`:

| From | Message |
|---|---|
| client | `{op: "join", version, app}`: the server version it has every change up to (0: none; a new client, or another server). |
| server | `{op: "changes", version, full, entries: [...]}`: every entry changed since the client's version (`full: true` and all of them when it's 0 or too old), with each entry's `rev`. Also sent to every client after a `report` changes something. |
| client | `{op: "seen", version}`: it has applied up to `version` (the server keeps each client's). |
| client | `{op: "report", entries: [...]}`: entries made or changed here (up to 200 a message). The server merges each (a reviewed entry beats an automatic one, then the newer `updated` wins), numbers the changed ones with a new version (`rev`), and sends `changes` to every client, the sender too. |
| client | `{op: "usage", items: [{name, cpu, peakCpu, memoryMB, peakMemoryMB, samples}]}`: this PC's figures, once an hour; the server averages them over its PCs into entries' `fleetCpu`, `fleetMemoryMB`, `fleetPcs`. |
| server | `{op: "clients", clients: [{name, version, lastSeen}]}` (optional): the PCs it knows. |

On the client: the server's `changes` replace its entries unless it has changed one since it last sent it (then the
newer wins); a reviewed entry is never replaced by an automatic one. A **new client** (version 0) takes the server's
entries over its own (the server's database has priority), then sends up what the server doesn't have. A change made
while it's disconnected is sent when it connects again.

## Settings

Settings → Support → Process Analyzer (`%APPDATA%\DaisysApp\ProcessAnalyzer.json`): **Use Daisy's Server's
database**; **Look while the tab isn't showing** and how often; **History** (days of Performance's log read);
VirusTotal's **API key** (free from virustotal.com: only files' hashes are sent, 4 lookups a minute) and **Look up
unknown programs that aren't signed**. The table's filter and the programs marked Fine Here are kept there too.

## Code

| File | What it does |
|---|---|
| `ProcessAnalyzerApplet.cs` | The tool: its settings, the server's sync, the look every few minutes with its notifications |
| `ProcessAnalyzerView.cs` | The tab: the bar (status, server, search, filter, Export…, Import…), the table, a look every 2 s |
| `ProgramRow.cs` | A table row, updated in place; the words and colours for threats, necessity, use and categories |
| `ProcessDetails.cs` | The details on the right, its buttons and the Classify editor |
| `Analyzer.cs` | One look: the processes grouped by program, new ones classified and stored, each assessed; automatic VirusTotal lookups |
| `Scanner.cs` | The processes (NtQuerySystemInformation: times, memory, threads, handles, file path, without opening them), files' facts, command lines, services and sign-in entries, TCP connections by process |
| `Signature.cs` | A file's Authenticode signature (WinVerifyTrust, embedded or in Windows' catalogs) and its signer |
| `Classifier.cs` | Guessing an unknown program's entry, the findings, the expected use, the suggestion |
| `PerfHistory.cs` | Reads Hardware → Performance's log (`logs\performance\perf-*.csv`, its `top_cpu` and `top_memory` columns) |
| `ProcessDb.cs` | The database: entries, this PC's figures, merging, export and import, the list it comes with |
| `known.json` | The list it comes with (embedded) |
| `ServerSync.cs` | The `processes` channel to Daisy's Server |
| `Lookup.cs` | SHA-256, VirusTotal's API, VirusTotal's page and a web search |
| `ProcessAnalyzerSettings.cs`, `ProcessAnalyzerSettingsView.cs` | Its settings and their page |
