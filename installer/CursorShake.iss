; Phase 3: startup/launch integration. Installation is deliberately blocked below.
#ifndef AppVersion
  #error AppVersion must be supplied by build/BuildInstaller.ps1
#endif
#ifndef PayloadRoot
  #error PayloadRoot must be supplied by build/BuildInstaller.ps1
#endif
#ifndef InstallerOutput
  #error InstallerOutput must be supplied by build/BuildInstaller.ps1
#endif

[Setup]
AppId=R3Neer.CursorShake
AppName=CursorShake
AppVersion={#AppVersion}
AppPublisher=R3Neer
AppPublisherURL=https://github.com/R3Neer/CursorShake
VersionInfoVersion={#AppVersion}.0
DefaultDirName={autopf}\CursorShake
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousAppDir=yes
UsePreviousPrivileges=yes
DisableDirPage=no
DisableWelcomePage=no
SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
WizardStyle=modern dynamic windows11
WizardImageFile=
WizardSmallImageFile=
WizardImageFileDynamicDark=
WizardSmallImageFileDynamicDark=
SetupIconFile=..\assets\branding\cursorshake-propuesta-01.ico
UninstallDisplayIcon={app}\CursorShake.exe
OutputDir={#InstallerOutput}
OutputBaseFilename=CursorShake-{#AppVersion}-phase3-preview-Setup
OutputManifestFile=payload-manifest.txt
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.PreviewNotice=Installer preview (phase 3). Installation is disabled until update and uninstall integration is complete. No application files will be installed.
spanish.PreviewNotice=Vista previa del instalador (fase 3). La instalación está deshabilitada hasta completar las integraciones de actualización y desinstalación. No se instalarán archivos de la aplicación.

english.AutoStart=Start CursorShake with Windows (current user only)
spanish.AutoStart=Iniciar CursorShake con Windows (solo este usuario)
english.Launch=Run CursorShake now
spanish.Launch=Ejecutar CursorShake al terminar
english.OriginalUserNotice=Open Setup normally. It requests elevation if needed. Starting Setup as administrator prevents safe per-user startup and launch integration.
spanish.OriginalUserNotice=Abre el instalador normalmente: solicitará elevarse si hace falta. Iniciarlo como administrador impide configurar el arranque e iniciar la aplicación de forma segura para tu usuario.
english.IntegrationFailed=The startup preference could not be applied. CursorShake will not launch automatically after installation. Open it from your normal Windows session and use the tray menu to review startup.
spanish.IntegrationFailed=No se ha podido aplicar la preferencia de inicio. CursorShake no se ejecutará automáticamente al terminar. Ábrelo desde tu sesión normal de Windows y revisa el inicio en el menú de bandeja.

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"

[Run]
Filename: "{app}\CursorShake.exe"; Parameters: "--installer-launch"; WorkingDir: "{app}"; Description: "{cm:Launch}"; Flags: postinstall nowait runasoriginaluser skipifsilent; Check: CanLaunchAfterInstall

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Excludes: ".cursorshake-build-owned"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CursorShake"; Filename: "{app}\CursorShake.exe"

[Code]
var
  StartupIntegrationSucceeded: Boolean;

function CanLaunchAfterInstall: Boolean;
begin
  Result := StartupIntegrationSucceeded;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Arguments: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  StartupIntegrationSucceeded := False;
  if WizardIsTaskSelected('autostart') then
    Arguments := '--installer-startup=enable'
  else
    Arguments := '--installer-startup=disable';
  try
    if ExecAsOriginalUser(ExpandConstant('{app}\CursorShake.exe'), Arguments,
      ExpandConstant('{app}'), SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    begin
      StartupIntegrationSucceeded := ResultCode = 0;
      if not StartupIntegrationSucceeded then
      begin
        Log(Format('CursorShake startup integration failed with exit code %d.', [ResultCode]));
        { The helper reports the detailed error. This explains the remaining installation state. }
        MsgBox(CustomMessage('IntegrationFailed'), mbError, MB_OK);
      end;
    end
    else
      MsgBox(CustomMessage('IntegrationFailed') + #13#10 + SysErrorMessage(ResultCode), mbError, MB_OK);
  except
    MsgBox(CustomMessage('IntegrationFailed') + #13#10 + GetExceptionMessage, mbError, MB_OK);
  end;
end;

procedure InitializeWizard;
var
  Margin: Integer;
begin
  { Removing image resources does not remove their reserved layout space. }
  WizardForm.WizardBitmapImage.Visible := False;
  WizardForm.WizardBitmapImage2.Visible := False;
  WizardForm.WizardSmallBitmapImage.Visible := False;
  Margin := ScaleX(16);
  WizardForm.WelcomeLabel1.Left := Margin;
  WizardForm.WelcomeLabel1.Width := WizardForm.WelcomeLabel1.Parent.ClientWidth - 2 * Margin;
  WizardForm.WelcomeLabel2.Left := Margin;
  WizardForm.WelcomeLabel2.Width := WizardForm.WelcomeLabel2.Parent.ClientWidth - 2 * Margin;
  WizardForm.WelcomeLabel2.Caption := CustomMessage('PreviewNotice') + #13#10#13#10 + CustomMessage('OriginalUserNotice');
  WizardForm.FinishedHeadingLabel.Left := Margin;
  WizardForm.FinishedHeadingLabel.Width := WizardForm.FinishedHeadingLabel.Parent.ClientWidth - 2 * Margin;
  WizardForm.FinishedLabel.Left := Margin;
  WizardForm.FinishedLabel.Width := WizardForm.FinishedLabel.Parent.ClientWidth - 2 * Margin;
  WizardForm.RunList.Left := Margin;
  WizardForm.RunList.Width := WizardForm.RunList.Parent.ClientWidth - 2 * Margin;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { No command-line mode bypasses this gate. Remove only with lifecycle integration. }
  Result := CustomMessage('PreviewNotice');
end;
