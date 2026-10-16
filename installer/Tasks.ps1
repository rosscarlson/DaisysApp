# Registers (or with -Remove, removes) the app's two tasks in Task Scheduler, in a folder named after it (\DaisysApp\,
# or the wrapper's, e.g. \Wrapper\), for the user installing it. Run by the installer (elevated).
#   Update: DaisysApp.exe --install-update, with the highest rights the user has. The app starts it for a quick update
#           (the Update button), so there's no admin question; it only installs the newest release it finds on
#           GitHub itself.
#   Start:  DaisysApp.exe with ordinary rights. The installer starts it after a quick update, so the app comes back as
#           the user rather than elevated.
param([string]$App = "", [string]$Name = "DaisysApp", [string]$Title = "Daisy's App", [switch]$Remove)

$ErrorActionPreference = "Stop"
$folder = "\$Name\"
foreach ($name in "Update", "Start") {
    try { Unregister-ScheduledTask -TaskPath $folder -TaskName $name -Confirm:$false -ErrorAction Stop } catch { }
}
if ($Remove) {
    try { $s = New-Object -ComObject Schedule.Service; $s.Connect(); $s.GetFolder("\").DeleteFolder($Name, 0) } catch { }
    exit 0
}
if (-not $App -or -not (Test-Path $App)) { exit 1 }

$user = if ($env:USERDOMAIN) { "$env:USERDOMAIN\$env:USERNAME" } else { $env:USERNAME }

# normal priority (a task's programs otherwise run below normal), on battery too
$updateSettings = New-ScheduledTaskSettingsSet -Priority 4 -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 1) -MultipleInstances IgnoreNew
$updatePrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
Register-ScheduledTask -TaskPath $folder -TaskName "Update" -Force `
    -Description "Installs the newest release of $Title from GitHub (started by the app: its Update button)." `
    -Action (New-ScheduledTaskAction -Execute $App -Argument "--install-update") `
    -Principal $updatePrincipal -Settings $updateSettings | Out-Null

$startSettings = New-ScheduledTaskSettingsSet -Priority 4 -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances Parallel
$startPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskPath $folder -TaskName "Start" -Force `
    -Description "Starts $Title again after an update, with ordinary rights." `
    -Action (New-ScheduledTaskAction -Execute $App) `
    -Principal $startPrincipal -Settings $startSettings | Out-Null
exit 0
