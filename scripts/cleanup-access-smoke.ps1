$ErrorActionPreference='Stop'
if($env:VITEKA_ACCESS_SMOKE_OWNS_AUTHORITY -ne '1'){exit 0}
$repo=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $repo 'artifacts/windows-x64/Servidor/Viteka.Server.exe'
& $exe --access-manage uninstall
if($LASTEXITCODE -ne 0){throw 'Failed to remove the owned CI access service.'}
$state=Join-Path $env:ProgramData 'Viteka/UtilidadesAccess'
if(Get-Service VitekaUtilidadesAccess -ErrorAction SilentlyContinue){throw 'The owned CI service is still registered.'}
if(Test-Path $state){Remove-Item $state -Recurse -Force}
"VITEKA_ACCESS_SMOKE_OWNS_AUTHORITY=0"|Out-File $env:GITHUB_ENV -Append -Encoding utf8
Write-Output 'PASS: isolated CI authority removed; production installation test can use a clean machine.'
