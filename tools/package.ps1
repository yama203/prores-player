# Builds the installer: dist\ProResPlayer-<version>-setup.exe
# (self-contained .NET, per-user install without admin; the script is tools\installer.iss).
#   .\tools\package.ps1
# Needs Inno Setup 6: winget install JRSoftware.InnoSetup

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = Join-Path $root "src\ProResPlayer\ProResPlayer.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not (Test-Path (Join-Path $root "lib\libmpv-2.dll"))) { & (Join-Path $root "tools\get-libmpv.ps1") }
if (-not (Test-Path (Join-Path $root "lib\ffmpeg\ffmpeg.exe"))) { & (Join-Path $root "tools\get-ffmpeg.ps1") }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
	Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 is required: winget install JRSoftware.InnoSetup" }

$stage = Join-Path $root "dist\package\app"
if (Test-Path (Split-Path -Parent $stage)) { Remove-Item -Recurse -Force (Split-Path -Parent $stage) }
dotnet publish $csproj -c Release -o $stage --self-contained true -p:DebugType=none
if ($LASTEXITCODE) { throw "dotnet publish failed" }

$dist = Join-Path $root "dist"
& $iscc /Q "/DAppVersion=$version" "/DStage=$stage" "/DOutDir=$dist" (Join-Path $root "tools\installer.iss")
if ($LASTEXITCODE) { throw "ISCC failed" }
$setup = Join-Path $dist "ProResPlayer-$version-setup.exe"
Write-Host ("Done: {0} ({1:0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
