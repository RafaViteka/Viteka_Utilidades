$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
& py -3.12 -c "import struct; assert struct.calcsize('P') == 8, 'Se requiere Python de 64 bits'"
if ($LASTEXITCODE -ne 0) { throw 'Instala Python 3.12 de 64 bits y su lanzador py antes de continuar.' }
& py -3.12 -m venv .venv
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el entorno Python.' }
$python = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
& $python -m pip install -r requirements.txt
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron instalar las dependencias.' }
& $python server.py --configurar
if ($LASTEXITCODE -ne 0) { throw 'La conexion no se ha guardado. Revisa el DSN, controlador y credenciales.' }
Write-Host ''
Write-Host 'MCP listo para anadir en la aplicacion instalada EN ESTE EQUIPO y con ESTE USUARIO WINDOWS.'
Write-Host 'Nombre: Viteka NixFarma'
Write-Host 'Tipo: STDIO'
Write-Host "Comando: $python"
Write-Host "Argumento unico: $(Join-Path $PSScriptRoot 'server.py')"
Write-Host "Directorio de trabajo: $PSScriptRoot"
Write-Host 'Variables de entorno y paso de variables: vacios.'
