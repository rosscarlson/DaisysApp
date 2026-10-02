# Daisy's App

A tabbed Windows app that hosts small tools. Each tool is a tab; **Settings** is always the last tab, with a sub-tab
per tool and **General** (startup, system tray, theme, updates) at the end.

![Daisy's App](docs/screenshot.png)

| Tab | What it does |
|---|---|
| Audio Leveler | Calibrated test signals, per-speaker level knobs, microphone leveling, Voicemeeter bus levels |
| USB Monitor | Real-time log of device connect / disconnect / status changes, with a per-launch log file |

- Installs to `C:\Program Files\Daisys App` (installer: `DaisysApp-Setup-x.y.z.exe`, needs admin).
- Settings: `%APPDATA%\DaisysApp\` (`settings.json` for the app, one JSON file per tool).
- Logs: `%LOCALAPPDATA%\DaisysApp\logs\`.
- Command line: `--tray` starts hidden in the tray, `--exit` closes the running copy.
- Updates: checked against GitHub Releases on launch (banner when one exists) and from the **Check for updates**
  button, tray menu, or Settings → General. The installer is verified by SHA-256 and run silently.

## Adding a tool

1. Create `src/DaisysApp/Tools/<Name>/` with a class implementing `Shell/ITool.cs` (a view, an optional settings view).
2. Add one line to `Shell/ToolRegistry.cs`.

The shell supplies the tab, the Settings sub-tab, tray, startup and updates. Keep the tool's settings in its own file
with `Settings/JsonStore`. UI follows the design system in `design.md` (styles in `Themes/Controls.xaml`).

## Build and release

Requires the .NET 8 SDK and Inno Setup 6.

```powershell
dotnet build src\DaisysApp\DaisysApp.csproj
.\build.ps1                 # -> artifacts\DaisysApp-Setup-<version>.exe
```

To release: bump `<Version>` in `DaisysApp.csproj`, commit, then `git tag v0.2.0; git push origin v0.2.0`. The Release
workflow builds the installer and publishes the GitHub Release that installed copies update from. The repository must
be public for the updater to see releases.

## Third-party

[NAudio](https://github.com/naudio/NAudio) (MIT), .NET 8 runtime (bundled), Inno Setup (installer).
