<#
.SYNOPSIS
    Removes the Multiplot OpenTelemetry Collector service (NFR-TEL-006).

.DESCRIPTION
    Run as administrator, e.g. when a workstation leaves active operations. Stops and deletes the
    "MultiplotOtelCollector" service, removes the machine environment variable MULTIPLOT_TELEMETRY and
    the collector executable. Configuration and collected data in C:\ProgramData\Multiplot\otel are kept
    unless -RemoveData is given.

.PARAMETER RemoveData
    Also delete C:\ProgramData\Multiplot\otel (configuration and collected telemetry files).

.PARAMETER InstallDir
    Where the collector executable was installed.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [switch]$RemoveData,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Multiplot\OtelCollector')
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'MultiplotOtelCollector'
$DataRoot = Join-Path $env:ProgramData 'Multiplot\otel'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Removed service $ServiceName"
} else {
    Write-Host "Service $ServiceName is not installed"
}

if ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    [System.Diagnostics.EventLog]::DeleteEventSource($ServiceName)
}

if ([Environment]::GetEnvironmentVariable('MULTIPLOT_TELEMETRY', 'Machine')) {
    [Environment]::SetEnvironmentVariable('MULTIPLOT_TELEMETRY', $null, 'Machine')
    Write-Host "Removed machine environment variable MULTIPLOT_TELEMETRY"
}

if (Test-Path $InstallDir) {
    Remove-Item -Recurse -Force $InstallDir
    Write-Host "Removed $InstallDir"
}

if ($RemoveData -and (Test-Path $DataRoot)) {
    Remove-Item -Recurse -Force $DataRoot
    Write-Host "Removed $DataRoot"
} elseif (Test-Path $DataRoot) {
    Write-Host "Kept $DataRoot (use -RemoveData to delete configuration and collected data)"
}
