# Generates src/DaisysApp/Assets/DaisysApp.ico (multi-size, PNG-compressed entries).
# Run with Windows PowerShell 5.1: powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\DaisysApp\Assets\DaisysApp.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square background
    $r = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = [single]($s - 1)
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $s, $s), ([System.Drawing.Color]::FromArgb(255, 0, 95, 184)), ([System.Drawing.Color]::FromArgb(255, 76, 194, 255))
    $g.FillPath($brush, $path)

    # daisy: white petals around a golden centre
    $c = [single]($s / 2.0)
    $petals = 8
    $petalLen = [single]($s * 0.30)
    $petalW = [single]($s * 0.19)
    $inner = [single]($s * 0.07)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    for ($i = 0; $i -lt $petals; $i++) {
        $state = $g.Save()
        $g.TranslateTransform($c, $c)
        $g.RotateTransform([single](360.0 / $petals * $i))
        $g.FillEllipse($white, [single](-$petalW / 2), [single](-$inner - $petalLen), $petalW, $petalLen)
        $g.Restore($state)
    }
    $centreR = [single]($s * 0.13)
    $gold = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 200, 61))
    $g.FillEllipse($gold, [single]($c - $centreR), [single]($c - $centreR), [single]($centreR * 2), [single]($centreR * 2))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "Wrote $out"
