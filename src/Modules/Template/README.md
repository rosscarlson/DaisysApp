# Template — writing a module

*A module of [Daisy's App](../../../README.md): a hello-world example, and this guide. It's installed with the app but
switched off; tick **Template** in Settings → General → Modules (then **Restart now**) to see its tab and its Settings
page.*

Every tab in Daisy's App is a **module**: a .NET class library in its own folder under `src/Modules/`, built into its
own folder under `modules\` next to `DaisysApp.exe`. When the app starts it loads every module it finds there, makes a
tab for each applet in it, adds its Settings page and lists it under Settings → General → Modules, where it can be
switched off. Nothing else in the app needs to change to add one. List it in `src/Modules/modules.json` (in Daisy's App, offered
by the wrapper, or both; see [The wrapper](../../../README.md#the-wrapper)).

## Make your own

1. **Copy this folder** to `src/Modules/<YourName>/` (letters only, e.g. `ClipboardLog`) and rename
   `Template.csproj` to `<YourName>.csproj`. The folder's name is the module's name: the assembly is
   `DaisysApp.<YourName>.dll` and it's installed to `modules\<YourName>\`.
2. **Rename the code**: the namespace `DaisysApp.Applets.Template` → `DaisysApp.Applets.<YourName>` (also in the
   `x:Class` of each `.xaml`), and the classes as you like.
3. **Describe the applet** in the `[Applet]` attribute on its class (see below). Give it its own Id, and remove
   `OnByDefault = false` so it's on when installed.
4. **Add it to the solution**: `dotnet sln DaisysApp.sln add src/Modules/<YourName>/<YourName>.csproj`.
5. **Build and run**: `dotnet build DaisysApp.sln`, then run `src/DaisysApp` (F5 in Visual Studio). Modules build
   straight into the app's `bin\…\modules\` folder, so the new tab is there. `build.ps1` puts every module in
   `src/Modules` into the installer.

To give someone a module without a new installer: build it in Release and copy its folder
(`src/DaisysApp/bin/Release/net8.0-windows/modules/<YourName>/`) into `C:\Program Files\Daisys App\modules\`, then
restart the app.

## What's in a module

```
Template/
  Template.csproj            nothing needed in it: ../Directory.Build.props makes it a module
  TemplateApplet.cs          the applet: [Applet] attribute + IApplet
  TemplateView.xaml(.cs)     the tab's content
  TemplateSettingsView.xaml(.cs)   its page under Settings (optional)
  TemplateSettings.cs        its saved settings (%APPDATA%\DaisysApp\Template.json)
  lang/en.json …             its translations (made by tools/strings.py)
  README.md                  this file: each module documents itself in its own README
```

**Its documentation stays with it.** The module's `README.md` says what the tab does, lists its tools (each with a
link to its folder's `README.md`), its settings files and its Settings page; a tool's `README.md` says how to use it
and ends with a **Code** section (a table of its files and what each does). The main README only gives the module one
line in its table, linking here, so working on a module never means editing the app's documentation: only its own
guides, plus its line in `CHANGELOG.md` when it's released.

`src/Modules/Directory.Build.props` gives every folder here the same setup: .NET 8 with WPF, the assembly name
`DaisysApp.<folder>`, output to the app's `modules\<folder>\`, a reference to **DaisysApp.Core** (not copied: the app
has it), and its `lang\*.json` and `README.md` copied alongside. Add NuGet packages to the `.csproj` as usual: they're
copied into the module's own folder, and the app finds them there. (System.Text.Json 9, which the app already has,
comes from the app, so all modules share one copy.) A module that needs **DaisysApp.Shared** (audio devices,
Voicemeeter, hardware readings: below) references it in its `.csproj`, and gets it, with NAudio, in its own folder.

## The applet

```csharp
[Applet("Template", "Template", "\uE70F", Order = 900, OnByDefault = false,
    Description = "A hello-world example module, for building your own (see its README)")]
public sealed class TemplateApplet : IApplet
```

- **Id** (`"Template"`): stable for good. It names the settings file and the on/off setting, so don't change it once
  people use the module.
- **Title**: the tab's caption, and its Settings page's.
- **Icon**: a [Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font) glyph.
- **Order**: where the tab goes (lower is further left; the built-in ones are 1–50).
- **Description**: one short sentence saying what it does, shown in the setup wizard and under it in Settings →
  General → Modules (translated like any other text: `tools/strings.py` picks it up).
- **OnByDefault**: leave it out (true) for a normal module.
- **Group** and **GroupOrder** (optional): the id of a tab this applet shares with applets from other modules, and its
  sub-tab's place there (lower first). With two or more of the group installed they're one tab of sub-tabs; with one,
  it's a tab of its own, with no sub-tabs. A module's own `AppletGroup` with that id is the tab, and the applet joins
  it (the Resizer: `Group = "Video", GroupOrder = 0`, the first sub-tab of Video); otherwise the tab is described by
  `[assembly: TabGroup(id, title, icon, Order = …, Description = …)]`, which each of its modules compiles in (Gaming:
  `src/Modules/Joy2Key/GamingTab.cs`, linked into ForzaWheel's project). The group's id is the tab's for the on/off
  switch, tab order, tab name and remembered sub-tab. This is how one tool can be a module of its own (and an app of
  its own) and still sit on a shared tab in Daisy's App.

`IApplet` (in DaisysApp.Core, `DaisysApp.Shell`):

| Member | |
|---|---|
| `View` | The tab's content, created once and kept while the app runs. |
| `SettingsView` | Its page under Settings, or `null`. |
| `Start()` | Called once at launch, also when the app starts hidden in the tray: start background work here. |
| `SaveSettings()` | Called when the window is hidden and before `Dispose` on exit. |
| `Dispose()` | Stop everything. |
| `OnPreviewKeyDown(e)` | Optional: key presses while its tab is open. |
| `OnWindowHidden()` | Optional: the window went to the tray. |
| `TrayMenu` | Optional: items for a submenu in the tray icon's menu. |
| `NeedsOnboarding` / `RunOnboarding(owner)` | Optional: the module's own first-run setup. The app asks `NeedsOnboarding` once the window first shows (after its own setup wizard, if that's due); return true when the module has never been set up or something it needs is missing (it decides, from its own settings), and show your wizard in `RunOnboarding`, owned by `owner`. Record that it's done in your own settings. |
| `SetupPage` | Optional: a page for the app's setup wizard, for what the user can set up or choose (Gaming's permission to count frames, Network's Zabbix server, say); `null` when there's none. The full wizard (after installing, and from Settings) shows it whenever there is one. Asked each time the wizard runs; the page does its own work (buttons, checkboxes). A module someone may not want can offer to switch itself off with `AppNavigation.SwitchOff(id)` (in the wizard its row goes off; elsewhere it's saved and a restart offered); a tool in a group can't be switched off by itself that way, but can keep its own on/off setting and have the group hide its sub-tab (`AppletGroup.ShowTool`, as Network does for Zabbix). |
| `SetupRequired` | Optional: true when a setting the module can't do without is missing (Zabbix on but never set up): the app then opens the wizard at start with only the pages of the modules that need it (`RequiredSetupPage`, which is `SetupPage` when this is true). Leave it false for anything optional, so the wizard doesn't open for it. |

**Several tools in one tab.** A module whose tools belong together (Audio, Video, Hardware) derives its applet from
`AppletGroup` (DaisysApp.Core) instead: each tool is an `IApplet` without an `[Applet]` attribute, in its own
folder, added with `Add(id, title, icon, () => new ToolApplet())` in the group's constructor. The group shows them as
sub-tabs (remembering the last one in `<Id>.json`), puts their Settings pages under its own as sub-tabs (the tray menu shows each tool's items at the top level, under
the tool's name), and starts, saves and stops them all. See `src/Modules/Video/VideoApplet.cs`. Such tools come and
go with the module; a tool that should also stand alone is better a module of its own with a **Group** (above).

**Keep it light when nobody's looking.** The app runs in the tray all day, often next to games. Do nothing heavy when
the tab isn't showing (`IsVisible` on your view), and nothing that redraws a lot every second (a list that grew to
three times its rows once doubled the app's CPU use and made games stutter).

### Sub-modules (switched on or off in Settings)

A module with several tools on sub-tabs derives from `AppletGroup` and adds each with `Add(id, title, icon, create)`:
each one is a **sub-module** in Settings → General → Modules, and one that's switched off isn't made at all (`needs:`
names a tool it can't work without, as Video's Recording needs the Overlay). A module that builds its own sub-tabs
(Audio) asks Core's `ToolSwitches`: `ToolSwitches.Declare(moduleId, toolId, title)` for each tool, then makes it only
if `ToolSwitches.IsOn(moduleId, toolId)`. Switches take effect at the next start.

## What DaisysApp.Core gives you

- **Settings**: `JsonStore.Load<T>("Name")` / `JsonStore.Save("Name", value)` — `%APPDATA%\DaisysApp\Name.json`, one
  file per module (see `TemplateSettings.cs`).
- **Logging**: `Log.Here.Info("what happened")`, `.Warn`, `.Error("what failed", exception)` and
  `.Debug(() => "detail")` (only built when Settings → General → Logging is Debug) write to the module's own log,
  `<Module>-MM-dd-yy.log`, shown in Support → Logs. `ErrorLog.Write("what was happening", exception)` does the same for
  errors. Log what the module does at Info (started, a device or profile chosen, an action run) and the detail at Debug;
  never log from a loop that runs many times a second.
- **Paths**: `AppPaths` (settings, logs and Documents folders).
- **Look**: the app's styles — `Card`, `CardHeader`, `SecondaryText`, `AccentButton`, `DangerButton`, `ChipToggle`,
  `ScrollPage`, the `EventGrid…` table styles, `IconFont` — and theme colours — `TextBrush`, `TextSecondaryBrush`,
  `AccentBrush`, `CardBrush`, `DangerBrush`, `SuccessBrush`, `ErrorTextBrush`… (use `{DynamicResource}` for colours so
  Dark / Light switches live). A module's own windows call `ThemeManager.ApplyTitleBar(this)` in `SourceInitialized`
  for a matching title bar. The design system is in `design.md` at the top of the repository.
- **Widgets**: `new Widget(place, save, delete) { Body = yourControl }` (in `DaisysApp.Shared.Widgets`) pops
  something out into a small window of its own, as every widget in the app: dragged and stretched while it's edited
  (just made, or right-click), opacity, On top, **Done**, **Delete**; `place` is an `IWidgetPlace` in your settings
  (where it is, physical pixels), `AddOption` adds your own control to its edit strip, `Aspect` keeps its shape as
  it's stretched, `card: false` when your control draws its own background (`opacity: false, stacked: true`: no
  Opacity slider, and the strip in rows: `AddSlider`'s sliders, On top and the options, Done and Delete at the bottom
  right). `Showing = false` hides one while your module is off and `true` brings it back (it isn't closed or
  deleted; one being edited stays until Done). To offer one, `Widget.Offer(element,
  make)` adds **Make Widget** to an element's right-click menu (or `Widget.MenuItem(make)` in a menu of yours), so
  every module says it the same way. Never write a widget window of your own.
- **Shortcuts**: `Hotkeys.Add(new HotkeySpec { Id = "MyModule.Start", Module = T("My Module"), Name = T("Start /
  stop"), Get = () => settings.Key, Set = v => { settings.Key = v; settings.Save(); } }, Toggle)` (in
  `DaisysApp.Shared.Hotkeys`) is all a shortcut needs: Core registers the key combination system-wide or listens for
  the controller button, pauses every shortcut while one is being picked, warns when another program has it, and
  lists it in Settings → Shortcuts. The value stays in your settings. `Toggle` is called on the UI thread. Keep the
  `Hotkey` it returns and `Dispose` it when what it's for goes away; for one per thing (a device, a profile), call
  `Hotkeys.Replace("MyModule.", items)` whenever the things change. `Plain = true` allows a key by itself, `Buttons =
  false` keys only, `Default` gives Reset a value, `Where` says where it works, and `hotkey.Enabled = false` keeps it
  listed but unregistered (while a window of yours that uses it is closed). To set one: `new ShortcutBox(hotkey)`
  (it stores the value and says under itself when it's taken or shared), or `HotkeyWindow.Open(title, owner,
  hotkeys…)` for a small window (a set binding stays shown until its × clears it, which listens; one binding with none set listens as it opens; Done closes). `Hotkeys.Press` numbers each
  press, for bindings sharing a key that should act once between them.
- **Confirm?**: `ConfirmButton.Make(T("Delete"), Delete)` (in `DaisysApp.Shared`) for anything that can't be undone,
  or `ConfirmButton.Arm(button, Delete)` for a button you already have; `skip` makes the first click do it when
  there's nothing to lose, and what `Arm` returns puts it back (another item picked). Never a question window.
- **Shared code** in Core: `DaisysApp.Shared.Hotkeys` (the above, and its pieces: `HotkeyManager` for system-wide
  keys, `ControllerButtons` for wheel / controller buttons), `DaisysApp.Shared.Charts` (`LineGraph`, the time graph
  Performance and Sensors use), `DaisysApp.Shared.Network` (`SpeedTester`, the speed test Performance and Support
  run, reading an `ISpeedTestSettings` of the module's) and `DaisysApp.Shared.Csv`.
- **More shared code** in **DaisysApp.Shared** (`src/DaisysApp.Shared`; reference it from your `.csproj`):
  `DaisysApp.Shared.Audio` (playback and recording devices, speaker layouts, mic volume, with NAudio),
  `DaisysApp.Shared.Voicemeeter` (Voicemeeter's Remote API, and the banner shown when it isn't running) and
  `DaisysApp.Shared.Hardware` (NVIDIA's GPU library, Windows' performance counters, HDR monitors' SDR brightness,
  and `SensorHub`, the app's one LibreHardwareMonitor connection). It's kept out of Core so the wrapper, with
  modules that don't use it, doesn't carry it.
- **The title bar and notifications**: `TitleBadges.Set("MyModule", "Running", tip, color)` shows a bubble after the
  app's name and version while something of yours is running (green, or `TitleBadges.Amber` / `TitleBadges.Red`, or a colour
  of your own such as the AI's status colours; null text removes it; a red one turns the tray icon red; ForzaWheel, Button2Key and AI use it, each with a setting to leave it out).
  Clicking the bubble opens the module or sub-tab whose id is the bubble's id; when that isn't one, say which with
  `TitleBadges.Set(id, text, tip, color, open)`: a module's or sub-tab's id, or `"Module/SubTab"` (the Webcam's is
  `"Video/Gaming"`; null opens nothing).
  `Notifications.Show(title, text)` raises a Windows notification from the tray icon (a notice in the window without
  one). Both are in `DaisysApp.Shell` and can be called from any thread.
- **Where your data goes**: files a module keeps adding to (history, sessions) belong in `AppPaths.LogFolder` or
  `%LOCALAPPDATA%\<app>\`. Add the folder to the list in `src/DaisysApp/Shell/Storage.cs`, so Settings → General →
  Storage shows it and can clean it up.
- **A job as administrator**: `ModuleTasks.RunAsync<T>(args, elevated: true)` runs a class of yours that implements
  `IModuleTask` in a second copy of DaisysApp.exe (Windows asks for permission first), and gives back its exit code;
  results go back through a file. Tools → Disk Usage reads a drive's file table this way (`FileTableTask`). Code two modules
  need goes in Core (or DaisysApp.Shared, if it needs packages), never in another module: each module only depends on
  those. Game controllers are **DaisysApp.Controllers** (`src/DaisysApp.Controllers`, referenced as Shared is):
  `GameControllers.List()` (made once), `Open`, `RefreshButton` and `Changed`, one DirectInput and one lock for every
  module; never list them on a timer. Watching for a game or program is Core's `Shared/RunningPrograms` (`Get()` /
  `GetAsync()`: one process list for every module, `Has`, `FirstOf`, `PidsOf`; `Foreground()`; `Windowed()` for a
  picker), never a module's own `Process.GetProcesses()` on a timer.

## Translations

All text a module shows goes through the translation helpers, with the English text as the key:

```csharp
T("Ready.")                                   // a text
F("Hello, {0}!", name)                        // a text with values in it: {0}, {1:0.0}…
P(count, "Clicked {0} time", "Clicked {0} times")   // a count: singular or plural
```

```xml
xmlns:l="clr-namespace:DaisysApp;assembly=DaisysApp.Core"
<TextBlock Text="{l:Tr 'Click me'}"/>          <!-- in XAML; escape ' as \' -->
```

They're found by the module's folder, so each module has its own `lang\` folder. After adding or changing text, run
`python tools/strings.py`: it rewrites `lang/en.json` from the code and reports what each other language is missing
(`--missing es` lists it). A translation file maps each English text to its translation; anything missing stays in
English. See [Translations](../../../README.md#translations) in the main README.
