$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$setup=Join-Path $root "artifacts/installer/VitekaUtilidades-Setup-$version-win-x64-preview.exe"
$state=Join-Path $env:ProgramData 'Viteka/UtilidadesAccess'
if((Get-Service VitekaUtilidadesAccess -ErrorAction SilentlyContinue) -or (Test-Path $state)){throw 'Refusing installer test over an existing authority.'}
$directory=Join-Path $env:TEMP ('VitekaInstallerSmoke-'+[guid]::NewGuid().ToString('N'))
$install=Join-Path $directory 'Viteka'
New-Item -ItemType Directory $directory|Out-Null
function Run-Owned([string]$Exe,[string[]]$Arguments,[int]$Seconds=120){
    $process=Start-Process $Exe -ArgumentList $Arguments -PassThru
    if(-not $process.WaitForExit($Seconds*1000)){$process.Kill();throw 'Owned installer process timed out.'}
    if($process.ExitCode -ne 0){throw "Installer operation failed with exit $($process.ExitCode)."}
}
$previousBootstrap=$env:VITEKA_ACCESS_BOOTSTRAP
try{
    Run-Owned -Exe $setup -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),('/LOG="'+(Join-Path $directory 'install.log')+'"'))
    $server=Join-Path $install 'Servidor/Viteka.Server.exe'
    Run-Owned -Exe $server -Arguments @('--access-health',('"'+(Join-Path $install 'Cliente/access-server.json')+'"')) -Seconds 10
    $env:VITEKA_ACCESS_BOOTSTRAP=Join-Path $install 'Cliente/access-server.json'
    $env:VITEKA_UI_SMOKE_REPORT=Join-Path $directory 'installed-native.json'
    $env:VITEKA_UI_SMOKE_SCREENSHOT=Join-Path $directory 'installed-native.png'
    Run-Owned -Exe (Join-Path $install 'Cliente/Viteka.Desktop.exe') -Arguments @('--smoke-ui') -Seconds 90
    $report=Get-Content $env:VITEKA_UI_SMOKE_REPORT -Raw|ConvertFrom-Json
    if(-not $report.passed){throw "Installed native checks failed: $($report.stage) $($report.error)"}
    $before=(Get-FileHash (Join-Path $state 'Private/access.dat') -Algorithm SHA256).Hash
    Run-Owned -Exe $setup -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),('/LOG="'+(Join-Path $directory 'upgrade.log')+'"'))
    if((Get-FileHash (Join-Path $state 'Private/access.dat') -Algorithm SHA256).Hash -ne $before){throw 'Installer upgrade changed the user authority.'}
    Run-Owned -Exe $server -Arguments @('--access-health',('"'+(Join-Path $install 'Cliente/access-server.json')+'"')) -Seconds 10
    Write-Output 'PASS: actual installer, installed native client with role guards, in-place upgrade and account preservation.'
} catch {
    Get-Content (Join-Path $directory '*.log') -Tail 15 -ErrorAction SilentlyContinue
    if(Test-Path $env:VITEKA_UI_SMOKE_REPORT){Get-Content $env:VITEKA_UI_SMOKE_REPORT}
    throw
} finally {
    $env:VITEKA_ACCESS_BOOTSTRAP=$previousBootstrap
    $uninstaller=Join-Path $install 'unins000.exe'
    if(Test-Path $uninstaller){Run-Owned -Exe $uninstaller -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Seconds 60}
    if(Get-Service VitekaUtilidadesAccess -ErrorAction SilentlyContinue){throw 'Installed access service was not removed.'}
    if(-not(Test-Path (Join-Path $state 'Private/access.dat'))){throw 'Uninstall must preserve the account authority.'}
    Remove-Item $state -Recurse -Force
}
