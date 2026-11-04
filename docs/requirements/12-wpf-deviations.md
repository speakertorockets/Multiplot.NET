# 12 — Design Decisions

This chapter lists the platform-driven design decisions of Multiplot.NET and every behavioural quirk
of the specified file formats, script language and expression language whose treatment had to be
decided. **Preserve** = keep the behaviour exactly as described; **Fix** = the behaviour is changed
as stated (users are told in the release notes); **Owner?** = recommendation awaiting confirmation in
13-open-questions.

## 12.1 Platform-driven decisions

| ID | Decision | Rationale |
|---|---|---|
| DEV-UI-001 | Plots are rendered with WPF/Direct2D-class graphics; anti-aliasing, font metrics and exact pixel positions differ. Layout rules, content, colours, scale values and interaction semantics are preserved (chapter 08). | GDI XOR rubber-banding and pixel-exact replication are neither possible nor useful. |
| DEV-UI-002 | Menu items for out-of-scope features (Print, Print setup, Printer ▸, EMF copy/aspect, Query, External Command Polling, Help Contents) are removed. Their script commands are accepted and ignored (DEV-SCR-001). | Owner scope decision. |
| DEV-UI-003 | MDI is emulated (WPF has no native MDI). Tiling/cascade/stack/keep-on-top behave as specified in FR-UI-001; minimised plots appear as icons/tabs in the document area. | Platform. |
| DEV-UI-004 | Rubber-band interactions are mouse-capture based; instead of confining the cursor, coordinates are clamped to the plot area. | Platform. |
| DEV-ACC-001 | No explicit memory management in the API; garbage collection and `IDisposable` instead. | .NET idiom. |
| DEV-PLG-001 | Native plugin DLLs are not loaded; extensions are .NET plugins (chapter 04). | Owner decision. |
| DEV-XPT-001 | No EMF output. `SAVEPIC EMF …` and `COPYMETA file` save a **PNG** instead and log a warning; `COPYMETA CLIPBOARD` copies a bitmap. | EMF out of scope; keeps existing scripts producing an image. Owner? (OQ-003) |
| DEV-SCR-001 | Out-of-scope script commands (`PRINT`, `PRINTERNAME`, `PRINTERMARGIN`, `COLORPRINTER`, `LANDSCAPE`, `EMFWIDE`, `COPYMETA` (file), `MCI`, `PLAYSOUND`, `EXTCMDPOLLING`, `DBPATH`) are parsed, their values stored/persisted where they are settings, otherwise ignored with an entry in the script log and diagnostic log — never an error dialog. `MCI` sets `MCI_RETURN` to an empty string. | Existing scripts must keep running. |
| DEV-SCR-002 | No mailslot command channel; `/comm/<name>` accepted and ignored; `$MAILSLOT_NAME` returns `Auto`. | Owner scope decision. |
| DEV-SCR-003 | In hidden mode (`/hide`) every modal message that would be shown is written to the script log and diagnostic log and answered automatically with its default/"safe" button (OK; Yes/No questions → the choice that continues without destructive action as specified per message; Abort/Retry/Ignore → Ignore). Visible mode keeps modal dialogs. | Hidden runs must never hang on a dialog. Owner? (OQ-009) |
| DEV-CFG-003 | Dialog geometry and other implementation-specific values are stored under `HKCU\Software\Multiplot\NET\…`. All other registry values follow FR-CFG-030. | Keeps implementation-specific values separate. |
| DEV-SUN-002 | The implementation may keep double-precision copies of time internally, but point selection and plotted/exported values must follow the float32 semantics of FR-TIM-003. | Bit-compatibility of results. |

## 12.2 Behavioural quirks — decisions

| ID | Behaviour | Decision |
|---|---|---|
| DEV-SUN-001 | MULTITIME data byte-swap test uses `typeIDS[1] != 'v'` (always true), so it relies on `DataTypeSize > 1` to skip `v1`. | Preserve observable result (no swap for `v1` objects, swap otherwise). |
| DEV-SCR-004 | Symbol substitution restarts from the first `$` after each replacement; a value containing its own `$NAME` loops forever. | Fix: stop after 1000 substitutions in one line, report `Symbol substitution limit exceeded` and continue with the partially substituted text. |
| DEV-SCR-005 | `$GMT_REFERENCE` consumes 16 characters (2 more than its length). | Preserve (scripts may compensate). Owner? (OQ-008) |
| DEV-SCR-006 | `FIXX/FIXY lo,hi, p` (space before `p`) produces a fixed increment of 0. | Preserve parsing; the scaler treats increment 0 as best-fit (avoids divide-by-zero). |
| DEV-SCR-007 | `TIME hh:mm:ss,…` treats the text before the first `:` as a group name and stops. | Preserve. |
| DEV-SCR-008 | `SET NAME=value` truncates the value at the first `;` even when written as `\;`. | Preserve. |
| DEV-SCR-009 | `RETAINMTGMT` shows the deprecation error and then also executes as `64BITTIME`. | Preserve. |
| DEV-SCR-010 | `REFRESH OFF` when auto refresh is not running performs a one-time refresh. | Preserve. |
| DEV-SCR-011 | `SET TRACE` logic index range is 0–3 in positional form but 1–4 in `KEY=` form. | Preserve. |
| DEV-EXP-001 | Unusual operator precedence (`%` below `*`/`/`, `<` above `>`, `~` below `&`/`|`, ties left-to-right for `^`). | Preserve; document for users. |
| DEV-EXP-002 | Bitwise/shift/logical operators convert doubles with C `(unsigned long)` casts; negative/NaN inputs are undefined behaviour in C (MSVC x86/x64 truncate via int64). `%` by 0 yields NaN which is stored unchanged. | Convert with `(uint)(long)Math.Truncate(v)` (matches MSVC x64), NaN/∞ → 0. Store NaN results as dropouts. Owner? (OQ-008) |
| DEV-EXP-003 | The `~` (delta) operator supports at most 5 occurrences (fixed array; more corrupts memory). | Fix: unlimited, same semantics. |
| DEV-EXP-004 | Constant-only expressions with a reference other than NONE read TIME from `pFile[basePid]` (an index mix-up) — effectively the first file of the group. | Preserve observable result (first file of the group). |
| DEV-FN-001 | `sqrt(0)` returns a dropout (test `> 0`). | Preserve. |
| DEV-FN-002 | `spline()` asks for confirmation when points > 50,000 (blocking scripts). | Preserve in interactive mode; in scripts without prompts proceed without asking. |
| DEV-TAB-001 | WPD `COLOR_INDEX=` is parsed at the wrong offset and never applied; `TAB_FORMAT=DECn` only recognised in some positions. | Owner? Recommendation: **Fix** (apply COLOR_INDEX and DECn as documented) — OQ-008. |
| DEV-IMP-001 | Scripted `IMPORT … /R ref` is overwritten by the dialog's default reference before conversion, so `/R` has no effect; the reference must be in `yyyy ddd hh:mm:ss.mmm.uuu` form (≥ 21 chars). | Owner? Recommendation: **Fix** (honour `/R`) — OQ-008. |
| DEV-XPT-002 | Slice/DATASTATS trapezoidal integral uses the previous *array* element even if it was a dropout or outside the range. | Preserve (numbers must match existing reports). Owner? (OQ-008) |
| DEV-CFG-001 | INI boolean keys with an empty value have no defined value. | Fix: empty value = true (non-zero). |
| DEV-CFG-002 | `DefaultReference` is saved as the actual value only when it is `start`; otherwise `NONE`. | Preserve. |
| DEV-RT-001 | A refresh on a background thread could mutate plot data while the UI paints. | Fix: snapshot/marshalling model (NFR-RT-001); same observable results. |
| DEV-LOAD-001 | `OPEN` of a comma list loads each item as a *separate* group; `ASDATABASE` makes one group. | Preserve. |
