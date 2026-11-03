# Icon designs for ProRes Player, rendered with System.Drawing.
#   . .\tools\icon-design.ps1; $png = Render-Icon -Variant A -Size 256
# Variants: A = minimal, B = alpha split, C = viewfinder.
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
	$p = New-Object System.Drawing.Drawing2D.GraphicsPath
	$d = $r * 2
	$p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
	$p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
	$p.CloseFigure(); return $p
}

# Play triangle with rounded corners: corners are cut back along both edges and joined with
# quadratic-like Bezier curves through the original vertex.
function New-RoundedTriangle([float]$cx, [float]$cy, [float]$r, [float]$round) {
	$pts = @(
		(New-Object System.Drawing.PointF ($cx - $r * 0.62), ($cy - $r * 0.92)),
		(New-Object System.Drawing.PointF ($cx + $r * 0.98), $cy),
		(New-Object System.Drawing.PointF ($cx - $r * 0.62), ($cy + $r * 0.92)))
	$p = New-Object System.Drawing.Drawing2D.GraphicsPath
	for ($i = 0; $i -lt 3; $i++) {
		$prev = $pts[($i + 2) % 3]; $cur = $pts[$i]; $next = $pts[($i + 1) % 3]
		$a = Lerp $cur $prev $round; $b = Lerp $cur $next $round
		if ($i -eq 0) { $p.StartFigure() }
		$p.AddBezier($a, (Lerp $a $cur 0.6), (Lerp $b $cur 0.6), $b)
		$p.AddLine($b, (Lerp $next $cur $round))
	}
	$p.CloseFigure(); return $p
}
function Lerp($p, $q, [float]$t) { New-Object System.Drawing.PointF ($p.X + ($q.X - $p.X) * $t), ($p.Y + ($q.Y - $p.Y) * $t) }
function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

function Render-Icon([string]$Variant, [int]$Size) {
	$s = [float]$Size
	$bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"; $g.CompositingQuality = "HighQuality"
	$m = [Math]::Max(0.5, $s * 0.035)
	$tile = New-RoundedRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.225)
	$rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s

	# Tile: deep charcoal, slightly lighter at the top.
	$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C 255 44 44 52), (C 255 14 14 18), 90.0
	$g.FillPath($bg, $tile)
	$g.SetClip($tile)

	if ($Variant -eq "B") {
		# Right half (diagonal) shows a faint checkerboard: transparency.
		$half = New-Object System.Drawing.Drawing2D.GraphicsPath
		$half.AddPolygon([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($s * 0.62), 0), (New-Object System.Drawing.PointF $s, 0), (New-Object System.Drawing.PointF $s, $s), (New-Object System.Drawing.PointF ($s * 0.38), $s)))
		$g.SetClip($half, [System.Drawing.Drawing2D.CombineMode]::Intersect)
		$n = if ($Size -le 32) { 4 } else { 8 }; $c = $s / $n
		$light = New-Object System.Drawing.SolidBrush (C 255 74 74 84); $dark = New-Object System.Drawing.SolidBrush (C 255 52 52 60)
		for ($i = 0; $i -lt $n; $i++) { for ($j = 0; $j -lt $n; $j++) { $g.FillRectangle($(if ((($i + $j) % 2) -eq 0) { $light } else { $dark }), $i * $c, $j * $c, $c, $c) } }
		$g.SetClip($tile)
		$edge = New-Object System.Drawing.Pen (C 120 255 255 255), ([Math]::Max(1, $s / 128))
		$g.DrawLine($edge, $s * 0.62, 0, $s * 0.38, $s)
	}

	# Soft light from the top-left.
	$glow = New-Object System.Drawing.Drawing2D.GraphicsPath
	$glow.AddEllipse(-$s * 0.35, -$s * 0.55, $s * 1.2, $s * 0.95)
	$gb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
	$gb.CenterColor = (C 38 255 255 255); $gb.SurroundColors = @((C 0 255 255 255))
	$g.FillPath($gb, $glow)
	$g.ResetClip()

	# Play mark.
	$warmA = C 255 255 190 92; $warmB = C 255 240 92 64
	if ($Variant -eq "C") {
		# Viewfinder corners + smaller play mark.
		$pen = New-Object System.Drawing.Pen (C 230 236 236 240), ([Math]::Max(1.2, $s * 0.045))
		$pen.StartCap = "Round"; $pen.EndCap = "Round"; $pen.LineJoin = "Round"
		$a = $s * 0.24; $b = $s * 0.76; $l = $s * 0.15
		$g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $a, ($a + $l)), (New-Object System.Drawing.PointF $a, $a), (New-Object System.Drawing.PointF ($a + $l), $a)))
		$g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($b - $l), $a), (New-Object System.Drawing.PointF $b, $a), (New-Object System.Drawing.PointF $b, ($a + $l))))
		$g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $b, ($b - $l)), (New-Object System.Drawing.PointF $b, $b), (New-Object System.Drawing.PointF ($b - $l), $b)))
		$g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($a + $l), $b), (New-Object System.Drawing.PointF $a, $b), (New-Object System.Drawing.PointF $a, ($b - $l))))
		$tri = New-RoundedTriangle ($s * 0.52) ($s * 0.5) ($s * 0.17) 0.22
	}
	else {
		$tri = New-RoundedTriangle ($s * 0.53) ($s * 0.5) ($s * 0.27) 0.2
	}
	$tb = $tri.GetBounds()
	$fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $tb, $warmA, $warmB, 60.0
	# Subtle drop shadow under the mark (skip at tiny sizes where it only muddies).
	if ($Size -ge 32) {
		$sh = $tri.Clone(); $mx = New-Object System.Drawing.Drawing2D.Matrix; $mx.Translate(0, $s * 0.02); $sh.Transform($mx)
		$g.FillPath((New-Object System.Drawing.SolidBrush (C 90 0 0 0)), $sh)
	}
	$g.FillPath($fill, $tri)

	# Hairline rim so the tile reads on dark taskbars.
	if ($Size -ge 32) { $g.DrawPath((New-Object System.Drawing.Pen (C 60 255 255 255), ([Math]::Max(1, $s / 128))), $tile) }

	$g.Dispose()
	return $bmp
}
