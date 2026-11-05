; Inno Setup script for ProRes Player (per-user, no admin). Built by tools\package.ps1:
;   ISCC.exe /DAppVersion=0.4.0 /DStage=<publish folder> /DOutDir=<dist> tools\installer.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Stage
  #define Stage "..\dist\package\ProResPlayer\app"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

[Setup]
AppId={{6F0B7D3E-2C4A-4F7B-9E61-5A8C3D21B9F4}
AppName=ProRes Player
AppVersion={#AppVersion}
AppVerName=ProRes Player {#AppVersion}
AppPublisher=yama203
AppPublisherURL=https://github.com/yama203/prores-player
VersionInfoVersion={#AppVersion}
; Same folder as the old zip installer, so it upgrades in place.
DefaultDirName={localappdata}\Programs\ProResPlayer
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutDir}
OutputBaseFilename=ProResPlayer-{#AppVersion}-setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\ProResPlayer.exe
UninstallDisplayName=ProRes Player
WizardStyle=modern dynamic
WizardSmallImageFile=..\assets\app_256.png
WizardSmallImageFileDynamicDark=..\assets\app_256.png
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにアイコンを作る"; Flags: unchecked

[InstallDelete]
; Start from an empty folder: leftovers of another build (hostfxr.dll etc.) can stop the exe from starting.
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\ProRes Player"; Filename: "{app}\ProResPlayer.exe"
Name: "{userdesktop}\ProRes Player"; Filename: "{app}\ProResPlayer.exe"; Tasks: desktopicon

[Run]
; "Open with" registration for .mov / .mp4 ...
Filename: "{app}\ProResPlayer.exe"; Parameters: "--register"; Flags: runhidden waituntilterminated
Filename: "{app}\ProResPlayer.exe"; Description: "ProRes Player を起動する"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\ProResPlayer.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "Unregister"

[Messages]
ja.FinishedLabel=ProRes Player をインストールしました。%n%n.mov を既定で ProRes Player で開くには、ファイルを右クリック →「プログラムから開く」→「別のプログラムを選択」で ProRes Player を選び、「常に使う」を押してください。
