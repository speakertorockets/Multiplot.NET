# 10 — ASCII Import, Data Export, Images and Statistics

---

## 10.1 ASCII import

**FR-IMP-001 (entry points)** Menu *File → Import*, drag-drop/command-line `.csv`, script `IMPORT`
(FR-SCR-150), *Edit → Import clipboard* (clipboard text saved to `<temp>\ClipBoard.NNN` then imported).
Interactive import shows the Import dialog; scripted import runs the same steps without user input
(a progress window is shown). While importing, other file loads are refused.

**FR-IMP-002 (dialog)** Title `Importing - <file>`; list box showing the first 512 lines (stop at
a blank line; lines > 2048 chars truncated with ` . . .`), trailer `- End of File -` or
`- Remainder of file not shown -`. Controls: delimiter (space/any, comma, tab), index column
(1..10000), skip rows (0..1,048,576), title row (0 = file name), generate TimeTags, standard format,
Unix epoch time, reference time (`yyyy ddd hh:mm:ss.mmm.uuu`), keep file (save permanently), info
buttons (format, reference, TimeTag, Unix time help texts).
Open failure: `Error opening selected import file.\nMake sure file is not open elsewhere.`
Cancel asks `Abort import?`.

**FR-IMP-003 (delimiter guess)** Examine (up to) the last 5 of the displayed lines: count lines
containing `,` (comma), else containing tab, else space. Comma wins if strictly greater than both
others; tab likewise; otherwise space ("any whitespace").

**FR-IMP-004 (record parsing)** A record has trailing CR/LF/TAB stripped, leading spaces and `"`
removed, trailing spaces removed; fields are split at the delimiter; `"` inside a field truncates it;
leading spaces/quotes of each field are skipped; a trailing delimiter (non-space) adds an empty
field; empty fields are counted as null fields.

**FR-IMP-005 (header detection)** After skipping `skip rows`, a record is a **data record** if every
field is non-empty and (`atof ≠ 0` or starts with one of `0 . + - #` or equals `NaN`); otherwise it is
a header line. The first data record determines the parameter count N. In it, the index column value
determines the reference time:
* contains `:` or `/` → clock `[ddd/|ddd ]hh:mm[:ss[.mmm[.uuu]|.ffffff]]` or TimeTag `ddd/msec[.usec]`
  → reference GMT, TimeTags enabled;
* numeric > 485,438,400 → Unix epoch seconds (UTC) → reference GMT, Unix-time mode, TimeTags enabled;
* otherwise relative seconds; the default reference is the current local date/time.
Header lines are assigned in order: **names** (first header line with N non-null fields, searching
later header lines if needed; else none), **units**, **descriptions**, **alternate names** — each only
if it has exactly N fields. Missing names → `Column<n>` (1-based), missing alternate → name, missing
units/description → `" "`. The title is the `title row` line (leading spaces/quotes trimmed, max 79
chars) or the source file name.
If N = 1 an index column is generated (`Index`, `Count`, `Data Point Index`, values 0..n−1).
(An index is generated only when N = 1, even when the first data field is not numeric.)

**FR-IMP-006 (data conversion)** For each data record (up to the counted points; a line of ≤ 2
characters ends the data):
* index column: Unix epoch → TimeTag (UTC) and relative = TIME(tag, ref); `ddd/hh:mm…` → clock with
  day (day roll-over handled: day increase adds days, day decrease = year wrap); `ddd/msec.usec` →
  TimeTag; `hh:mm…` → seconds since midnight relative to the first record; plain number → relative
  seconds as given (not referenced);
* data column: empty → dropout; text starting `N`, `-N`, or `1.#QNAN` → dropout; else `atof`.
* If TimeTags are generated and the record had no absolute time, TimeTag = GMT(relative − base, ref).
If the index column is not 1, column data and nomenclature are swapped so that time becomes column 1.
On 32-bit limits or allocation failure the point count is reduced by 2 % steps with the message
`Unable to process complete file\nPoint count reduced from %d to %d` (OK/Cancel).

**FR-IMP-007 (output file)** Default output = `<temp>\<source file name>.~NNN` (NNN = import counter),
deleted when files are closed. Format: **SUN_MULTITIME64** (default; time not stored as a separate
parameter; each parameter LZH-compressed with float time or TimeTag time stamps) or, with *standard
format* / `/F`, **SUN_COMMONTIME** (TIME as parameter 0, optional `Index`, optional `GMT`/`TimeTag`
parameter at position 1). Header: reference time from the reference field, cutoff 0, title, comment
`Imported by ` + product version (e.g. `Imported by Multiplot.NET v1.0.0`). With TimeTags, the comment block gets
`[events]\nFILE_REF=ddd/msec.usec`. *Keep file* asks for a destination (`*.sun`, initial folder last
used or ExportPath) and renames the temporary file. Finally the file is opened as a group (FR-LOAD).
Import failures show the dialog's status message (`Unable to determine parameter values starting
record (invalid content).`, `Failed to determine parameter count.`, `Scan failed`, `Import aborted.`).

**FR-IMP-008 (recording)** A completed import records
`import "<src>"[ "<dst>"][ /In][ /Sn][ /Tn][ /G][ /F][ /R<ref>]`.

*Acceptance (import):* TST-IMP-001 — each import fixture (comma, tab, space; relative, clock,
TimeTag, Unix epoch; with/without header rows; NaN/empty fields) produces a SUN file whose parameter
values, names and times equal the golden results (compared via export).

---

## 10.2 Value formatting (shared by export, tabular display and statistics)

**FR-XPT-001 `FormatValue(value, format, exp)`**: if `(float)value` has the dropout bit pattern →
`DROP_OUT`; else by format: HEX2 `0x%02x` of `(uint32)value & 0xFF`; HEX4 `0x%04x` (& 0xFFFF); HEX8
`0x%08x`; OCT3 `0d%03o` (& 0xFF); OCT6 `0d%06o` (& 0xFFFF); OCT12 `0d%012o`; GENERAL (−1) and any
other value `%.17g` of the **double-converted** value; 0..7 → `%.<n>f` or `%.<n>e` when exponential
format is selected. Output must be byte-identical to the MSVC CRT `printf` family (invariant culture,
same rounding).

## 10.3 ASCII export

**FR-XPT-010 (entry points)** *File → Export* (save dialog `Plot%04d.csv` in ExportPath, filter CSV),
`EXPORT [file]` (comma), `EXPORT CLIPBOARD`, *Tabulate* (`TABULATE`: tab-delimited to
`<temp>\PlotNNNN.tab`, opened with the configured *View application*; error `Define Tab Viewer
application (Options->External Applilcations)` if none), *Export application* (comma file opened with
the configured export application). Relative output names go to ExportPath, else temp.

**FR-XPT-011 (layout)** The ASCII export output shall be byte-identical to the golden files for the
same plot state. Rules:
* End-to-end plots prompt `Tabulation of End-to-End plots will include full parameter data sets with
  actual time stamps.\n\nUse standard axis mode for zooming sub-sections of data.` (OK/Cancel).
* **Columns**: per curve, optionally a GMT/clock column (if INCLUDEGMT or clock time), a relative time
  column, for parameter-vs-parameter an X column, then the value column. Time columns are emitted for
  every curve ("multi-time") unless *SharedTabTime* is on and all curves have identical in-range
  point counts and start times (then only the first curve has time columns).
* **Header rows** (each only if the corresponding Y nomenclature flag is set): DBNAME → group names;
  FILENAME → source file paths; NAME → time stamp name(s), X name, curve names (leading `$`
  removed); UNITS → units (left-trimmed); DESC → descriptions (scale-group suffix removed).
* **Rows**: for normal plots only points within the current X range (tabular style uses the fixed X
  range) starting at each curve's first in-range point; rows continue while any curve still has
  in-range points; a curve without a point in a row writes `" "` placeholders.
* Time values use the time precision; values use the data precision (tabular plots: each trace's tab
  format; Y nomenclature EFORMAT → exponential). Clock columns use `%03d/%02u:%02u:%02u.%03u.%03u`
  when a valid GMT array exists, else the plot GMT format computed from reference + relative time
  (minus applied time shift); unavailable → `n/a`.
*Acceptance:* TST-XPT-001 — golden CSV/TAB files for a matrix of plot states (normal, zoomed,
multi-file, parameter-vs-parameter, end-to-end, shared time on/off, GMT on/off, each precision).

**FR-XPT-020 (SUN export, `SAVESUN`/*Save as SUN*)** Writes the active plot as **SUN_MULTITIME**
(`Plot%04d.sun` default): title = plot title, comment `Plot data from Multiplot display`, reference time
and cutoff from the first curve's file. Comment block:
```
[events]                      (only when all curves share a GMT reference with TimeTags)
FILE_REF=ddd/msec.uuu
[history]
Data created <plot date>
*note that parameters may not be referenced to same times or file header reference
pos(<i>) <name> source=<file> Tref=<clock of curve reference>
```
Each curve → parameter (name without `$`, alternate name `CurveNNN`, units, description, sequence
number), float data, time stamps as TimeTags (when all curves share the GMT reference) else float
relative times, LZH-compressed, native byte order.

**FR-XPT-021 (parameter/file details)** `SAVEPARMLIST`, `SAVEPARMDETAILS`, `SAVEFILEDETAILS` and the
File/Parameter Details dialogs' Save/Copy/Copy-all produce the text formats specified in FR-SCR-157..159.

## 10.3a Parquet archive conversion

**FR-XPT-050 (multi2parquet)** The command-line tool `tools/MultiToParquet` (`multi2parquet.exe`) shall convert
a SUN_MULTITIME, MULTITIME64 or `_SRT` file to an Apache Parquet file in the `multiplot.timeseries.wide/1` layout
of `docs/parquet-archive-format.md`. That layout has an INT64 `time_ticks` column (.NET ticks, UTC, ascending) and
one nullable DOUBLE column per parameter. Null means "no sample" and NaN means dropout. The header, parameter map,
events and verbatim comment block go into `multiplot.*` key/value metadata.

* The tool reads the stored samples only, at the stored precision. It does not add display points: no EOF hold,
  no window clamping and no full-rate rebuild.
* The year comes from `refTime[0]`, or from `--year`. Each time stamp is placed in a year with the ±180-day rule
  of FR-TIM-001.
* Defaults are Zstandard compression at the smallest-size level, DELTA_BINARY_PACKED for the time column and
  1,048,576 rows per row group.
* These parameters are skipped and listed in `multiplot.skipped`: `v1` data, `t8` data values and image/`p4`
  data values.
* The tool warns about out-of-order time stamps (it sorts them, keeping the order of equal times), about 64-bit
  integers above 2^53, and about packed time stamps that decode to a day outside 1..366 (OQ-016). Those stamps
  are exported as decoded.
* The `info`, `verify` (bit-for-bit re-read), `crosscheck` (against the SUN reader) and `synth` (fixture writer)
  commands support testing.

*Acceptance:* TST-XPT-050.

## 10.4 Images

**FR-XPT-030 (Save Picture)** *Edit → Save Picture* lists the supported raster formats
(BMP, JPEG, GIF, TIFF, PNG), defaulting to the last used, file name
`plotNNNN.<ext>` in ExportPath; renders the active plot at its current window size (WYSIWYG) and
saves. Scripts: `SAVEPIC`, `COPYBMP`, `COPYJPEG` (FR-SCR-153/154). *Copy as bitmap* puts the plot on
the clipboard. Auto-save JPEG (Ctrl+J) saves `plotNNNN.jpg` without dialog; `Nothing to copy` when
no plot. EMF/metafile output is out of scope (DEV-XPT-001).

## 10.5 Statistics (slice average / DATASTATS)

**FR-XPT-040 (computation)** For each curve, over points with `xmin ≤ t ≤ xmax` (current X scale, or
the dragged slice range) and value ≠ dropout: min, max, average = Σv/n, range, sum, n,
sigma = sqrt(max(0, (Σv² − n·avg²)/(n−1))) (n ≥ 2, else `n/a`), CV = sigma/avg (`n/a` if avg = 0),
TI = trapezoidal integral Σ (v[i−1]+v[i])/2·(t[i]−t[i−1]) over consecutive *included* points using the
raw previous array element (quirk, DEV-XPT-002). No points → `No Data` then `*`.
Values are formatted with FormatValue as float.

**FR-XPT-041 (DATASTATS file)** `DATASTATS file` (no path → ExportPath) writes:
`Time range:  %.3f   to   %.3f` (or clock `ddd/hh:mm:ss.mmm   to   …` when clock time is on), blank
line, header `Source,Parameter,Min,Max,Average,Range,Sigma,CV,TI`, then one line per curve:
`<file name>,<curve name>,` followed by 7 values each followed by `,` (data precision).
Open failure: `Slice Average failed to open <name>`.
`DATASTATS` without a file opens the Slice Average dialog for the visible X range.

**FR-XPT-042 (Slice Average dialog)** Shows `Range:  a  to  b  (d sec.)` (clock form when clock time
is on) and a list with columns Source, Parameter, Minimum, Maximum, Average, Range, Sum, #points, Sigma,
CV, TI; precision combo (General, 0..7); *Show column average* adds a final row `All Parameters` /
`Column Avg.` (averages of the column values present); column-header click sorts (text columns
case-insensitive, numeric columns by value, toggling direction), **Ctrl+click** copies the column to
the clipboard; *Save* writes `plotNNNN.avg.csv` (comma, or tab when not `.csv`) with header
`Source,Parameter,Min,Max,Average,Range,Sum,#Points,Sigma,CV,TI`; *Curve Fit* fits a polynomial of the
chosen order (combo) to each selected curve over the slice and, after confirmation (`Plot equation?`
with the fit description), adds it as curve `PolyFit(<name> <order>)`; failures `No parameter(s)
selected.\nMake selection from list.` / `Failed to calculate curve fit.`. Printing is out of scope.
