# Puts ffmpeg.exe (+ LICENSE / README.txt, it is GPL) into lib\ffmpeg\ for the cut feature.
# Uses the winget Gyan.FFmpeg.Essentials install when present, otherwise downloads gyan.dev's release build.
#   .\tools\get-ffmpeg.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dst = Join-Path $root "lib\ffmpeg"

$exe = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue |
	Where-Object { $_.FullName -match "Gyan\.FFmpeg" } | Select-Object -First 1
if ($exe) {
	$src = Split-Path -Parent (Split-Path -Parent $exe.FullName)
}
else {
	$tmp = Join-Path ([IO.Path]::GetTempPath()) "ffmpeg_essentials"
	if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
	New-Item -ItemType Directory -Force $tmp | Out-Null
	$zip = Join-Path $tmp "ffmpeg.zip"
	Write-Host "Downloading ffmpeg-release-essentials.zip"
	Invoke-WebRequest "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" -OutFile $zip
	Expand-Archive $zip $tmp
	$src = (Get-ChildItem $tmp -Directory | Select-Object -First 1).FullName
}

New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path $src "bin\ffmpeg.exe") $dst -Force
foreach ($f in "LICENSE", "README.txt") { Copy-Item (Join-Path $src $f) $dst -Force }
Write-Host "lib\ffmpeg\ffmpeg.exe <- $src"
