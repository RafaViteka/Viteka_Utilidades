#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Start', 'Stop', 'Restart', 'Uninstall')]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$serviceName = 'VitekaUtilidadesPreview'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath
if ([System.IO.Path]::GetFileName($executable) -ne 'Viteka.Server.exe') {
    throw 'Expected the published Viteka.Server.exe, not a script or DLL.'
}
$expectedCommand = '"' + $executable + '" --service'
$registered = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'"
if ($registered -and $registered.PathName -ine $expectedCommand) {
    throw 'A service with this name uses another executable. It has not been changed.'
}
function Invoke-ServiceControl([string[]]$Arguments) {
    & "$env:SystemRoot/System32/sc.exe" @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Service control failed: $LASTEXITCODE" }
}
function Stop-VitekaService {
    $service = Get-Service -Name $serviceName
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }
}
function Test-VitekaReadiness {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'"
        if (-not $service -or $service.State -eq 'Stopped') { throw 'The service exited before becoming ready.' }
        $listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort 5187 -State Listen -ErrorAction SilentlyContinue
        if ($listener -and ($listener | Where-Object { $_.OwningProcess -eq $service.ProcessId })) {
            try {
                $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5187/health' -TimeoutSec 2
                if ($health.status -eq 'ok' -and $health.mode -eq 'demo') { return }
            }
            catch { }
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'The service did not expose the expected local health endpoint.'
}
switch ($Action) {
    'Install' {
        if ($registered) { throw 'Already installed. Use Start or Restart; installation was not overwritten.' }
        $directory = Join-Path $env:ProgramData 'Viteka/UtilidadesPreview/Server'
        # Do not silently repurpose an existing unmanaged directory or follow a junction.
        if (Test-Path -LiteralPath $directory) { throw 'Service state directory already exists. Review its owner and ACLs before reinstallation.' }
        $ancestor = Split-Path $directory -Parent
        while ($ancestor) {
            if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'A service state parent is a junction or symbolic link. Installation was not continued.'
            }
            $parent = Split-Path $ancestor -Parent
            if ($parent -eq $ancestor) { break }
            $ancestor = $parent
        }
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        & "$env:SystemRoot/System32/icacls.exe" $directory '/inheritance:r' '/grant:r' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M'
        if ($LASTEXITCODE -ne 0) { throw 'Could not protect the service state directory.' }
        # Parent Program Files and executable permissions must remain read/execute for ordinary users.
        Invoke-ServiceControl -Arguments @('create', $serviceName, 'binPath=', $expectedCommand, 'start=', 'auto',
            'obj=', 'NT AUTHORITY\LocalService', 'DisplayName=', 'Viteka Utilidades - Servidor de vista previa')
        try {
            Invoke-ServiceControl -Arguments @('description', $serviceName, 'Servidor local de demostracion. Sin conexion Oracle ni acceso LAN.')
            Start-Service -Name $serviceName
            (Get-Service -Name $serviceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
            Test-VitekaReadiness
        }
        catch {
            $failure = $_
            try { Stop-VitekaService } catch { Write-Warning 'Could not confirm that the failed service stopped.' }
            try { Invoke-ServiceControl -Arguments @('delete', $serviceName) } catch { Write-Warning 'Could not remove the failed service registration; review it manually.' }
            throw $failure
        }
        Write-Output 'Service registered, started and locally checked. Oracle has not been validated.'
    }
    'Start' {
        if (-not $registered) { throw 'Service is not installed.' }
        Start-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
        Test-VitekaReadiness
    }
    'Stop' {
        if (-not $registered) { throw 'Service is not installed.' }
        Stop-VitekaService
    }
    'Restart' {
        if (-not $registered) { throw 'Service is not installed.' }
        Stop-VitekaService
        Start-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
        Test-VitekaReadiness
    }
    'Uninstall' {
        if (-not $registered) { throw 'Service is not installed.' }
        Stop-VitekaService
        Invoke-ServiceControl -Arguments @('delete', $serviceName)
        Write-Output 'Service removed. State and revocations are preserved in ProgramData.'
    }
}
