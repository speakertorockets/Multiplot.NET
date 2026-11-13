# 14 — Test Strategy, Fixtures and Golden Files

Multiplot.NET is verified against **golden files**: reviewed reference outputs, checked into the
repository, that are produced from a fixed fixture set. Any change to a golden file must be reviewed
as a behaviour change. Fixture availability is tracked in OQ-011.

## 14.1 Fixture set (`tests/fixtures/`)

| ID | Fixture | Purpose | Origin |
|---|---|---|---|
| FX-SUN-01 | SUN_COMPRESSED, single block, 20 parameters, f4 only, 10 k points | basic read | owner / generated with the SUN writer |
| FX-SUN-02 | SUN_COMPRESSED, double-blocked around t = 0 (≥ 500 points each side) | FR-SUN-040..043 | same |
| FX-SUN-03 | SUN_COMPRESSED with extended types i2, i4, u2, u4, b1, d8, t8 UTC index at position 1, 36-char description | types, UTC access | same |
| FX-SUN-04 | SUN_COMMONTIME (variable map, long names, rates in seq numbers) | map decoding | same |
| FX-SUN-05 | SUN_UNCOMPRESSED (live recorder layout) | raw blocks | same |
| FX-SUN-06 | SUN_CIRCULAR, wrapped and unwrapped | FR-SUN-046 | same |
| FX-SUN-07 | SUN_MULTITIME, several blocks per parameter, index types f4 / d8 / u4 / t8 / p4, compressed and raw, big- and little-endian payloads, `v1` object parameter, nomen override | FR-SUN-070..074 | same |
| FX-SUN-08 | SUN_MULTITIME64 > 4 GB offsets (sparse file allowed) | 64-bit links | same |
| FX-SUN-09 | *_SRT signatures | FR-SUN-002 | patch FX-SUN-07 header |
| FX-SUN-10 | Comment block with `[events]` (both formats, invalid day), `[rates]`, `[extendednomen]`, `[script]`, `[history]`, `FILE_REF` | FR-SUN-080 | same |
| FX-SUN-11 | Corrupted/truncated variants of 01, 02, 07 | robustness | generated |
| FX-IMP-01..08 | CSV/TAB/space files: relative time; clock `hh:mm:ss.mmm`; `ddd/hh:mm:ss`; TimeTag `ddd/msec.usec`; Unix epoch; header rows (names/units/descr/alt); NaN/empty fields; index column ≠ 1; single column | chapter 10 | handwritten |
| FX-SCR-xx | Real production scripts from users (≥ 20) plus synthetic scripts covering every command and lexical rule (continuations, comments, `\;`, symbols, `CALL` nesting, `PAUSE`) | chapter 05 | owner + generated |
| FX-CFG-xx | Real `Multiplot.ini` files (old short colour table, current), favourites, `.markers`, `.wpd` (with bitmap, markers, ynomen), `.flt` with several `[FILTER]=` sections | chapter 09/07 | owner |
| FX-RT-01 | Writer tool that appends MULTITIME blocks / UNCOMPRESSED points to a live file at configurable rates, adds events, rewrites the header (reference change) | chapter 11 | to be built (C# using the SUN writer) |
| FX-PQ-01 | MULTITIME64 written by the SUN writer in the test (t8/d8/f4 index, dropout, empty parameter, EOF event), converted with `multi2parquet` into several row groups | FR-PLG-033, FR-XPT-050 | generated in `ParquetPluginTests` |
| FX-PQ-02 | Generic Parquet files: TIMESTAMP, relative-seconds (with reference metadata) and nullable/string columns across several row groups | FR-PLG-033 | generated in `ParquetPluginTests` |
| FX-SAMPLE | `ColorPrinter.wps`, `samples/MinimalPlugin/demo_data.min` | misc | repo |

## 14.2 Golden outputs

For each scenario a script produces outputs that become golden files once reviewed:
* `EXPORT` CSV/TAB of plots (covers data decoding, time references, expressions, functions,
  formatting) — TST-SUN-001, TST-EXP-001, TST-FN-001, TST-XPT-001, TST-LOAD-001.
* `DATASTATS` files — FR-XPT-041.
* `SAVEPARMLIST`, `SAVEFILEDETAILS`, `SAVEPARMDETAILS` — chapter 05.
* `SAVESUN` files (binary compare + re-read) — FR-XPT-020.
* `SAVESETTINGS` INI — TST-CFG-001.
* `markerset save`, WPD save.
* `SAVEPIC PNG` images — used only for **visual review**, not byte comparison (DEV-UI-001).
* Script recordings of scripted UI sessions — TST-SCR-010.

## 14.3 Test catalogue

| ID | Test | Type |
|---|---|---|
| TST-SUN-001 | Open every FX-SUN fixture; export all parameters; compare to golden CSV (exact text) | golden |
| TST-SUN-010 | Files written by the SUN writer re-open with identical exports | golden |
| TST-LZH-001 | Decode every LZH block of the fixtures; compare to golden decoded bytes | golden |
| TST-LZH-002 | Round-trip 10,000 random buffers through encoder/decoder | property |
| TST-LZH-003 | Fuzz the decoder (truncation, bit flips) — only defined error codes, no exceptions/hangs | fuzz |
| TST-TIM-001 | `UTC()`/`TIME()`/`TIME_location`/`UTC_location` edge-case tables | unit |
| TST-SCR-001 | Table-driven parser tests for every command form in §5.6, including error texts | unit |
| TST-SCR-002 | Lexer tests: continuation, comment stripping, `\;`, 16 K lines, CRLF/LF | unit |
| TST-SCR-010 | Record a UI session covering every recordable action; replay; compare plots & settings | UI automation |
| TST-SCR-020 | `/hide` batch run producing PNG + CSV, no window, exit code 0 | integration |
| TST-EXP-001 | ≥ 200 constant expressions and ≥ 100 parameter expressions vs. golden exports | golden |
| TST-FN-001 | Each function on fixture data vs. golden exports | golden |
| TST-LOAD-001 | Time slice/reference matrix (NONE/START/CUTOFF/EOF/events/UTC/constants with offsets) | golden |
| TST-PLT-001 | Axis scaling vs. golden reference values (10 k random ranges, log, clock) | unit |
| TST-CFG-001 | INI load→save round-trip vs. golden saved INI | golden |
| TST-CFG-002 | Command-id map covers all menu command ids | unit |
| TST-IMP-001 | Import fixtures vs. golden imported SUN (via export) | golden |
| TST-XPT-001 | Export matrix (zoom, multi-file, PvP, end-to-end, shared time, UTC, precisions) | golden |
| TST-RT-001 | Live-file refresh scenario (FX-RT-01) equals static reload; UI responsive | integration |
| TST-PLG-001 | Sample plugin loads, is listed, can be blocked/unblocked via registry and script | integration |
| TST-PLG-002 | The Parquet plugin loads from `plugins\Multiplot.Parquet\`. Reads of FX-PQ-01 equal SUN reads of the source file (values bit for bit, times within 1e-5 relative, UTC within 1 µs, same error codes) for full and windowed ranges. FX-PQ-02 times, nulls, dropouts and windowed reads are correct. A `/hide` script opens, plots and exports a `.parquet` file | integration |
| TST-XPT-050 | `multi2parquet verify` on converted sample and synthetic files (every index type, LZH, big-endian, appended blocks, 32- and 64-bit) reports a bit-for-bit match. `crosscheck` matches the SUN reader except for the OQ-017 leap-day case | integration (manual / tool) |
| TST-TEL-001 | Telemetry gate: off when `MULTIPLOT_TELEMETRY` is unset or not "on" (and no probe is made), `OTEL_SDK_DISABLED`, non-loopback or non-`http` endpoints rejected, no collector → off, endpoint/interval overrides; TCP probe; log forwarder never throws (`TelemetryTests`) | unit |
| TST-TEL-002 | `data.open` span and `multiplot.data.opens` counter (with `multiplot.file`) for a good and a missing file via BCL listeners; plugin load contexts share `System.Diagnostics.DiagnosticSource`/`OpenTelemetry*` (`TelemetryTests`) | unit |
| TST-TEL-003 | `/hide` script against a fake OTLP/HTTP collector on a loopback port: `/v1/traces`, `/v1/metrics`, `/v1/logs` received with resource attributes, `script.run`/`command`/`data.open`/`load.parameters`/`export` spans, the full command text, error status and metrics; without the variable no connection is made; with the variable but no collector the run succeeds and the log says why telemetry is off (`TelemetryEndToEndTests`) | integration |
| TST-UI-001 | Smoke UI automation (FlaUI/WinAppDriver): menus, dialogs open/close, accelerators | UI automation |
