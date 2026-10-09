# Template — writing a module

*A module of [Daisy's App](../../../README.md): a hello-world example, and this guide. It's installed with the app but
switched off; tick **Template** in Settings → General → Applets (then **Restart now**) to see its tab and its Settings
page.*

Every tab in Daisy's App is a **module**: a .NET class library in its own folder under `src/Modules/`, built into its
own folder under `modules\` next to `DaisysApp.exe`. When the app starts it loads every module it finds there, makes a
tab for each applet in it, adds its Settings page and lists it under Settings → General → Applets, where it can be
switched off. Nothing else in the app needs to change to add one.

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

`src/Modules/Directory.Build.props` gives every folder here the same setup: .NET 8 with WPF, the assembly name
`DaisysApp.<folder>`, output to the app's `modules\<folder>\`, a reference to **DaisysApp.Core** (not copied: the app
has it), and its `lang\*.json` and `README.md` copied alongside. Add NuGet packages to the `.csproj` as usual: they're
copied into the module's own folder, and the app finds them there. (Packages the app already has — NAudio and
System.Text.Json 9 — come from the app, so all modules share one copy.)

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
  General → Applets (translated like any other text: `tools/strings.py` picks it up).
- **OnByDefault**: leave it out (true) for a normal module.

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

**Keep it light when nobody's looking.** The app runs in the tray all day, often next to games. Do nothing heavy when
the tab isn't showing (`IsVisible` on your view), and nothing that redraws a lot every second (a list that grew to
three times its rows once doubled the app's CPU use and made games stutter).

## What DaisysApp.Core gives you

- **Settings**: `JsonStore.Load<T>("Name")` / `JsonStore.Save("Name", value)` — `%APPDATA%\DaisysApp\Name.json`, one
  file per module (see `TemplateSettings.cs`).
- **Errors**: `ErrorLog.Write("what was happening", exception)` — `errors.log`, viewable from Settings → General.
- **Paths**: `AppPaths` (settings, logs and Documents folders).
- **Look**: the app's styles — `Card`, `CardHeader`, `SecondaryText`, `AccentButton`, `DangerButton`, `ChipToggle`,
  `ScrollPage`, the `EventGrid…` table styles, `IconFont` — and theme colours — `TextBrush`, `TextSecondaryBrush`,
  `AccentBrush`, `CardBrush`, `DangerBrush`, `SuccessBrush`, `ErrorTextBrush`… (use `{DynamicResource}` for colours so
  Dark / Light switches live). A module's own windows call `ThemeManager.ApplyTitleBar(this)` in `SourceInitialized`
  for a matching title bar. The design system is in `design.md` at the top of the repository.
- **Shared code**: `DaisysApp.Shared.Audio` (playback and recording devices, speaker layouts, mic volume),
  `DaisysApp.Shared.Voicemeeter` (Voicemeeter's Remote API, and the banner shown when it isn't running) and
  `DaisysApp.Shared.Hotkeys` (system-wide shortcuts, wheel / controller buttons, a shortcut box) and
  `DaisysApp.Shared.Hardware` (NVIDIA's GPU library, Windows' performance counters, HDR monitors' SDR brightness,
  and `SensorHub`, the app's one LibreHardwareMonitor connection), `DaisysApp.Shared.Charts` (`LineGraph`, the time
  graph Performance and Sensors use) and `DaisysApp.Shared.Csv`. Code two modules
  need goes in Core, never in another module: each module only depends on Core.

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
