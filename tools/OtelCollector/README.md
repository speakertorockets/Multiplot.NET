# OpenTelemetry Collector for Multiplot.NET

Bundled collector and installation scripts for the optional operations-monitoring telemetry
(requirements NFR-TEL-001..006, user guide *Operations telemetry* in `docs/user-guide/settings.md`).

| File | Purpose |
|---|---|
| `otelcol-contrib_0.164.0_windows_amd64.tar.gz` | OpenTelemetry Collector **contrib** distribution v0.164.0 for Windows x64, unmodified from the [official release](https://github.com/open-telemetry/opentelemetry-collector-releases/releases/tag/v0.164.0). Stored with **Git LFS** (105 MB, above GitHub's 100 MB file limit). |
| `otelcol-contrib_0.164.0_windows_amd64.tar.gz.sha256` | Published SHA-256 checksum, verified by the install script. |
| `LICENSE` | Apache License 2.0 of the collector (permitted by ARC-003). |
| `multiplot-collector.yaml` | Collector configuration: OTLP/HTTP receiver on `127.0.0.1:4318` only, health check on `127.0.0.1:13133`, rotated JSON-lines files under `C:\ProgramData\Multiplot\otel\data`, commented forwarder for a monitoring backend. |
| `Install-MultiplotCollector.ps1` | Administrator install: verifies the checksum, extracts the collector to `C:\Program Files\Multiplot\OtelCollector`, installs the config, validates it, creates and starts the `MultiplotOtelCollector` service; `-EnableTelemetry` also sets `MULTIPLOT_TELEMETRY=1` machine-wide. |
| `Uninstall-MultiplotCollector.ps1` | Administrator removal of the service, executable and variable (`-RemoveData` also deletes config and collected data). |

## When to install

Only on **operations workstations** that are monitored by operations staff during active operations.
Desktop workstations used for post-operation technical reviews do not need it: without the collector
and the variable, Multiplot makes no network connection at all (ARC-002 / NFR-SEC-001 exception).
Installation is done by the production tools with administrator rights.

```powershell
# after cloning: fetch the LFS archive
git lfs pull --include "tools/OtelCollector/*"

# as administrator
powershell -ExecutionPolicy Bypass -File tools\OtelCollector\Install-MultiplotCollector.ps1 -EnableTelemetry

# check
Get-Service MultiplotOtelCollector
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:13133/

# remove when the workstation leaves operations
powershell -ExecutionPolicy Bypass -File tools\OtelCollector\Uninstall-MultiplotCollector.ps1
```

Multiplot instances read `MULTIPLOT_TELEMETRY` at start-up, so instances that were already running when the
variable or the service was added keep running without telemetry until they are restarted.

## Forwarding to a monitoring backend

Edit `C:\ProgramData\Multiplot\otel\multiplot-collector.yaml`: uncomment `otlp/backend`, set its endpoint,
add it to the `exporters` list of each pipeline, then:

```powershell
& "C:\Program Files\Multiplot\OtelCollector\otelcol-contrib.exe" validate --config C:\ProgramData\Multiplot\otel\multiplot-collector.yaml
Restart-Service MultiplotOtelCollector
```

Re-running the install script keeps an edited config (use `-ForceConfig` to replace it).

## Updating the collector version

1. Download `otelcol-contrib_<version>_windows_amd64.tar.gz` and its `.sha256` from the release page.
2. Check the hash, replace the two files here (delete the old ones: the install script expects exactly one archive),
   and update the version in this README.
3. Validate `multiplot-collector.yaml` with the new executable (component options change between releases).
