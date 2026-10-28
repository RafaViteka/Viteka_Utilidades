[CmdletBinding()]
param([ValidateSet('Install','Stop','Start','Uninstall')][string]$Action='Install',[string]$ClientDirectory=(Join-Path $PSScriptRoot '../Cliente'))
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'Viteka.Server.exe'
& $exe --access-manage $Action.ToLowerInvariant() $ClientDirectory
exit $LASTEXITCODE
