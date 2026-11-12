<#
.SYNOPSIS
    Installs the OpenTelemetry Collector for Multiplot.NET as a Windows service (NFR-TEL-006).

.DESCRIPTION
    For operations workstations that are monitored during active operations. Run as administrator
    (normally by the production deployment tools). The script:
      1. verifies the bundled otelcol-contrib archive against its SHA-256 file,
      2. extracts otelcol-contrib.exe to "C:\Program Files\Multiplot\OtelCollector",
      3. copies multiplot-collector.yaml to "C:\ProgramData\Multiplot\otel" (an existing, edited
         config is kept unless -ForceConfig is given) and validates it,
      4. creates and starts the "MultiplotOtelCollector" service (automatic start, LocalService account),
      5. with -EnableTelemetry, sets the machine environment variable MULTIPLOT_TELEMETRY=1 so that
         Multiplot instances started afterwards export telemetry.

    Telemetry stays off unless BOTH the variable is set AND the collector is running. Desktop
    workstations used for technical reviews do not need this installation.

.PARAMETER EnableTelemetry
    Also set MULTIPLOT_TELEMETRY=1 machine-wide.

.PARAMETER ForceConfig
    Overwrite an existing multiplot-collector.yaml in C:\ProgramData\Multiplot\otel.

.PARAMETER InstallDir
    Where the collector executable is installed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Install-MultiplotCollector.ps1 -EnableTelemetry
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [switch]$EnableTelemetry,
    [switch]$ForceConfig,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Multiplot\OtelCollector')
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'MultiplotOtelCollector'
$DataRoot = Join-Path $env:ProgramData 'Multiplot\otel'
$ConfigPath = Join-Path $DataRoot 'multiplot-collector.yaml'
$here = $PSScriptRoot

# 1. bundled archive + checksum
$archives = @(Get-ChildItem -Path $here -Filter 'otelcol-contrib_*_windows_amd64.tar.gz')
if ($archives.Count -eq 0) { throw "otelcol-contrib_*_windows_amd64.tar.gz not found next to this script ($here)." }
if ($archives.Count -gt 1) { throw "More than one otelcol-contrib archive in $here; keep only the version to install." }
$archive = $archives[0]
if ((Get-Item $archive.FullName).Length -lt 1MB) {
    throw "$($archive.Name) is only a Git LFS pointer. Run 'git lfs pull' in the repository first."
}
$shaFile = "$($archive.FullName).sha256"
if (-not (Test-Path $shaFile)) { throw "Checksum file $shaFile is missing." }
$expected = ((Get-Content $shaFile -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$actual = (Get-FileHash -Algorithm SHA256 $archive.FullName).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw "SHA-256 mismatch for $($archive.Name): expected $expected, got $actual." }
Write-Host "Verified $($archive.Name) ($actual)"

# stop an existing service before replacing the executable
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
    Write-Host "Stopping existing $ServiceName service"
    Stop-Service -Name $ServiceName -Force
    $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

# 2. extract (tar.exe ships with Windows 10 1803+ / Server 2019+)
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'
& $tar -xzf $archive.FullName -C $InstallDir otelcol-contrib.exe
if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
$exe = Join-Path $InstallDir 'otelcol-contrib.exe'
Copy-Item -Force (Join-Path $here 'LICENSE') (Join-Path $InstallDir 'LICENSE')
Write-Host "Installed $exe"

# 3. configuration and data folders
New-Item -ItemType Directory -Force -Path (Join-Path $DataRoot 'data') | Out-Null
if ($ForceConfig -or -not (Test-Path $ConfigPath)) {
    Copy-Item -Force (Join-Path $here 'multiplot-collector.yaml') $ConfigPath
    Write-Host "Wrote $ConfigPath"
} else {
    Write-Host "Kept existing $ConfigPath (use -ForceConfig to replace it)"
}
# the service runs as LocalService: it must be able to write the rotated output files
& icacls.exe (Join-Path $DataRoot 'data') /grant '*S-1-5-19:(OI)(CI)M' /T /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls failed with exit code $LASTEXITCODE" }

& $exe validate --config $ConfigPath
if ($LASTEXITCODE -ne 0) { throw "Collector configuration $ConfigPath is not valid (exit code $LASTEXITCODE)." }
Write-Host "Configuration is valid"

# 4. service (with an event log source: as a service the collector logs to the Application event log)
if (-not [System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    [System.Diagnostics.EventLog]::CreateEventSource($ServiceName, 'Application')
}
$binPath = "`"$exe`" --config `"$ConfigPath`""
if (-not $svc) {
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName 'Multiplot OpenTelemetry Collector' `
        -Description 'Receives OpenTelemetry data from Multiplot.NET instances on 127.0.0.1:4318 for operations monitoring.' `
        -StartupType Automatic | Out-Null
} else {
    & sc.exe config $ServiceName binPath= $binPath start= auto | Out-Null
}
& sc.exe config $ServiceName obj= 'NT AUTHORITY\LocalService' | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

$healthy = $false
for ($i = 0; $i -lt 20 -and -not $healthy; $i++) {
    try { $healthy = (Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:13133/' -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 500 }
}
if (-not $healthy) { Write-Warning "The service is running but the health check on 127.0.0.1:13133 did not answer yet; see the Application event log." }
else { Write-Host "Collector is healthy on 127.0.0.1:13133; OTLP/HTTP receiver on 127.0.0.1:4318" }

# 5. optional switch for Multiplot
if ($EnableTelemetry) {
    [Environment]::SetEnvironmentVariable('MULTIPLOT_TELEMETRY', '1', 'Machine')
    Write-Host "Set machine environment variable MULTIPLOT_TELEMETRY=1 (applies to Multiplot instances started from new sessions/processes)"
} else {
    Write-Host "MULTIPLOT_TELEMETRY was not changed; set it to 1 (machine or user) to enable Multiplot telemetry."
}
