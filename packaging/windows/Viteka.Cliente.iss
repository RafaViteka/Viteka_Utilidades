#ifndef AppVersion
#define AppVersion "0.18.24"
#endif

[Setup]
AppId={{7BEAD843-779C-4624-A2A7-C13EA8E3402E}
AppName=Viteka Conecta - Puesto cliente
VersionInfoProductName=Viteka Conecta - Puesto cliente
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
AppVersion={#AppVersion}
AppPublisher=Dinaro Partners, S.L.
AppPublisherURL=mailto:conecta@viteka.es
AppSupportURL=mailto:conecta@viteka.es
DefaultDirName={localappdata}\Viteka\ConectaPuesto
DefaultGroupName=Viteka Conecta
SetupIconFile=viteka.ico
OutputDir=..\..\artifacts\installer
OutputBaseFilename=VitekaConecta-Cliente-Setup-{#AppVersion}-win-x64
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={code:ClientExe}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "LEEME-CLIENTE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Viteka Conecta"; Filename: "{code:ClientExe}"; WorkingDir: "{code:ClientDir}"; IconFilename: "{code:ClientIcon}"; AppUserModelID: "Viteka.Utilidades"
Name: "{userdesktop}\Viteka Conecta"; Filename: "{code:ClientExe}"; WorkingDir: "{code:ClientDir}"; IconFilename: "{code:ClientIcon}"; AppUserModelID: "Viteka.Utilidades"
Name: "{group}\Información para puestos cliente"; Filename: "{app}\LEEME-CLIENTE.txt"

[Run]
Filename: "{code:ClientExe}"; Description: "Iniciar Viteka Conecta"; Flags: nowait postinstall skipifsilent

[Code]
function ClientDir(Param: String): String;
begin
  Result := ExpandConstant('{param:SHAREDIR|X:\Viteka\Conecta\Cliente}');
end;

function ClientExe(Param: String): String;
begin
  Result := AddBackslash(ClientDir('')) + 'Viteka.Desktop.exe';
end;

function ClientIcon(Param: String): String;
begin
  Result := AddBackslash(ClientDir('')) + 'viteka.ico';
end;

function InitializeSetup(): Boolean;
var Shared: String;
begin
  Shared := ClientDir('');
  Result := FileExists(ClientExe('')) and FileExists(AddBackslash(Shared) + 'access-server.json');
  if not Result then
    MsgBox('No se encuentra la instalación compartida de Viteka Conecta en ' + Shared + '.' + #13#10 + #13#10 +
      'Comprueba que el disco X: está conectado al servidor y que el instalador de servidor ya se ha ejecutado.', mbError, MB_OK);
end;
