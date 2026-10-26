; Phase 2: compile and preview the wizard. Installation is deliberately blocked below.
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
OutputBaseFilename=CursorShake-{#AppVersion}-phase2-preview-Setup
OutputManifestFile=payload-manifest.txt
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.PreviewNotice=Installer preview (phase 2). Installation is disabled until startup, update and uninstall integration is complete. No application files will be installed.
spanish.PreviewNotice=Vista previa del instalador (fase 2). La instalación está deshabilitada hasta completar las integraciones de inicio, actualización y desinstalación. No se instalarán archivos de la aplicación.

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Excludes: ".cursorshake-build-owned"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CursorShake"; Filename: "{app}\CursorShake.exe"

[Code]
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
  WizardForm.WelcomeLabel2.Caption := CustomMessage('PreviewNotice');
  WizardForm.FinishedHeadingLabel.Left := Margin;
  WizardForm.FinishedHeadingLabel.Width := WizardForm.FinishedHeadingLabel.Parent.ClientWidth - 2 * Margin;
  WizardForm.FinishedLabel.Left := Margin;
  WizardForm.FinishedLabel.Width := WizardForm.FinishedLabel.Parent.ClientWidth - 2 * Margin;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { No command-line mode bypasses this gate. Remove only with lifecycle integration. }
  Result := CustomMessage('PreviewNotice');
end;
