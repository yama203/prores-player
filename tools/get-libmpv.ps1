# Downloads libmpv-2.dll (shinchiro/mpv-winbuild-cmake) into lib/.
#   .\tools\get-libmpv.ps1               # pinned version below
#   .\tools\get-libmpv.ps1 -Tag 20261014 # another release
param([string]$Tag = "20260928")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$lib = Join-Path $root "lib"
$sevenZip = "C:\Program Files\7-Zip\7z.exe"
if (-not (Test-Path $sevenZip)) { throw "7-Zip is required: winget install 7zip.7zip" }

$rel = Invoke-RestMethod "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/tags/$Tag"
$asset = $rel.assets | Where-Object name -match "^mpv-dev-x86_64-\d" | Select-Object -First 1
if (-not $asset) { throw "mpv-dev-x86_64 asset not found in release $Tag" }

$tmp = Join-Path ([IO.Path]::GetTempPath()) "libmpv_$Tag"
New-Item -ItemType Directory -Force $tmp | Out-Null
$archive = Join-Path $tmp $asset.name
Write-Host "Downloading $($asset.name)"
Invoke-WebRequest $asset.browser_download_url -OutFile $archive
& $sevenZip e $archive "libmpv-2.dll" "-o$tmp" -y | Out-Null

New-Item -ItemType Directory -Force $lib | Out-Null
Copy-Item (Join-Path $tmp "libmpv-2.dll") $lib -Force
Write-Host "lib\libmpv-2.dll ($Tag)"
