; Installer candidate: lifecycle integrated; real installation QA precedes publication.
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
OutputBaseFilename=CursorShake-{#AppVersion}-phase4-candidate-Setup
OutputManifestFile=payload-manifest.txt
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.PreviewNotice=Installer candidate. Installation, update and uninstall testing must be completed before public release.
spanish.PreviewNotice=Candidato del instalador. Las pruebas reales de instalación, actualización y desinstalación deben completarse antes de publicarlo.

english.MaintenanceFailed=CursorShake could not complete verified shutdown. No process was forcibly terminated. Application files have not been replaced or removed. Exit all instances from the tray and retry.
spanish.MaintenanceFailed=CursorShake no ha podido completar el cierre verificado. No se ha terminado ningún proceso por la fuerza ni sustituido o retirado archivos de la aplicación. Sal de todas las instancias desde la bandeja y reintenta.
english.DeletePersonalData=Also remove this user's CursorShake configuration and personal themes? The application will show their exact locations for confirmation. Choose No to keep them.
spanish.DeletePersonalData=¿Quieres eliminar también la configuración y los temas personales de CursorShake de este usuario? La aplicación mostrará sus ubicaciones exactas para confirmar. Elige No para conservarlos.
english.SharedDataKept=Personal configuration and themes for all users will be preserved.
spanish.SharedDataKept=Se conservarán la configuración y los temas personales de todos los usuarios.
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
; A separate temporary copy can close the installed app without locking its binaries.
Source: "{#PayloadRoot}\*"; DestDir: "{tmp}\CursorShakeMaintenance"; Excludes: ".cursorshake-build-owned"; Flags: dontcopy recursesubdirs createallsubdirs
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Excludes: ".cursorshake-build-owned"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CursorShake"; Filename: "{app}\CursorShake.exe"

[Code]
var
  StartupIntegrationSucceeded: Boolean;
  MaintenanceMutex: THandle;
  MaintenanceOwned: Boolean;
  TemporaryPayloadReady: Boolean;
  DeletePersonalData: Boolean;
  RecoveryIndexRoot: String;
  RecoveryIndexHashRoot: String;
  DirectoryPins: array of THandle;

type
  TAttributeTag = record
    Attributes: LongWord;
    Tag: LongWord;
  end;

function CreateMutex(Attributes: NativeUInt; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function ReleaseMutex(Handle: THandle): Boolean;
  external 'ReleaseMutex@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function OpenDirectory(Name: String; Access, Share: LongWord; Security: NativeUInt;
  Disposition, Flags: LongWord; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function ReadAttributeTag(Handle: THandle; InfoClass: Integer; var Info: TAttributeTag; Size: LongWord): Boolean;
  external 'GetFileInformationByHandleEx@kernel32.dll stdcall';
function ConvertStringSid(Text: String; var Sid: NativeUInt): Boolean;
  external 'ConvertStringSidToSidW@advapi32.dll stdcall';
function FreeLocal(Pointer: NativeUInt): NativeUInt;
  external 'LocalFree@kernel32.dll stdcall';

procedure UnpinDirectories;
var
  I: Integer;
begin
  for I := GetArrayLength(DirectoryPins) - 1 downto 0 do CloseHandle(DirectoryPins[I]);
  SetArrayLength(DirectoryPins, 0);
end;

procedure PinDirectoryChain(const Directory: String);
var
  Parent: String;
  Handle: THandle;
  Info: TAttributeTag;
  Count: Integer;
begin
  Parent := ExtractFileDir(Directory);
  if (Parent <> Directory) and (Parent <> '') then PinDirectoryChain(Parent);
  Handle := OpenDirectory(Directory, $80, 3, 0, 3, $02200000, 0);
  if Handle = THandle(-1) then RaiseException(SysErrorMessage(DLLGetLastError));
  if not ReadAttributeTag(Handle, 9, Info, 8) then
  begin
    CloseHandle(Handle);
    RaiseException(SysErrorMessage(DLLGetLastError));
  end;
  if ((Info.Attributes and $400) <> 0) or ((Info.Attributes and $10) = 0) then
  begin
    CloseHandle(Handle);
    RaiseException('Recovery index cleanup rejected an unverified directory: ' + Directory);
  end;
  Count := GetArrayLength(DirectoryPins);
  SetArrayLength(DirectoryPins, Count + 1);
  DirectoryPins[Count] := Handle;
end;

function IsIndexRecord(const Name: String): Boolean;
var
  Sid: NativeUInt;
begin
  Result := False;
  if (Copy(Name, 1, 4) <> 'S-1-') or (ExtractFileExt(Name) <> '.json') then Exit;
  Sid := 0;
  Result := ConvertStringSid(Copy(Name, 1, Length(Name) - 5), Sid);
  if Sid <> 0 then FreeLocal(Sid);
end;

procedure CleanupRecoveryIndex;
var
  Found: TFindRec;
  Name, ErrorText: String;
  Entries: array of String;
  I, Count: Integer;
begin
  try
    { No profile directory is removed: only flat installation metadata inside protected parents. }
    PinDirectoryChain(RecoveryIndexRoot);
    if FindFirst(AddBackslash(RecoveryIndexRoot) + '*', Found) then
    begin
      try
        repeat
          Name := Found.Name;
          if (Name <> '.') and (Name <> '..') then
          begin
            if ((Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) or not IsIndexRecord(Name) then
              RaiseException('Unknown recovery-index entry was preserved: ' + Name);
            Count := GetArrayLength(Entries);
            SetArrayLength(Entries, Count + 1);
            Entries[Count] := Name;
          end;
        until not FindNext(Found);
      finally
        FindClose(Found);
      end;
    end;
    for I := 0 to GetArrayLength(Entries) - 1 do
      if not DeleteFile(AddBackslash(RecoveryIndexRoot) + Entries[I]) then
        RaiseException('Could not remove installation metadata: ' + Entries[I]);
  except
    ErrorText := GetExceptionMessage;
    MsgBox(ErrorText, mbError, MB_OK);
  end;
  UnpinDirectories;
  if ErrorText <> '' then Exit;
  { RemoveDir never recurses and preserves any unknown entries. }
  if not RemoveDir(RecoveryIndexRoot) then
  begin
    MsgBox('Installation metadata remains at ' + RecoveryIndexRoot + '. The scope marker was preserved.', mbError, MB_OK);
    Exit;
  end;
  if not RemoveDir(RecoveryIndexHashRoot) then
  begin
    MsgBox('Installation metadata remains at ' + RecoveryIndexHashRoot + '. The scope marker was preserved.', mbError, MB_OK);
    Exit;
  end;
  { Other installations may legitimately retain this shared parent. }
  RemoveDir(ExtractFileDir(RecoveryIndexHashRoot));
  try
    PinDirectoryChain(RemoveBackslashUnlessRoot(ExpandConstant('{app}')));
    if not DeleteFile(ExpandConstant('{app}\maintenance-scope.txt')) then
      RaiseException('Could not remove the installation scope marker.');
  except
    MsgBox(GetExceptionMessage, mbError, MB_OK);
  end;
  UnpinDirectories;
  RemoveDir(ExpandConstant('{app}'));
end;

procedure EndMaintenance;
begin
  if MaintenanceMutex <> 0 then
  begin
    if MaintenanceOwned then ReleaseMutex(MaintenanceMutex);
    CloseHandle(MaintenanceMutex);
    MaintenanceMutex := 0;
    MaintenanceOwned := False;
  end;
end;

procedure BeginMaintenance;
var
  Name: String;
  WaitResult: LongWord;
begin
  if MaintenanceOwned then Exit;
  Name := 'Global\CursorShake.Installation.' + GetSHA256OfUnicodeString(Uppercase(RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')))));
  MaintenanceMutex := CreateMutex(0, False, Name);
  if MaintenanceMutex = 0 then RaiseException(SysErrorMessage(DLLGetLastError));
  WaitResult := WaitForSingleObject(MaintenanceMutex, 10000);
  if (WaitResult <> 0) and (WaitResult <> 128) then
  begin
    EndMaintenance;
    RaiseException(CustomMessage('MaintenanceFailed'));
  end;
  MaintenanceOwned := True;
end;

function RunMaintenance(const Helper, Command: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if not FileExists(Helper) then
  begin
    MsgBox(CustomMessage('MaintenanceFailed') + #13#10 + Helper, mbError, MB_OK);
    Exit;
  end;
  if not Exec(Helper, Command + ' "' + RemoveBackslashUnlessRoot(ExpandConstant('{app}')) + '"',
    ExtractFileDir(Helper), SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    MsgBox(CustomMessage('MaintenanceFailed') + #13#10 + SysErrorMessage(ResultCode), mbError, MB_OK)
  else if ResultCode = 0 then Result := True
  else if ResultCode <> 1223 then
    MsgBox(CustomMessage('MaintenanceFailed') + #13#10 + IntToStr(ResultCode), mbError, MB_OK);
end;

procedure DeinitializeSetup;
begin
  EndMaintenance;
end;

function InitializeUninstall: Boolean;
var
  Choice: Integer;
  Scope: AnsiString;
  ScopeText, Hash: String;
begin
  DeletePersonalData := False;
  Result := False;
  if not LoadStringFromFile(ExpandConstant('{app}\maintenance-scope.txt'), Scope) then
  begin
    MsgBox(CustomMessage('MaintenanceFailed'), mbError, MB_OK);
    Exit;
  end;
  ScopeText := Trim(String(Scope));
  Hash := GetSHA256OfUnicodeString(Uppercase(RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')))));
  if ScopeText = 'all-users' then
    RecoveryIndexHashRoot := ExpandConstant('{commonappdata}\CursorShake.Installations\') + Hash
  else if (ScopeText = 'user') and not IsAdminInstallMode then
    RecoveryIndexHashRoot := ExpandConstant('{localappdata}\CursorShake.Installations\') + Hash
  else
  begin
    MsgBox(CustomMessage('MaintenanceFailed') + #13#10 + CustomMessage('OriginalUserNotice'), mbError, MB_OK);
    Exit;
  end;
  RecoveryIndexRoot := AddBackslash(RecoveryIndexHashRoot) + 'Users';
  Result := True;
  if IsAdminInstallMode then
  begin
    if not UninstallSilent then MsgBox(CustomMessage('SharedDataKept'), mbInformation, MB_OK);
  end
  else if not UninstallSilent then
  begin
    Choice := MsgBox(CustomMessage('DeletePersonalData'), mbConfirmation, MB_YESNOCANCEL or MB_DEFBUTTON2);
    if Choice = IDCANCEL then Result := False;
    DeletePersonalData := Choice = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    CleanupRecoveryIndex;
    Exit;
  end;
  if CurUninstallStep <> usUninstall then Exit;
  try
    BeginMaintenance;
    if DeletePersonalData then Command := '--installer-uninstall-remove-data'
    else Command := '--installer-uninstall-keep-data';
    if not RunMaintenance(ExpandConstant('{app}\CursorShake.exe'), Command) then Abort;
  except
    EndMaintenance;
    RaiseLastException;
  end;
end;

procedure DeinitializeUninstall;
begin
  UnpinDirectories;
  EndMaintenance;
end;

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
  if IsAdminInstallMode then Arguments := 'all-users' else Arguments := 'user';
  if not SaveStringToFile(ExpandConstant('{app}\maintenance-scope.txt'), Arguments, False) then
    RaiseException(CustomMessage('MaintenanceFailed'));
  if not RunMaintenance(ExpandConstant('{app}\CursorShake.exe'), '--installer-provision-index') then
    RaiseException(CustomMessage('MaintenanceFailed'));
  { Provisioning is complete before reopening normal application admission. }
  EndMaintenance;
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

function CheckDestination: String;
var
  Scope: AnsiString;
  ExpectedScope: String;
  Found: TFindRec;
begin
  Result := '';
  if IsAdminInstallMode then ExpectedScope := 'all-users' else ExpectedScope := 'user';
  if FileExists(ExpandConstant('{app}\maintenance-scope.txt')) then
  begin
    if not LoadStringFromFile(ExpandConstant('{app}\maintenance-scope.txt'), Scope) then
      Result := 'Cannot verify the existing installation scope.'
    else if Trim(String(Scope)) <> ExpectedScope then
      Result := 'Use the existing installation scope and location, or uninstall it before changing scope.';
  end
  else if DirExists(ExpandConstant('{app}')) then
  begin
    if FindFirst(ExpandConstant('{app}\*'), Found) then
    begin
      try
        repeat
          if (Found.Name <> '.') and (Found.Name <> '..') then
          begin
            Result := 'The destination is not an empty folder or a verified CursorShake installation. Select a different folder.';
            Exit;
          end;
        until not FindNext(Found);
      finally
        FindClose(Found);
      end;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := CheckDestination;
  if Result <> '' then Exit;
  try
    BeginMaintenance;
    if not TemporaryPayloadReady then
    begin
      ExtractTemporaryFiles('{tmp}\CursorShakeMaintenance\*');
      TemporaryPayloadReady := True;
    end;
    if not RunMaintenance(ExpandConstant('{tmp}\CursorShakeMaintenance\CursorShake.exe'), '--installer-quiesce') then
      Result := CustomMessage('MaintenanceFailed');
  except
    Result := CustomMessage('MaintenanceFailed') + #13#10 + GetExceptionMessage;
  end;
  if Result <> '' then EndMaintenance;
end;
