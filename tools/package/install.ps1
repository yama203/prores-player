# ProRes Player のインストーラー（配布用 zip に入れて使う）。管理者権限は不要。
#   install.cmd    : %LOCALAPPDATA%\Programs\ProResPlayer にインストール
#   uninstall.cmd  : アンインストール
param([switch]$Uninstall)

$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "app"
$target = Join-Path $env:LOCALAPPDATA "Programs\ProResPlayer"
$exe = Join-Path $target "ProResPlayer.exe"
$lnk = Join-Path ([Environment]::GetFolderPath("Programs")) "ProRes Player.lnk"

try {
	Get-Process ProResPlayer -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force
	Start-Sleep -Milliseconds 500

	if ($Uninstall) {
		if (Test-Path $exe) { Start-Process $exe -ArgumentList "--unregister" -Wait }
		Remove-Item $lnk -ErrorAction SilentlyContinue
		Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue
		Write-Host "ProRes Player をアンインストールしました。"
		return
	}

	if (-not (Test-Path (Join-Path $src "ProResPlayer.exe"))) { throw "app フォルダが見つかりません。zip を全部展開してから実行してください。" }

	if (Test-Path $target) { Remove-Item -Recurse -Force $target }
	New-Item -ItemType Directory -Force $target | Out-Null
	Copy-Item -Recurse -Force (Join-Path $src "*") $target

	$shell = New-Object -ComObject WScript.Shell
	$s = $shell.CreateShortcut($lnk)
	$s.TargetPath = $exe
	$s.WorkingDirectory = $target
	$s.IconLocation = "$exe,0"
	$s.Description = "ProRes Player"
	$s.Save()

	Start-Process $exe -ArgumentList "--register" -Wait

	Write-Host "ProRes Player をインストールしました: $target"
	Write-Host "スタートメニューの「ProRes Player」から起動できます。"
	Write-Host ".mov などを既定で開くには、ファイルを右クリック →「プログラムから開く」→「別のプログラムを選択」で"
	Write-Host "ProRes Player を選び、「常に使う」を押してください。"
}
catch {
	Write-Host "エラー: $($_.Exception.Message)" -ForegroundColor Red
	exit 1
}
