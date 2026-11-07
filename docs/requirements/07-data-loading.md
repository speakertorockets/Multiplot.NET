# 07 — Files, Groups, Time Slices, References and Data Loading

---

## 7.1 Open files and file groups

**FR-LOAD-001 (model)** The application holds an ordered list of open **files** (max 1000) partitioned
into ordered **groups** (max 1000). Each group has: definition file name, display name, engine serial
number (first non-blank serial in the group, 12 chars, default `unknown`), cutoff (first non-zero,
`%.3f`, default `unknown`), and its own time slice `{start, stop, reference}` initialised from the
current global time settings when the group is created.

**FR-LOAD-002 (open dispatch)** `Open(spec)` (menu, script `OPEN`, drag-drop, command line, recent
list) shall:
1. remove `"`; if spec contains `,` → open each comma-separated item separately (each becomes its own group);
2. refuse when 1000 groups exist (`Max number of file groups reached!`);
3. if the extension is **not** `.flt`: open the file (as given, else by file name on the `DBpath`
   search list); if the first 4 bytes are not a SUN signature and are printable ASCII (CR/LF treated
   as spaces) treat it as a **group file** (FR-LOAD-003);
4. if the extension is `.flt` → **filter file** (FR-LOAD-004);
5. otherwise open it as a single data file (FR-LOAD-005) forming a one-file group named after the
   file (name without path).
After a group loads, if `EMBEDDEDSCRIPT ON`, every file's comment block `[script]` section is run.
Imported temporary files (`*.~*`) have the `.~…` suffix removed from the group name and are deleted
when files are closed.

**FR-LOAD-003 (group file — "DB_DIR" text format)**
```
<group display name>
<N>                      ; decimal, 1..1000
<file path 1>
...
<file path N>
```
Each path is opened per FR-LOAD-005; the group exists if ≥ 1 file opened. A group file whose name
contains `~Multiplot_` adds each member to the recent-files list. Temporary group files created by
`OPEN … ASDATABASE` follow this format (FR-SCR-120).
Owner scope note: DB_DIR *browsing/query* is out of scope; reading this text format is retained
because `ASDATABASE` and existing scripts depend on it (OQ-002 confirms).

**FR-LOAD-004 (PC GOAL filter files `.flt`)** The name may carry a channel prefix in brackets
(`[ch]name.flt`). The file is scanned line by line (trimmed). A line containing `[FILTER]=name`
starts a sub-filter; following non-empty lines not starting with `;` are copied into a temporary file
`<temp>\<name sanitised>.~.flt`; each sub-filter with ≥ 1 line is opened as a data source
(with the channel prefix inserted before the file name; the first opened source's channel is reused
for subsequent sub-filters when none was given). The group name is the `.flt` file name without
extension. The `.flt` path is searched on `FilePath` if not found directly.
Interpretation of the filter body is done by the stream data-access plugin — see FR-RT-040 and OQ-001.

**FR-LOAD-005 (single data file)** Ask the data-access layer to open the file (all registered
providers, FR-ACC); if that fails, retry with the file name searched along `FilePath` (preserving a
`[ch]` prefix). On failure show `Open failed for <name>\n\nUnsupported format.` (status 0) or
`…\n\nStatus: <error text>`; in a script the box has OK/Cancel and Cancel aborts the script.
On success: add to recent files (when requested); if the file is dynamic (or a stream) and auto refresh
is off, turn auto refresh on (FR-RT-001); mark "real-time data loaded".

**FR-LOAD-006 (close)** *Close all files* closes all plot windows first, frees all files, deletes
imported temporaries, clears the real-time flag and the real-time clock pane, and records
`CLEAR FILES`. *Close last* closes only the most recently opened file/group (`CLEAR LAST`).

**FR-LOAD-007 (drag & drop)** Dropped files are handled by extension: `.wps` run script (refused while
recording), `.csv` import, `.ini` load settings (recorded `loadsettings`), `.wpd` plot the display
(recorded `PLOT`), `.markers` load markers (recorded `markerset load`), anything else open as data
(recorded `OPEN` with `;` escaped as `\;`).

**FR-LOAD-008 (recent files)** Up to 10 recent files/groups in the File menu, persisted in the
registry (09-settings).

---

## 7.2 Time slice and reference

Every group has a **start**, **stop** and **reference** string. Times are seconds relative to the
reference. The global defaults are set by the Time Slice dialog or `TIME` (FR-SCR-131).

**FR-LOAD-020 (reference values)** The reference string `ref` is interpreted as:

| `ref` | Meaning | Resolution |
|---|---|---|
| `NONE` | file time base | TIME as stored in the file (file reference GMT; for streams `STREAMnnREF`) |
| `START` | t = 0 | relative offset 0 |
| `SHUTDOWN`, `C`, `CUTOFF` | engine cutoff | relative offset = cutoff of current file, else first non-zero cutoff in the group; a 0 cutoff on a dynamic file is "not found" |
| `EOF` | end of file | relative offset = max TIME of the file for COMPRESSED/UNCOMPRESSED/CIRCULAR/COMMONTIME, else +3e38; for streams the stream's current GMT |
| number | constant offset | relative offset = value (must be a valid constant) |
| `ddd/msec[.usec]` | explicit GMT | TimeTag |
| `[ddd/]hh[:mm[:ss[.mmm[.uuu]]]]` | explicit GMT (contains `:`) | TimeTag; day defaults to the file header day |
| event name | file event | case-insensitive match in the current file's `[events]`, else in other files of the group (unless *strict* mode and the current file has no `t8` GMT index) |
GMT-type references access data by GMT (FR-SUN-050); offset-type references access by relative TIME.
If neither resolves: `Reference Event, <ref>, not found\nfile: <file>\ngroup: <group>` and the file
is skipped. Option `FIRSTOCCURRENCE` affects stream parameters (FR-LOAD-032).

**FR-LOAD-021 (start/stop values)** A start or stop string is `base[(+|-)offset]` (spaces removed;
an offset is recognised only after the first character). `base` may be anything accepted for `ref`
(GMT, event, START, CUTOFF, EOF, number). Conversion to seconds relative to `ref`:
* base resolves to a GMT: time = GMT(base) − GMT(ref) (ref `NONE` → file reference GMT). If ref is not a
  GMT, the GMT is located in the file's GMT index and converted to the TIME value at that point,
  then the relative `ref` offset is subtracted (errors −2 / −4 when impossible).
* base is relative: time = value; if ref ≠ `NONE` and base is not a plain number, subtract the ref
  (for a GMT ref: subtract GMT(ref) − GMT(`START` event)…).
* finally add the offset.
Invalid combinations report `Invalid Start/Reference time combination\nfor <group>` or `…Stop…`
(OK/Cancel; Cancel aborts the load) and the group is skipped.
*Acceptance:* TST-LOAD-001 — a table of (start, stop, ref) triples against fixture files with events
reproduces the golden slices exactly.

**FR-LOAD-022** Default global time settings come from the INI `[access time]` section (09-settings).
The Time Slice dialog lists `NONE`, `START`, `EOF` and all event names of open files for the
reference, and `EOF` plus event names for start/stop; `TIME MULTI` opens a per-group dialog.

---

## 7.3 Loading parameters into a plot

**FR-LOAD-030 (Plot command)** *Plot* (menu/`PLOT`): refuse re-entry while loading
(`Can't execute, data/file load in progress`); if no files are open and dialogs are allowed open the
File Open dialog; if the Y list is empty and dialogs are allowed open the Parameter Select dialog;
then load and display. Auto refresh is paused during the load and restored afterwards.

**FR-LOAD-031 (load algorithm)** For each group in order: compute the slice (FR-LOAD-021); if X is
not `(default)` resolve the X item (FR-EXP-040); then for each Y list item (FR-EXP-001) and each file of
the group, produce at most one curve (FR-EXP-003); stop at 1000 curves. Loading requests float data
(and GMT arrays when 64-bit time is on). Curves get default attributes: line size = current, solid,
symbols = current, no scatter, symbol id = colour index = curve number mod 100, tab format = current
data precision, tab legend on, logic labels = current default.

**FR-LOAD-032 (stream first occurrence)** With `FIRSTOCCURRENCE ON`, once a list item has been loaded
from a stream source it is not loaded from further stream sources.

**FR-LOAD-033 (results)** If no curve was produced: if an error text exists show
`Parameter(s) not available for time requested\nor not found in file group\n\n<errors>`, else
`Parameter(s) not available\nX=<x>\nY=<y>\nStatus: <library error text>`; when the cause was the time
slice: `Data not available for requested time slice.` + (`\nExtrapolation is currently OFF.` when off).
If the user cancelled, all loaded curves are discarded.
**FR-LOAD-034 (plot creation)** On success a new plot window is created with: the curves, a copy of
the current plot configuration, the master marker set (if markers enabled), X nomenclature (X
parameter's name/units/description, or the time stamp nomenclature of the last curve), current
titles/udef texts, the first group's reference and slice string `start,stop,ref`. Plot numbers
increment per created plot. Auto-labels (if enabled): end-to-end → X udef `Accumulated Time Seconds
(<start> to <stop>)`; parameter-vs-parameter → ` (<start> to <stop> seconds[ from <ref>])`.
X vs X end-to-end is not allowed (end-to-end turned off).

**FR-LOAD-035 (placeholder)** With `USEPLACEHOLDER ON` and a script running, when a plot cannot be
produced a placeholder plot is created from the constant expression `-1.40130E-45` (a dropout) named
`* Placeholder for {<Y list>} *`, tabular style, frozen (image markers no longer exist, so no `NoData.bmp` image is shown). If even
that fails: `Failed to generate data for Placeholder
plot.\nVerify validity of file parameter 1.` with Abort (abort script) / Retry / Ignore (turn script
prompting off).

**FR-LOAD-036 (Add Parameter)** *Add Parameter* (and `ADDPARAMETER`) loads a list into the active plot.
Interactively it opens Parameter Select and, for tabular plots, uses the mouse click position
(converted to the 4096×4096 layout grid, snapped to the layout grid if enabled, Y flipped) and then
opens Trace Attributes. A list item `NAME@x:y` places a single tabular item at (x,y).
Rescale: end-to-end → full; else within current X range.

**FR-LOAD-037 (remembered parameters)** Each plotted Y list (except empty, `(default)`, `TIME`) is
pushed onto a most-recently-used list of 64 entries (duplicates removed), shown in Parameter Select
and persisted in the registry (`Recent Parameters`).

---

## 7.4 Display files (`.wpd`)

**FR-LOAD-040** A Y list ending in `.wpd` (case-insensitive) loads a **tabular display definition**
(file opened as given, else searched on `WPDpath`). Lines (trimmed, max 1023 chars):

| Line | Meaning |
|---|---|
| `; …` | comment |
| `bitmap <file>` | background image (searched on WPDpath if not found) |
| `coloredtext ON|OFF` | legend/value text in curve colour |
| `ynomen <items>` | Y nomenclature (FR-SCR-230) for this display |
| `display <item>@<x>,<y> [COLOR_INDEX=n] [TAB_LEGEND=0|1] [TAB_FORMAT=fmt]` | item to load (expression, function, `file:name`, …) positioned at (x,y) on the 4096 grid; must contain `@` and `,` |
| `marker <timestamp>,<text>[,TOP\|BOTTOM]` | as `MARKER` arguments (FR-SCR-240), added to the master set used for the display; invalid → `Invalid marker command in WPD file\nCommand: <line>` |
| `redline <value>,<text>[,LEFT\|RIGHT]` | as `REDLINE` arguments (FR-SCR-241), added to the master redline set used for the display; invalid → `Invalid redline command in WPD file\nCommand: <line>` |
All `display` items form the Y list; after loading, each curve is matched to the first unused display
line by name (case-insensitive) or, for `file:name` items without brackets, by file/group name match,
and receives its position and options. The plot is forced to tabular style. The global marker and redline sets and
Y nomenclature are restored after the WPD plot is created. Failure to open:
`Failed to open Multiplot display file (.wpd)`.
`COLOR_INDEX=` and `TAB_FORMAT=DECn` are applied as documented — see DEV-TAB-001 / OQ-008.

**FR-LOAD-041 (WPD save)** *Save display* writes the active tabular plot as a `.wpd` (default name
`plotNNNN.wpd` in ExportPath) including `ynomen` with item lengths (`NAME:n + ALTNAME:n …` format),
`display` lines with positions and attributes, background and markers, such that re-loading reproduces
the display.

---

## 7.5 Favourites

**FR-LOAD-050** The favourites file (path in INI/registry, default `Multiplot.favorites` in the user folder) is
a text file; each line: `[name=<label>] [yaxis=<list>] [xaxis=<x>]` (keys found by substring, values
trimmed; a line without `name=` is displayed verbatim). The Favourites dialog lists labels; choosing one
sets Y (and X, default `(default)`) and optionally plots, optionally using the label (quoted) as the
plot's caption/title. The dialog allows browsing for another favourites file.

---

## 7.6 Marker files (`.markers`)

**FR-LOAD-060 (read)** Text file, one event marker per line in the `MARKER` argument form
`<timestamp>,<text>[,TOP|BOTTOM]` (FR-SCR-240), or one redline marker per line as
`redline <value>,<text>[,LEFT|RIGHT]` (FR-SCR-241; keyword case-insensitive; added to the master
redline set and the target plots like the event markers). Blank lines and lines starting with `;` or `#` are
ignored. Invalid lines — including lines of the retired format `Marker = n valid type x1 y1 x2 y2
color label` — are skipped and reported once: `<n> invalid line(s) in marker file skipped (first:
line <k>).\nExpected: <timestamp>,<text>[,TOP|BOTTOM]\nor: redline <value>,<text>[,LEFT|RIGHT]`. The markers read are added to the master set
and to the active plot (all plots with apply-global); identical markers are not added twice.
Failure: `Failed to open 'Marker' file`.
**FR-LOAD-061 (write)** Writes a `;` comment header line, then every marker of the active plot (or the
master set if none) as `<timestamp>,<text>[,BOTTOM]\r\n`, then every redline as
`redline <value>,<text>[,RIGHT]\r\n` (value in round-trip invariant format).
