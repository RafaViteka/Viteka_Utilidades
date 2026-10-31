$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$normal=Join-Path $root 'artifacts/normal-server/Viteka.Server.exe'
$setup=Join-Path $root "artifacts/installer/VitekaUtilidades-Test-Setup-$version-win-x64.exe"
$production=Join-Path $env:ProgramData 'Viteka/UtilidadesAccess'
$review=Join-Path $env:ProgramData 'Viteka/UtilidadesReviewAccess'
foreach($service in @('VitekaUtilidadesAccess','VitekaUtilidadesReviewAccess')){if(Get-Service $service -ErrorAction SilentlyContinue){throw 'Refusing review test over existing services.'}}
foreach($state in @($production,$review)){if(Test-Path $state){throw 'Refusing review test over existing data.'}}
$directory=Join-Path $env:TEMP ('VitekaPcReviewSmoke-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $directory|Out-Null
$install=Join-Path $directory 'Pruebas'
$productionOwned=$false
$normalKey='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AAC03BF6-8845-4E96-AFC1-73AF54176983}_is1'
$reviewKey='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8817D5EC-91E7-4C88-88C9-27C66C565B8D}_is1'
if(Test-Path $normalKey){throw 'Refusing test over pre-existing normal installer registration.'}
$registryOwned=$false
function Run([string]$Exe,[string[]]$Arguments,[int]$Seconds=120){
 $process=Start-Process $Exe -ArgumentList $Arguments -PassThru
 if(-not $process.WaitForExit($Seconds*1000)){$process.Kill();throw 'Review test operation timed out.'}
 if($process.ExitCode -ne 0){throw "Review operation failed: $($process.ExitCode)."}
}
function Hash-Private([string]$Path){
 return @(Get-ChildItem (Join-Path $Path 'Private') -File -Recurse|Sort-Object FullName|ForEach-Object { $_.Name+':'+(Get-FileHash $_.FullName -Algorithm SHA256).Hash }) -join '|'
}
try{
 $productionOwned=$true
 Run -Exe $normal -Arguments @('--access-manage','install',('"'+(Join-Path $directory 'NormalClient')+'"'))
 $before=Hash-Private $production
 New-Item $normalKey -Force|Out-Null
 $registryOwned=$true
 New-ItemProperty $normalKey -Name ProductionSmokeMarker -Value 'unchanged'|Out-Null
 New-ItemProperty $normalKey -Name DisplayName -Value 'Viteka normal coexistence fixture'|Out-Null
 Run -Exe $setup -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),('/LOG="'+(Join-Path $directory 'review-install.log')+'"'))
 if((Get-Item $setup).VersionInfo.ProductName -ne 'Viteka Utilidades - Pruebas'){throw 'Wrong installer product identity.'}
 if(-not(Test-Path $reviewKey) -or (Get-ItemProperty $normalKey).DisplayName -ne 'Viteka normal coexistence fixture'){throw 'Review installer changed normal registration.'}
 $metadata=Get-Content (Join-Path $install 'Cliente/access-server.json') -Raw|ConvertFrom-Json
 if($metadata.ServerUrl -ne 'https://localhost:7448/'){throw 'Review installer did not use its local test endpoint.'}
 $listeners=@(Get-NetTCPConnection -LocalPort 7448 -State Listen)
 if($listeners|Where-Object {$_.LocalAddress -notin @('::1','127.0.0.1')}){throw 'Review service must never listen on the LAN.'}
 if(Get-NetFirewallRule -DisplayName 'VitekaUtilidadesReviewAccess-LAN' -ErrorAction SilentlyContinue){throw 'Review must not add a LAN firewall rule.'}
 $server=Join-Path $install 'Servidor/Viteka.Server.exe'
 Run -Exe $server -Arguments @('--access-health',('"'+(Join-Path $install 'Cliente/access-server.json')+'"'))
 $idHash=(Get-FileHash (Join-Path $review 'Private/review-device.dat') -Algorithm SHA256).Hash
 $env:VITEKA_ACCESS_BOOTSTRAP=Join-Path $install 'Cliente/access-server.json'
 $env:VITEKA_UI_SMOKE_REPORT=Join-Path $directory 'review-native.json'
 $env:VITEKA_UI_SMOKE_SCREENSHOT=Join-Path $directory 'review-native.png'
 Run -Exe (Join-Path $install 'Cliente/Viteka.Desktop.exe') -Arguments @('--smoke-ui') -Seconds 100
 $report=Get-Content $env:VITEKA_UI_SMOKE_REPORT -Raw|ConvertFrom-Json
 if(-not $report.passed){throw "Review WebView2 failed: $($report.stage) $($report.error)"}
 $accounts=(Get-FileHash (Join-Path $review 'Private/access.dat') -Algorithm SHA256).Hash
 Run -Exe $setup -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$install+'"'),('/LOG="'+(Join-Path $directory 'review-upgrade.log')+'"'))
 if((Get-FileHash (Join-Path $review 'Private/review-device.dat') -Algorithm SHA256).Hash -ne $idHash){throw 'Review upgrade changed its device identity.'}
 if((Get-FileHash (Join-Path $review 'Private/access.dat') -Algorithm SHA256).Hash -ne $accounts){throw 'Review upgrade changed accounts.'}
 if((Hash-Private $production) -ne $before -or (Get-Service VitekaUtilidadesAccess).Status -ne 'Running'){throw 'Review modified normal installation.'}
 Run -Exe (Join-Path $install 'unins000.exe') -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
 if(Get-Service VitekaUtilidadesReviewAccess -ErrorAction SilentlyContinue){throw 'Review service was not removed.'}
 if(-not(Test-Path $normalKey) -or (Get-ItemProperty $normalKey).ProductionSmokeMarker -ne 'unchanged'){throw 'Review removal damaged normal registration.'}
 if((Hash-Private $production) -ne $before -or (Get-Service VitekaUtilidadesAccess).Status -ne 'Running'){throw 'Review removal affected normal installation.'}
 if((Get-FileHash (Join-Path $review 'Private/review-device.dat') -Algorithm SHA256).Hash -ne $idHash){throw 'Review uninstall lost its identity.'}
 Write-Output 'PASS: isolated installer, local-only service, native WebView2, stable PC ID, upgrade/uninstall and untouched normal service and private data.'
}catch{
 Get-Content (Join-Path $directory '*.log') -Tail 12 -ErrorAction SilentlyContinue
 if(Test-Path $env:VITEKA_UI_SMOKE_REPORT){Get-Content $env:VITEKA_UI_SMOKE_REPORT}
 throw
}finally{
 $uninstall=Join-Path $install 'unins000.exe'
 if((Test-Path $uninstall) -and (Get-Service VitekaUtilidadesReviewAccess -ErrorAction SilentlyContinue)){Run -Exe $uninstall -Arguments @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')}
 if($productionOwned){Run -Exe $normal -Arguments @('--access-manage','uninstall')}
 if($registryOwned -and (Test-Path $normalKey)){Remove-Item $normalKey -Recurse -Force}
 foreach($state in @($production,$review)){if(Test-Path $state){Remove-Item $state -Recurse -Force}}
}
