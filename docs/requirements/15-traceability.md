# 15 — Traceability: Requirements ↔ Implementation

Every source file of Multiplot.NET is mapped to the requirement areas it implements.

## 15.1 Application

| File | Main content | Requirements |
|---|---|---|
| `Multiplot.Application/Startup.cs` | command line, user folder, INI/registry start-up, exit checks | FR-SCR-300/301, FR-CFG-001..004 |
| `Multiplot.Application/Session.cs` | session state, messages, diagnostic log, script recording, system symbols | ARC-010, FR-SCR-040, FR-SCR-050..053, NFR-LOG-001 |
| `Multiplot.Application/CommandProcessor.cs` | script command interpreter, `SET` commands | 05 (all FR-SCR) |
| `Multiplot.Application/ScriptRunner.cs` | script files, `CALL` stack, pause/resume/abort | FR-SCR-010/011, FR-SCR-100..114 |
| `Multiplot.Application/MenuCommands.cs` | command-id dispatch for menus, toolbars, accelerators, popups | ARC-010, FR-UI-002..004, FR-CFG-040 |
| `Multiplot.Application/Loading.cs` | file groups, parameter loading, temporary groups, WPD loading, recent files | 07 FR-LOAD-*, FR-PLG-010..013 |
| `Multiplot.Application/Handlers.Axis.cs` | fix X/Y, multi-Y, log, zoom/pan, rescale | FR-PLT-015/016, FR-UI-027 |
| `Multiplot.Application/Handlers.Style.cs` | style toggles, colours, fonts, nomenclature, captions, precision | FR-PLT-020..023, FR-PLT-040 |
| `Multiplot.Application/Handlers.Misc.cs` | markers, export, pictures, import, settings files, plugins | FR-MRK-*, FR-XPT-*, FR-IMP-*, FR-CFG-010..013, FR-PLG-012 |
| `Multiplot.Application/PlotMouse.cs` | mouse interactions in plot windows | FR-PLT-030..036 |
| `Multiplot.Application/RealTime.cs` | refresh timer, append/replace, AutoRun, compact/discard | 11 FR-RT-* |
| `Multiplot.Application/Shell.cs` | UI service interfaces (dialogs, prompts) used by the session | ARC-011, DEV-SCR-003 |

## 15.2 Core, data access and file formats

| File | Main content | Requirements |
|---|---|---|
| `Multiplot.Core/Model/*` | settings, plot configuration, plot documents, curves, markers | 08, 09 |
| `Multiplot.Core/Plotting/Scale.cs` | axis scaling, zoom stack | FR-PLT-010..014 |
| `Multiplot.Core/ParameterOps.cs` | min/max, append, average, quantize, full-rate rebuild, compaction | FR-ACC-002, FR-FN-030, FR-SUN-076, FR-RT-031 |
| `Multiplot.Core/TimeReference.cs`, `TimeParse.cs`, `Formatting.cs` | time slices and references, time strings, value formatting | FR-LOAD-020/021, FR-XPT-001 |
| `Multiplot.Core/Crt.cs`, `Strtok.cs`, `AnsiText.cs` | C runtime number semantics, tokenizer, text encoding | NFR-COMP-001/002 |
| `Multiplot.Core/CommandIds.cs`, `ToolbarDefaults.cs` | command ids, default toolbar bands | FR-CFG-040, FR-UI-004 |
| `Multiplot.Sun/*` | SUN reader/writer, LZH codec, comment blocks | 03 (all) |
| `Multiplot.Plugins.Abstractions/*` | plugin contracts, data model, data types, error codes, time math | 04 FR-ACC-*, FR-PLG-*, FR-SUN-090, FR-TIM-* |
| `Multiplot.DataAccess/DataAccessService.cs` | provider façade, plugin loading (including `Name\Name.dll` sub-folders) and blocking | 04 FR-ACC-*, FR-PLG-010..013 |
| `Multiplot.Parquet/*` | bundled Apache Parquet data-source plugin | FR-PLG-033, DEV-PLG-002 |

## 15.3 Expressions, scripting, settings, import/export, rendering, UI

| File | Main content | Requirements |
|---|---|---|
| `Multiplot.Expressions/*` | expression evaluator and function library | 06 FR-EXP-*, FR-FN-* |
| `Multiplot.Scripting/*` | script lexer, keywords, command argument parsers, symbols | 05 FR-SCR-010..053, FR-SCR-1xx..2xx |
| `Multiplot.Settings/IniFile.cs`, `RegistryStore.cs` | INI reader/writer, registry adapter | 09 FR-CFG-010..040 |
| `Multiplot.ImportExport/*` | ASCII import, CSV/TAB/SUN export, details files, statistics | 10 FR-IMP-*, FR-XPT-*, FR-SCR-157..159 |
| `Multiplot.Rendering/*` | plot renderer, picture output | 08 FR-PLT-*, FR-TAB-*, FR-XPT-030 |
| `Multiplot.App/*` | WPF shell, menus, toolbars, document area, dialogs | 08 FR-UI-*, DEV-UI-001..004 |
| `samples/MinimalPlugin` | plugin SDK sample | FR-PLG-030 |
| `tools/MultiToParquet/*` | `multi2parquet` MULTITIME(64) → Parquet converter | FR-XPT-050, OQ-016 |
| `tests/Multiplot.Tests/EndToEnd/ParquetPluginTests.cs` | Parquet plugin integration tests | TST-PLG-002 |

## 15.4 Requirement → verification

Each FR carries its own acceptance line; the test catalogue is in 14 §14.3. A coverage matrix
(`docs/requirements/coverage.csv`: requirement id, test ids, status) shall be maintained by the
project and checked in CI (every FR must map to ≥ 1 test or a review checklist item).
