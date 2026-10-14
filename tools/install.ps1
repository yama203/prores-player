# Installs ProRes Player for the current user (no admin):
#   %LOCALAPPDATA%\Programs\ProResPlayer\  + Start menu shortcut + "Open with" registration.
#   .\tools\install.ps1
#   .\tools\install.ps1 -Uninstall
param([switch]$Uninstall)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$target = Join-Path $env:LOCALAPPDATA "Programs\ProResPlayer"
$exe = Join-Path $target "ProResPlayer.exe"
$lnk = Join-Path ([Environment]::GetFolderPath("Programs")) "ProRes Player.lnk"

Get-Process ProResPlayer -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force

if ($Uninstall) {
	if (Test-Path $exe) { Start-Process $exe -ArgumentList "--unregister" -Wait }
	Remove-Item $lnk -ErrorAction SilentlyContinue
	Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue
	Write-Host "Uninstalled"
	return
}

& (Join-Path $root "tools\publish.ps1")
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item -Recurse -Force (Join-Path $root "dist\ProResPlayer\*") $target

$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut($lnk)
$s.TargetPath = $exe
$s.WorkingDirectory = $target
$s.IconLocation = "$exe,0"
$s.Description = "ProRes Player"
$s.Save()

Start-Process $exe -ArgumentList "--register" -Wait
Write-Host "Installed: $target"
