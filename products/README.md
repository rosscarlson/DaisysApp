# Products

Apps of their own made from Daisy's App's program with only some modules: the same `DaisysApp.exe` under another
name (as the wrapper is), its modules installed beside it, with its own title, icon, installer, settings, logs,
layout and releases. Each lives in **its own repository**, which holds no module code, only what makes it that app
(`product.json`, its icon, CHANGELOG, the Daisy's App commit it's built from, its release workflow):

| Product | Repository (local) | Modules |
|---|---|---|
| SimEffects Analysis | [rosscarlson/SimEffectsAnalysis](https://github.com/rosscarlson/SimEffectsAnalysis) (`D:\OneDrive\Code\SimEffects Analysis`) | SimEffects (Analysis, Overlays), Video (Recording); Forza Horizon |

Its 0.2.0 and 0.2.1 were released from here (`products/SimEffectsAnalysis`, tags `sea-v0.2.0`, `sea-v0.2.1`, never
the latest release); from 0.2.2 its releases are in its own repository.

**Build:** `.\build.ps1 -Product <its folder>` → `artifacts\<Name>-Setup-<version>.exe` (self-contained; only that). A
name instead of a path still means `products\<Name>`. A Daisy's App release never rebuilds a product.

## The hooks

The build writes `product.json`'s keys into `app.json` beside the program, with `app.ico`; Core's `AppPaths.Product`
reads it at start. With it the program is the product. All of these are off in Daisy's App:

| Key | What it does | Read in |
|---|---|---|
| `Title` | the name wherever the app's shows (Core's `Loc` puts it in place of "Daisy's App" in every text) | `AppPaths.DisplayName`, `Loc.Named` |
| `TagPrefix`, `Repo` | its updater reads `Repo`'s releases (Daisy's App's if none) tagged `TagPrefix<version>` | `UpdateService` (`Releases`, `TagVersion`) |
| `Tabs` | its tabs, each a group's sub-tab (`Module/SubTab`) taken out of it, in order before Settings; the group's header (SimEffects' Play / Stop) goes left of Update | `MainWindow.AddProductTabs`, `AppletGroup.TakeView`/`TakeHeader` |
| `Hidden` | sub-tabs made but not shown (one a tab needs: Video's Overlay for Recording): no settings page, setup page or tray items. Any sub-tab in neither list isn't made | `AppletGroup.Add`, `ProductInfo.Makes`/`Shows` |
| `Game` | its one game: only that SimEffects profile, always in use, no Profile drop-down; its settings (Options and Modules) as the card `SimEffects/Profiles/Game` ("Forza Horizon Settings"); Forza Horizon's port 8000 (Daisy's App's 8001 moved once) | `SimProfiles.OnlyGame`, `ProfileCard.SettingsCard`, `Games.Port` |
| `Hide` | parts of a module left out, by name: `SimEffects/Analysis/KnownTracks`, `SimEffects/Analysis/CurrentLocation`, `SimEffects/Analysis/ShareAccount` (Share Manager's Account button), `SimEffects/Analysis/SharePort` (the account's Port: the one set is used), `Video/Recording/Benchmark`, `Video/Recording/History` (none logged) | `ProductInfo.Hides` (AnalysisView, SessionsView, AnalysisApplet, ShareWindow, AccountPanel, GamingView, GamingSettings…) |
| `Settings` | Settings on one page, no sub-tabs: rows, each of columns side by side, each column's cards stacked: General's (`Startup`, `Updates`, `Appearance`, `Notifications`, `Language`, `Shortcuts`, `Files`), a sub-tab's settings (`Video/Recording`), a card a module offers (`SimEffects/Analysis/Account`, `SimEffects/Profiles/Game`: Core's `SettingsCards`), `YouTube`. General's others aren't on it | `SettingsPage.OnePage`, `AppletGroup.TakeSettings` |
| `Ask` | what a module asks for once, on the first run: `SimEffects/Analysis/Account` (the ForzaLeaderBoard account) | `ProductInfo.Asks` (AnalysisApplet's onboarding) |

Always, in a product: its icon in the tray (`TrayIcon`; no tray items of the sub-tabs only in `Hidden`), no FPS overlay
when Video's Overlay isn't a tab (`GamingSettings.Load`), no setup tool picker (`SetupWindow`), Settings → General
without Save / Load, Modules, Daisy's Server, Audio or Storage (`SettingsPage`), no virtual webcam (Windows knows it as
Daisy's App's), every overlay unticked at first (Overlays, once: `InputOverlayApplet.Start`), Video's overlay and webcam shortcuts left out when those aren't there (`GamingService`).

**A new hook:** a switch that's off unless the product asks (`AppPaths.Product?.Hides("Module/Part") == true`, or a new
key in `ProductInfo` and `build.ps1`'s list of keys copied into app.json), added to the table above; then the
product's repository moves its `daisysapp.ref` to the commit.
