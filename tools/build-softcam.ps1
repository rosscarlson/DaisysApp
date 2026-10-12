<#
  Builds the virtual webcam ("Daisy's App Camera") from softcam (https://github.com/tshino/softcam, MIT) and puts
  the two DLLs in third_party\softcam\x64 and \x86, which build.ps1 ships and the installer registers.

  Only needs running again to change softcam's version or our changes to it; the built DLLs are in the repository.
  Requires the Visual Studio 2022 C++ build tools (MSVC v143) and git.

  Our changes to softcam: its DirectShow filter is named "Daisy's App Camera" and has its own class id, and its shared
  memory and mutex have their own names, so it can't clash with another program's softcam; and the C runtime is linked
  in statically (/MT), so it loads in any camera app (Discord, Teams, browsers) without the VC++ redistributable.
#>
param([string]$Version = 'v1.8.1')
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) "softcam-build-$Version"
if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
$ErrorActionPreference = 'Continue' # git writes notes to stderr
git -c advice.detachedHead=false clone --quiet --depth 1 --branch $Version https://github.com/tshino/softcam.git $work 2>&1 | Out-Null
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE) { throw 'git clone failed' }

function Patch([string]$file, [string]$from, [string]$to) {
    $path = Join-Path $work $file
    $text = [IO.File]::ReadAllText($path)
    if (-not $text.Contains($from)) { throw "softcam changed: '$from' not found in $file" }
    [IO.File]::WriteAllText($path, $text.Replace($from, $to))
}

# our name and class id ({DFD81BEC-16F1-4CC2-B483-9BB6AADC1DA5})
Patch 'src\softcam\softcam.cpp' 'L"DirectShow Softcam"' 'L"Daisy''s App Camera"'
Patch 'src\softcam\softcam.cpp' '0xaef3b972, 0x5fa5, 0x4647, 0x95, 0x71, 0x35, 0x8e, 0xb4, 0x72, 0xbc, 0x9e' '0xdfd81bec, 0x16f1, 0x4cc2, 0xb4, 0x83, 0x9b, 0xb6, 0xaa, 0xdc, 0x1d, 0xa5'
# our shared memory and mutex
Patch 'src\softcamcore\FrameBuffer.cpp' '"DirectShow Softcam/NamedMutex"' '"DaisysApp Camera/NamedMutex"'
Patch 'src\softcamcore\FrameBuffer.cpp' '"DirectShow Softcam/SharedMemory"' '"DaisysApp Camera/SharedMemory"'
# the C runtime linked in (/MT), in every project of the release build: a Directory.Build.props MSBuild picks up
@'
<Project>
  <ItemDefinitionGroup Condition="'$(Configuration)' == 'Release'">
    <ClCompile><RuntimeLibrary>MultiThreaded</RuntimeLibrary></ClCompile>
  </ItemDefinitionGroup>
</Project>
'@ | Set-Content (Join-Path $work 'Directory.Build.props') -Encoding ascii

# (its release link names the DLL runtime's import library outright)
Patch 'src\softcam\softcam.vcxproj' 'winmm.lib;msvcrt.lib;' 'winmm.lib;libcmt.lib;'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found (install the Visual Studio 2022 C++ build tools)' }

$out = Join-Path $root 'third_party\softcam'
foreach ($p in @(@{ Platform = 'x64'; Dir = 'x64' }, @{ Platform = 'Win32'; Dir = 'x86' })) {
    & $msbuild (Join-Path $work 'src\softcam\softcam.vcxproj') /nologo /verbosity:minimal /p:Configuration=Release "/p:Platform=$($p.Platform)" "/p:SolutionDir=$work\"
    if ($LASTEXITCODE) { throw "softcam $($p.Platform) build failed" }
    $dll = Get-ChildItem $work -Recurse -Filter softcam.dll | Where-Object { $_.FullName -match "\\$($p.Platform)\\Release\\" } | Select-Object -First 1
    if (-not $dll) { throw "softcam.dll ($($p.Platform)) not found after the build" }
    New-Item -ItemType Directory -Force (Join-Path $out $p.Dir) | Out-Null
    Copy-Item $dll.FullName (Join-Path $out "$($p.Dir)\softcam.dll") -Force
}
Copy-Item (Join-Path $work 'LICENSE') (Join-Path $out 'LICENSE') -Force
Get-ChildItem $out -Recurse -File | Select-Object FullName, Length
