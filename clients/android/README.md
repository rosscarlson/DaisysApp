# Button2Key Remote for Android

*The Android client for [Daisy's App](../../README.md)'s **Button2Key Remote**
([its guide](../../src/Modules/Butt2KeyRemote/README.md)): your ButtScreens on a phone or tablet. It speaks the protocol
in [API.md](../../API.md#button2key-remote) (version 1). Not part of Daisy's App's installer: it's an APK of its own, attached
to the Daisy's App release it came with (`Button2Key-Remote-<version>.apk`).*

**Shared with Daisy's App Mobile.** This code and its resources are also built into Daisy's App Mobile
(`D:\OneDrive\Code\Android\DaisysApp`, its `b2kremote/` module), as its Button2Key module: nothing is copied, so a
change here changes both apps. Build, test and publish both. A new activity or receiver goes in that project's
`b2kremote/src/main/AndroidManifest.xml` too. Inside it, `Updater.embedded()` is true and this app's own updates are
left out, because Daisy's App Mobile updates itself.

## Using it

1. On the PC: Daisy's App → Gaming → **Button2Key Remote**: switch it **On** and set a **Passphrase** under Devices
   (8 characters or more) and **Save**. The port is there too; the PC's IP address is in Windows' network settings
   (or `ipconfig`), if Network Scan doesn't find it.
2. On the device: install the APK (Android 6 or later; allow installing from your browser or file manager when Android
   asks), open **Button2Key Remote**, and **Network Scan** (then **Add** the one found) or **Add a PC**: its
   **Name**, **IP Address**, **Port** (38870 unless changed) and **Passphrase**. **Test** tries them before saving.
3. Tap the PC to connect. The **BigButt in use** on the PC shows, its first ButtScreen first; it follows the PC (a
   game starting switches it, as does picking one in the tab).

Connected:

- **Swipe** across the space between buttons (or **◀ ▶** at the top) to go through the BigButt's ButtScreens, in the
  order the tab gives them (round from the last to the first). A **Show a ButtScreen** button shows that one.
- **Press** buttons as on the PC's pop-outs: each finger holds its button until lifted, several at once; held buttons
  are let go if the connection drops or the app goes to the background.
- **The BigButts**: right after connecting (when the PC has more than one) the **BigButts** screen lists them, a card
  each with its ButtScreens and the one in use marked: tap one to use it on the PC (since 1.1.1; Settings can leave it
  out). **BigButts** at the top (or the BigButt's name, or ⋮) opens it any time, and a row of chips under the top bar
  has them too, the one in use lit. The BigButts screen follows how the device is held (since 1.1.2): the screen only
  turns to fit once a ButtScreen shows.
- **⛶ Full screen** shows only the ButtScreen, with a small **✕** in the corner chosen in Settings to leave it (Back
  leaves it too). Since 1.1.4 the ✕ fades away 3 seconds after full screen starts or after your last touch;
  **press and hold anywhere for 3 seconds** (without moving) and it fades back in for 5 seconds. A border
  in the ButtScreen's background keeps the buttons clear of the screen's rounded corners and
  camera (worked out from the device), plus the margin chosen in Settings.
- **⋮**: BigButts…, Full screen, Settings, Disconnect.
- The connection comes back by itself when it drops (the ButtScreen dims meanwhile); a wrong passphrase says so and
  offers **Edit this PC**.

**Settings**: this device's **Name** (what the PC's Devices list and the editor's sizes call it), **Start in full
screen**, **the Exit button's corner** (top left, top right, bottom left, bottom right) and **Exit needs a long press**,
**Turn the screen to fit each ButtScreen** (a wide one in landscape, a tall one in portrait), showing a ButtScreen's name
as it changes, **vibrate** on a press, **keep the screen on** while connected, **connect to the PC used last** when
the app starts, **show the BigButts to pick from after connecting**, full screen's **Border round the buttons** (None, Small, Medium, Large), and **Check for updates when
the app starts**.

**Updates** (since 1.1.0; on the PCs screen): the version, **Check for updates**, and **Update** when Daisy's App's
releases carry a newer APK: it downloads it (checked against the SHA-256 GitHub gives), and Android asks to confirm,
the first time also to let Button2Key Remote install apps (allow it, come back, and press Update again). It checks by
itself when the app starts. 1.0.0 hasn't this: install 1.1.0 by hand once.

Saved PCs are kept in the app's private storage, their passphrases encrypted with a key in the Android Keystore (and
left out of backups).

## Building

Needs JDK 17 and the Android SDK (platform 36, build tools); no Android Studio. On the PC this was made on they're in
`%USERPROFILE%\Android` (`jdk-17…`, `sdk`).

```powershell
cd clients\android
$env:JAVA_HOME = "$env:USERPROFILE\Android\jdk-17.0.20.1+1"
# local.properties (not in git): sdk.dir=C:/Users/<you>/Android/sdk
.\gradlew assembleDebug                      # unsigned debug build
.\gradlew assembleRelease -PB2KR_KEYSTORE=D:/OneDrive/Code/Windows/Butt2KeyRemote-signing/keystore.properties
adb install -r $env:TEMP\b2kr-build\app\outputs\apk\release\app-release.apk
```

The build goes to `%TEMP%\b2kr-build` (OneDrive, where the repo is, locks files Gradle replaces). Release builds are
signed with the key in `Butt2KeyRemote-signing\` beside the repo (not in git): keep it, since every update has to be
signed with the same key to install over the last. A new version: raise `versionCode` and `versionName` in
`app/build.gradle.kts`, and attach the APK as `Button2Key-Remote-<versionName>.apk` to that Daisy's App release
(`gh release upload`): installed apps find it there.

It targets API 35 rather than 36 on purpose: Android 16 ignores an app's screen orientation on tablets when it targets
36, and a ButtScreen should turn the screen to fit it.

## Code

`app/src/main/java/com/jasbone/butt2keyremote/`, plain Android views made in code (no libraries: the APK is under
100 KB):

| File | |
|---|---|
| `Wire.kt` | The connection: frames, PBKDF2-SHA256 (Android's own, or by hand so the passphrase is surely UTF-8), AES-256-GCM with the direction-and-count nonces (the tag moved to where the PC wants it), hello and welcome |
| `Remote.kt` | Keeps a connection to one PC while the screen is showing: connecting again after a drop, a ping every 5 seconds, sending in order, the PC's messages to the main thread |
| `Discovery.kt` | Finding PCs: `{"op":"discover"}` broadcast to the port (to 255.255.255.255 and each network's own broadcast address), the answers collected for 2 seconds |
| `Model.kt` | The state (the BigButt in use and its ButtScreens, every BigButt and ButtScreen) and a ButtScreen's layout, parsed from the PC's JSON; the grid's sums; colours |
| `SvgPath.kt` | SVG path data (the icon library's) into an `android.graphics.Path`, arcs included |
| `ScreenView.kt` | Draws a ButtScreen to fit, as the PC's `ScreenVisual` does (icon 45% / 55% of the smaller side, outline inside, pressed lighter), and turns touches into presses and swipes |
| `RemoteActivity.kt` | Connected: the bar (BigButt, PC, which ButtScreen, BigButts), the BigButts screen and chips, following `state` and `show`, fetching layouts when their `version` changes, turning to fit, full screen and the Exit button |
| `MainActivity.kt` | The PCs: the list, Network Scan, Add / Edit a PC (Test, and Delete as a two-click Confirm? button); the hello |
| `SettingsActivity.kt` | Settings |
| `Prefs.kt` | The PCs and settings (SharedPreferences), passphrases sealed with the Keystore |
| `Updater.kt` | Updates: the newest `Button2Key-Remote-<version>.apk` on Daisy's App's releases, downloading and checking it, PackageInstaller; `InstallReceiver` shows Android's confirmation |
| `Ui.kt` | Daisy's App's dark colours and small view builders, the Confirm? button |
