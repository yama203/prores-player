# Builds a ready-to-run folder: dist\ProResPlayer\ (needs the .NET 8 Desktop Runtime on the target PC).
#   .\tools\publish.ps1
#   .\tools\publish.ps1 -SelfContained   # bundles the runtime (~+150 MB), runs anywhere
param([switch]$SelfContained)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not (Test-Path (Join-Path $root "lib\libmpv-2.dll"))) { & (Join-Path $root "tools\get-libmpv.ps1") }
if (-not (Test-Path (Join-Path $root "lib\ffmpeg\ffmpeg.exe"))) { & (Join-Path $root "tools\get-ffmpeg.ps1") }

$out = Join-Path $root "dist\ProResPlayer"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root "src\ProResPlayer\ProResPlayer.csproj") -c Release -o $out `
	--self-contained:$($SelfContained.IsPresent) -p:DebugType=none
if ($LASTEXITCODE) { throw "dotnet publish failed" }
Write-Host "Done: $out"
