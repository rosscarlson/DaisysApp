# Video: Fire TV

*A tool in [Daisy's App](../../../../README.md)'s [Video](../README.md) tab: its **Fire TV** sub-tab (since 3.8.0). Switch Video, or just this sub-module, on or off in Settings → General → Modules. Its code is in this folder, part of the Video module (installed to `modules\Video\` next to `DaisysApp.exe`).*

Debloats an Amazon Fire TV over the network: its ads, tracking and Amazon's own apps removed, the apps you use left
alone, and a few tweaks. Everything it does can be put back. It speaks ADB itself, so nothing else needs installing
(no Android tools), and it works the same on a friend's PC: install Daisy's App (or the wrapper with Video), open
Video → Fire TV.

## Which Fire TVs

Every Fire TV that runs **Fire OS** (Amazon's Android) has ADB, so this works on it:

| Model | Code | Fire OS |
|---|---|---|
| Fire TV Stick 4K (1st gen, 2018) | AFTMM | 6, then 7 |
| Fire TV Stick 4K Max (1st gen, 2021) | AFTKA | 7 |
| Fire TV Stick 4K (2nd gen, 2023) | AFTKM | 8 |
| Fire TV Stick 4K Max (2nd gen, 2023) | AFTKRT | 8 |
| Fire TV Stick (3rd gen, 2020), Fire TV Stick Lite | AFTSSS, AFTSS | 7 |
| Fire TV Cube (2nd gen, 2019; 3rd gen, 2022) | AFTR, AFTGAZL | 7 |
| Older sticks, boxes and the 1st-gen Cube | AFTM, AFTT, AFTB, AFTS, AFTN, AFTA | 5–6 |
| TVs with Fire TV built in (Fire TV Edition, Omni) | various | 6–8 |

**Not** the **Fire TV Stick 4K Select** (2025) or any later stick: they run **Vega OS** (not Android), with no ADB
to connect to. The model code shows once it's connected (its name, if this table has it).

The table's codes and Fire OS versions come from memory and public lists. Only the emulator has been tried, not a
real Fire TV yet.

## On the TV first

1. **Settings → My Fire TV → About**: press **OK** on the device's name (*Fire TV Stick 4K*…) 7 times, until it says
   you're a developer. (On older Fire OS, **Developer Options** is already there.)
2. **Settings → My Fire TV → Developer Options → ADB Debugging: On.**
3. Its IP address is in **Settings → My Fire TV → About → Network**.

## Using it

- **Find** looks for Fire TVs on this PC's networks: every address of each one (up to 254) tried on ADB's port,
  5555, and a DIAL search (what casting uses), which Fire TVs answer with their name even when ADB is off. Each
  one found is a button: *Living Room · 192.168.1.20 · ADB on*. Or type the address (`192.168.1.20`, or `ip:port`)
  and **Connect** (or Enter).
- The first time, the TV asks **Allow USB debugging?** for this PC: tick **Always allow from this computer**, then
  **OK**. This PC's key is in `%APPDATA%\DaisysApp\FireTv\adbkey`; if the PC has adb's own key
  (`%USERPROFILE%\.android\adbkey`), it's tried too, so a TV that already knows it doesn't ask.
- **No prompt on the TV?** Some Fire TVs don't show it until ADB Debugging is turned off and on again. If nothing
  answers for a minute, a small window walks through it on the TV, a step at a time (**Next**, **Quit**): Settings,
  My Fire TV, Developer Options, ADB Debugging Off, then On, the Home screen; its last step, **Connect**, connects
  again so the prompt shows (since 3.8.2).
- **Apps**: everything on the TV that can go, by group: **Installed apps** (yours), **Ads and tracking**, **Amazon
  apps**, **System updates**, and (with **Show System**) Fire OS's other parts. A tick means *on*: ticked apps stay
  (or come back), unticked ones go (since 3.8.2; 3.8.0's tick meant remove). The ticks show the TV as it is, except
  on a TV this tool hasn't changed yet, where what's advised to go starts unticked: ads, tracking, Amazon's apps and
  the apps you installed, but the ones you keep (Plex, F1 TV and YouTube TV at first; ticking or unticking any of your
  apps keeps or drops it next time, on any TV). Optional ones (Alexa, casting, Live TV, the screensaver, system
  updates) and the **Appstore** (without it, your apps don't update) start ticked. A removed or disabled app shows as
  such, unticked. Hover a name for its package and a note.
- **Apply (n)** makes the TV as ticked, one by one: unticked apps removed, ticked removed ones put back; *n* is how
  many it changes. When it would delete one of your own apps, it asks **Confirm?** first. **Done** lists what
  happened.
- **Restore All** (Confirm?) puts back everything this tool removed or changed on that TV.
- **Tweaks**, applied as soon as they're ticked, and put back when unticked:
  - **Ad-blocking DNS**: the TV's Private DNS set to [AdGuard DNS](https://adguard-dns.io/) (`dns.adguard-dns.com`),
    which blocks ad and tracking servers for every app (YouTube TV's ads, which come with the video, stay). If the
    TV loses the internet with it on, untick it.
  - **Faster animations**: Android's window, transition and animator scales at 0.5.
- **Restart TV**, **Disconnect**.

**What a removal is.** Amazon's apps are removed for the TV's user (`pm uninstall --user 0`): gone from the TV, but
their file stays in the system, so ticking them and **Apply** brings them back (`cmd package install-existing`), and a
**factory reset** brings everything back. Where Fire OS doesn't allow that, the app is disabled instead
(`pm disable-user`), and ticking it enables it. **Your own apps** (installed from the Appstore) are really
uninstalled: they leave the list, and only the Appstore brings them back. A Fire OS update can bring Amazon's apps
back too: untick **System updates** in Apps to stop updates (Amazon's security updates too), or Apply again after one.

**Never offered**: the home screen, Settings, the keyboard, the remote and Bluetooth, the setup screens, WebView,
Android's own parts, and anything else named like a part the TV can't work without (`Catalog.Locked`). A Fire TV
can't take another home screen (Fire OS 7.6 and later bring Amazon's back), so the home screen's own rows (*Sponsored*
and the big banner) stay, but with the ads and recommendations services gone they have less to show.

**By hand on the TV** (no setting ADB can reach on every Fire OS):
- **Settings → Preferences → Featured Content**: video and audio autoplay off.
- **Settings → Preferences → Privacy Settings**: Device Usage Data, Collect App Usage Data and Interest-based Ads off.
- **Settings → Preferences → Data Monitoring**: off.
- **Settings → Applications → Appstore**: Automatic Updates stay on (your apps' updates).

## Files and log

`FireTv.json` in `%APPDATA%\DaisysApp\`: the last address, the apps you keep, and per TV (by serial number) whether
Apply has run on it, what was removed and how, and the settings' values before (for Restore All). The Video log has each connection (model, Fire
OS), what was removed or put back, and what the TV said when it refused.

## Code

| File | What it is |
|---|---|
| `FireTvApplet.cs`, `FireTvView.cs` | The sub-tab: the device, the apps (ticked = on; Apply), the tweaks and what was done |
| `PromptWizard.cs` | The steps on the TV when its prompt doesn't show (ADB Debugging off and on), ending in Connect |
| `Adb.cs` | ADB over TCP (`AdbClient`: the messages, signing in with RSA keys, sending our public key the first time, shell commands one at a time) and the keys (`AdbKeys`: ours, adb's, and the public key in Android's format) |
| `FireTvDevice.cs` | A connected TV: model, Fire OS, serial; its packages (`pm list packages`), removing (`uninstall --user 0`, else `disable-user`), putting back, settings |
| `Finder.cs` | Find: port 5555 on the local subnets and an SSDP search for DIAL devices, with their names |
| `Catalog.cs` | The models by code, the packages known (name, group, advice) and the parts never offered |
| `FireTvSettings.cs` | `FireTv.json` |
