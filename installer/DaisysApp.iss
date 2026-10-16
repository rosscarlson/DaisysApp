; Inno Setup script for Daisy's App, and for the wrapper made from it (the same program, named Wrapper.exe, with no
; modules: it downloads the ones picked, and it's framework-dependent, so NeedsDotNet gets Microsoft's .NET 8 Desktop
; Runtime first if it isn't installed).
; Built by build.ps1 (locally and by the GitHub release workflow), which passes AppVersion and PublishDir, and for the
; wrapper AppName, AppShortName (the program's name), AppGuid, AppDir and NeedsDotNet.
; The in-app updater downloads this installer from GitHub Releases and runs it with /SILENT, or /VERYSILENT for a
; quick update (the Update button), which the Update task (Tasks.ps1) starts elevated with /FROMTASK.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

#ifndef AppName
  #define AppName "Daisy's App"
#endif
#ifndef AppShortName
  #define AppShortName "DaisysApp"
#endif
; Never change an app's AppGuid - upgrades and the auto-updater rely on it.
#ifndef AppGuid
  #define AppGuid "D032C022-E821-4A06-B25E-8BF80C259CDD"
#endif
#ifndef AppDir
  #define AppDir "Daisys App"
#endif
#define AppExe AppShortName + ".exe"
#define RepoUrl "https://github.com/rosscarlson/DaisysApp"
; the virtual webcam comes with the Video module
#define HasCamera FileExists(PublishDir + "\modules\Video\camera\x64\softcam.dll")

[Setup]
AppId={{{#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=rosscarlson
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppDir}
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
; force: the app may be running in the tray (USB monitoring, Voicemeeter levels, controller mapping)
CloseApplications=force
; only programs using our .exe: a camera app (Discord, Teams...) showing the virtual webcam has its DLL open, and mustn't
; be closed for it (the DLL is replaced at the next restart instead)
CloseApplicationsFilter=*.exe
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

#if AppShortName == "DaisysApp"
[InstallDelete]
; (Daisy's App's own history: the wrapper has none)
; 0.8 kept only translations in modules\AudioLevel; since 0.9 that module is modules\AudioLeveler
Type: filesandordirs; Name: "{app}\modules\AudioLevel"
; since 0.15 Audio Tools (was AudioLeveler), Audio Levels and Audio Delay are one module, modules\AudioTools
Type: filesandordirs; Name: "{app}\modules\AudioLeveler"
Type: filesandordirs; Name: "{app}\modules\AudioLevels"
Type: filesandordirs; Name: "{app}\modules\AudioDelay"
; since 0.22 Mini Mirror and Gaming are modules\Video (the Resizer too until 1.1.68, when it became modules\Resizer
; again), and Performance, USB Monitor and Sensors modules\Hardware
Type: filesandordirs; Name: "{app}\modules\MiniMirror"
; modules\Gaming: Video's Gaming tool until 0.22; from 0.53 to 1.0 Joy2Key and ForzaWheel, each a module of its own
; (modules\Joy2Key, modules\ForzaWheel) since 1.1.68
Type: filesandordirs; Name: "{app}\modules\Gaming"
Type: filesandordirs; Name: "{app}\modules\Performance"
Type: filesandordirs; Name: "{app}\modules\UsbMonitor"
Type: filesandordirs; Name: "{app}\modules\Sensors"
; since 0.39 Zabbix and Logs are modules\Network
Type: filesandordirs; Name: "{app}\modules\Zabbix"
Type: filesandordirs; Name: "{app}\modules\Logs"
; since 0.55 only English ships: the Spanish, French and Portuguese files are removed (a language someone adds stays)
Type: files; Name: "{app}\lang\es.json"
Type: files; Name: "{app}\lang\fr.json"
Type: files; Name: "{app}\lang\pt.json"
Type: files; Name: "{app}\modules\AudioTools\lang\es.json"
Type: files; Name: "{app}\modules\AudioTools\lang\fr.json"
Type: files; Name: "{app}\modules\AudioTools\lang\pt.json"
Type: files; Name: "{app}\modules\Gaming\lang\es.json"
Type: files; Name: "{app}\modules\Gaming\lang\fr.json"
Type: files; Name: "{app}\modules\Gaming\lang\pt.json"
Type: files; Name: "{app}\modules\Hardware\lang\es.json"
Type: files; Name: "{app}\modules\Hardware\lang\fr.json"
Type: files; Name: "{app}\modules\Hardware\lang\pt.json"
Type: files; Name: "{app}\modules\Network\lang\es.json"
Type: files; Name: "{app}\modules\Network\lang\fr.json"
Type: files; Name: "{app}\modules\Network\lang\pt.json"
Type: files; Name: "{app}\modules\Template\lang\es.json"
Type: files; Name: "{app}\modules\Template\lang\fr.json"
Type: files; Name: "{app}\modules\Template\lang\pt.json"
Type: files; Name: "{app}\modules\Tools\lang\es.json"
Type: files; Name: "{app}\modules\Tools\lang\fr.json"
Type: files; Name: "{app}\modules\Tools\lang\pt.json"
Type: files; Name: "{app}\modules\Video\lang\es.json"
Type: files; Name: "{app}\modules\Video\lang\fr.json"
Type: files; Name: "{app}\modules\Video\lang\pt.json"
#endif

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "\modules\Video\camera\*"
#if HasCamera
; the virtual webcam's DLLs: open in any camera app showing it, so replaced at the next restart when they're busy
Source: "{#PublishDir}\modules\Video\camera\*"; DestDir: "{app}\modules\Video\camera"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace uninsrestartdelete
#endif

; the Task Scheduler tasks for quick updates (registered at the end of the install)
Source: "Tasks.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; "Start when I sign in" is set by the app (Settings > General); remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppShortName}"; Flags: uninsdeletevalue

[UninstallRun]
; the quick-update tasks: removed
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Tasks.ps1"" -Name ""{#AppShortName}"" -Remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveTasks"
; Ask a running (tray) instance to exit cleanly so it releases Voicemeeter and closes its log, then make sure it's gone.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitApp"
Filename: "{sys}\timeout.exe"; Parameters: "/t 2 /nobreak"; Flags: runhidden; RunOnceId: "WaitApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopApp"
#if HasCamera
; the virtual webcam: Windows forgets it (64- and 32-bit)
Filename: "{sys}\regsvr32.exe"; Parameters: "/u /s ""{app}\modules\Video\camera\x64\softcam.dll"""; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterCamera64"
Filename: "{syswow64}\regsvr32.exe"; Parameters: "/u /s ""{app}\modules\Video\camera\x86\softcam.dll"""; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterCamera32"
#endif

[UninstallDelete]
; quick updates' downloads and status
Type: filesandordirs; Name: "{app}\updates"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
#if HasCamera
; the virtual webcam ("Daisy's App Camera"): registered with Windows, 64- and 32-bit, so camera apps of either kind find it
Filename: "{sys}\regsvr32.exe"; Parameters: "/s ""{app}\modules\Video\camera\x64\softcam.dll"""; Flags: runhidden waituntilterminated
Filename: "{syswow64}\regsvr32.exe"; Parameters: "/s ""{app}\modules\Video\camera\x86\softcam.dll"""; Flags: runhidden waituntilterminated
#endif
; the quick-update tasks (Update, elevated; Start, ordinary) for the user installing, in a Task Scheduler folder named after the app
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Tasks.ps1"" -App ""{app}\{#AppExe}"" -Name ""{#AppShortName}"" -Title ""{#AppName}"""; Flags: runhidden waituntilterminated; StatusMsg: "Setting up updates..."
; Interactive install: optional "Launch" checkbox on the last page.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Silent install (auto-update): relaunch the app as the signed-in user, not elevated.
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent runasoriginaluser; Check: not FromTask
; a quick update, started by the elevated Update task (so "original user" would be elevated too): the Start task runs it
; with ordinary rights instead
Filename: "{sys}\schtasks.exe"; Parameters: "/run /tn ""{#AppShortName}\Start"""; Flags: runhidden nowait skipifnotsilent; Check: FromTask

[Code]
// started by the Update task (DaisysApp.exe --install-update passes /FROMTASK)
function FromTask(): Boolean;
begin
  Result := Pos('/FROMTASK', UpperCase(GetCmdTail)) > 0;
end;

#ifdef NeedsDotNet
// The wrapper runs on the .NET 8 Desktop Runtime (Daisy's App carries its own): downloaded from Microsoft and installed
// when it's missing, before anything is copied.
const
  RuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  RuntimeFile = 'windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

function DesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then FindClose(FindRec);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Getting .NET', 'Downloading the .NET 8 Desktop Runtime from Microsoft (once; other programs use it too)...', nil);
end;

function InstallRuntime(): Boolean;
var
  ResultCode: Integer;
begin
  DownloadPage.Clear;
  DownloadPage.Add(RuntimeUrl, RuntimeFile, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      DownloadPage.SetText('Installing the .NET 8 Desktop Runtime...', '');
      // 3010: installed, a restart is due; 1638: a newer one is already there
      Result := Exec(ExpandConstant('{tmp}\' + RuntimeFile), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
        and ((ResultCode = 0) or (ResultCode = 3010) or (ResultCode = 1638));
      if not Result then
        SuppressibleMsgBox('The .NET 8 Desktop Runtime couldn''t be installed (code ' + IntToStr(ResultCode) + '). Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again.', mbError, MB_OK, IDOK);
    except
      if not DownloadPage.AbortedByUser then SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not DesktopRuntimeInstalled() then Result := InstallRuntime();
end;

// (a silent install, an update, skips the pages: the runtime is there already, as the program it updates runs on it)
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not DesktopRuntimeInstalled() and not InstallRuntime() then
    Result := 'The .NET 8 Desktop Runtime is needed: install it from https://dotnet.microsoft.com/download/dotnet/8.0';
end;
#endif
