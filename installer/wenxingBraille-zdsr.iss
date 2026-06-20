#define MyAppName "文星点显器争渡插件"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "wangru"
#define MyAppExeName "wenxingBraille.dll"

[Setup]
AppId={{70BB1E1D-962C-4D2C-98B3-2CCBF18D5BA9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf32}\zdsr\zdsr\addins\BrailleDisplay\wenxingBraille
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=wenxingBraille-zdsr-{#MyAppVersion}-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName}

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Files]
Source: "..\dist\app\wenxingBraille.dll"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  if not FileExists(ExpandConstant('{autopf32}\zdsr\zdsr\ZDSRBrailleDisplayAddin.dll')) then
  begin
    MsgBox('未检测到默认位置的争渡读屏，请先安装争渡读屏，或确认安装目录为 C:\Program Files (x86)\zdsr\zdsr。', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  Result := True;
end;
