$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'artifacts/windows-x64/Servidor/Viteka.Server.exe'
$manage = Join-Path $repo 'artifacts/windows-x64/Servidor/manage-access-service.ps1'
if (Get-Service VitekaUtilidadesAccess -ErrorAction SilentlyContinue) { throw 'Refusing to replace an existing access service during CI.' }
$serviceState = Join-Path $env:ProgramData 'Viteka/UtilidadesAccess'
if (Test-Path $serviceState) { throw 'Refusing to replace pre-existing authority state during CI.' }
$root = Join-Path $env:TEMP ('VitekaAccessSmoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    & $manage -Action Install -ClientDirectory (Join-Path $root 'InstalledClient')
    if ($LASTEXITCODE -ne 0) { throw 'Access service installation failed.' }
    $before = (Get-FileHash (Join-Path $serviceState 'Private/access.dat') -Algorithm SHA256).Hash
    & $manage -Action Install -ClientDirectory (Join-Path $root 'InstalledClient')
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash (Join-Path $serviceState 'Private/access.dat') -Algorithm SHA256).Hash -ne $before) { throw 'Reinstall did not preserve the account authority.' }
    & $exe --access-health (Join-Path $root 'InstalledClient/access-server.json')
    if ($LASTEXITCODE -ne 0) { throw 'Installed LocalService authority TLS health failed.' }
} finally {
    & $manage -Action Uninstall
    if ($LASTEXITCODE -ne 0) { throw 'Access service cleanup failed.' }
    Remove-Item $serviceState -Recurse -Force
}
$fixture = Join-Path $root 'Fixture'
$process = Start-Process $exe -ArgumentList @('--access-console',('"'+$fixture+'"'),'7448') -RedirectStandardOutput (Join-Path $root 'service-out.log') -RedirectStandardError (Join-Path $root 'service-error.log') -PassThru
$bootstrap = Join-Path $fixture 'Public/access-server.json'
$ready=$false
for ($attempt=0;$attempt -lt 20;$attempt++) {
    if (Test-Path $bootstrap) {
        & $exe --access-health $bootstrap *> $null
        if ($LASTEXITCODE -eq 0) { $ready=$true;break }
    }
    if ($process.HasExited) { throw 'Fixture service exited before startup.' }
    Start-Sleep -Seconds 1
}
if (-not $ready) { $process.Kill(); throw 'Fixture service TLS health failed.' }
# A different installation's valid CA must not authenticate this server.
$wrong = Join-Path $root 'WrongTrust'
New-Item -ItemType Directory $wrong | Out-Null
Copy-Item (Join-Path $root 'InstalledClient/access-root.cer') (Join-Path $wrong 'access-root.cer')
$metadata = Get-Content $bootstrap -Raw | ConvertFrom-Json
$metadata.RootSha256 = (Get-FileHash (Join-Path $wrong 'access-root.cer') -Algorithm SHA256).Hash
$metadata | ConvertTo-Json | Set-Content (Join-Path $wrong 'access-server.json')
& $exe --access-health (Join-Path $wrong 'access-server.json') *> $null
if ($LASTEXITCODE -eq 0) { $process.Kill(); throw 'TLS accepted an unrelated authority.' }
"VITEKA_ACCESS_BOOTSTRAP=$bootstrap" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
"VITEKA_ACCESS_FIXTURE_PID=$($process.Id)" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
if (Test-Path (Join-Path $repo 'artifacts/windows-x64/Cliente/access-server.json')) { throw 'CI bootstrap must never be packaged in the client.' }
Write-Output 'PASS: installed LocalService authority, protected state, verified TLS, cleanup and rejection of an unrelated CA; isolated UI authority is ready.'
