#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\\..\\artifacts\\publish\\win-x64"
#endif

[Setup]
AppId={{B7E0DF9A-4CF4-4C01-9BC5-000000000001}
AppName=Termox
AppVersion={#MyAppVersion}
AppPublisher=Termox Project
AppPublisherURL=https://github.com/cosqnetwork/termox
DefaultDirName={autopf}\Termox
DefaultGroupName=Termox
OutputDir=..\..\artifacts\release
OutputBaseFilename=Termox-{#MyAppVersion}-windows-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\..\Assets\Icons\termox-icon.ico
UninstallDisplayIcon={app}\Termox.exe
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Termox"; Filename: "{app}\Termox.exe"
Name: "{autodesktop}\Termox"; Filename: "{app}\Termox.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\Termox.exe"; Description: "Launch Termox"; Flags: nowait postinstall skipifsilent
