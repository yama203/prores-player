# Renders assets\app.ico (16-256 px, PNG-compressed entries) with System.Drawing.
#   .\tools\make-icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $root "assets\app.ico"

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
	$p = New-Object System.Drawing.Drawing2D.GraphicsPath
	$d = $r * 2
	$p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
	$p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
	$p.CloseFigure(); return $p
}

function Render([int]$s) {
	$bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"
	$m = [Math]::Max(1, $s * 0.04)
	$tile = RoundedRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.2)

	# Dark tile with a checkerboard (transparency) fading in from the right.
	$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 34, 38))), $tile)
	$g.SetClip($tile)
	$n = if ($s -le 24) { 4 } else { 6 }
	$c = ($s - 2 * $m) / $n
	for ($i = 0; $i -lt $n; $i++) { for ($j = 0; $j -lt $n; $j++) {
		if ((($i + $j) % 2) -eq 0) { continue }
		$a = [int](20 + 70 * ($i / ($n - 1)))
		$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($a, 255, 255, 255))), $m + $i * $c, $m + $j * $c, $c, $c)
	} }
	$g.ResetClip()

	# Play triangle.
	$cx = $s * 0.53; $cy = $s * 0.5; $r = $s * 0.27
	$pts = [System.Drawing.PointF[]]@(
		(New-Object System.Drawing.PointF ($cx - $r * 0.75), ($cy - $r)),
		(New-Object System.Drawing.PointF ($cx + $r), $cy),
		(New-Object System.Drawing.PointF ($cx - $r * 0.75), ($cy + $r)))
	$g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 232, 163, 61))), $pts)

	# Thin outline so the tile reads on dark taskbars.
	if ($s -ge 32) {
		$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([Math]::Max(1, $s / 96))
		$g.DrawPath($pen, $tile)
	}
	$g.Dispose()
	$ms = New-Object System.IO.MemoryStream
	$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
	return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { ,(Render $s) }

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
Write-Host "Done: $out"
