# Daisy's App

**Version 0.64.0** · [Download the latest release](https://github.com/rosscarlson/DaisysApp/releases/latest) · [What's new](CHANGELOG.md)

A multitool app for Windows: one window of small tools, each on its own tab: hardware monitoring, audio levels and
EQ, window and screen tools, recording, network monitoring, file tools and controller mapping. Each tab is a
**module** the app loads from its `modules` folder, with its own guide; switch any of them off and it isn't loaded at
all. Some modules hold several tools, each on its own sub-tab.

![Daisy's App](docs/screenshot.png)

**This README is about the app itself.** What each module does, and how its code works, is in its own guide (below),
so the two never say the same thing twice.

## The modules

| Tab | Its tools | Guide |
|---|---|---|
| Hardware | Performance · USB Monitor · Devices · Sensors | [src/Modules/Hardware](src/Modules/Hardware/README.md) |
| Audio | Volume · Levels · Delay | [src/Modules/AudioTools](src/Modules/AudioTools/README.md) |
| Video | Resizer · Mini Mirror · Overlay · Recording | [src/Modules/Video](src/Modules/Video/README.md) |
| Network | Zabbix · Logs | [src/Modules/Network](src/Modules/Network/README.md) |
| Tools | File Sync · Disk Usage · Renamer | [src/Modules/Tools](src/Modules/Tools/README.md) |
| Gaming | Joy2Key (controller buttons to keys, mouse and macros, a profile per game) and ForzaWheel (wheel, pedals, shifter and button boxes as one vJoy wheel for Forza Horizon) | [src/Modules/Gaming](src/Modules/Gaming/README.md) |
| *(Template)* | A hello-world module, off by default: the guide to writing one | [src/Modules/Template](src/Modules/Template/README.md) |

Each module's guide links to a guide for each of its tools (e.g. [Tools → Disk Usage](src/Modules/Tools/DiskUsage/README.md)),
which ends with a **Code** section: its files and what each one does.

---

## Contents

- [Install](#install)
- [Using the app](#using-the-app)
- [Settings → General](#settings--general)
- [Translations](#translations)
- [Files and command line](#files-and-command-line)
- [Updates](#updates)
- [Project layout and modules](#project-layout-and-modules)
- [Build and release](#build-and-release)
- [Third-party](#third-party)
- [Version history](CHANGELOG.md)

---

## Install

1. Download `DaisysApp-Setup-x.y.z.exe` from the [latest release](https://github.com/rosscarlson/DaisysApp/releases/latest).
2. Run it. Windows asks for admin approval, because the app installs to `C:\Program Files\Daisys App`.
   The installer isn't code-signed, so SmartScreen may warn: choose **More info → Run anyway**.
3. Start **Daisy's App** from the Start menu.

Everything the app needs is included (.NET doesn't have to be installed). Installing a newer version upgrades in
place. Uninstall from Settings → Apps → *Daisy's App*; that closes a running copy and removes the sign-in entry and
the update tasks.

---

## Using the app

- **Tabs.** One per module, then **Settings**, always last. Right-click a tab to rename it (Enter saves, Esc cancels);
  drag it to move it, as in a browser (it follows the pointer and the others slide out of its way). A module with
  several tools shows them as sub-tabs at the top of its tab; the one used last opens next time.
- **Only one copy runs.** Starting it again brings the running copy to the front.
- **The tray.** Closing the window keeps the app running in the tray (if that's on); right-click the tray icon to open
  it, check for updates, use the modules' own tray items, or exit.
- **The notification bar** (at the top of the window): updates and app-wide messages. A message such as *Updated to
  version …* fades away by itself after the time set in Settings → General → **Notifications**; problems, an update
  on offer and things in progress stay until you close them.
- **The setup wizard.** The first time the window opens after installing, a short setup asks which modules to switch
  on (each with a one-line description) and lets you drag them into the order you want their tabs in. Then come steps
  from the modules themselves (each module's guide says what its step is). **Back** goes to the step before, on every
  step. Changes restart the app. After that it opens by itself only when a module is missing a setting it can't do
  without (Zabbix switched on but never set up, say), and then with just that module's step: not after every update.
  **Run the setup wizard…** in Settings → General → Applets goes through every step, whatever is set.
- **Saving.** Every setting is saved as soon as it changes (a **✓ Saved** note appears beside it); there's no Save
  button.

---

## Settings → General

The first page of the Settings tab. After it comes a page for each module that has settings (described in that
module's guide), with a sub-tab per tool where it has several. Every page uses the whole width of the window.

- **Startup and system tray** — keep running in the tray when the window is closed (on by default), start when you
  sign in to Windows, and start hidden in the tray.
- **Updates** — the installed version, check for updates when the app starts, **Check for updates** and
  **Release notes**.
- **Applets** — every module the app contains, each with an on/off checkbox and its description. A switched-off module
  isn't loaded at all (no tab, no settings page, nothing running in the background); its settings are kept for when
  it's switched back on. Changes apply after a restart: **Restart now** appears when there's one to apply. **Run the
  setup wizard…** opens the setup wizard.
- **Tab names** — rename any tab; Reset puts its own name back. The new name shows on the tab, its Settings page and
  the tray menu.
- **Appearance** — Dark (default), Light, or System theme.
- **Notifications** — how long the notification bar shows a message before it fades away: 3 seconds to a minute (8 by
  default), or never.
- **Language** — English, plus any language someone has added (see [Translations](#translations)). Takes effect
  after **Restart now**.
- **Files** — open the settings and logs folders, and **Logging**: Normal or Debug (much more detail, for tracking
  down a problem); **View** opens Network → Logs.

---

## Translations

Every piece of text in the app is in translation files, one per language, as plain JSON next to the program. Only
English ships (since 0.55: Spanish, French and Portuguese were dropped, so each change needn't be translated), but any
language can be added:
- `lang\` beside `DaisysApp.exe` holds the main window's and the Settings page's text.
- `modules\<applet>\lang\` holds each applet's (e.g. `modules\Hardware\lang\`).

Each file maps the English text to its translation, with `_language` naming the language in Settings:

```json
{
  "_language": "Español",
  "Run now": "Ejecutar ahora",
  "Next at {0:t}": "Próxima a las {0:t}"
}
```

**To add a language,** copy `en.json` in each `lang` folder to the language's code (`de.json`, `it.json`…), set
`_language`, and translate the text on the right. Keep the English on the left exactly as it is, and keep the `{0}`,
`{1}`… placeholders (the app fills in the numbers and names; they can move around in the sentence). Anything left out,
or a file that's missing, stays in English. The new language appears in Settings → General → Language.

For developers: code uses `T("text")`, `F("text {0}", value)` and `P(count, "{0} profile", "{0} profiles")`; XAML uses
`{l:Tr 'text'}`. `python tools/strings.py` rewrites every `en.json` from the source (`--prune` drops texts no longer
used); with other languages' files present it also reports what each is missing (`--missing de` lists it), and
`tools/merge_tr.py` merges a table of translations in.

---

## Files and command line

| Item | Location |
|---|---|
| Program | `C:\Program Files\Daisys App\DaisysApp.exe`, with each module in `modules\<Name>\` beside it |
| Settings | `%APPDATA%\DaisysApp\`: `settings.json` (the app), a `<Name>.json` per tool, and a `<Module>.json` per tabbed module remembering which sub-tab was showing; each module's guide names its files |
| Logs | `%LOCALAPPDATA%\DaisysApp\logs\`: the app's and each module's log, one a day (`DaisysApp-10-10-26.log`, `Video-10-10-26.log`…, kept 7 days); modules' own logs as their guides say |
| Start at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `DaisysApp` |
| Update downloads | `%TEMP%\DaisysApp-Update\`; a quick update's in `{app}\updates\` |
| Update tasks | Task Scheduler → *DaisysApp* folder: **Update** and **Start** (see [Updates](#updates)) |

| Argument | Effect |
|---|---|
| *(none)* | Start, or bring the running copy to the front (only one copy runs) |
| `--tray` | Start hidden in the tray (used by start at sign-in) |
| `--exit` | Ask the running copy to exit cleanly (used by the uninstaller) |
| `--restart` | Wait for the running copy to close, then start (used by **Restart now**) |
| `--install-update` | The Update task: install the newest release from GitHub, with no window (a quick update) |
| `--module-task <module> <class> …` | Run a module's job in a second copy, e.g. as administrator (Core's `ModuleTasks`) |

---

## Updates

On launch (if enabled) and from **Check for updates** (Settings → General, or the tray menu) the app reads the latest
[GitHub release](https://github.com/rosscarlson/DaisysApp/releases). When it's newer, a banner offers **What's new**
and **Install update**; nothing installs until you click. The installer is downloaded, checked against the SHA-256
GitHub publishes for it, run silently (Windows asks for admin approval), and the app restarts on the new version.

**Quick update: double-click the Settings tab.** It checks, and if there's a newer version it downloads it (the banner
shows how far), closes the app, installs it with no windows and no questions, and starts the app again on the new
version, without the setup wizard (unless the wizard itself has something new; a tool's own first-run setup still
appears if something it needs is missing). If you're up to date it says so. No admin question: the installer registers
two tasks in Task Scheduler (folder *DaisysApp*) for the user installing it: **Update** runs the app's own updater with
that user's highest rights (it only ever installs the newest release it finds on GitHub itself, checked against its
SHA-256, so starting it can't make it run anything else) and **Start** starts the app again with ordinary rights.
Installs from before 0.44 don't have the tasks yet, so the first quick update asks for admin approval once; after
that it doesn't. Uninstalling removes the tasks.

---

## Project layout and modules

Each applet is a **module**: its own project in `src/Modules/<Name>/`, built into its own folder,
`modules\<Name>\`, next to `DaisysApp.exe`. The app loads every module it finds there (unless it's switched off in
Settings → General → Applets), so a module can be added, updated or removed without touching the rest.

```
src/
  Directory.Build.props   settings every project shares (version, .NET 8, WPF)
  DaisysApp/              the app: startup, single instance, window and tabs, update banner, tray, Settings → General,
                          and the module loader (Shell/AppletCatalog); lang/ is its translations
  DaisysApp.Core/         what the app and the modules share: IApplet + [Applet], AppletGroup (one tab of sub-tabs), translations (Loc, {l:Tr}),
                          settings storage (JsonStore), the logs (Logging: Log, ErrorLog, Windows' event logs),
                          theming, and code more than one module uses:
                          Shared/Audio (devices, speaker layouts, mic volume), Shared/Voicemeeter, Shared/Hotkeys,
                          Shared/Hardware (NVIDIA's GPU library, Windows' counters, HDR monitors' SDR brightness,
                          the LibreHardwareMonitor connection), Shared/Charts (the time graph), Shared/Csv
  Modules/
    Directory.Build.props   makes each folder here a module (output to modules\<Name>\, references Core)
    Template/               a hello-world module, and the guide to writing one
    Hardware/ (Performance/ UsbMonitor/ Sensors/)   AudioTools/ (Levels/ = Volume, Tests/ = Levels, Delay/)
    Video/ (Resizer/ MiniMirror/ Gaming/)   Network/ (Zabbix/ Logs/)   Tools/ (FileSync/ DiskUsage/ Renamer/)   Gaming/ (Joy2Key/ ForzaWheel/)
                            each with its README, lang/ translations and code; a module with several tools
                            (one tab, a sub-tab each) has a folder per tool and an AppletGroup (in Core)
installed:
  DaisysApp.exe, lang\, modules\<Name>\ (DaisysApp.<Name>.dll, lang\, README.md, its own dependencies)
```

**Documentation.** This README covers the app (the shell, Settings → General, translations, files, updates, the
build); each module's `README.md` covers that module, and each tool's (in its folder) what it does and, in its
**Code** section, its files. A change to a module only touches that module's guides; a release adds its line to
[CHANGELOG.md](CHANGELOG.md).

**To write a module,** copy `src/Modules/Template`, rename it, and follow
[src/Modules/Template/README.md](src/Modules/Template/README.md). The Template ships with the app but is off: tick it
in Settings → General → Applets to see it. The UI follows the design system in `design.md` (styles in
`src/DaisysApp/Themes/Controls.xaml`, available to every module).

## Build and release

Requires the .NET 8 SDK and Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```powershell
dotnet build DaisysApp.sln   # the app and every module (modules land in src\DaisysApp\bin\...\modules)
.\build.ps1                  # -> artifacts\DaisysApp-Setup-<version>.exe
```

To release: bump `<Version>` in `src/Directory.Build.props` (and the version at the top of this file), add the
release to [CHANGELOG.md](CHANGELOG.md), commit,
then tag and push, e.g. `git tag v0.6.3; git push origin v0.6.3`. The Release workflow builds the installer on GitHub
and publishes the release that installed copies update from. CI builds every push to `main`.

## Third-party

[NAudio](https://github.com/naudio/NAudio) (MIT), [softcam](https://github.com/tshino/softcam) (MIT, the virtual webcam; built with `tools/build-softcam.ps1` into `third_party/softcam`), [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (read over its web server if you run it; not included), [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT, Mini Mirror's screen capture and Gaming's recording), .NET 8 runtime (bundled), Inno Setup (installer). The Resizer is ported
from [Resize Rabbit](https://github.com/rosscarlson/resize-rabbit) and [Resize Raccoon](https://github.com/mistenkt/resize-raccoon)
by mistenkt (MIT). Voicemeeter and
its Remote API are VB-Audio's (installed separately, not shipped).
