# 02 — Target Platform, Architecture and Non-Functional Requirements

## 2.1 Platform

**ARC-001** Language C# (latest stable language version supported by the chosen SDK), target
**.NET 8 LTS** (or the LTS current at project start — OQ-004), `net8.0-windows`, 64-bit (`x64`;
`ARM64` optional). UI: **WPF**. Windows 10 22H2 / Windows 11 and Windows Server 2019+.
**ARC-002** Single-user desktop application, per-user install (MSIX or self-contained folder
deployment); no administrator rights required; runs from a network share.
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
| `Multiplot.App` | WPF shell, MVVM view models, dialogs, commands, menus/toolbars | all |
| `Multiplot.Tests.*` | Unit, golden-file, UI automation tests (chapter 14) | – |
| `samples/MinimalPlugin` | Plugin SDK sample (FR-PLG-030) | Abstractions |

**ARC-010 (single command layer)** All user actions (menus, toolbars, popups, accelerators, user
buttons, scripts, command line, drag & drop) shall go through one application command service, so
that script equivalence (NFR-SCR-003) and recording (FR-SCR-040) are automatic.
**ARC-011 (MVVM)** Views contain no business logic; view models are unit-testable without a UI thread.
**ARC-012 (no global mutable singletons)** Error status, work buffers, LZH decoder state and the
import day roll-over state are instance state, not globals.

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
| NFR-SEC-001 | Scripts may start external processes (`RUN`) and load plugins — these are trusted local features; plugins load only from the configured folders; no network access is initiated by the core application | review |
| NFR-LOG-001 | Diagnostic log (rolling file in `%LOCALAPPDATA%\Multiplot\logs`) records errors, plugin load results, script errors (in addition to the user-selected script log) | test |
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
