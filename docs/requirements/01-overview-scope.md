# 01 — Overview, Scope, Glossary and Global Limits

## 1.1 Product

Multiplot.NET is a Windows desktop tool for rocket-engine and other test data. It opens binary test
data files (the "SUN" family), lets engineers select parameters, evaluate expressions and functions
over them, plot them in many styles and time references, annotate plots with markers, tabulate and
export data, monitor growing (live) files in real time, and automate all of this with a script
language (`.wps`). It is written in C# for .NET with a WPF user interface.

## 1.2 Goals

**GEN-001** Complete support for all in-scope features (§1.3), such that users' data files,
scripts, INI settings, registry settings, favourites, marker files, display (`.wpd`) files and
filter (`.flt`) files in the formats of this specification work without modification.
**GEN-002** No native-code dependencies: the SUN reader/writer and LZH codec are implemented in
managed code; extension is through a .NET plugin API.
**GEN-003** Modern, maintainable architecture with automated tests that pin the specified behaviour
(golden files) — see 02-architecture and 14-test-fixtures.
**GEN-004** Where specified behaviour is clearly a defect, the decision to keep or fix it is explicit
and recorded in 12-wpf-deviations; nothing is changed silently.

## 1.3 Scope

| Area | In scope | Out of scope |
|---|---|---|
| Data formats | SUN_COMPRESSED, UNCOMPRESSED, CIRCULAR, MULTITIME, COMMONTIME, MULTITIME64, *_SRT read; COMPRESSED/COMMONTIME/MULTITIME/MULTITIME64 write; ASCII import | Native plugin DLLs |
| Extensibility | .NET plugin API (data sources, expression functions, menu commands) | ExternalFn.dll / RefPropFn.dll / PolyFit.dll native ABIs |
| Expressions | full syntax, operators, functions | — |
| Scripts | all 120 commands incl. parsing quirks; recording | mailslot/external command polling (`EXTCMDPOLLING`, `/comm`) — parsed and ignored |
| Plotting | all styles, scaling, multi-Y, markers, tabular WPD displays, legends, titles, presentation mode | printing (`PRINT`, print setup, margins, colour printer, text printing), EMF/metafile output and clipboard metafiles, MCI/`PLAYSOUND`, HTML Help |
| Data loading | files, groups (incl. temporary groups for `ASDATABASE`), time slices/references/events, favourites, filters | Query tool and DB_DIR database browsing |
| Real time | live SUN files, auto refresh, AutoRun, PC GOAL `.flt` streams (via plugin), compaction/discard, dropouts | — |
| Settings | INI, registry, user folder, command line | — |

## 1.4 Users and usage

* Test engineers analysing post-test data files (typically 10²–10⁴ parameters, 10³–10⁷ samples each).
* Test-stand operators watching live data during tests (real-time refresh, AutoRun scripts).
* Automation: batch scripts generating standard plot packages (images, CSV, statistics),
  frequently run hidden (`/hide`) from other tools.

## 1.5 Glossary

| Term | Meaning |
|---|---|
| Parameter (PID) | A named measured channel (map entry) in a data file; also "curve" once plotted |
| SUN file | Binary data file family (chapter 03) |
| TIME | Relative time in seconds from a reference (stored as float32) |
| GMT / TimeTag | Absolute time of day: Julian day + ms + µs (no year) |
| Reference (tRef) | The zero of the plotted time axis: NONE, START, CUTOFF, EOF, event, GMT or constant |
| Event | Named GMT stored in a file's comment block `[events]` |
| Cutoff / Shutdown | Engine cutoff time stored in the file header |
| Group / database | A set of files loaded together; one curve per file per parameter |
| Dropout | Missing sample, encoded as bit pattern 0x80000001 |
| Expression | Arithmetic/logical formula over bracketed parameters (chapter 06) |
| Function | Built-in transformation such as `avg()`, `spline()` (chapter 06) |
| Symbol / symbolic | Script variable `$NAME` (chapter 05) |
| Marker | Annotation drawn on a plot (text, line, box, circle, event, redline, image) |
| WPD | Display file defining a tabular display |
| Multi-Y | Several Y scales side by side |
| End-to-end | Groups laid sequentially along the X axis |
| Apply global | Style changes apply to all plot windows |
| Stream | Live network/telemetry data source (PC GOAL) delivered via plugin |

## 1.6 Global limits (must be honoured as minima)

| ID | Limit | Value |
|---|---|---|
| LIM-001 | open files | 1000 |
| LIM-002 | file groups | 1000 |
| LIM-003 | curves per plot / per load | 1000 |
| LIM-004 | markers per plot | 1000 |
| LIM-005 | colour table | 100 curve colours + grid, label, background, mono, title |
| LIM-006 | bracketed items per expression | 128 |
| LIM-007 | expression stack | 512 operands |
| LIM-008 | remembered parameter lists | 64 |
| LIM-009 | toolbars / user buttons | 19 / 5 |
| LIM-010 | legend columns / reserved rows | 64 / 32 |
| LIM-011 | multi-Y scales | 10 |
| LIM-012 | logic label sets | 4 |
| LIM-013 | zoom stack depth per axis | 20 |
| LIM-014 | script line length | 16,383 chars |
| LIM-015 | import record length / columns | 2 MiB / 16 K |
| LIM-016 | file events | 4096 |
| LIM-017 | comment block | 64 MiB |
| LIM-018 | tabular layout grid | 4096 × 4096 |
| LIM-019 | function parameter arguments | 8 |
| LIM-020 | recent files | 10 |

The implementation may raise limits (e.g. via dynamic collections) provided that observable behaviour at or
below the limits is unchanged and limit messages remain where scripts could depend on them.

## 1.7 Document map

See [README.md](README.md).
