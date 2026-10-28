$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $repo 'artifacts/windows-x64/Servidor/Viteka.Server.exe'
if(Get-Service VitekaUtilidadesAccess -ErrorAction SilentlyContinue){throw 'Refusing to replace an existing access service during CI.'}
$serviceState=Join-Path $env:ProgramData 'Viteka/UtilidadesAccess'
if(Test-Path $serviceState){throw 'Refusing to replace pre-existing authority state during CI.'}
$root=Join-Path $env:TEMP ('VitekaAccessSmoke-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root|Out-Null
function Invoke-OwnServer([string[]]$Arguments,[int]$Seconds=60,[switch]$ExpectFailure){
    Write-Output "::notice::Access smoke: $($Arguments[0]) $($Arguments[1])"
    $label=[guid]::NewGuid().ToString('N')
    $stdout=Join-Path $root "$label-out.log"; $stderr=Join-Path $root "$label-error.log"
    $process=Start-Process $exe -ArgumentList $Arguments -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if(-not $process.WaitForExit($Seconds*1000)){
        $process.Kill();$null=$process.WaitForExit(5000)
        Get-Content $stdout -Tail 20 -ErrorAction SilentlyContinue
        Get-Content $stderr -Tail 20 -ErrorAction SilentlyContinue
        throw "Owned CLI timed out: $($Arguments[0]) $($Arguments[1])"
    }
    if($ExpectFailure){if($process.ExitCode -eq 0){throw 'TLS accepted an unrelated authority.'};return}
    Get-Content $stdout -ErrorAction SilentlyContinue
    if($process.ExitCode -ne 0){Get-Content $stderr -Tail 30 -ErrorAction SilentlyContinue;throw "Owned CLI failed: $($Arguments[0]) $($Arguments[1])"}
}
$failed=$false
try{
    Invoke-OwnServer -Arguments @('--access-manage','install',('"'+(Join-Path $root 'InstalledClient')+'"'))
    $before=(Get-FileHash (Join-Path $serviceState 'Private/access.dat') -Algorithm SHA256).Hash
    Invoke-OwnServer -Arguments @('--access-manage','install',('"'+(Join-Path $root 'InstalledClient')+'"'))
    if((Get-FileHash (Join-Path $serviceState 'Private/access.dat') -Algorithm SHA256).Hash -ne $before){throw 'Reinstall did not preserve accounts.'}
    Invoke-OwnServer -Arguments @('--access-health',('"'+(Join-Path $root 'InstalledClient/access-server.json')+'"')) -Seconds 10
}catch{$failed=$true;throw}finally{
    try{
        Invoke-OwnServer -Arguments @('--access-manage','uninstall') -Seconds 45
        Remove-Item $serviceState -Recurse -Force
    }catch{if(-not $failed){throw};Write-Output 'Cleanup did not finish; the failed authority is isolated to this CI VM.'}
}
$fixture=Join-Path $root 'Fixture'
$process=Start-Process $exe -ArgumentList @('--access-console',('"'+$fixture+'"'),'7448') -RedirectStandardOutput (Join-Path $root 'service-out.log') -RedirectStandardError (Join-Path $root 'service-error.log') -PassThru
$bootstrap=Join-Path $fixture 'Public/access-server.json'
"VITEKA_ACCESS_BOOTSTRAP=$bootstrap"|Out-File $env:GITHUB_ENV -Append -Encoding utf8
"VITEKA_ACCESS_FIXTURE_PID=$($process.Id)"|Out-File $env:GITHUB_ENV -Append -Encoding utf8
$ready=$false
for($attempt=0;$attempt -lt 20;$attempt++){
    if($process.HasExited){Get-Content (Join-Path $root 'service-error.log') -Tail 20;throw 'Fixture exited before startup.'}
    if(Test-Path $bootstrap){
        $socket=[System.Net.Sockets.TcpClient]::new()
        try{if($socket.ConnectAsync('localhost',7448).Wait(500) -and $socket.Connected){$ready=$true;break}}catch{}finally{$socket.Dispose()}
    }
    Start-Sleep -Seconds 1
}
if(-not $ready){$process.Kill();throw 'Fixture did not start listening.'}
Invoke-OwnServer -Arguments @('--access-health',('"'+$bootstrap+'"')) -Seconds 10
$wrong=Join-Path $root 'WrongTrust';New-Item -ItemType Directory $wrong|Out-Null
Copy-Item (Join-Path $root 'InstalledClient/access-root.cer') (Join-Path $wrong 'access-root.cer')
$metadata=Get-Content $bootstrap -Raw|ConvertFrom-Json
$metadata.RootSha256=(Get-FileHash (Join-Path $wrong 'access-root.cer') -Algorithm SHA256).Hash
$metadata|ConvertTo-Json|Set-Content (Join-Path $wrong 'access-server.json')
Invoke-OwnServer -Arguments @('--access-health',('"'+(Join-Path $wrong 'access-server.json')+'"')) -Seconds 10 -ExpectFailure
if(Test-Path (Join-Path $repo 'artifacts/windows-x64/Cliente/access-server.json')){throw 'CI bootstrap must never be packaged.'}
Write-Output 'PASS: LocalService installation/reinstallation, preserved accounts, TLS health, removal, rejection of an unrelated CA, isolated authority ready.'
