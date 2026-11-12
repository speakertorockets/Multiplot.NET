# How to add OpenTelemetry to Multiplot.NET

Status: **implemented** (owner decision OQ-018; requirements NFR-TEL-001..006 in
`docs/requirements/02-architecture.md` §2.5; operator instructions in the user guide, *Settings and files →
Operations telemetry*; collector and install scripts in `tools/OtelCollector`). This document is kept as the
design rationale. Where the implementation differs from the sketches below:

* The owner decided (OQ-018) to make an exception to ARC-002 and NFR-SEC-001. Telemetry is disabled by
  default. It is active only when `MULTIPLOT_TELEMETRY` is set **and** a collector is listening on the
  loopback endpoint. Multiplot checks this with a short TCP probe at start-up, and only when the variable is
  set. There is no `[Telemetry]` INI section.
* The collector is started by **option A** (section 4.1): a Windows service that the production tools install
  with administrator rights, on operations workstations only.
* §5.6 is reversed: paths, parameter names, command text and script paths are recorded unchanged.
* The BCL façade is `Multiplot.Core.Diagnostics.MultiplotTelemetry` (in Core, not Application, so that
  DataAccess, Rendering and ImportExport can use it). The diagnostic log is bridged through
  `MultiplotTelemetry.LogSink` instead of an `ILogger` dependency in the libraries.
* The bundled collector configuration uses only the OTLP/HTTP receiver (no gRPC port) and `file` exporters
  (no `debug` exporter, because there is no console when it runs as a service).

This document explains how traces, metrics and logs could be added to Multiplot.NET with
[OpenTelemetry](https://opentelemetry.io/) (OTel), such that:

* any number of Multiplot instances can run **at the same time on the same machine**, and
* all instances send their telemetry to **one local OpenTelemetry Collector listening on one
  port**, which merges the streams and forwards them to whatever backend is wanted.

It covers the architecture, the NuGet dependencies, the code changes (as sketches), the collector
configuration, and the problems and requirement conflicts this introduces.

---

## 1. Architecture

```
 ┌──────────────────┐   OTLP/HTTP    ┌────────────────────────────────┐
 │ Multiplot.exe #1 │───────────────▶│                                │
 │ instance.id=A    │                │   OpenTelemetry Collector      │      ┌──────────────┐
 └──────────────────┘                │   (otelcol-contrib.exe)        │─────▶│ Backend(s):  │
 ┌──────────────────┐   OTLP/HTTP    │                                │      │ Jaeger, Seq, │
 │ Multiplot.exe #2 │───────────────▶│   receiver: 127.0.0.1:4318     │      │ Prometheus,  │
 │ instance.id=B    │                │   (single port, all instances) │      │ Aspire dash, │
 └──────────────────┘                │                                │      │ file, ...    │
 ┌──────────────────┐   OTLP/HTTP    │   memory_limiter → batch →     │      └──────────────┘
 │ multiplot /hide  │───────────────▶│   exporters                    │
 │ instance.id=C    │                └────────────────────────────────┘
 └──────────────────┘
```

Key points:

1. **The application never listens on a port.** Each instance is a pure OTLP *client* (push
   model). Any number of clients can connect to the same collector port, so multiple instances
   cannot collide. This is the main reason for using a collector: a per-process pull endpoint
   (e.g. the in-process Prometheus exporter, which binds an HTTP port) would fail for the second
   instance.
2. **Each instance identifies itself** through OTel *resource attributes*: `service.name =
   Multiplot`, a unique `service.instance.id` (a GUID generated at start-up), `process.pid`,
   `host.name`, version, and a run mode (interactive vs. `/hide`). The collector and backend use
   these to tell instances apart or to aggregate across them.
3. **The collector is the single integration point.** It owns the port, batching, retry, and the
   connection to the real backend. Changing backends (or adding one) is a collector config change;
   the application is not rebuilt.
4. **Instrumentation code uses only the BCL.** `System.Diagnostics.ActivitySource` (traces) and
   `System.Diagnostics.Metrics.Meter` (metrics) are part of .NET 10, so the library projects
   (`Multiplot.Application`, `Multiplot.DataAccess`, `Multiplot.Rendering`, …) need **no NuGet
   packages**. Only the executable (`Multiplot.App`) references the OpenTelemetry SDK and exporter.
   When the SDK is not enabled, the instrumentation costs almost nothing (an `ActivitySource` with
   no listener returns `null` from `StartActivity`).

---

## 2. Dependencies

### 2.1 NuGet packages

Pin all OpenTelemetry packages to the **same current stable 1.x version** (check nuget.org when
implementing; the core packages are released together). An MSBuild property keeps them in step.

| Project | Package | Why |
|---|---|---|
| `Multiplot.App` | `OpenTelemetry` | The SDK (TracerProvider, MeterProvider, logging provider). |
| `Multiplot.App` | `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP exporter (gRPC and HTTP/protobuf) to the collector. |
| `Multiplot.App` *(optional)* | `OpenTelemetry.Instrumentation.Process` (beta) | Process CPU/memory/thread metrics. |
| `Multiplot.App` *(optional)* | `OpenTelemetry.Resources.Host`, `OpenTelemetry.Resources.ProcessRuntime` (beta, contrib) | Adds `host.*` / `process.runtime.*` resource attributes automatically (can also be added by hand, see 3.2). |
| `Multiplot.Application` *(only if the log bridge is used)* | `Microsoft.Extensions.Logging.Abstractions` | `ILogger` for bridging `Session.DiagLog` into OTel logs. |
| `Multiplot.Tests` | `OpenTelemetry.Exporter.InMemory` | Assert in unit tests that spans/metrics are emitted. |

Not needed:

* **`OpenTelemetry.Instrumentation.Runtime`** — .NET 9+ publishes runtime metrics (GC, JIT, thread
  pool, exceptions) natively on the built-in meter `System.Runtime`; just call
  `.AddMeter("System.Runtime")`.
* **`OpenTelemetry.Extensions.Hosting`** — Multiplot is a plain WPF app without the Generic Host;
  the providers are built directly with `Sdk.Create…Builder()`.
* **`OpenTelemetry.Instrumentation.Http`** — the core app makes no HTTP calls (NFR-SEC-001).

Example `Multiplot.App.csproj` addition:

```xml
<PropertyGroup>
  <!-- set to the current stable OpenTelemetry .NET release; all core packages share it -->
  <OpenTelemetryVersion>1.x.y</OpenTelemetryVersion>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="OpenTelemetry" Version="$(OpenTelemetryVersion)" />
  <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="$(OpenTelemetryVersion)" />
</ItemGroup>
```

These packages pull in transitive dependencies (`Microsoft.Extensions.Logging`,
`Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Options`, …). They are MIT/Apache-2.0 licensed, which satisfies ARC-003, but
they add roughly 1–2 MB to the deployment folder.

### 2.2 External component: the OpenTelemetry Collector

* Download **`otelcol-contrib`** (Windows amd64 `.exe` or `.msi`) from the
  `open-telemetry/opentelemetry-collector-releases` GitHub releases. The *contrib* distribution is
  needed for the `file` exporter and the `resourcedetection` processor; the core `otelcol`
  distribution is enough if only `otlp`/`otlphttp`/`debug` exporters are used.
* Apache-2.0 licensed, a single ~200 MB self-contained executable, no runtime prerequisites.
* It runs natively as a Windows service (the MSI installs it as one).

Optional, for viewing data locally during development:

* **.NET Aspire Dashboard** (standalone container `mcr.microsoft.com/dotnet/aspire-dashboard`) —
  accepts OTLP and shows traces, metrics and structured logs in a browser. Needs Docker/Podman.
* **Jaeger** (traces), **Seq** (logs + traces), or **Prometheus + Grafana** (metrics).

---

## 3. Application changes (sketch)

### 3.1 A telemetry façade in `Multiplot.Application` (BCL only)

```csharp
// src/Multiplot.Application/Diagnostics/MultiplotTelemetry.cs
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Multiplot.Application.Diagnostics;

/// <summary>Names and instruments shared by all Multiplot telemetry. No SDK dependency.</summary>
public static class MultiplotTelemetry
{
    public const string SourceName = "Multiplot";
    public static readonly string Version =
        typeof(MultiplotTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static readonly ActivitySource Source = new(SourceName, Version);
    public static readonly Meter Meter = new(SourceName, Version);

    public static readonly Histogram<double> PlotDrawMs =
        Meter.CreateHistogram<double>("multiplot.plot.draw.duration", "ms", "Time to render one plot window");
    public static readonly Histogram<double> LoadMs =
        Meter.CreateHistogram<double>("multiplot.load.duration", "ms", "Time to load the curves of one plot");
    public static readonly Counter<long> PointsLoaded =
        Meter.CreateCounter<long>("multiplot.load.points", "{point}", "Samples loaded from data files");
    public static readonly Counter<long> ScriptErrors =
        Meter.CreateCounter<long>("multiplot.script.errors", "{error}");
    public static readonly Counter<long> RealtimeRefreshes =
        Meter.CreateCounter<long>("multiplot.realtime.refreshes", "{refresh}");
}
```

Plugins can create their own `ActivitySource`/`Meter` (for example `"Multiplot.Parquet"`); the
host subscribes to them by name or with a wildcard (`AddSource("Multiplot*")`).

### 3.2 Bootstrapping the SDK in `Multiplot.App`

WPF has no host, so the providers are created at the top of `Program.Main` (before
`new App(raw)`) and disposed in the shutdown path (`App.ExitApplication` and the end of `Main`).

```csharp
// src/Multiplot.App/TelemetryBootstrap.cs
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Multiplot.Application.Diagnostics;

namespace Multiplot.App;

internal sealed class TelemetryBootstrap : IDisposable
{
    private readonly TracerProvider? _tracer;
    private readonly MeterProvider? _meter;
    public ILoggerFactory? LoggerFactory { get; }

    public TelemetryBootstrap(bool hidden)
    {
        // opt-in (see section 6): OTEL_SDK_DISABLED is honoured as well
        if (!IsEnabled()) return;

        // 127.0.0.1, not "localhost": avoids an IPv6 (::1) attempt + fallback delay on Windows
        string endpoint = Environment.GetEnvironmentVariable("MULTIPLOT_OTLP_ENDPOINT") ?? "http://127.0.0.1:4318";

        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName: "Multiplot",
                        serviceVersion: MultiplotTelemetry.Version,
                        serviceInstanceId: Guid.NewGuid().ToString())     // unique per process
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("process.pid", Environment.ProcessId),
                new("host.name", Environment.MachineName),
                new("multiplot.mode", hidden ? "hidden" : "interactive"),
                new("multiplot.session.id", Environment.GetEnvironmentVariable("SESSIONNAME") ?? ""),
            });

        // With HttpProtobuf and a code-supplied Endpoint, the signal path must be included.
        void Otlp(OtlpExporterOptions o, string path)
        {
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.Endpoint = new Uri(endpoint.TrimEnd('/') + path);
            o.TimeoutMilliseconds = 2000;            // keep exit and a missing collector cheap
        }

        _tracer = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource("Multiplot*")
            .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
            .AddOtlpExporter(o => Otlp(o, "/v1/traces"))
            .Build();

        _meter = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter("Multiplot*")
            .AddMeter("System.Runtime")               // built-in .NET runtime metrics
            .AddOtlpExporter((o, reader) =>
            {
                Otlp(o, "/v1/metrics");
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15000;
            })
            .Build();

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b
            .AddOpenTelemetry(o =>
            {
                o.SetResourceBuilder(resource);
                o.IncludeFormattedMessage = true;
                o.AddOtlpExporter(e => Otlp(e, "/v1/logs"));
            }));
    }

    private static bool IsEnabled() =>
        !string.Equals(Environment.GetEnvironmentVariable("OTEL_SDK_DISABLED"), "true", StringComparison.OrdinalIgnoreCase)
        && Environment.GetEnvironmentVariable("MULTIPLOT_TELEMETRY") is "1" or "true";   // or an INI switch

    public void Flush(int timeoutMs = 2000)
    {
        _tracer?.ForceFlush(timeoutMs);
        _meter?.ForceFlush(timeoutMs);
    }

    public void Dispose()   // Shutdown flushes; bounded by the exporter timeout
    {
        _tracer?.Dispose();
        _meter?.Dispose();
        LoggerFactory?.Dispose();
    }
}
```

Wiring in `Program.Main` (`src/Multiplot.App/App.cs`):

```csharp
using var telemetry = new TelemetryBootstrap(hidden: args.Any(a => a.Equals("/hide", StringComparison.OrdinalIgnoreCase)));
GlobalExceptionHandler.Install("Multiplot", telemetry);   // ForceFlush in the UnhandledException handler
var app = new App(raw, telemetry.LoggerFactory);
...
```

Configuration should come from (in priority order) the standard `OTEL_*` environment variables,
then a `[Telemetry]` section in `Multiplot.ini` (`Enabled=`, `Endpoint=`, `Protocol=`), then the
defaults above. Note that the INI is only read in `AppStartup.LoadInitialSettings`, *after*
`Program.Main` would build the providers — either read the `[Telemetry]` keys early with
`IniFile` directly, or build the providers lazily after settings load.

### 3.3 Where to instrument

Instrument **coarse operations** only; never per sample or per drawn point.

| Location | Signal | Notes |
|---|---|---|
| `ScriptRunner.RunAsync` / `RunNestedAsync` | Span `script.run` | Attributes: script file *name* only (not path or content), line count, result. Nested scripts become child spans automatically (`Activity.Current` flows through `async`/`await`). |
| `CommandProcessor.ExecuteAsync` | Span `command` | Attribute: the command keyword (`LOAD`, `PLOT`, `EXPORT`…), **not** its arguments. Increment `ScriptErrors` on failure. |
| `Session.LoadParameters` / `LoadFileGroup` / `LoadDB` | Span `load.parameters` + `LoadMs`, `PointsLoaded` | Attributes: file count, curve count, file type signature, total bytes. |
| `DataAccessService.Open` | Span `data.open` | Attributes: provider (SUN, Parquet, plugin name), success/error code. |
| `DataAccessService` plugin scan | Log + counter | Loaded/blocked plugin names (already logged via `DataAccessService.Log`). |
| `RealTime.RefreshTick` / `RefreshNow` | **Root** span `realtime.refresh` + `RealtimeRefreshes` | Timer callbacks have no parent; each tick is its own trace. Record lag (now − newest sample time) as a histogram. |
| `PlotRenderer.Draw` | `PlotDrawMs` histogram only | Called on every repaint on the UI thread — a span per paint would flood the backend. Use `Stopwatch.GetTimestamp()`. |
| `Exporter` / `AsciiImporter` | Span `export` / `import` | Format, row count, duration. |
| `Session.DiagLog` | OTel log record | Bridge: keep writing the file and also call `ILogger.LogInformation`. |
| `GlobalExceptionHandler`, `App.OnUnhandled` | Error log + `Activity.Current?.AddException(ex)` | Then `telemetry.Flush()`. |

Example inside `CommandProcessor.ExecuteAsync`:

```csharp
using var act = MultiplotTelemetry.Source.StartActivity("command");
act?.SetTag("multiplot.command", keyword);
try { ... }
catch (Exception ex)
{
    act?.SetStatus(ActivityStatusCode.Error);
    act?.AddException(ex);
    throw;
}
```

---

## 4. Collector configuration

One collector per machine, listening on loopback. Save as e.g.
`C:\ProgramData\Multiplot\otel\multiplot-collector.yaml`:

```yaml
extensions:
  health_check:
    endpoint: 127.0.0.1:13133          # lets Multiplot / scripts probe whether the collector is up

receivers:
  otlp:
    protocols:
      http:
        endpoint: 127.0.0.1:4318       # THE single port for all Multiplot instances
      grpc:
        endpoint: 127.0.0.1:4317       # optional, if some clients prefer gRPC

processors:
  memory_limiter:                      # must be first: protects the machine if instances flood it
    check_interval: 1s
    limit_mib: 256
    spike_limit_mib: 64
  resourcedetection:                   # contrib: adds host/os attributes once, centrally
    detectors: [system]
    system:
      hostname_sources: [os]
  batch:
    send_batch_size: 1024
    timeout: 5s

exporters:
  debug:
    verbosity: basic                   # prints a summary per batch to the console
  file:                                # contrib: JSON lines on disk, rotated
    path: C:/ProgramData/Multiplot/otel/telemetry.jsonl
    rotation:
      max_megabytes: 50
      max_backups: 5
  # forward to a real backend, e.g. Aspire dashboard / Jaeger / Seq / a central collector:
  # otlp/backend:
  #   endpoint: some-backend:4317
  #   tls: { insecure: true }

service:
  extensions: [health_check]
  telemetry:
    metrics:
      level: none                      # avoids the collector's own :8888 metrics port
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, resourcedetection, batch]
      exporters: [debug, file]
    metrics:
      receivers: [otlp]
      processors: [memory_limiter, resourcedetection, batch]
      exporters: [debug, file]
    logs:
      receivers: [otlp]
      processors: [memory_limiter, resourcedetection, batch]
      exporters: [debug, file]
```

Run it in a console for testing:

```powershell
otelcol-contrib.exe validate --config C:\ProgramData\Multiplot\otel\multiplot-collector.yaml
otelcol-contrib.exe --config C:\ProgramData\Multiplot\otel\multiplot-collector.yaml
```

### 4.1 Who starts the collector?

| Option | Pros | Cons |
|---|---|---|
| **A. Windows service** (MSI, or `sc.exe create`) — *recommended for shared/lab machines* | Always running, independent of Multiplot; one config; survives instance crashes. | Needs administrator rights once — conflicts with ARC-002 ("no administrator rights required"). |
| **B. Per-user scheduled task at logon** | No admin rights; fits ARC-002. | One collector per *user*; on a multi-user terminal server each user needs a different port. |
| **C. First Multiplot instance launches it** | Zero setup for the user. | Race when several instances start together; collector lifetime tied to whichever process started it; Multiplot must find the `.exe`. Needs a named mutex (`Local\Multiplot.OtelCollector`) plus a health-check probe on `127.0.0.1:13133` before launching, and must launch it detached so it outlives the instance. |
| **D. No collector running** | — | Telemetry is silently dropped (see 5.2). The app must still work. |

Option A (or B where admin rights are not available) is preferred. Option C adds process
management to a desktop app and is the most fragile.

### 4.2 Verifying multi-instance operation

1. Start the collector in a console (debug exporter visible).
2. `set MULTIPLOT_TELEMETRY=1` and start three Multiplot instances (one with `/hide` running a
   script).
3. The debug output shows resources with three different `service.instance.id` and `process.pid`
   values arriving on the same port.
4. In the backend, group by `service.instance.id` to see one instance, or aggregate over it to see
   the whole machine.

---

## 5. Problems and issues this creates

### 5.1 Requirement conflicts (must be resolved with the owner first)

* **NFR-SEC-001** states *"no network access is initiated by the core application"*. Sending OTLP
  to `127.0.0.1` is still a TCP connection, and a collector config can forward data off the
  machine. This requirement must be amended (e.g. "only loopback, only when telemetry is enabled")
  and recorded as a new open question in `docs/requirements/13-open-questions.md`.
* **ARC-002** (no administrator rights, runs from a network share) conflicts with installing the
  collector as a service (option A). Option B satisfies it.
* **NFR-LOG-001** (rolling file log) should stay as is; OTel logs are an addition, not a
  replacement, so diagnostics still exist when no collector is running.
* **ARC-012** (no global mutable singletons): a static `ActivitySource`/`Meter` is the idiomatic
  .NET pattern and is immutable after creation, but it should be noted as an accepted exception.

### 5.2 Runtime behaviour

* **Collector not running.** Exports fail with "connection refused"; the SDK retries, then drops
  the batch and logs to its own internal EventSource. The UI is not blocked (export runs on a
  background thread), but data from that period is lost. The OTLP exporter's experimental disk
  retry (`OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY=disk`) can buffer to disk — with multiple instances,
  each needs its own directory (`OTEL_DOTNET_EXPERIMENTAL_OTLP_DISK_RETRY_DIRECTORY_PATH`).
* **Shutdown delay.** Disposing the providers flushes pending data and can block the UI thread up
  to the exporter timeout on exit. Keep the timeout short (≈2 s). `/hide` batch runs that end
  quickly must still dispose/flush, otherwise their spans never arrive.
* **Abnormal exit.** `Environment.Exit`, a crash, or the render-thread `COMException` path in
  `Program.Main` skip disposal; call `Flush()` in `GlobalExceptionHandler` and in the catch block.
  A hard kill (Task Manager) loses whatever was still buffered.
* **Port 4317/4318 already in use.** These are the standard OTLP ports; Aspire, Docker Desktop,
  other collectors or IDEs may already use them. The collector then fails to start, or — worse —
  Multiplot's data ends up in somebody else's collector. Make the endpoint configurable, and
  consider a Multiplot-specific port if the machine is shared with other OTel tooling.
* **`localhost` vs `127.0.0.1`.** On Windows `localhost` resolves to `::1` first; if the collector
  only listens on IPv4, each connection pays a fallback delay. Use `127.0.0.1` on both sides.
* **HTTP/protobuf endpoint paths.** When the endpoint is set *in code* with `HttpProtobuf`, the
  exporter uses the URL as-is, so `/v1/traces`, `/v1/metrics`, `/v1/logs` must be appended per
  signal. When set through `OTEL_EXPORTER_OTLP_ENDPOINT`, the SDK appends them itself. Mixing the
  two is a common cause of HTTP 404s.
* **Collector is a single point of failure / resource.** All instances share one process; the
  `memory_limiter` processor protects the machine but will refuse data under load.

### 5.3 Multi-instance data semantics

* **Metrics from several instances must not be merged by stripping `service.instance.id` in the
  collector.** Each instance reports its own *cumulative* counters; if the identifying attribute
  is removed, the backend sees several streams with identical identity and the values jump
  between them. Aggregate in the backend (`sum by (...)`) instead.
* **Restarts create new series.** A new GUID per process means every start creates new metric
  series; on Prometheus-like backends this grows cardinality over time. Use
  `metric_expiration` in a `prometheus` exporter, or use a stable ID (e.g. per user + slot)
  if long-lived dashboards per instance are required.
* **Several users on one machine** (Remote Desktop / terminal server): one machine-wide collector
  receives every user's data on the same loopback port — data from different users is mixed, and
  any local process can send to or impersonate Multiplot on that port. Add `user.name`/session
  attributes and decide whether that is acceptable (privacy), or use per-user collectors (option B).
* **Correlation between instances** does not happen automatically; traces from different instances
  are independent. If one instance launches another (`RUN` command), trace context could be passed
  via the `TRACEPARENT` environment variable, but this needs explicit code.

### 5.4 Performance

* NFR-PERF-002 (redraw 20 × 10 M points in < 200 ms) and the expression evaluator must not be
  instrumented per point or per sample. Only spans around whole operations and histograms for
  draw time (section 3.3).
* `AlwaysOnSampler` is fine at desktop volumes; if real-time refresh runs at a high rate with many
  windows, switch real-time spans to a `TraceIdRatioBasedSampler` or record them only as metrics.
* SDK overhead with telemetry *disabled* is negligible; with it enabled, expect a background thread
  per signal and a small amount of allocation per span.

### 5.5 Plugins and assembly load contexts

* `DataAccessService.PluginLoadContext.Load` only shares `Multiplot.Plugins.Abstractions` with the
  host. If a plugin's `.deps.json` brings its own copy of `System.Diagnostics.DiagnosticSource`
  (some NuGet packages reference it), that plugin's `ActivitySource` lives in a different type
  universe and **the host's listener never sees it**. The load context should also return `null`
  (defer to the default context) for `System.Diagnostics.DiagnosticSource` and any
  `OpenTelemetry.*` assembly.
* Plugin load contexts are **collectible**. An `ActivitySource`/`Meter` created by a plugin and
  subscribed by the host's providers holds references into the plugin's context and prevents it
  from unloading. Plugins should dispose their sources in their unload/dispose path.
* A plugin must **not** create its own `TracerProvider`/exporter; only the host does.
* Optionally expose the source naming convention (`"Multiplot.<PluginName>"`) in the plugin
  documentation (`docs/user-guide/plugins.md`) once implemented.

### 5.6 Privacy and data content

*Owner decision (OQ-018):* no scrubbing is needed. Multiplot.NET is used mainly on protected
workstations, and the telemetry is monitored by privileged production staff. File paths, parameter
names, command arguments and script contents are therefore not sensitive and are recorded as they are.
The feature is still **off** by default (NFR-TEL-001). Forwarding off the machine is configured only in
the collector, by the production tools.

### 5.7 Build, deployment and tests

* Unit tests construct `Session` without `Program.Main`, so no SDK is started in tests by default;
  set `OTEL_SDK_DISABLED=true` in the test environment anyway to be safe.
* Use `OpenTelemetry.Exporter.InMemory` (or a plain `ActivityListener`) in `Multiplot.Tests` to
  assert that key spans and metrics are emitted.
* The collector binary and its YAML are a separate deployable; they are not part of
  `dotnet publish` and need their own install steps and version management.
* Existing issue made more visible: `Session.DiagLog` uses `File.AppendAllText` on one shared
  daily file; with several instances writing at once, some lines are lost (`IOException` is
  swallowed). Moving to OTel logs or using a per-process file name (include the PID) fixes this.

---

## 6. Suggested rollout

1. Owner decision on NFR-SEC-001 / ARC-002 and the collector start-up option (section 4.1);
   record it in `13-open-questions.md`.
2. Add `MultiplotTelemetry` (BCL only) and instrument the operations in section 3.3. No behaviour
   change without the SDK.
3. Add `TelemetryBootstrap` and the two NuGet packages to `Multiplot.App`, disabled by default,
   enabled by `MULTIPLOT_TELEMETRY=1` / `[Telemetry] Enabled=1`.
4. Fix the plugin load context sharing (5.5).
5. Ship the collector config and install script; verify with three concurrent instances (4.2).
6. Add tests with the in-memory exporter; document the feature in `docs/user-guide/settings.md`.
