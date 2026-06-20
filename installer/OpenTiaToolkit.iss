#define MyAppName "OpenTiaToolkit"
#define MyAppVersion GetEnv("OPEN_TIA_INSTALLER_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "OpenTiaToolkit Contributors"
#define MyAppURL "https://github.com/mistik91/OpenTiaToolkit"
#define MyAppExeName "OpenTiaToolkit.MCP.exe"
#define StageDir "..\artifacts\installer\stage"

[Setup]
AppId={{C8F39502-DB8E-4C50-B882-5C6681E0C9E2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\OpenTiaToolkit
DefaultGroupName=OpenTiaToolkit
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=OpenTiaToolkit-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "registermcp"; Description: "Register OpenTiaToolkit MCP with Siemens Openness"; GroupDescription: "Siemens Openness registration:"; Flags: checkedonce
Name: "registercli"; Description: "Register OpenTiaToolkit CLI with Siemens Openness"; GroupDescription: "Siemens Openness registration:"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OpenTiaToolkit docs"; Filename: "{app}\docs\SETUP.md"
Name: "{group}\Agent plugin setup"; Filename: "{app}\docs\INSTALL_AGENT_PLUGINS.md"
Name: "{group}\OpenTiaToolkit CLI"; Filename: "{app}\OpenTiaToolkit.CLI.exe"; WorkingDir: "{app}"

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "OPEN_TIA_MCP_EXE"; ValueData: "{app}\OpenTiaToolkit.MCP.exe"; Flags: preservestringtype uninsdeletevalue

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Register-OpennessAllowList.ps1"" -ExecutablePath ""{app}\OpenTiaToolkit.MCP.exe"""; StatusMsg: "Registering OpenTiaToolkit MCP with Siemens Openness..."; Flags: runhidden; Tasks: registermcp
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Register-OpennessAllowList.ps1"" -ExecutablePath ""{app}\OpenTiaToolkit.CLI.exe"""; StatusMsg: "Registering OpenTiaToolkit CLI with Siemens Openness..."; Flags: runhidden; Tasks: registercli

[UninstallDelete]
Type: filesandordirs; Name: "{app}\plugins"
Type: filesandordirs; Name: "{app}\skills"
Type: filesandordirs; Name: "{app}\docs"
Type: filesandordirs; Name: "{app}\scripts"
