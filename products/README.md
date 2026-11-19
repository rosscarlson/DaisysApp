# Products

Apps of their own made from Daisy's App's program with only some modules: the same `DaisysApp.exe` under another
name (as the wrapper is), its modules installed beside it, with its own title, icon, installer, settings, logs and
releases. A folder each:

| File | |
|---|---|
| `product.json` | `Name` (the program's and installer's name), `Title` (what people see), `Version` (its own, apart from Daisy's App's), `TagPrefix` (its releases' tags), `Guid` (its installer's AppId: never change it), `Modules` (the folders under `src/Modules` it carries) |
| `icon.png`, `icon.ico` | its icon: the program's, the installer's and the tray's (`icon.ico` is made from `icon.png`, sizes 16–256) |
| `CHANGELOG.md` | its releases' notes, newest first |

**At run time** the program finds `app.json` beside it (Title, TagPrefix; written by the build) and `app.ico`: Core's
`AppPaths.Product`. With them it's the product: its title wherever the app's name shows (Core's `Loc` puts it in place
of "Daisy's App" in every text), its icon in the tray, no Daisy's Server card, no module picker; and its updater
(`UpdateService`) reads only releases tagged with its prefix. Video's virtual webcam isn't shipped in one (Windows
knows it as Daisy's App's).

**Build:** `.\build.ps1 -Product SimEffectsAnalysis` → `artifacts\SimEffectsAnalysis-Setup-<version>.exe` (self-contained).

**Release** (only when asked for that product): raise `Version` in its `product.json`, add its section to its
`CHANGELOG.md`, commit, then tag `<TagPrefix><Version>` and push it (`git tag sea-v0.2.1; git push origin sea-v0.2.1`).
`.github/workflows/release-product.yml` builds it and publishes a release that is **never marked latest**, so Daisy's
App's updater (which reads the latest release) never sees it, and Daisy's App's own tags (`v*`) never build a product.

| Product | Modules | Tags |
|---|---|---|
| SimEffects Analysis (`SimEffectsAnalysis/`) | SimEffects, Video | `sea-v0.2.0`… |
