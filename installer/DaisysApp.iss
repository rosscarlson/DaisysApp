; Inno Setup script for Daisy's App.
; Built by build.ps1 (locally and by the GitHub release workflow), which passes AppVersion and PublishDir.
; The in-app updater downloads this installer from GitHub Releases and runs it with /SILENT, or /VERYSILENT for a
; quick update (double-click Settings), which the Update task (Tasks.ps1) starts elevated with /FROMTASK.

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
; only programs using our .exe: a camera app (Discord, Teams...) showing the virtual webcam has its DLL open, and mustn't
; be closed for it (the DLL is replaced at the next restart instead)
CloseApplicationsFilter=*.exe
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
; since 0.22 Resizer, Mini Mirror and Gaming are modules\Video, and Performance, USB Monitor and Sensors modules\Hardware
Type: filesandordirs; Name: "{app}\modules\Resizer"
Type: filesandordirs; Name: "{app}\modules\MiniMirror"
Type: filesandordirs; Name: "{app}\modules\Gaming"
Type: filesandordirs; Name: "{app}\modules\Performance"
Type: filesandordirs; Name: "{app}\modules\UsbMonitor"
Type: filesandordirs; Name: "{app}\modules\Sensors"
; since 0.39 Zabbix and Logs are modules\Network
Type: filesandordirs; Name: "{app}\modules\Zabbix"
Type: filesandordirs; Name: "{app}\modules\Logs"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "\modules\Video\camera\*"
; the virtual webcam's DLLs: open in any camera app showing it, so replaced at the next restart when they're busy
Source: "{#PublishDir}\modules\Video\camera\*"; DestDir: "{app}\modules\Video\camera"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace uninsrestartdelete

; the Task Scheduler tasks for quick updates (registered at the end of the install)
Source: "Tasks.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; "Start when I sign in" is set by the app (Settings > General); remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppShortName}"; Flags: uninsdeletevalue

[UninstallRun]
; the quick-update tasks: removed
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Tasks.ps1"" -Remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveTasks"
; Ask a running (tray) instance to exit cleanly so it releases Voicemeeter and closes its log, then make sure it's gone.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitDaisysApp"
Filename: "{sys}\timeout.exe"; Parameters: "/t 2 /nobreak"; Flags: runhidden; RunOnceId: "WaitDaisysApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopDaisysApp"
; the virtual webcam: Windows forgets it (64- and 32-bit)
Filename: "{sys}\regsvr32.exe"; Parameters: "/u /s ""{app}\modules\Video\camera\x64\softcam.dll"""; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterCamera64"
Filename: "{syswow64}\regsvr32.exe"; Parameters: "/u /s ""{app}\modules\Video\camera\x86\softcam.dll"""; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterCamera32"

[UninstallDelete]
; quick updates' downloads and status
Type: filesandordirs; Name: "{app}\updates"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; the virtual webcam ("Daisy's App Camera"): registered with Windows, 64- and 32-bit, so camera apps of either kind find it
Filename: "{sys}\regsvr32.exe"; Parameters: "/s ""{app}\modules\Video\camera\x64\softcam.dll"""; Flags: runhidden waituntilterminated
Filename: "{syswow64}\regsvr32.exe"; Parameters: "/s ""{app}\modules\Video\camera\x86\softcam.dll"""; Flags: runhidden waituntilterminated
; the quick-update tasks (Update, elevated; Start, ordinary) for the user installing, in Task Scheduler's DaisysApp folder
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Tasks.ps1"" -App ""{app}\{#AppExe}"""; Flags: runhidden waituntilterminated; StatusMsg: "Setting up updates..."
; Interactive install: optional "Launch" checkbox on the last page.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Silent install (auto-update): relaunch the app as the signed-in user, not elevated.
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent runasoriginaluser; Check: not FromTask
; a quick update, started by the elevated Update task (so "original user" would be elevated too): the Start task runs it
; with ordinary rights instead
Filename: "{sys}\schtasks.exe"; Parameters: "/run /tn ""DaisysApp\Start"""; Flags: runhidden nowait skipifnotsilent; Check: FromTask

[Code]
// started by the Update task (DaisysApp.exe --install-update passes /FROMTASK)
function FromTask(): Boolean;
begin
  Result := Pos('/FROMTASK', UpperCase(GetCmdTail)) > 0;
end;
