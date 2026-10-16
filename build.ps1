<#
  Builds a self-contained release and the Inno Setup installers: Daisy's App (one exe, with DaisysApp.Core inside, and
  every module in src\Modules in modules\<name>\ beside it), then each one-tool app in $OneToolApps: the same exe named
  after the app, with only its modules beside it (the app builds its tabs from the modules it finds).
  Usage:  .\build.ps1                 # version from src\Directory.Build.props
          .\build.ps1 -Version 0.2.0  # override (the release workflow passes the tag)
  Output: artifacts\DaisysApp-Setup-<version>.exe, artifacts\Joy2Key-Setup-<version>.exe, ...
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

# The one-tool apps: a module (or a few) shared on its own. Name is the program's name (and the installer's, the
# settings folder's and the update tasks'); Guid is its installer's AppId: never change one once released.
$OneToolApps = @(
    @{ Name = 'Joy2Key'; Modules = @('Joy2Key'); Guid = 'A6292469-8577-4082-BE42-803C83FA3622' },
    @{ Name = 'Resizer'; Modules = @('Resizer'); Guid = '8148AE09-9C05-4D49-AEB3-F46B88664BF2' }
)
$proj = Join-Path $root 'src\DaisysApp\DaisysApp.csproj'
if (-not $Version) {
    $Version = [regex]::Match((Get-Content (Join-Path $root 'src\Directory.Build.props') -Raw), '<Version>(.+?)</Version>').Groups[1].Value
}

$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$modulesOut = Join-Path $publish 'modules'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# What the app already carries (DaisysApp.Core and its packages): a module's copies of these are left out.
dotnet build $proj -c Release -p:Version=$Version | Out-Null
if ($LASTEXITCODE) { throw 'dotnet build of the app failed' }
$appHas = Get-ChildItem (Join-Path $root 'src\DaisysApp\bin\Release\net8.0-windows') -Filter *.dll | ForEach-Object Name

foreach ($module in Get-ChildItem (Join-Path $root 'src\Modules') -Filter *.csproj -Recurse) {
    dotnet build $module.FullName -c Release -p:Version=$Version -p:DebugType=none "-p:ModulesOut=$modulesOut"
    if ($LASTEXITCODE) { throw "dotnet build of $($module.BaseName) failed" }
    $dir = Join-Path $modulesOut $module.Directory.Name
    Get-ChildItem $dir -File | Where-Object { $appHas -contains $_.Name -or $_.Name -like '*.runtimeconfig.json' -or $_.Extension -eq '.pdb' } |
        Remove-Item -Force
    # a package's WebAssembly build (System.Text.Encodings.Web brings one) has no use on Windows
    $browser = Join-Path $dir 'runtimes\browser'
    if (Test-Path $browser) { Remove-Item $browser -Recurse -Force }
    $runtimes = Join-Path $dir 'runtimes'
    if ((Test-Path $runtimes) -and -not (Get-ChildItem $runtimes -Recurse -File)) { Remove-Item $runtimes -Recurse -Force }
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup)' }

& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$artifacts" (Join-Path $root 'installer\DaisysApp.iss')
if ($LASTEXITCODE) { throw 'Inno Setup compile failed' }
Get-Item (Join-Path $artifacts "DaisysApp-Setup-$Version.exe")

foreach ($app in $OneToolApps) {
    $name = $app.Name
    $dir = Join-Path $artifacts "publish-$name"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $dir 'modules') | Out-Null
    Copy-Item (Join-Path $publish 'DaisysApp.exe') (Join-Path $dir "$name.exe")
    Copy-Item (Join-Path $publish 'lang') $dir -Recurse
    foreach ($m in $app.Modules) {
        $from = Join-Path $modulesOut $m
        if (-not (Test-Path $from)) { throw "$name needs the $m module, which wasn't built" }
        Copy-Item $from (Join-Path $dir 'modules') -Recurse
    }
    & $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$dir" "/O$artifacts" "/DAppName=$name" "/DAppShortName=$name" `
        "/DAppGuid=$($app.Guid)" "/DAppDir=$name" (Join-Path $root 'installer\DaisysApp.iss')
    if ($LASTEXITCODE) { throw "Inno Setup compile of $name failed" }
    Get-Item (Join-Path $artifacts "$name-Setup-$Version.exe")
}
