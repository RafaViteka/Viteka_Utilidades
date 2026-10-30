#ifndef AppVersion
#define AppVersion "0.12.0"
#endif
#define PublishRoot "..\..\artifacts\windows-x64"

[Setup]
AppId={{AAC03BF6-8845-4E96-AFC1-73AF54176983}
AppName=Viteka Utilidades - Vista previa local
AppVersion={#AppVersion}
AppPublisher=Viteka
ChangesAssociations=yes
DefaultDirName={autopf}\Viteka\UtilidadesVistaPrevia
DefaultGroupName=Viteka Utilidades - Vista previa
UninstallDisplayIcon={app}\Cliente\Viteka.Desktop.exe
SetupIconFile=viteka.ico
OutputDir=..\..\artifacts\installer
OutputBaseFilename=VitekaUtilidades-Setup-{#AppVersion}-win-x64-preview
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
InfoBeforeFile=LEEME.txt

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "..\..\artifacts\prerequisites\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy
Source: "{#PublishRoot}\Cliente\*"; DestDir: "{app}\Cliente"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishRoot}\Servidor\*"; DestDir: "{app}\Servidor"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LEEME.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Viteka Utilidades"; Filename: "{app}\Cliente\Viteka.Desktop.exe"; WorkingDir: "{app}\Cliente"; IconFilename: "{app}\Cliente\viteka.ico"; AppUserModelID: "Viteka.Utilidades"
Name: "{group}\Leer antes de probar"; Filename: "{app}\LEEME.txt"

[InstallDelete]
Type: files; Name: "{app}\Servidor\Catalogs\tenencia\*.json"
Type: files; Name: "{group}\Iniciar servidor de demostración.lnk"

[Run]
Filename: "{app}\Cliente\Viteka.Desktop.exe"; Description: "Abrir Viteka Utilidades"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure StopAccessService();
var Code: Integer;
begin
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesAccess') then
    if (not Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage stop', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
      RaiseException('No se pudo detener el servicio de usuarios. No se reemplazarán sus archivos.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    if (not Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage install "' + ExpandConstant('{app}\Cliente') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
      RaiseException('El servicio de usuarios no está disponible. Las cuentas se han conservado. Contacta con Viteka para revisar la instalación.');
  end;
end;

// Actualización desde la propia aplicación: /VERYSILENT ... /RELAUNCH=1.
// Al terminar (o si falla), se vuelve a abrir Viteka para que el usuario recupere su sesión.
function RelaunchRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

procedure RelaunchApplication();
var Code: Integer;
    Client: String;
begin
  Client := ExpandConstant('{app}\Cliente\Viteka.Desktop.exe');
  if FileExists(Client) then
    // explorer.exe abre el programa en la sesión del escritorio sin heredar la elevación del instalador.
    Exec(ExpandConstant('{win}\explorer.exe'), AddQuotes(Client), '', SW_SHOWNORMAL, ewNoWait, Code);
end;

procedure DeinitializeSetup();
var Code: Integer;
begin
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesAccess') and FileExists(ExpandConstant('{app}\Servidor\Viteka.Server.exe')) then
    Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage start', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if RelaunchRequested() then
    RelaunchApplication();
end;

function WebViewInstalled(): Boolean;
var Version: String;
    Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := (RegQueryStringValue(HKLM32, Key, 'pv', Version) or RegQueryStringValue(HKLM64, Key, 'pv', Version) or RegQueryStringValue(HKCU, Key, 'pv', Version)) and (Version <> '') and (Version <> '0.0.0.0');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  StopAccessService();
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesPreview') then
    Result := 'Desinstala primero el servicio de vista previa con manage-service.ps1 -Action Uninstall. Sus datos se conservarán. No se reemplazarán archivos del servicio mientras esté registrado.';
  if (Result = '') and not WebViewInstalled() then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := 'No se pudo iniciar la instalación de Microsoft Edge WebView2.'
    else if (Code <> 0) and (Code <> 3010) then
      Result := 'No se pudo instalar Microsoft Edge WebView2. Comprueba tu conexión a internet y vuelve a intentarlo.';
  end;
end;

function InitializeUninstall(): Boolean;
var Code: Integer;
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesPreview');
  if Result and RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesAccess') then
    Result := Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage uninstall', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then
    MsgBox('Desinstala primero el servicio de vista previa con manage-service.ps1 -Action Uninstall. Después podrás desinstalar la aplicación.', mbError, MB_OK);
end;
