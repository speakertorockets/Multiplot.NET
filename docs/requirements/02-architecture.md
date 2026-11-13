# 02 — Target Platform, Architecture and Non-Functional Requirements

## 2.1 Platform

**ARC-001** Language C# (latest stable language version supported by the chosen SDK), target
**.NET 8 LTS** (or the LTS current at project start — OQ-004), `net8.0-windows`, 64-bit (`x64`;
`ARM64` optional). UI: **WPF**. Windows 10 22H2 / Windows 11 and Windows Server 2019+.
**ARC-002** Single-user desktop application, per-user install (MSIX or self-contained folder
deployment); no administrator rights required; runs from a network share.
*Exception (owner decision OQ-018): operations telemetry.* To let operations staff monitor
workstations during active operations, Multiplot.NET may send OpenTelemetry data over a loopback
network connection to a local OpenTelemetry Collector (NFR-TEL-001..006). The collector is an optional,
separately deployed component that production tools install **with administrator rights** as a Windows
service (`tools/OtelCollector`). The capability is **disabled by default** and active only when the
`MULTIPLOT_TELEMETRY` environment variable is set **and** the collector is installed and running. It is
intended for operations workstations during active operations; it is not needed (and not installed) on
normal desktop workstations used for post-operation technical reviews, where Multiplot.NET keeps the
no-administrator, no-network behaviour above. Multiplot.NET itself still needs no administrator rights.
**ARC-003** Third-party components must be permissively licensed (MIT/BSD/Apache) or approved by the
owner. Charting: a high-performance WPF-compatible plotting approach is required (custom
`DrawingVisual`/`WriteableBitmap` renderer, or a library such as ScottPlot / OxyPlot / SciChart
subject to OQ-004); it must support the custom layout of chapter 08 (multi-Y columns, logic bands,
3-D ribbons, markers, tabular layouts) and FR-PLT-024 data reduction.

## 2.2 Solution structure (recommended)

| Project | Responsibility | Depends on |
|---|---|---|
| `Multiplot.Core` | Domain model: files, groups, parameters (`ParameterData`), time (`TimeTag`, `TimeMath`), value formatting, settings model | – |
| `Multiplot.Sun` | SUN reader/writer, LZH codec, comment block parsing, error codes (chapter 03) | Core |
| `Multiplot.DataAccess` | Provider façade, plugin loading/blocking, library options (chapter 04) | Core, Sun, Plugins.Abstractions |
| `Multiplot.Plugins.Abstractions` | Public plugin contracts (versioned NuGet) | – |
| `Multiplot.Expressions` | Expression tokenizer/evaluator, functions, timeline alignment (chapter 06) | Core |
| `Multiplot.Scripting` | Lexer, command parser, interpreter, symbols, recorder (chapter 05) | Core, application service interfaces |
| `Multiplot.Application` (loading) | Group/time-slice/reference resolution, curve loading, WPD/favourites/markers/filter files (chapter 07) | Core, DataAccess, Expressions |
| `Multiplot.ImportExport` | ASCII import, CSV/TAB/SUN export, statistics (chapter 10) | Core, Sun |
| `Multiplot.Application` (real time) | Refresh scheduler, append/replace, AutoRun, compaction/discard (chapter 11) | Loading |
| `Multiplot.Rendering` | Scale computation, layout engine, curve reduction, renderers (screen/image) | Core |
| `Multiplot.Settings` | INI reader/writer, registry adapter, command-id map (chapter 09) | Core |
| `Multiplot.App` | WPF shell, MVVM view models, dialogs, commands, menus | all |
| `Multiplot.Tests.*` | Unit, golden-file, UI automation tests (chapter 14) | – |
| `Multiplot.Parquet` | Bundled Apache Parquet data-source plugin, deployed to `plugins\Multiplot.Parquet\` (FR-PLG-033) | Abstractions, Parquet.Net |
| `tools/MultiToParquet` | `multi2parquet` converter, SUN_MULTITIME(64) → Parquet | Sun, Parquet.Net |
| `samples/MinimalPlugin` | Plugin SDK sample (FR-PLG-030) | Abstractions |
| `tools/OtelCollector` | Bundled OpenTelemetry Collector, its configuration and administrator install scripts (NFR-TEL-006) | – |

**ARC-010 (single command layer)** All user actions (menus, popups, accelerators, user
buttons, scripts, command line, drag & drop) shall go through one application command service, so
that script equivalence (NFR-SCR-003) and recording (FR-SCR-040) are automatic.
**ARC-011 (MVVM)** Views contain no business logic; view models are unit-testable without a UI thread.
**ARC-012 (no global mutable singletons)** Error status, work buffers, LZH decoder state and the
import day roll-over state are instance state, not globals.
*Accepted exception:* the telemetry `ActivitySource`, `Meter` and instruments in
`Multiplot.Core.Diagnostics.MultiplotTelemetry` are process-wide statics (the idiomatic .NET pattern); they
are immutable after creation, and the log forwarder is set once by the host at start-up (NFR-TEL-004).

## 2.3 Non-functional requirements

| ID | Requirement | Acceptance |
|---|---|---|
| NFR-PERF-001 | Open a 2 GB MULTITIME64 file and list its parameters in < 3 s (warm cache) | benchmark fixture |
| NFR-PERF-002 | Redraw a plot with 20 curves × 10 M points each in < 200 ms after the first load (pixel reduction) | benchmark |
| NFR-PERF-003 | Load 20 parameters × 1 M points from a COMPRESSED file in < 2 s | benchmark |
| NFR-PERF-004 | Expression evaluation per NFR-EXP-002 | benchmark |
| NFR-PERF-005 | Memory: plotted data stored as float32 — ≤ 1.2× raw sample size per curve | profiler |
| NFR-REL-001 | No unhandled exception may terminate the application; errors are reported with the message texts defined in this specification, and logged | fault-injection tests |
| NFR-REL-002 | Corrupt or truncated SUN files never crash the reader (fuzzing, FR-LZH-006) | fuzz test 1 h without failure |
| NFR-REL-003 | Live files opened with `FileShare.ReadWrite|Delete`; Multiplot.NET never writes to input files | test |
| NFR-COMP-001 | Text I/O (scripts, INI, CSV, markers, WPD, favourites) uses Windows-1252 by default; UTF-8 with BOM accepted on input | tests |
| NFR-COMP-002 | Number parsing/formatting is culture-invariant and replicates C runtime semantics (`atof`, `atoi`, `%g`, `%f`, `%e`) | golden tests |
| NFR-COMP-003 | Paths: long paths (> 260) supported; relative-path search rules on `;`-separated lists | tests |
| NFR-UX-001 | Per-monitor DPI aware; dialogs resizable where useful, keyboard accessible, consistent tab order | review |
| NFR-UX-002 | Theme: specified colour defaults; plot colours user-configurable; UI chrome follows Windows light theme (dark theme optional) | review |
| NFR-SEC-001 | Scripts may start external processes (`RUN`) and load plugins — these are trusted local features; plugins load only from the configured folders; no network access is initiated by the core application, **except** the opt-in operations telemetry of NFR-TEL-001: OTLP/HTTP to a loopback address only, only when `MULTIPLOT_TELEMETRY` is set and a local collector is listening (ARC-002 exception, OQ-018) | review, test |
| NFR-LOG-001 | Diagnostic log (rolling file in `%LOCALAPPDATA%\Multiplot\logs`) records errors, plugin load results, script errors (in addition to the user-selected script log); it is kept when telemetry is active (OTel logs are an addition, not a replacement) | test |
| NFR-I18N-001 | UI language English; strings in resources (localisable) but message texts must remain byte-identical where scripts/logs depend on them | review |
| NFR-MAINT-001 | ≥ 80 % line coverage for Core, Sun, Expressions, Scripting, Settings, Import/Export | CI |
| NFR-BUILD-001 | `dotnet build` / `dotnet test` from a clean clone; CI pipeline runs unit + golden tests | CI |

## 2.4 Threading model

**ARC-020** UI thread owns view models and plot render state. Data loading, expression evaluation,
import/export and real-time refresh run on background tasks producing immutable `ParameterData`
snapshots, which are published to the UI thread. The script interpreter is an async state machine on
the UI context that awaits these operations (so commands still execute strictly sequentially).
**ARC-021** Cancellation: every long operation accepts a `CancellationToken`; Ctrl+Esc (*break-out*)
and script *Abort* cancel the current operation.

## 2.5 Operations telemetry (OpenTelemetry)

Added by owner decision OQ-018 as an exception to ARC-002 / NFR-SEC-001. Design notes:
`docs/opentelemetry-howto.md`; operator instructions: user guide *Settings and files → Operations telemetry*.

| ID | Requirement | Acceptance |
|---|---|---|
| NFR-TEL-001 | **Opt-in, off by default.** The OpenTelemetry SDK starts only when the environment variable `MULTIPLOT_TELEMETRY` is `1`/`true`/`on`/`yes`, `OTEL_SDK_DISABLED` is not `true`, the endpoint (`MULTIPLOT_OTLP_ENDPOINT`, default `http://127.0.0.1:4318`) is a loopback `http` URL, and a collector accepts a connection on it at start-up (≤ 300 ms probe). Otherwise no provider is created and no connection is made (not even the probe when the variable is unset). The decision is written to the diagnostic log. Telemetry never prevents start-up, never blocks the UI, and a collector that stops later only loses data. | unit + end-to-end tests |
| NFR-TEL-002 | **Traces.** Spans for coarse operations only: `script.run` (script name, full path, lines, end state), `command` (command keyword and the full command text after symbol substitution, error status and text), `data.open` (file path, provider, status), `load.parameters` (parameter list, X axis, groups, curves, points), `realtime.refresh` (one root span per cycle), `export` / `import` (format, files). Nested scripts and commands are child spans. | end-to-end test |
| NFR-TEL-003 | **Metrics.** `multiplot.commands`, `multiplot.script.errors`, `multiplot.data.opens` (per file path, provider and result), `multiplot.plot.parameters` (one per curve placed on a plot, per parameter name, source file path and action `plot`/`add`; attribute limit 10 000 per metric), `multiplot.load.points`, `multiplot.load.duration`, `multiplot.plot.draw.duration` (histogram only — no span per repaint), `multiplot.realtime.refreshes`, `multiplot.realtime.refresh.duration`, plus the .NET `System.Runtime` meter. Export interval 15 s (`MULTIPLOT_OTEL_METRIC_INTERVAL_MS`). Instrumentation must not be per sample or per drawn point (NFR-PERF-002). | unit + end-to-end tests |
| NFR-TEL-004 | **Logs and identity.** Every diagnostic log line (NFR-LOG-001) is also exported as an OTel log record. Each process reports `service.name=Multiplot`, `service.version`, a unique `service.instance.id`, `process.pid`, `host.name`, `user.name`, `multiplot.mode` (`interactive`/`hidden`) and the command line, so that any number of concurrent instances share one collector port. File paths, parameter names, command arguments and script contents are **not** treated as sensitive (OQ-018): they are recorded as they are. Buffered data is flushed on normal exit, on `/hide` script exit, before the fatal render-failure exit and in the unhandled-exception handler (export timeout 2 s). | end-to-end test |
| NFR-TEL-005 | **Libraries and plugins.** Library projects use only the BCL `ActivitySource`/`Meter` (`Multiplot.Core.Diagnostics`); only `Multiplot.App` references the OpenTelemetry SDK and OTLP exporter (Apache-2.0, ARC-003). The host subscribes to `Multiplot*`, so plugins may create `Multiplot.<PluginName>` sources; plugin load contexts always share `System.Diagnostics.DiagnosticSource` and `OpenTelemetry*` with the host. Plugins must not create their own providers or exporters. | unit test |
| NFR-TEL-006 | **Collector.** The OpenTelemetry Collector (contrib distribution, Apache-2.0) is bundled in `tools/OtelCollector` with its configuration (OTLP/HTTP receiver on `127.0.0.1:4318` only, health check `127.0.0.1:13133`, memory limiter, rotated files, optional forwarder to a monitoring backend) and administrator install/uninstall scripts that create the `MultiplotOtelCollector` Windows service and optionally set `MULTIPLOT_TELEMETRY=1` machine-wide. It is deployed by the production tools to operations workstations only and is not part of `dotnet publish`. | review |
