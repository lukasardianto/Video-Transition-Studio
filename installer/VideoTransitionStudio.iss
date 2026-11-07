; Video Transition Studio -- Inno Setup installer
; Kompilasi: ISCC.exe VideoTransitionStudio.iss
;   (atau jalankan build-installer.bat di folder ini)

#define MyAppName "Video Transition Studio"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Blessed Multimedia"
#define MyAppExeName "VideoTransitionStudio.exe"

[Setup]
AppId={{8A3D42C1-3B7E-4F1A-9A2B-1C9D5E6F7A8B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=VideoTransitionStudio-{#MyAppVersion}-win64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
SetupIconFile=..\src\icon.ico
LicenseFile=..\installer\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat pintasan di Desktop"; GroupDescription: "Pintasan tambahan:"

[Files]
Source: "..\build\VideoTransitionStudio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\transition\*.mp4"; DestDir: "{app}\transition"; Flags: ignoreversion
Source: "..\build\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Jalankan Video Transition Studio"; Flags: nowait postinstall skipifsilent
