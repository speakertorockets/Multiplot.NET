<#
.SYNOPSIS
    Builds the Multiplot.NET release package (a .zip to attach to a GitHub release).

.DESCRIPTION
    1. runs the tests (skip with -SkipTests),
    2. publishes Multiplot.exe (Release, framework-dependent, x64) with the Parquet plugin and the HTML
       user guide, and the multi2parquet conversion tool into tools\multi2parquet,
    3. adds LICENSE, README.md, THIRD-PARTY-NOTICES.txt and the Apache-2.0 licence text,
    4. zips everything under one top-level folder Multiplot.NET-<version> into
       artifacts\Multiplot.NET-<version>-win-x64.zip and writes its SHA-256 next to it.

    The version comes from Directory.Build.props. The OpenTelemetry Collector (tools\OtelCollector) is
    not included: it is deployed separately to operations workstations (NFR-TEL-006).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Build-Release.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$OutputDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Version not found in Directory.Build.props' }
$name = "Multiplot.NET-$version"

$dirty = git -C $root status --porcelain
if ($dirty) { Write-Warning "The working tree has uncommitted changes; the package will include them." }
$commit = (git -C $root rev-parse --short HEAD)

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (-not $SkipTests) {
    Write-Host "Running tests..."
    Invoke-Dotnet test (Join-Path $root 'tests\Multiplot.Tests') -c Release -nologo -v q
}

$stage = Join-Path $OutputDir $name
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

Write-Host "Publishing Multiplot $version ($commit)..."
Invoke-Dotnet publish (Join-Path $root 'src\Multiplot.App\Multiplot.App.csproj') -c Release -o $stage -nologo -v q
Invoke-Dotnet publish (Join-Path $root 'tools\MultiToParquet\MultiToParquet.csproj') -c Release -o (Join-Path $stage 'tools\multi2parquet') -nologo -v q

foreach ($required in 'Multiplot.exe', 'Multiplot.ini', 'plugins\Multiplot.Parquet\Multiplot.Parquet.dll',
                      'docs\user-guide\index.html', 'tools\multi2parquet\multi2parquet.exe') {
    if (-not (Test-Path (Join-Path $stage $required))) { throw "Missing from the package: $required" }
}

Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'README.md')
Copy-Item (Join-Path $root 'tools\MultiToParquet\README.md') (Join-Path $stage 'tools\multi2parquet\README.md') -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage 'licenses') | Out-Null
Copy-Item (Join-Path $root 'tools\OtelCollector\LICENSE') (Join-Path $stage 'licenses\Apache-2.0.txt')

@"
Multiplot.NET $version includes the following third-party components.

Component                                   Version   Licence        Project
------------------------------------------  --------  -------------  ----------------------------------------------
OpenTelemetry, OpenTelemetry.Api,           1.21.0    Apache-2.0     https://github.com/open-telemetry/opentelemetry-dotnet
  OpenTelemetry.Api.ProviderBuilderExtensions,
  OpenTelemetry.Exporter.OpenTelemetryProtocol
Microsoft.Extensions.* (Configuration,      10.x      MIT            https://github.com/dotnet/runtime
  DependencyInjection, Logging, Options,
  Primitives, Diagnostics.Abstractions),
  System.Diagnostics.DiagnosticSource
Parquet.Net (Parquet plugin, multi2parquet) 6.1.0     MIT            https://github.com/aloneguid/parquet-dotnet
CommunityToolkit.HighPerformance            8.4.2     MIT            https://github.com/CommunityToolkit/dotnet
K4os.Compression.LZ4                        1.3.8     MIT            https://github.com/MiloszKrajewski/K4os.Compression.LZ4
Microsoft.IO.RecyclableMemoryStream         3.0.1     MIT            https://github.com/Microsoft/Microsoft.IO.RecyclableMemoryStream
Snappier                                    1.3.1     BSD-3-Clause   https://github.com/brantburnett/Snappier
ZstdSharp.Port                              0.8.8     MIT            https://github.com/oleg-st/ZstdSharp

The Apache License 2.0 text is in licenses\Apache-2.0.txt. The MIT and BSD-3-Clause licence texts are
published with each component at the project address above.
"@ | Set-Content -Encoding utf8 (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')

@"
Multiplot.NET $version (commit $commit)

Requirements: Windows x64 and the .NET 10 Desktop Runtime
(https://dotnet.microsoft.com/download/dotnet/10.0).

Installation: extract this folder anywhere and run Multiplot.exe. Keep the plugins folder next to the
executable. Help > User Guide opens docs\user-guide\index.html in the web browser. User settings are
created in %USERPROFILE%\My Multiplot on first start.

tools\multi2parquet\multi2parquet.exe converts SUN .multi files to Apache Parquet (see its README.md).
"@ | Set-Content -Encoding utf8 (Join-Path $stage 'INSTALL.txt')

$zip = Join-Path $OutputDir "$name-win-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $zip)" | Set-Content -Encoding ascii "$zip.sha256"

Write-Host ""
Write-Host "Package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Host "SHA-256: $hash"
