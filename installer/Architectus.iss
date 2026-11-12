; Architectus setup wizard (Inno Setup 6)
; Built by CI: iscc /DAppVersion=x.y.z /DSourceDir=<publish folder> installer\Architectus.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "Architectus"
#define AppPublisher "Opifex Studios"
#define AppURL "https://github.com/arkbullet2626-droid/architectus"
#define AppExe "Architectus.exe"

[Setup]
AppId={{6B2E8F0A-3C4D-4E59-9A17-A7C3D1E0B5F2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://opifexstudios.ca
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=Architectus-Setup-{#AppVersion}
SetupIconFile=..\src\Architectus\Assets\Architectus.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=WizardLarge.bmp
WizardSmallImageFile=WizardSmall.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nArchitectus turns rough requests into polished master prompts using your own OpenAI, Anthropic or Gemini API key. Keys are stored in Windows Credential Manager.%n%nIt is recommended that you close all other applications before continuing.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  RemoveData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  RemoveData := False;
  if not UninstallSilent then
    RemoveData := MsgBox('Also remove your saved API keys (from Windows Credential Manager), settings and prompt library?' + #13#10 + #13#10 +
      'Choose No to keep them in case you reinstall later.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if (CurUninstallStep = usUninstall) and RemoveData then
  begin
    // Ask the app itself to delete its Credential Manager entries before its files are removed.
    Exec(ExpandConstant('{app}\{#AppExe}'), '--purge', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  if (CurUninstallStep = usPostUninstall) and RemoveData then
    DelTree(ExpandConstant('{localappdata}\Architectus'), True, True, True);
end;
