#define MyAppName "CameraSwitch"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Eduard Sanz"
#define MyAppExeName "CameraSwitch.exe"

[Setup]
AppId={{8F6F1F0F-8B7D-4C9B-9C1E-6E2D1E7B9A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName=C:\CameraSwitch
DisableDirPage=no
DefaultGroupName=CameraSwitch
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=CameraSwitch-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\CameraSwitch.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el Escritorio"; GroupDescription: "Accesos directos adicionales:"; Flags: unchecked

[Files]
Source: "..\publish\CameraSwitch.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\CameraSwitch"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\CameraSwitch"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Desinstalar CameraSwitch"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Ejecutar CameraSwitch ahora"; Flags: nowait postinstall skipifsilent

[Code]
var
  OBSPage: TInputOptionWizardPage;

function IsOBSInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\OBS Studio') or
            RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\OBS Studio') or
            FileExists(ExpandConstant('{commonpf64}\obs-studio\bin\64bit\obs64.exe'));
end;

procedure InitializeWizard();
begin
  OBSPage := CreateInputOptionPage(wpSelectTasks,
    'OBS Studio no detectado',
    'CameraSwitch necesita OBS Studio para funcionar correctamente.',
    'No se ha detectado una instalación de OBS Studio en este equipo. Elige cómo quieres continuar:',
    True, False);
  OBSPage.Add('Instalar automáticamente con winget (requiere permisos de administrador)');
  OBSPage.Add('Abrir la página oficial de descarga (instalación manual)');
  OBSPage.Add('Omitir por ahora');
  OBSPage.SelectedValueIndex := 0;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = OBSPage.ID then
    Result := IsOBSInstalled();
end;

procedure InstallOBSWithWinget();
var
  ResultCode: Integer;
begin
  if Exec('winget.exe', 'install --id OBSProject.OBSStudio --silent --accept-source-agreements --accept-package-agreements',
     '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 0 then
      MsgBox('OBS Studio se ha instalado correctamente.', mbInformation, MB_OK)
    else
      MsgBox('winget no ha podido completar la instalación de OBS Studio (código ' + IntToStr(ResultCode) + '). Puedes instalarlo manualmente desde https://obsproject.com/download', mbError, MB_OK);
  end
  else
    MsgBox('No se ha podido ejecutar winget en este equipo. Instala OBS Studio manualmente desde https://obsproject.com/download', mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and not IsOBSInstalled() then
  begin
    case OBSPage.SelectedValueIndex of
      0: InstallOBSWithWinget();
      1: ShellExec('open', 'https://obsproject.com/download', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
    Exec('schtasks.exe', '/Delete /TN "CameraSwitch" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
