#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "artifacts\publish\win-x64"
#endif
#define AppName "PSUM Check Interrogation"
#define AppExe "PSUM Check Interrogation WinUI 3.exe"

[Setup]
AppId={{C1A67476-49A9-4B69-87A8-DC8679CC7612}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=KyotoBlazeDev
AppPublisherURL=https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation
AppSupportURL=https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/issues
AppUpdatesURL=https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/releases
DefaultDirName={localappdata}\Programs\PSUM Check Interrogation
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SourceDir=..
LicenseFile=LICENSE
SetupIconFile=Assets\battery_paper_app_icon.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=artifacts\installer
OutputBaseFilename=PSUM-Check-Interrogation-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
