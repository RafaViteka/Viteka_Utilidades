#ifndef AppVersion
#define AppVersion "0.7.0"
#endif
#define PublishRoot "..\..\artifacts\windows-x64"

[Setup]
AppId={{AAC03BF6-8845-4E96-AFC1-73AF54176983}
AppName=Viteka Utilidades - Vista previa local
AppVersion={#AppVersion}
AppPublisher=Viteka
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
Name: "{group}\Viteka Utilidades"; Filename: "{app}\Cliente\Viteka.Desktop.exe"; WorkingDir: "{app}\Cliente"
Name: "{group}\Iniciar servidor de demostración"; Filename: "{app}\Servidor\Viteka.Server.exe"; WorkingDir: "{app}\Servidor"
Name: "{group}\Leer antes de probar"; Filename: "{app}\LEEME.txt"

[Run]
Filename: "{app}\Cliente\Viteka.Desktop.exe"; Description: "Abrir Viteka Utilidades"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function InitializeSetup(): Boolean;
begin
  Result := MsgBox('Esta es una vista previa local: no conecta con NixFarma ni permite acceso desde otros puestos. No instala el servicio definitivo ni actualizaciones online. ¿Quieres instalarla para evaluación?', mbConfirmation, MB_YESNO) = IDYES;
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
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesPreview');
  if not Result then
    MsgBox('Desinstala primero el servicio de vista previa con manage-service.ps1 -Action Uninstall. Después podrás desinstalar la aplicación.', mbError, MB_OK);
end;
