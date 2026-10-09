; Inno Setup script for Daisy's App.
; Built by build.ps1 (locally and by the GitHub release workflow), which passes AppVersion and PublishDir.
; The in-app updater downloads this installer from GitHub Releases and runs it with /SILENT.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

#define AppName "Daisy's App"
#define AppShortName "DaisysApp"
#define AppExe "DaisysApp.exe"
#define RepoUrl "https://github.com/rosscarlson/DaisysApp"

[Setup]
; Never change AppId - upgrades and the auto-updater rely on it.
AppId={{D032C022-E821-4A06-B25E-8BF80C259CDD}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=rosscarlson
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Daisys App
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
OutputBaseFilename={#AppShortName}-Setup-{#AppVersion}
SetupIconFile=..\src\DaisysApp\Assets\DaisysApp.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; force: the app may be running in the tray (USB monitoring, Voicemeeter levels)
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; 0.8 kept only translations in modules\AudioLevel; since 0.9 that module is modules\AudioLeveler
Type: filesandordirs; Name: "{app}\modules\AudioLevel"
; since 0.15 Audio Tools (was AudioLeveler), Audio Levels and Audio Delay are one module, modules\AudioTools
Type: filesandordirs; Name: "{app}\modules\AudioLeveler"
Type: filesandordirs; Name: "{app}\modules\AudioLevels"
Type: filesandordirs; Name: "{app}\modules\AudioDelay"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; "Start when I sign in" is set by the app (Settings > General); remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppShortName}"; Flags: uninsdeletevalue

[UninstallRun]
; Ask a running (tray) instance to exit cleanly so it releases Voicemeeter and closes its log, then make sure it's gone.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitDaisysApp"
Filename: "{sys}\timeout.exe"; Parameters: "/t 2 /nobreak"; Flags: runhidden; RunOnceId: "WaitDaisysApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopDaisysApp"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Interactive install: optional "Launch" checkbox on the last page.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Silent install (auto-update): relaunch the app as the signed-in user, not elevated.
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent runasoriginaluser
