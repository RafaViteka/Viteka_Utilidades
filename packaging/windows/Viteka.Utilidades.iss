#ifndef AppVersion
#define AppVersion "0.12.0"
#endif
#ifndef PublishRoot
#define PublishRoot "..\..\artifacts\windows-x64"
#endif

#ifdef ReviewBuild
#define IdentityGuid "8817D5EC-91E7-4C88-88C9-27C66C565B8D"
#define ProductTitle "Viteka Conecta - Pruebas"
#define ShortcutTitle "Viteka Conecta - Pruebas"
#define GroupTitle "Viteka Conecta - Pruebas"
#define InstallFolder "UtilidadesPruebas"
#define AccessService "VitekaUtilidadesReviewAccess"
#define ShellIdentity "Viteka.Utilidades.Pruebas"
#define SetupFile "VitekaUtilidades-Test-Setup-" + AppVersion + "-win-x64"
#define ReviewMode "True"
#define PreviousGroupTitle "Viteka Utilidades - Pruebas"
#define PreviousShortcutTitle "Viteka Utilidades - Pruebas"
#else
#define IdentityGuid "AAC03BF6-8845-4E96-AFC1-73AF54176983"
#define ProductTitle "Viteka Conecta"
#define ShortcutTitle "Viteka Conecta"
#define GroupTitle "Viteka Conecta"
#define InstallFolder "UtilidadesVistaPrevia"
#define AccessService "VitekaUtilidadesAccess"
#define ShellIdentity "Viteka.Utilidades"
#define SetupFile "VitekaUtilidades-Setup-" + AppVersion + "-win-x64-preview"
#define ReviewMode "False"
#define PreviousGroupTitle "Viteka Utilidades - Vista previa"
#define PreviousShortcutTitle "Viteka Utilidades"
#endif
[Setup]
AppId={{{#IdentityGuid}}
AppName={#ProductTitle}
VersionInfoProductName={#ProductTitle}
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
AppVersion={#AppVersion}
AppPublisher=Dinaro Partners, S.L.
AppPublisherURL=mailto:conecta@viteka.es
AppSupportURL=mailto:conecta@viteka.es
AppComments=Herramientas y servicios para tu farmacia
ChangesAssociations=yes
DefaultDirName={autopf}\Viteka\{#InstallFolder}
DefaultGroupName={#GroupTitle}
UninstallDisplayIcon={app}\Cliente\Viteka.Desktop.exe
SetupIconFile=viteka.ico
OutputDir=..\..\artifacts\installer
OutputBaseFilename={#SetupFile}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
#ifdef ReviewBuild
DisableDirPage=yes
#endif
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
Name: "{group}\{#ShortcutTitle}"; Filename: "{app}\Cliente\Viteka.Desktop.exe"; WorkingDir: "{app}\Cliente"; IconFilename: "{app}\Cliente\viteka.ico"; AppUserModelID: "{#ShellIdentity}"
Name: "{group}\Información de uso y asistencia"; Filename: "{app}\LEEME.txt"

[InstallDelete]
Type: files; Name: "{app}\Servidor\Catalogs\tenencia\*.json"
Type: files; Name: "{group}\Iniciar servidor de demostración.lnk"
Type: files; Name: "{autoprograms}\{#PreviousGroupTitle}\{#PreviousShortcutTitle}.lnk"
Type: files; Name: "{autoprograms}\{#PreviousGroupTitle}\Leer antes de probar.lnk"

[Run]
Filename: "{app}\Cliente\Viteka.Desktop.exe"; Description: "Iniciar Viteka Conecta"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: not IsUpgrade

[Code]
var
  Upgrade: Boolean;

// Actualización sobre una instalación existente: no se muestra ningún paso del asistente (bienvenida, LEEME,
// carpeta, grupo, listo ni finalizado). Solo queda la ventana de progreso y, al terminar, se reabre Viteka.
// Así funciona igual aunque el instalador lo abra una versión anterior de la app sin parámetros silenciosos.
function InitializeSetup(): Boolean;
begin
  Upgrade := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{#IdentityGuid}}_is1');
  Result := True;
end;

procedure InitializeWizard();
begin
  WizardForm.Caption := 'Instalación de Viteka Conecta';
  WizardForm.WelcomeLabel1.Caption := 'Bienvenido a Viteka Conecta';
  WizardForm.WelcomeLabel2.Caption := 'Viteka Conecta reúne en un único entorno las herramientas y servicios de Viteka para facilitar el trabajo diario de tu farmacia.' + #13#10 + #13#10 +
    'Este asistente instalará la aplicación en este equipo.' + #13#10 + #13#10 +
    'Si vas a actualizar una instalación existente, cierra Viteka Conecta en el servidor y en los puestos de la farmacia antes de continuar.' + #13#10 + #13#10 +
    'Pulsa «Siguiente» para continuar o «Cancelar» para salir del asistente.';
  WizardForm.FinishedHeadingLabel.Caption := 'Instalación completada';
  WizardForm.FinishedLabel.Caption := 'La instalación de Viteka Conecta ha finalizado.' + #13#10 + #13#10 +
    'Antes de comenzar a trabajar, comprueba la configuración de la farmacia, la conexión con el programa de gestión y los permisos de acceso.' + #13#10 + #13#10 +
    'Pulsa «Finalizar» para cerrar el asistente.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpInfoBefore then
    WizardForm.PageNameLabel.Caption := 'Información importante antes de instalar'
  else if CurPageID = wpInstalling then
  begin
    WizardForm.PageNameLabel.Caption := 'Instalando Viteka Conecta';
    WizardForm.StatusLabel.Caption := 'Espera mientras se preparan los componentes de la aplicación.';
  end
  else if CurPageID = wpFinished then
    WizardForm.PageNameLabel.Caption := 'Instalación completada';
end;

function IsUpgrade(): Boolean;
begin
  Result := Upgrade;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := Upgrade;
end;

procedure StopAccessService();
var Code: Integer;
begin
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#AccessService}') then
    if (not Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage stop', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
      RaiseException('No se ha podido detener el servicio de acceso de Viteka Conecta. La instalación no puede continuar con la sustitución de sus archivos. Si el problema persiste, contacta con conecta@viteka.es.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    if (not Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage install "' + ExpandConstant('{app}\Cliente') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
      RaiseException('No se ha podido completar la configuración del servicio de acceso. No inicies el uso de la aplicación hasta resolver la incidencia. Contacta con conecta@viteka.es.');
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
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#AccessService}') and FileExists(ExpandConstant('{app}\Servidor\Viteka.Server.exe')) then
    Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage start', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if RelaunchRequested() or (Upgrade and not WizardSilent()) then
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
  if (not {#ReviewMode}) and RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesPreview') then
    Result := 'Se ha detectado un servicio de una instalación anterior que debe retirarse antes de continuar. Contacta con Viteka para revisar la instalación. No elimines archivos ni servicios manualmente.';
  if (Result = '') and not WebViewInstalled() then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := 'No se ha podido iniciar la instalación de Microsoft Edge WebView2, un componente necesario para ejecutar Viteka Conecta. Vuelve a intentarlo. Si el problema persiste, contacta con conecta@viteka.es.'
    else if (Code <> 0) and (Code <> 3010) then
      Result := 'No se ha podido completar la instalación de Microsoft Edge WebView2. Comprueba la conexión a internet y los permisos del equipo antes de volver a intentarlo.';
  end;
end;

function InitializeUninstall(): Boolean;
var Code: Integer;
begin
  Result := {#ReviewMode} or not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\VitekaUtilidadesPreview');
  if Result and RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#AccessService}') then
    Result := Exec(ExpandConstant('{app}\Servidor\Viteka.Server.exe'), '--access-manage uninstall', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then
    MsgBox('No se ha podido completar la preparación de la desinstalación. Contacta con conecta@viteka.es antes de eliminar archivos o servicios manualmente.', mbError, MB_OK);
end;
