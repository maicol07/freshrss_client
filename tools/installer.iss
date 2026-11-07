#ifndef AppVersion
  #error AppVersion must match Package.appxmanifest
#endif

[Setup]
AppId=Maicol.FreshRssClient
AppName=FreshRSS Client
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\FreshRssClient
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
SetupIconFile=..\FreshRssClient\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Assets\AppIcon.ico
OutputDir=..\publish\installer
OutputBaseFilename=FreshRssClient-{#AppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\FreshRSS Client"; Filename: "{app}\FreshRssClient.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; AppUserModelID: "Maicol.FreshRssClient.App"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "FreshRssClient"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\FreshRssClient.exe"; Description: "{cm:LaunchProgram,FreshRSS Client}"; Flags: nowait postinstall skipifsilent
