$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $repo 'artifacts/windows-x64/Servidor/Viteka.Server.exe'
if(Get-Service VitekaUtilidadesAccess -ErrorAction SilentlyContinue){throw 'Refusing to replace an existing access service during CI.'}
$state=Join-Path $env:ProgramData 'Viteka/UtilidadesAccess'
if(Test-Path $state){throw 'Refusing to replace pre-existing authority state during CI.'}
$root=Join-Path $env:TEMP ('VitekaAccessSmoke-'+[guid]::NewGuid().ToString('N'))
$client=Join-Path $root 'InstalledClient'
New-Item -ItemType Directory $root|Out-Null
"VITEKA_ACCESS_SMOKE_OWNS_AUTHORITY=1"|Out-File $env:GITHUB_ENV -Append -Encoding utf8
function Invoke-OwnServer([string[]]$Arguments,[int]$Seconds=60,[switch]$ExpectFailure){
    Write-Output "::notice::Access smoke: $($Arguments[0]) $($Arguments[1])"
    $start=[System.Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    # .NET uses an explicit standard-handle list; do not leave a descendant
    # holding the GitHub runner's output pipe open after this script returns.
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
    $process=[System.Diagnostics.Process]::Start($start)
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit($Seconds*1000)){$process.Kill();throw "Owned CLI timed out: $($Arguments[0])"}
    if($ExpectFailure){if($process.ExitCode -eq 0){throw 'TLS accepted an unrelated authority.'};return}
    Write-Output $stdout.GetAwaiter().GetResult()
    if($process.ExitCode -ne 0){Write-Output $stderr.GetAwaiter().GetResult();throw "Owned CLI failed: $($Arguments[0])"}
}
Invoke-OwnServer -Arguments @('--access-manage','install',$client)
$before=(Get-FileHash (Join-Path $state 'Private/access.dat') -Algorithm SHA256).Hash
Invoke-OwnServer -Arguments @('--access-manage','install',$client)
if((Get-FileHash (Join-Path $state 'Private/access.dat') -Algorithm SHA256).Hash -ne $before){throw 'Reinstall did not preserve accounts.'}
$bootstrap=Join-Path $client 'access-server.json'
Invoke-OwnServer -Arguments @('--access-health',$bootstrap) -Seconds 10
$wrong=Join-Path $root 'WrongTrust';New-Item -ItemType Directory $wrong|Out-Null
$key=[System.Security.Cryptography.RSA]::Create(2048)
try{
    $request=[System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Unrelated CI Authority',$key,[System.Security.Cryptography.HashAlgorithmName]::SHA256,[System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$false,0,$true))
    $certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(1))
    try{[System.IO.File]::WriteAllBytes((Join-Path $wrong 'access-root.cer'),$certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))}finally{$certificate.Dispose()}
}finally{$key.Dispose()}
$metadata=Get-Content $bootstrap -Raw|ConvertFrom-Json
$metadata.RootSha256=(Get-FileHash (Join-Path $wrong 'access-root.cer') -Algorithm SHA256).Hash
$metadata|ConvertTo-Json|Set-Content (Join-Path $wrong 'access-server.json')
Invoke-OwnServer -Arguments @('--access-health',(Join-Path $wrong 'access-server.json')) -Seconds 10 -ExpectFailure
Invoke-OwnServer -Arguments @('--access-manage','uninstall') -Seconds 45
if(-not(Test-Path (Join-Path $state 'Private/access.dat'))){throw 'Removal must preserve authority data.'}
Invoke-OwnServer -Arguments @('--access-manage','install',$client)
if((Get-FileHash (Join-Path $state 'Private/access.dat') -Algorithm SHA256).Hash -ne $before){throw 'Restore recreated accounts.'}
"VITEKA_ACCESS_BOOTSTRAP=$bootstrap"|Out-File $env:GITHUB_ENV -Append -Encoding utf8
if(Test-Path (Join-Path $repo 'artifacts/windows-x64/Cliente/access-server.json')){throw 'CI bootstrap must never be packaged.'}
Write-Output '::notice::PASS: installed authority, preserved accounts, rejected unrelated CA; real LocalService authority ready for native UI.'
