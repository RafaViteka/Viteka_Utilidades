[CmdletBinding()]
param([switch]$BuildInstaller, [switch]$InstallerOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    function Invoke-Dotnet([string[]] $Arguments) {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
    }
    if (-not $InstallerOnly) {
    Push-Location 'frontend'
    try {
        & npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Angular dependency installation failed.' }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Angular production build failed.' }
    } finally { Pop-Location }
    & python scripts/copy-ui.py
    if ($LASTEXITCODE -ne 0) { throw 'UI packaging failed.' }
    Invoke-Dotnet -Arguments @('restore', 'Viteka.Utilidades.slnx', '--locked-mode')
    Invoke-Dotnet -Arguments @('build', 'Viteka.Utilidades.slnx', '-c', 'Release', '--no-restore')
    Invoke-Dotnet -Arguments @('run', '--project', 'tests/Viteka.Core.Checks', '-c', 'Release', '--no-build')
    Invoke-Dotnet -Arguments @('publish', 'src/Viteka.Desktop/Viteka.Desktop.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:RestoreLockedMode=true', '-o', 'artifacts/windows-x64/Cliente')
    Invoke-Dotnet -Arguments @('publish', 'src/Viteka.Server/Viteka.Server.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:RestoreLockedMode=true', '-o', 'artifacts/windows-x64/Servidor')
    Copy-Item 'packaging/windows/manage-service.ps1' 'artifacts/windows-x64/Servidor/manage-service.ps1' -Force
    Copy-Item 'packaging/windows/LEEME.txt' 'artifacts/windows-x64/LEEME.txt' -Force
    }
    foreach ($exe in @('Cliente/Viteka.Desktop.exe', 'Servidor/Viteka.Server.exe')) {
        if (-not (Test-Path (Join-Path 'artifacts/windows-x64' $exe))) { throw "Executable missing: $exe" }
    }
    if ($BuildInstaller -or $InstallerOnly) {
        & ./packaging/windows/prepare-webview.ps1

        $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $iscc = if ($compiler) { $compiler.Source } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
        if (-not (Test-Path $iscc)) { throw 'Install Inno Setup 6 on Windows to build the installer.' }
        $version = ([xml](Get-Content 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version
        & $iscc "/DAppVersion=$version" 'packaging/windows/Viteka.Utilidades.iss'
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    }
}
finally { Pop-Location }
