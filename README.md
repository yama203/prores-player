# ProRes Player

Windows 用の ProRes 確認プレイヤー。Cinema 4D などから書き出した ProRes を、アルファやフレーム単位で確認するためのもの。
ProRes 以外にも、FFmpeg が読めるもの（H.264 / H.265 の mp4、mkv、avi、webm、DNxHR の mxf など）はそのまま再生できます。
再生エンジンは [libmpv](https://mpv.io/)（FFmpeg の ProRes デコーダ、GPU 描画）。UI は C# / WPF。

## 機能

| 機能 | 操作 |
|---|---|
| 再生 / 一時停止 | Space、動画をクリック |
| コマ送り | ← / →（押しっぱなしでも取りこぼしなし） |
| 1秒 / 10秒移動 | Shift+← / →、↑ / ↓ |
| 先頭 / 末尾 | Home / End |
| フレーム番号・タイムコード表示 | 下のバー（`HH:MM:SS:FF`、ノンドロップ） |
| 背景（アルファ確認） | 1 市松模様 / 2 黒 / 3 白 / 4 アルファのみ、A で切り替え |
| 情報パネル | Tab。コーデック、プロファイル（422 HQ / 4444 など）、解像度、fps、画素形式、アルファ、色空間、ビットレート |
| ループ | I で開始点、O で終了点、P でクリア、L でファイル全体のループ |
| 比較 | C で オフ → 左右 → ワイプ。ワイプ位置はスライダーか [ / ] |
| 全画面 | F / F11 / ダブルクリック、Esc で戻る |
| フレームを PNG で保存 | Ctrl+S。元の解像度、16bit RGBA（アルファ付き）。ファイル名は `<動画名>_f00032.png` |
| フレームをコピー | Ctrl+C。PNG 形式（アルファ付き、Photoshop / Office / ブラウザ向け）と通常の画像の両方で入れる |
| ファイルを開く | ドラッグ＆ドロップ、Ctrl+O（B は Ctrl+Shift+O） |
| 「プログラムから開く」に登録 | ファイル メニュー（HKCU に登録、管理者権限は不要） |

- 比較中は再生・シーク・コマ送り・ループが A と B で同期します（再生中は1フレーム以上ずれたら B を合わせ直します）。
- ファイルを2つ同時にドロップするか、コマンドラインで2つ渡すと A と B に読み込みます。
- PNG 保存とコピーは A の映像です。「アルファのみ」表示中はアルファのマットが保存されます。
- 「プログラムから開く」に登録したあと、既定のアプリにするには .mov を右クリック →「プログラムから開く」→「別のプログラムを選択」で ProRes Player を選び「常に使う」。

## インストール（このPCのユーザーだけ、管理者権限なし）

```powershell
.	ools\install.ps1             # %LOCALAPPDATA%\Programs\ProResPlayer にコピー、スタートメニュー登録、「プログラムから開く」に登録
.	ools\install.ps1 -Uninstall  # 元に戻す
```

コマンドラインで `ProResPlayer.exe --register` / `--unregister` でも登録・解除できます。

## ビルド

必要なもの：.NET 8 SDK、7-Zip（libmpv の取得用）

```powershell
.\tools\get-libmpv.ps1        # lib\libmpv-2.dll を取得（git には入れていない）
.\tools\publish.ps1           # dist\ProResPlayer\ に実行用フォルダを作る
```

アイコン（`assetspp.ico`）は `.	ools\make-icon.ps1` で作り直せます。

開発中は `dotnet build -c Release src\ProResPlayer` で `src\ProResPlayer\bin\Release\net8.0-windows\win-x64\` にできます。

実行には .NET 8 Desktop Runtime が必要です。入っていない PC 向けには `.\tools\publish.ps1 -SelfContained`。

## 仕組みのメモ

- 動画は Win32 の子ウィンドウ（`VideoHost`）に mpv が描画します（`wid` オプション）。WPF の要素は動画の上に重ねられないため、
  ワイプの境界線は A と B の間の 2px の隙間です。
- ワイプでは A は左側、B は右側だけを覆うウィンドウにして、`video-zoom` と `video-pan-x` で「エリア全体に表示したときと同じ位置」に
  描かせることで、境界でぴったりつながるようにしています。
- mpv のウィンドウは別スレッドにあるので、クリックは `WM_PARENTNOTIFY` で拾い、キーボードのフォーカスをメインウィンドウに戻しています。
- フレームの保存は `screenshot-sw=yes` にしています（GPU で撮ると背景の市松模様が焼き込まれてアルファが消えるため）。
- コマ送りは mpv の `frame-step` ではなく、フレーム番号を計算して絶対位置にシークしています（連打したときの取りこぼし対策）。

## ライセンス

libmpv / FFmpeg は GPL / LGPL です（shinchiro のビルドは GPL）。このアプリを配布する場合は、そのライセンスに従ってください。
