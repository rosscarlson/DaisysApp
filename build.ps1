<#
  Builds a release: the two downloads and the modules the wrapper downloads, all from the same source.
    - Daisy's App: self-contained (the .NET runtime inside, nothing else to install), with every module that
      src\Modules\modules.json puts in it, in modules\<name>\ beside it. -> DaisysApp-Setup-<version>.exe
    - The wrapper ($WrapperName): the same program with no modules, framework-dependent (its installer gets
      Microsoft's .NET 8 Desktop Runtime if it isn't there), so it's small; it downloads the modules you pick.
      -> <WrapperName>-Setup-<version>.exe
    - Each module modules.json offers the wrapper, zipped: Module-<name>-<version>.zip, and the list of them with
      what each is, its size and SHA-256: Modules-<version>.json (written by the app itself, from the modules' DLLs).
  Usage:  .\build.ps1                 # version from src\Directory.Build.props
          .\build.ps1 -Version 0.2.0  # override (the release workflow passes the tag)
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

# The wrapper: its program's name (and its installer's, settings folder's and update tasks'), its name in Windows, and
# its installer's AppId (never change the Guid once released; renaming the wrapper later keeps it).
$WrapperName = 'Wrapper'
$WrapperTitle = 'Wrapper'
$WrapperGuid = '00491034-12CC-4C0A-9521-36F4FFE15972'

$proj = Join-Path $root 'src\DaisysApp\DaisysApp.csproj'
if (-not $Version) {
    $Version = [regex]::Match((Get-Content (Join-Path $root 'src\Directory.Build.props') -Raw), '<Version>(.+?)</Version>').Groups[1].Value
}

$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$built = Join-Path $artifacts 'modules'
$wrapperDir = Join-Path $artifacts "publish-$WrapperName"
foreach ($d in @($publish, $built, $wrapperDir)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $artifacts | Out-Null
Get-ChildItem $artifacts -File | Where-Object { $_.Name -like 'Module-*.zip' -or $_.Name -like 'Modules-*.json' -or $_.Name -like '*-Setup-*.exe' } | Remove-Item -Force

# which module goes where (a folder that isn't listed goes in both)
$list = (Get-Content (Join-Path $root 'src\Modules\modules.json') -Raw | ConvertFrom-Json).Modules
function Listed([string]$name, [string]$what) {
    $m = $list | Where-Object { $_.Name -eq $name }
    if (-not $m) { return $true }
    return [bool]$m.$what
}

# ---- Daisy's App: self-contained
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# ---- the wrapper: the same program, framework-dependent
dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $wrapperDir
if ($LASTEXITCODE) { throw 'dotnet publish of the wrapper failed' }
Move-Item (Join-Path $wrapperDir 'DaisysApp.exe') (Join-Path $wrapperDir "$WrapperName.exe")

# ---- the modules: every one built once, into artifacts\modules\<name>
# What the app already carries (DaisysApp.Core and its packages): a module's copies of these are left out.
dotnet build $proj -c Release -p:Version=$Version | Out-Null
if ($LASTEXITCODE) { throw 'dotnet build of the app failed' }
$appHas = Get-ChildItem (Join-Path $root 'src\DaisysApp\bin\Release\net8.0-windows') -Filter *.dll | ForEach-Object Name

$names = @()
foreach ($module in Get-ChildItem (Join-Path $root 'src\Modules') -Filter *.csproj -Recurse) {
    $name = $module.Directory.Name
    if (-not ($list | Where-Object { $_.Name -eq $name })) { Write-Warning "$name isn't in src\Modules\modules.json: it goes in both" }
    dotnet build $module.FullName -c Release -p:Version=$Version -p:DebugType=none "-p:ModulesOut=$built"
    if ($LASTEXITCODE) { throw "dotnet build of $name failed" }
    $dir = Join-Path $built $name
    Get-ChildItem $dir -File | Where-Object { $appHas -contains $_.Name -or $_.Name -like '*.runtimeconfig.json' -or $_.Extension -eq '.pdb' } |
        Remove-Item -Force
    # (debug files a package carries in its subfolders, e.g. a native DLL's)
    Get-ChildItem $dir -Recurse -Filter *.pdb | Remove-Item -Force
    # a package's WebAssembly build (System.Text.Encodings.Web brings one) has no use on Windows
    $browser = Join-Path $dir 'runtimes\browser'
    if (Test-Path $browser) { Remove-Item $browser -Recurse -Force }
    $runtimes = Join-Path $dir 'runtimes'
    if ((Test-Path $runtimes) -and -not (Get-ChildItem $runtimes -Recurse -File)) { Remove-Item $runtimes -Recurse -Force }
    $names += $name
}

# Daisy's App's modules
New-Item -ItemType Directory -Force (Join-Path $publish 'modules') | Out-Null
foreach ($name in $names | Where-Object { Listed $_ 'DaisysApp' }) { Copy-Item (Join-Path $built $name) (Join-Path $publish 'modules') -Recurse }

# the wrapper's: a zip each, and the list
Add-Type -AssemblyName System.IO.Compression.FileSystem
$offered = @($names | Where-Object { Listed $_ 'Wrapper' })
foreach ($name in $offered) {
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $built $name), (Join-Path $artifacts "Module-$name-$Version.zip"), 'Optimal', $false)
}
$catalog = Join-Path $artifacts "Modules-$Version.json"
$p = Start-Process (Join-Path $publish 'DaisysApp.exe') -Wait -PassThru -ArgumentList (@('--write-catalog', "`"$built`"", "`"$artifacts`"", $Version, "`"$catalog`"") + $offered)
if ($p.ExitCode -or -not (Test-Path $catalog)) {
    $why = if (Test-Path "$catalog.error.txt") { Get-Content "$catalog.error.txt" -Raw } else { "exit code $($p.ExitCode)" }
    throw "Writing the list of modules failed: $why"
}
Get-Item $catalog

# ---- the installers
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup)' }

& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$artifacts" (Join-Path $root 'installer\DaisysApp.iss')
if ($LASTEXITCODE) { throw 'Inno Setup compile failed' }
Get-Item (Join-Path $artifacts "DaisysApp-Setup-$Version.exe")

& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$wrapperDir" "/O$artifacts" "/DAppName=$WrapperTitle" "/DAppShortName=$WrapperName" `
    "/DAppGuid=$WrapperGuid" "/DAppDir=$WrapperTitle" "/DNeedsDotNet=1" (Join-Path $root 'installer\DaisysApp.iss')
if ($LASTEXITCODE) { throw 'Inno Setup compile of the wrapper failed' }
Get-Item (Join-Path $artifacts "$WrapperName-Setup-$Version.exe")
