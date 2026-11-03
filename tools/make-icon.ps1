# Renders assets\app.ico (16-256 px, PNG-compressed entries) and assets\app_256.png.
# The design lives in tools\icon-design.ps1; -Variant picks one of its designs (B = alpha split).
#   .\tools\make-icon.ps1
param([string]$Variant = "B")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $root "tools\icon-design.ps1")
$out = Join-Path $root "assets\app.ico"

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
	$bmp = Render-Icon -Variant $Variant -Size $s
	$ms = New-Object System.IO.MemoryStream
	$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
	, $ms.ToArray()
}

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($k = 0; $k -lt $sizes.Count; $k++) {
	$s = $sizes[$k]; $len = $images[$k].Length
	$w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
	$w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
	$offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
[System.IO.File]::WriteAllBytes((Join-Path $root "assets\app_256.png"), $images[-1])
Write-Host "Done: $out (variant $Variant)"
