# Multiplot.NET — Requirements Specification

Requirements for **Multiplot.NET**, a **C# / .NET / WPF** desktop application for viewing,
analysing and plotting engineering test data. The document set is self-contained: an implementer
(human or Claude) should be able to build and maintain the application from these files alone.

## How to read this document set

| # | File | Contents | ID prefixes | Status |
|---|------|----------|-------------|--------|
| 00 | [README.md](README.md) | Index, conventions, ID scheme | – | done |
| 01 | [01-overview-scope.md](01-overview-scope.md) | Purpose, users, scope in/out, glossary, global limits | GEN, LIM | done |
| 02 | [02-architecture.md](02-architecture.md) | Target platform, solution layout, non-functional requirements | ARC, NFR | done |
| 03 | [03-sun-data-format.md](03-sun-data-format.md) | Complete SUN binary file format spec + LZH codec + time math | SUN, LZH, TIM | done |
| 04 | [04-data-access-plugins.md](04-data-access-plugins.md) | Data-access abstraction, .NET plugin API, plugin management | ACC, PLG | done |
| 05 | [05-script-language.md](05-script-language.md) | `.wps` script language: lexing, execution model, every command | SCR | done |
| 06 | [06-expression-language.md](06-expression-language.md) | Parameter expressions, operators, functions | EXP, FN | done |
| 07 | [07-data-loading.md](07-data-loading.md) | Files, groups, time slices/references, events, filters, favorites | LOAD | done |
| 08 | [08-plotting-ui.md](08-plotting-ui.md) | Main window, plot windows, styles, scaling, mouse, markers, tabular, dialogs | UI, PLT, MRK, TAB | done |
| 09 | [09-settings-compat.md](09-settings-compat.md) | INI file, registry, command line, user folder | CFG | done |
| 10 | [10-import-export.md](10-import-export.md) | ASCII import, ASCII/SUN export, Parquet conversion, images, data stats | IMP, XPT | done |
| 11 | [11-realtime.md](11-realtime.md) | Live files, refresh, AutoRun, streams, compact/discard | RT | done |
| 12 | [12-wpf-deviations.md](12-wpf-deviations.md) | Design decisions: removed features, behaviour fixes, quirks that are kept | DEV | done |
| 13 | [13-open-questions.md](13-open-questions.md) | Unresolved items needing an owner decision | OQ | done |
| 14 | [14-test-fixtures.md](14-test-fixtures.md) | Required test data, golden files, acceptance test strategy | TST | done |
| 15 | [15-traceability.md](15-traceability.md) | Requirement area ↔ implementation map | – | done |

## Requirement ID scheme

`<TYPE>-<AREA>-<NNN>` — e.g. `FR-SCR-012`.

* `FR` functional requirement, `NFR` non-functional, `DEV` design decision, `OQ` open question, `TST` test.
* IDs are **stable**: never renumber. New requirements get the next free number in their area;
  retired requirements are marked `(withdrawn)` and kept.
* Numbers within an area are grouped in blocks per sub-topic so that additions can be inserted
  logically (e.g. FR-SCR-100..114 flow-control commands, FR-SCR-250..252 the `SET` command).
* Some chapters define requirements as **table rows** (`| FR-… |`); a row is a requirement exactly
  like a bold `**FR-…**` paragraph.

| Area | Meaning | File |
|------|---------|------|
| GEN | General / application-wide | 01 |
| LIM | Capacity limits | 01 |
| ARC / NFR | Architecture, platform, quality attributes | 02 |
| SUN | SUN file format structure | 03 |
| LZH | LZH (ar002 -lh5-) codec | 03 |
| TIM | TimeTag / relative-time arithmetic | 03 |
| ACC | Data-access layer (file model, parameter model) | 04 |
| PLG | Plugin API and plugin management | 04 |
| SCR | Script language | 05 |
| EXP | Expression syntax and evaluation | 06 |
| FN | Expression functions | 06 |
| LOAD | Data loading, groups, time references, filters | 07 |
| UI | Main window, menus, dialogs | 08 |
| PLT | Plot window rendering, scaling, interaction | 08 |
| MRK | Markers | 08 |
| TAB | Tabular (WPD) displays | 08 |
| CFG | Settings / persistence / command line | 09 |
| IMP | ASCII import | 10 |
| XPT | Export (ASCII, SUN, image, stats) | 10 |
| RT | Real-time | 11 |

## Conventions

* **Shall** = mandatory, **should** = desirable, **may** = optional.
* **Acceptance.** Requirements with their own *Acceptance:* line are verified as stated. Every other
  requirement is verified by the default test of its chapter in [14-test-fixtures.md](14-test-fixtures.md):

  | Chapter | Default verification |
  |---|---|
  | 03 SUN / LZH / TIM | TST-SUN-001, TST-SUN-010, TST-LZH-001..003, TST-TIM-001 |
  | 04 ACC / PLG | TST-PLG-001, TST-PLG-002 + unit tests of the façade |
  | 05 SCR | TST-SCR-001 (one table-driven case per syntax form and error text in §5.6), TST-SCR-002, TST-SCR-010, TST-SCR-020 |
  | 06 EXP / FN | TST-EXP-001, TST-FN-001 |
  | 07 LOAD | TST-LOAD-001 + golden exports of WPD/favourites/markers fixtures |
  | 08 UI / PLT / MRK / TAB | TST-PLT-001 for scaling; TST-UI-001 UI automation plus a review checklist (screenshots) for layout and dialogs |
  | 09 CFG | TST-CFG-001, TST-CFG-002 + registry round-trip tests |
  | 10 IMP / XPT | TST-IMP-001, TST-XPT-001 |
  | 11 RT | TST-RT-001 |
  | 02 NFR | the benchmark/test named in each row |

  Where acceptance needs data, the fixture is named in [14-test-fixtures.md](14-test-fixtures.md).
* "Compatible" means: an existing data file, script, settings file, marker file or display file in
  the formats specified here is accepted and yields the same observable result.
* Byte layouts are given with explicit offsets and byte order. All multi-byte on-disk header
  fields of SUN files are **big-endian** unless stated otherwise.

## Normative tables kept in the source tree

Some large tables are normative **by reference** rather than copied into this specification:

| Artefact | Used for |
|---|---|
| `src/Multiplot.Core/CommandIds.cs` | command ids ↔ commands (`CaptionSource`, `GroupBy`, `MULTIY GROUPBY_n`) — FR-CFG-040 |
| `src/Multiplot.App/MenuDefinitions.cs`, `src/Multiplot.App/CommandPrompts.cs` | menu captions, status-bar prompts and tooltips — FR-UI-002 |
| `src/Multiplot.Plugins.Abstractions/SunError.cs` | error enum values and message strings — FR-SUN-090 |
| `src/Multiplot.Sun/Lzh.cs`, `LzhEncoder.cs` | LZH codec — FR-LZH-001..006 |
| `src/Multiplot.Core/ParameterOps.cs` | `average`, `quantize`, interpolation, full-rate rebuild — FR-FN-030, FR-SUN-076 |
| `src/Multiplot.Core/Plotting/Scale.cs` | axis scaling — FR-PLT-010..014 |
| `src/Multiplot.ImportExport/DetailsWriter.cs`, `src/Multiplot.Settings/IniFile.cs`, `src/Multiplot.ImportExport/Exporter.cs` | exact text output formats (details files, INI writer, CSV/TAB export) — FR-SCR-157..159, FR-CFG-011, FR-XPT-011 |
| `src/Multiplot.Core/Crt.cs` | C runtime `printf`/`atof`/`atoi`/`_gcvt` semantics — NFR-COMP-002 |

## Owner decisions already taken (2026-10-16)

| Topic | Decision |
|-------|----------|
| Data access | Managed C# implementation of all SUN formats (including LZH codec) + .NET plugin API. Native plugin DLLs are **not** supported. |
| Compatibility (must) | `.wps` scripts incl. parsing quirks; expression syntax; INI + registry + favorites settings; `.markers`, `.wpd`, `.flt` files. |
| In scope | Real-time: live files, auto refresh, AutoRun, PC GOAL `.flt` filter streams, compact/discard, dropout handling. |
| Out of scope | External control (mailslot polling, `/COMM`); Query tool and `DB_DIR` database browsing; printing, EMF/clipboard metafiles, MCI/`PLAYSOUND`, HTML Help. |
| Deliverable | This multi-file Markdown set with stable IDs and acceptance criteria. |

Remaining decisions are tracked in [13-open-questions.md](13-open-questions.md).
