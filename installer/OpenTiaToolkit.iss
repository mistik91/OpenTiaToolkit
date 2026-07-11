#define MyAppName "OpenTiaToolkit"
#define MyAppVersion GetEnv("OPEN_TIA_INSTALLER_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "OpenTiaToolkit Contributors"
#define MyAppURL "https://github.com/mistik91/OpenTiaToolkit"
#define MyAppExeName "mcp\OpenTiaToolkit.MCP.exe"
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
Name: "registerworker"; Description: "Register the isolated OpenTiaToolkit V21 worker with Siemens Openness"; GroupDescription: "Siemens Openness registration:"; Flags: checkedonce

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OpenTiaToolkit docs"; Filename: "{app}\docs\SETUP.md"
Name: "{group}\Agent plugin setup"; Filename: "{app}\docs\INSTALL_AGENT_PLUGINS.md"
Name: "{group}\OpenTiaToolkit CLI"; Filename: "{app}\cli\OpenTiaToolkit.CLI.exe"; WorkingDir: "{app}\cli"

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "OPEN_TIA_MCP_EXE"; ValueData: "{app}\mcp\OpenTiaToolkit.MCP.exe"; Flags: preservestringtype uninsdeletevalue
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "OPEN_TIA_WORKER_EXE"; ValueData: "{app}\worker-v21\OpenTiaToolkit.Worker.V21.exe"; Flags: preservestringtype uninsdeletevalue
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "OPEN_TIA_WORKER_V21_EXE"; ValueData: "{app}\worker-v21\OpenTiaToolkit.Worker.V21.exe"; Flags: preservestringtype uninsdeletevalue

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Register-OpennessAllowList.ps1"" -ExecutablePath ""{app}\worker-v21\OpenTiaToolkit.Worker.V21.exe"""; StatusMsg: "Registering isolated OpenTiaToolkit V21 worker with Siemens Openness..."; Flags: runhidden; Tasks: registerworker

[UninstallDelete]
Type: filesandordirs; Name: "{app}\plugins"
Type: filesandordirs; Name: "{app}\skills"
Type: filesandordirs; Name: "{app}\docs"
Type: filesandordirs; Name: "{app}\scripts"
