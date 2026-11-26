# ProRes Player

Windows 用の ProRes 確認プレイヤー。Cinema 4D などから書き出した ProRes を、アルファやフレーム単位で確認するためのもの。
ProRes 以外にも、FFmpeg が読めるもの（H.264 / H.265 の mp4、mkv、avi、webm、DNxHR の mxf、Resolume の DXV など）はそのまま再生できます。
再生エンジンは [libmpv](https://mpv.io/)（FFmpeg の ProRes デコーダ、GPU 描画）。UI は C# / WPF。

## 機能

| 機能 | 操作 |
|---|---|
| 再生 / 一時停止 | Space、動画をクリック |
| コマ送り | ← / →（押しっぱなしでも取りこぼしなし） |
| 音量 | 下のバーのスピーカー（クリックでミュート）とスライダー（クリック・ドラッグ・ホイール）、M でミュート、Ctrl+↑ / ↓ で 5% ずつ。音量とミュートは次に起動したときも同じ |
| 1秒 / 10秒移動 | Shift+← / →、↑ / ↓ |
| 先頭 / 末尾 | Home / End |
| 同じフォルダの前 / 次の動画 | Ctrl+← / Ctrl+→（PageUp / PageDown でも可）、下のバーの ‹ ›。名前順（数字は数値として比較、エクスプローラーと同じ） |
| フレーム番号・タイムコード表示 | 下のバー（`HH:MM:SS:FF`、ノンドロップ） |
| 背景（アルファ確認） | 1 市松模様 / 2 黒（既定） / 3 白 / 4 アルファのみ、A で切り替え。下のバーの「表示」メニューでも選べる |
| 情報パネル | 下のバーの ⓘ ボタン、パネル右上の ✕、または Tab で出す・閉じる。コーデック、プロファイル（422 HQ / 4444 など）、解像度、fps、画素形式、アルファ、色空間、ビットレート |
| ループ | 既定でオン。L か下のバーの「ループ」でオン/オフ。I で開始点、O で終了点、P でクリア（範囲はシークバーに [ ] と帯、下にタイムコードで表示）。ループがオフでも、最後のフレームで再生すると頭から再生 |
| カット（再エンコードなし） | 下のバーの「カット」（または X）で IN / OUT / クリア / 保存のパネルを出す。I / O で範囲を決めて Ctrl+X か「カットして保存...」。上書き（既定）か別名（`<名前>_cut.mov`）で保存。ProRes・DXV などイントラ形式はフレーム単位、PCM 音声はサンプル単位で正確。mp4（H.264 など）はキーフレーム単位 |
| 比較 | C で オフ → 左右 → ワイプ、下のバーの「表示」メニューでも選べる。ワイプ位置はスライダーか [ / ] |
| 全画面 | F / F11 / ダブルクリック、Esc で戻る |
| フレームを PNG で保存 | Ctrl+S、下のバーのカメラのボタン。元の解像度、16bit RGBA（アルファ付き）。ファイル名は `<動画名>_f00032.png` |
| フレームをコピー | Ctrl+C、下のバーのコピーのボタン。PNG 形式（アルファ付き、Photoshop / Office / ブラウザ向け）と通常の画像の両方で入れる |
| フレームにマークアップ | Ctrl+E、下のバーのペンのボタン。今のフレームに矢印・直線・四角・丸・手描き・文字を書き込み、Ctrl+C でコピー（AI のチャットなどに貼れる）か PNG で保存。「情報を入れる」で下にファイル名・フレーム・タイムコード・秒数・解像度・fps の帯を付ける。元の解像度のまま書き出す |
| ファイルを開く | ドラッグ＆ドロップ、Ctrl+O（B は Ctrl+Shift+O） |
| 「プログラムから開く」に登録 | ファイル メニュー（HKCU に登録、管理者権限は不要） |

- 情報パネルの開閉、背景の選択、音量は `%APPDATA%\ProResPlayer\settings.json` に保存され、次に起動したときに元に戻ります。
- 比較中は再生・シーク・コマ送り・ループが A と B で同期します（再生中は1フレーム以上ずれたら B を合わせ直します）。
- ファイルを2つ同時にドロップするか、コマンドラインで2つ渡すと A と B に読み込みます。
- PNG 保存とコピーは A の映像です。「アルファのみ」表示中はアルファのマットが保存されます。
- 「プログラムから開く」に登録したあと、既定のアプリにするには .mov を右クリック →「プログラムから開く」→「別のプログラムを選択」で ProRes Player を選び「常に使う」。

## 配布用インストーラー

[Releases](https://github.com/yama203/prores-player/releases) の `ProResPlayer-<バージョン>-setup.exe` を実行するだけです。
このPCのユーザーだけに入れるので管理者権限は不要、.NET も同梱しています。「設定 → アプリ」からアンインストールできます。

作り方（[Inno Setup 6](https://jrsoftware.org/isinfo.php) が必要：`winget install JRSoftware.InnoSetup`）：

```powershell
.\tools\package.ps1           # dist\ProResPlayer-<バージョン>-setup.exe（スクリプトは tools\installer.iss）
```

## 開発用インストール（このPCのユーザーだけ、管理者権限なし）

```powershell
.\tools\install.ps1             # %LOCALAPPDATA%\Programs\ProResPlayer にコピー、スタートメニュー登録、「プログラムから開く」に登録
.\tools\install.ps1 -Uninstall  # 元に戻す
```

コマンドラインで `ProResPlayer.exe --register` / `--unregister` でも登録・解除できます。

## ビルド

必要なもの：.NET 10 SDK、7-Zip（libmpv の取得用）

```powershell
.\tools\get-libmpv.ps1        # lib\libmpv-2.dll を取得（git には入れていない）
.\tools\get-ffmpeg.ps1        # lib\ffmpeg\ にカット用の ffmpeg.exe を置く（同上）
.\tools\publish.ps1           # dist\ProResPlayer\ に実行用フォルダを作る
```

アイコン（`assets\app.ico`）は `.\tools\make-icon.ps1` で作り直せます。

開発中は `dotnet build -c Release src\ProResPlayer` で `src\ProResPlayer\bin\Release\net10.0-windows\win-x64\` にできます。

実行には .NET 10 Desktop Runtime が必要です。入っていない PC 向けには `.\tools\publish.ps1 -SelfContained`。

## 仕組みのメモ

- 動画は Win32 の子ウィンドウ（`VideoHost`）に mpv が描画します（`wid` オプション）。WPF の要素は動画の上に重ねられないため、
  ワイプの境界線は A と B の間の 2px の隙間です。
- ワイプでは A は左側、B は右側だけを覆うウィンドウにして、`video-zoom` と `video-pan-x` で「エリア全体に表示したときと同じ位置」に
  描かせることで、境界でぴったりつながるようにしています。
- mpv のウィンドウは別スレッドにあるので、クリックは `WM_PARENTNOTIFY` で拾い、キーボードのフォーカスをメインウィンドウに戻しています。
- フレームの保存は `screenshot-sw=yes` にしています（GPU で撮ると背景の市松模様が焼き込まれてアルファが消えるため）。
- コマ送りは mpv の `frame-step` ではなく、フレーム番号を計算して絶対位置にシークしています（連打したときの取りこぼし対策）。

## Mac 版（`mac` ブランチ、`src/ProResPlayer.Mac`）

Avalonia UI ＋ libmpv（render API）。機能は Windows 版と同じで、ショートカットの Ctrl は ⌘ に置き換え。

必要なもの：.NET 10 SDK、Homebrew の mpv と ffmpeg（`brew install dotnet mpv ffmpeg`）

```bash
tools/mac/make-app.sh          # ~/Applications/ProRes Player.app を作る（libmpv・ffmpeg も中に入れる）
tools/mac/make-app.sh --dmg    # dist/mac/ProResPlayer-<バージョン>-mac-arm64.dmg を作る
```

- libmpv・ffmpeg とそれが使うライブラリは、Homebrew のものを `tools/mac/bundle-libs.py` が `.app` の中にコピーする。
  同梱したものの一覧と版は `.app` の `Contents/Resources/THIRD-PARTY.txt`。
- Homebrew のライブラリは、ビルドした Mac の macOS のバージョン以降でしか動かない（`LSMinimumSystemVersion` に自動で入る）。
- 署名は ad-hoc のみで公証していないので、初回は右クリック →「開く」が必要（dmg の「はじめにお読みください.txt」に書いてある）。
- 開発中の動作確認：`--selftest <出力フォルダ> <アルファ付きの動画> [音声付きの動画 ...]` で、表示・コマ送り・ループ・カット・
  比較・PNG / コピー・再生の滑らかさを自動で確かめる（`PRORESPLAYER_SETTINGS` で設定ファイルの場所を変えられる）。

## Mac 版の方針（2026-11 に決めたもの）

Mac 版を作るときは、Mac 上で開発・ビルドする前提で、次の方針で進める（2026-11 に決めたもの）。

### 方式：Avalonia UI（C#）＋ libmpv

- 今の UI は WPF（Windows 専用）なので、画面は [Avalonia UI](https://avaloniaui.net/) で作り直す。XAML の書き方は WPF に近い。
- 再生エンジンは今と同じ libmpv。ProRes 以外（DXV、mkv、H.264 / HEVC など）も今と同じように再生でき、カット・マークアップ・A/B 比較などの機能も同じ形で持っていける。
- Swift ＋ AVFoundation（Mac 純正）にしなかった理由：全部書き直しになるうえ、DXV など Apple が対応していない形式を再生できないため。
- 将来は Windows 版も Avalonia に移せば、1 つのコードで両方を出せる（今すぐはやらない）。

### 使い回せるもの・作り直すもの

| そのまま、またはほぼそのまま使う | 作り直す |
|---|---|
| `Cutter.cs`（ffmpeg でのカット。ffmpeg のパスだけ変える） | `MainWindow` / `CutDialog` / `MarkupWindow` の画面（Avalonia 版に移植） |
| `AppSettings.cs`（保存先を `~/Library/Application Support/ProResPlayer/` に） | `VideoHost.cs`（動画の表示。下を参照） |
| `FolderPlaylist.cs`（名前順の比較は Windows の `StrCmpLogicalW` の代わりを自前で書く） | `FileAssociation.cs`（Mac では `Info.plist` の `CFBundleDocumentTypes` で宣言） |
| `MpvPlayer.cs` / `LibMpv.cs` の大部分（libmpv の API は同じ。dll 名だけ `libmpv.dylib` に） | `ClipboardImage.cs`（Avalonia のクリップボードで PNG を入れる） |
| タイムコード計算、コマ送り、ループ、比較の同期などのロジック | インストーラー（`.app` を入れた `.dmg` にする） |

### 動画の表示（いちばん大きな作り直し）

- Windows 版は mpv に子ウィンドウ（`wid`）へ直接描かせているが、Mac では `wid` での埋め込みはうまく動かない。
- Mac 版は mpv の **render API**（`mpv_render_context`、OpenGL）を使い、Avalonia の `OpenGlControlBase` に描かせる。
- こうすると動画が普通の UI 部品になるので、Windows 版の「動画の上に WPF の部品を重ねられない」制約がなくなる。
  ワイプの境界線、カットのパネル、範囲の表示などを動画の上に直接重ねられ、`video-zoom` / `video-pan-x` で合わせている
  ワイプの仕組みも、1 つの部品の中で 2 つの映像を描く形に単純化できる。
- macOS の OpenGL は非推奨扱いだが、現状は動く。将来問題になったら Metal（mpv の対応状況を確認）を検討する。

### 依存物の入手

- libmpv：まずは Homebrew の `brew install mpv` の `libmpv.dylib` で開発する。配布時は `.app` の中に dylib と依存ライブラリを同梱し、
  `install_name_tool` でパスを直す（または同梱済みの libmpv ビルドを探す）。
- ffmpeg（カット用）：Mac 用の静的ビルドを `.app` に同梱する。GPL なので、今と同じくライセンスとソースの入手先を README とリリースノートに書く。
- Apple Silicon（arm64）を主な対象にし、Intel Mac は必要になったら考える。

### Mac 特有の対応

- ショートカットは Ctrl の代わりに Cmd（Cmd+C / Cmd+S / Cmd+E / Cmd+X など）。
- メニューは Mac の画面上部のメニューバー（Avalonia の `NativeMenu`）に出す。
- アイコンは `assets/app_256.png` などから `.icns` を作る（`tools/icon-design.ps1` のデザイン B）。

### 配布

- 署名・公証（Apple Developer Program、年 99 ドル）なしでも使えるが、初回に「開発元を確認できません」と出て、
  ユーザーが「システム設定 → プライバシーとセキュリティ」から許可する必要がある。広く配るなら署名・公証を検討する。

### 進め方

1. まず「動画を開いて、再生・一時停止・コマ送り・シークができる」ところまでを作り、render API での表示に問題がないか確かめる
   （ProRes 4444 のアルファと市松模様の背景、4K の ProRes が滑らかに再生できるか）。
2. 問題なければ、下のバー → 情報パネル → ループ・カット → A/B 比較 → PNG 保存・コピー・マークアップ → 音量 → 設定の保存 の順に移す。
3. 最後に `.app` / `.dmg` を作るスクリプトを用意してリリースする。

### C4D プラグイン（c4d-prores）の Mac 版

作らなくてよい可能性が高い。Mac 版の Cinema 4D は OS 標準の機能（AVFoundation）で ProRes（4444 を含む）を書き出せるため。
必要になったら、まず Mac の C4D で標準の書き出しが使えるか（アルファ、色）を確かめてから判断する。

## ライセンス

このアプリのソースコードは [GPL-3.0](LICENSE) です（GPL ビルドの libmpv を使っているため）。

配布物（setup.exe）に同梱しているもの：

| もの | ライセンス | ソース |
|---|---|---|
| libmpv-2.dll（[shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20260928) 20260928） | GPL v3 | [mpv](https://github.com/mpv-player/mpv)、ビルドに使った各ライブラリは同リリースの説明を参照 |
| ffmpeg.exe（[gyan.dev](https://www.gyan.dev/ffmpeg/builds/) 9.0.1 essentials） | GPL v3 | [FFmpeg bf1b838f2a](https://github.com/FFmpeg/FFmpeg/commit/bf1b838f2a)、同梱ライブラリの版はインストール先の `ffmpeg\README.txt` |
| .NET ランタイム | MIT | [dotnet/runtime](https://github.com/dotnet/runtime)、[dotnet/wpf](https://github.com/dotnet/wpf) |

FFmpeg の ProRes デコーダ / エンコーダは Apple の認定を受けたものではありません。ProRes は Apple Inc. の商標です。
