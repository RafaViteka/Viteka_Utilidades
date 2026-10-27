[CmdletBinding()]
param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$directory = Join-Path $root 'artifacts/prerequisites'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$bootstrapper = Join-Path $directory 'MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path $bootstrapper)) {
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
}
$signature = Get-AuthenticodeSignature $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Microsoft Corporation(,|$)') {
    throw 'WebView2 bootstrapper signature is not a valid Microsoft signature.'
}
if ($Install) {
    $process = Start-Process -FilePath $bootstrapper -ArgumentList '/silent', '/install' -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) { throw "WebView2 runtime installation failed: $($process.ExitCode)" }
}
Write-Output 'Microsoft WebView2 bootstrapper signature verified.'
