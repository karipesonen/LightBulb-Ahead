#define AppName "LightBulb.Fork"
#define AppVersion GetEnv("INSTALLER_APP_VERSION")
#define SourceDir GetEnv("INSTALLER_SOURCE_DIR")
#define ArtifactDir GetEnv("INSTALLER_OUTPUT_DIR")

[Setup]
AppId={{EED00CB9-9606-48AD-A6BA-816813D21874}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher="Local LightBulb fork"
AppPublisherURL="https://github.com/Tyrrrz/LightBulb"
AppSupportURL="https://github.com/Tyrrrz/LightBulb/issues"
AppUpdatesURL="https://github.com/Tyrrrz/LightBulb/releases"
AppMutex=LightBulb.Fork_Identity
DefaultDirName={localappdata}\Programs\{#AppName}
PrivilegesRequired=lowest
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableWelcomePage=yes
DisableProgramGroupPage=no
DisableReadyPage=yes
SetupIconFile=..\favicon.ico
UninstallDisplayIcon={app}\LightBulb.Fork.exe
OutputDir={#ArtifactDir}
OutputBaseFilename=LightBulb.Fork-Installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: ".installed"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\License.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\LightBulb.Fork.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{group}\{#AppName} on Github"; Filename: "https://github.com/Tyrrrz/LightBulb"

[Run]
Filename: "{app}\LightBulb.Fork.exe"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
