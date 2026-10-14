# Builds the distributable zip: dist\ProResPlayer-<version>-win-x64.zip
# (self-contained .NET, so the target PC needs nothing installed; see tools\package\ for the installer).
#   .\tools\package.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = Join-Path $root "src\ProResPlayer\ProResPlayer.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not (Test-Path (Join-Path $root "lib\libmpv-2.dll"))) { & (Join-Path $root "tools\get-libmpv.ps1") }

$stage = Join-Path $root "dist\package\ProResPlayer"
$zip = Join-Path $root "dist\ProResPlayer-$version-win-x64.zip"
if (Test-Path (Split-Path -Parent $stage)) { Remove-Item -Recurse -Force (Split-Path -Parent $stage) }
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish $csproj -c Release -o (Join-Path $stage "app") --self-contained true -p:DebugType=none
if ($LASTEXITCODE) { throw "dotnet publish failed" }

$pkg = Join-Path $root "tools\package"
$utf8Bom = New-Object System.Text.UTF8Encoding $true
foreach ($f in Get-ChildItem $pkg -File) {
	$text = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8) -replace "`r?`n", "`r`n"
	$dst = Join-Path $stage $f.Name
	switch ($f.Extension) {
		".ps1" { [IO.File]::WriteAllText($dst, $text, $utf8Bom) }               # Windows PowerShell 5.1 needs the BOM for Japanese
		".cmd" { [IO.File]::WriteAllText($dst, $text, [Text.Encoding]::ASCII) } # cmd.exe needs CRLF
		default { [IO.File]::WriteAllText($dst, $text, $utf8Bom) }
	}
}

# .NET Framework writes "\" in entry names unless told otherwise; zip requires "/".
[AppContext]::SetSwitch("Switch.System.IO.Compression.ZipFile.UseBackslash", $false)
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zip) { Remove-Item $zip }
# UTF-8 entry names so the Japanese file name survives on any PC.
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true, [Text.Encoding]::UTF8)
Write-Host ("Done: {0} ({1:0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
