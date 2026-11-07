# 08 — User Interface, Plot Windows, Markers and Tabular Displays

Menus, plot popup menus and command prompts are listed in `src/Multiplot.App/MenuDefinitions.cs`
and `src/Multiplot.App/CommandPrompts.cs`.

Rendering need not be pixel-identical between renderers (DEV-UI-001) but layout rules,
content, colours, scale values and interaction semantics listed here are mandatory.

---

## 8.1 Main window

**FR-UI-001 (shell)** A single main window hosting many **plot windows** (MDI children).
The WPF application shall provide an MDI-equivalent document area that supports: overlapping child windows,
maximise/minimise/restore, cascade, tile horizontally, tile vertically, stack horizontally, tile
across *N* monitors (`Monitors` setting), keep-on-top, move, close, close all,
next/previous (Ctrl+Tab / Shift+Tab), select from list (*Window → Select*), auto-tab on a timer (all
plots). Plot group names (Set Group Name, Tile By Group, Auto Tab By Group) were removed. New plots open maximised if *Maximized Plots* (`PlotMax`) is on.

**FR-UI-002 (menus)** The main menu shall contain these items (same order, captions and check
states) except out-of-scope items (print, EMF, Query, external command polling), which
are omitted (DEV-UI-002). Structure (`IDR_MAINFRAME`):
* **File**: Open (closes the open files first, `ID_FILE_NEW`), Add (`ID_FILE_OPEN`), Import Tabular, Recent Files ▸, Close ▸ (Last, All), Properties, Exit.
* **Edit**: Copy to Clipboard ▸ (as BMP, as Text), Copy to File ▸ (as Picture, as Text, as WPD, as Sun),
  Import from Clipboard, Export to Application, Symbolic Names, Tabular Display Layout ▸ (Alignment
  Grid, Snap To Grid).
* **Script**: Execute, Record, Edit, GoTo Next, Pause, Abort, Pause Any Command (✓), Allow Prompt and
  Error Dialogs (✓), Log Script Errors, Script Placeholder Plot, Enable File Embedded Scripts.
* **Parameter**: Selection, Favorites, Filter Helper, Add to Plot, Select WPD, Parameter Properties.
* **TimeSlice**: Global Time Slice, Per Database, Alignment Tool, Freeze Plot.
* **View**: New Plot, Tabulation, Presentation Mode ▸ (Enable, Full Screen), Appearance ▸ (Light, Dark) (FR-UI-046). (File Properties is in the File menu
  only; Parameter Properties moved to the Parameter menu.)
* **Axis**: Multi Y ▸ (On, Group By ▸ Independent/Name/Units/Description/User Defined), End To End,
  Log X, Log Y, Auto Scale X (Time Axis) ✓, Auto Scale Y (Value Axis) ✓, Auto Range X/Y Once (Home),
  Fix X, Fix Y, Revert Scales (Backspace), Zoom In/Out X (Insert/Delete), Zoom In/Out Y
  (PgUp/PgDn), Pan Left/Right/Up/Down (arrows), 3D Z Axis Angle.
* **Style**: 2D Line, 2D Scatter, 3D Line, 3D Area, Logic, Tabular, Trace Attributes, Data Rate ▸
  (Set Mode, Density ▸ Average/Sample/3D Segments, Tabular Step), Colors ▸ (Line, Label, Title, Grid,
  Marker, Redline, Background, Restore), 2D Line Size, 3D Line Size, Grid Lines, Minor Tics, Bold Frame, Stair Step,
  Symbols ▸ (Show, Density, Size ▸ Small/Normal/Large), Legend ▸ (Show, Set Columns, Fixed Parameter
  Space, Minimum Grid Offset, Multi-Y Shift Left, Text in Curve Color).
* **Nomenclature**: Fonts ▸ (Labels..., Titles...), X Axis/Legend Labeling, Y
  Axis/Legend Labeling, Set Title, Set Sub-Title, Show Time as GMT/Clock, Set GMT Format, Scale Group
  ID, Test Info (Test Title), Set Plot Window Title... (Version, Script Name, User Name, Page Number,
  Time Stamp and Set Group Name were removed).
* **Markers**: Add Event Marker... (Ctrl+D), Edit..., Add Redline Marker..., Edit Redlines..., Show File
  Events, Topmost, Marker Color..., Redline Color..., Clear All, Save, Load (FR-MRK).
* **Options**: File Paths, External Applications ▸ (Assign, Include GMTs), Tabular Output/Exports ▸
  (Data Format, Time Format, Shared Time), Expressions/Calculations ▸ (Align to Closest Point, Perform
  Every Unique Time), Draw Mode ▸ (Optimized, Offscreen), Average Function ▸ (Default Rate, Time Stamp
  Mode), Right Button Click, Show System Load, Show Coordinates, Show Status Bar, Maximized Plots, Load Symbolic Names, Number of Monitors, Save Window Position, Exit Safety
  Prompt, Continue Titles, Continue Sub-Titles, Continue Markers, X Axis Auto Label, Apply Changes
  Globally, Window Caption ▸ (Disable, Source), Save Settings, Load Settings,
  Import WinPlot Settings (FR-CFG-050).
* **User**: 1..5 (user-defined labels), Assign.
* **Data**: Rebuild Full Rate, Extrapolate End Points, Use 64 Bit Timing, Dropouts ▸ (Ignore, Show
  Markers), Real-Time ▸ (Refresh, Auto Refresh, Ignore Static Data, First Occurrence,
  Auto Run, Auto Data Compact, Auto Data Discard, Show Stream Certification), Tabular Options ▸
  (Maintain History, Default Time Stamp), plugin commands, Manage Plug-ins.
* **Window**: as FR-UI-001. **Help**: User Guide (opens `docs\user-guide\README.md`, shipped beside the
  executable, in the default web browser: the http handler, else Microsoft Edge, else the file
  association), Window Position, About Multiplot.
The Axis menu no longer has *Auto Scale REDLINEs*: redline markers (FR-MRK-010) are not part of
autoscaling.
Every toggle shows its check state for the active plot (or global default when no plot is active);
with *Apply Changes Globally* the action applies to all plots.

**FR-UI-003 (accelerators)** Arrows = pan (Shift/Ctrl variants identical); PgUp/PgDn = zoom Y in/out;
Insert/Delete = zoom X in/out; Home = auto range X/Y once (FR-PLT-017); Backspace = revert zoom; Enter = Parameter
Selection; Esc = toggle presentation mode; Ctrl+Esc = break-out (stop current long operation);
Space = script pause/resume, Ctrl+Space = resume; Ctrl+Tab / Shift+Tab = next/previous window;
Ctrl+D = Add Event Marker; Ctrl+O = File → Open; Ctrl+J = save JPEG; Ctrl+R = refresh windows; Alt+F1..F5 = user buttons
1..5; Ctrl+Alt+Home = bring main window to the primary screen *(inferred from `ID_WINDOW_BRINGHOME` — verify)*; (F1 help and Ctrl+E/Ctrl+P EMF are out of scope).

**FR-UI-004 (no toolbars)** The main window has no toolbars, so that the plot area gets the
screen space; every command is reachable from the main menu, a plot popup menu or an accelerator.
(The former toolbar-only buttons were either out of scope — print, print setup, colour print,
landscape, margins, copy, EMF aspect, external command poll, query, help contents — or duplicated by
a menu: the Multi-Y group-by picker by *Axis → Multi Y → Group By ▸*.) Toolbar settings in INI files
and the registry are ignored (FR-CFG-012, FR-CFG-030); the user buttons stay in the *User* menu.

**FR-UI-007 (first plot)** After an interactive file open (*File → Open*, *File → Add*, *Recent Files*,
drag-and-drop of a data file) that leaves data files open but no plot window, the Parameter
Selection dialog opens (FR-LOAD-030). Not while a script runs, in hidden mode or for files given on
the command line.

**FR-UI-005 (status bar)** Panes: message text (help/coordinates/progress), CPU load %, memory load %
(updated by a timer when *Show System Load*), current real-time GMT. Messages used by operations
(e.g. `Initializing <file>`, `Retrieving…`, `Calculating expression …`, `calculating spline (%n)`,
`Paused n...`) shall be shown.

**FR-UI-006 (presentation mode)** Presentation mode hides menus, status bar and window
borders (Esc toggles); *Full Screen* additionally maximises to the whole monitor (`MaxPresentation`).
Caption bars of plots can be disabled (`NoCaptionBar`).

**FR-UI-007 (window captions)** Plot caption source (`CaptionSource`): parameter set (default), plot
number, title, sub-title, script name, or the plot window title (user text, *Set Plot Window
Title...*, `CAPTION USER text`).

**FR-UI-008 (busy behaviour)** Long operations (loading, expression evaluation, spline, import, export,
refresh) run asynchronously with progress in the status bar and a wait cursor; the UI shall not
freeze; re-entrant load commands are refused with `Can't execute, data/file load in progress`.

---

## 8.2 Plot window layout

**FR-PLT-001 (regions)** From top to bottom/left to right:
* **Title** (X title, top centre, title font, title colour). `\n` splits into two centred lines;
  text after `\t` is a **footer title** at the bottom centre (also `\n`-splittable).
* **Legend** below the title (if shown): one entry per curve in *C* columns (`LegendColumns`, reduced
  so no column is empty and so rows ≤ 22/16/12/10 for font sizes 1/2/3/4), each entry = line sample
  (4 tick widths, curve colour & style), symbol (if symbols/redline), and the nomenclature string
  (FR-PLT-040); a fixed number of reserved rows may be requested (`ReservedLegend`). Text colour =
  label colour, or curve colour with *Text in Curve Color*. With *Multi-Y Shift Left* the legend
  starts at the leftmost scale.
* **Sub-title** (Y title) rotated 90°, vertically centred at the left; text after `\t` goes to the
  right edge; `\n` gives two lines.
* **Y scale(s)** left of the plot area (one per scale in multi-Y), the selected scale marked `*`;
  in multi-Y with legend, each scale shows the line/symbol IDs of its curves below the axis; with
  *Scale Group ID* the scale group name is printed under each axis.
* **Plot area** with frame (bold if *Bold Frame*), grid lines (solid/dotted/dashed, grid colour; in
  multi-Y the horizontal grid uses the selected scale's first curve colour), major ticks, optional
  minor ticks.
* **X scale labels** and the **X axis label** (X nomenclature string, FR-PLT-040), clock format when
  *Clock Time*.
* **Footer** (bottom line): with *Test Info* on, the test title in the label font at the bottom-left
  corner — the trimmed first line of `FileInfo.Title` of the first curve's source file (for curves
  without one, the first file of the curve's group); one label line is reserved for it. Then, in the
  small font (half the label size), the stream certification text, `    >> Contains time shifted
  parameters`, `    *Data is Averaged to %d points` / `    *Data is Sampled to %d points`. The version,
  script name, page number, user name and creation time stamp are no longer drawn.
Minimum left offset = `GridOffset` % of the width. Text cell height (FR-UI-029) for a font of p points
= max(1.2·p, min(1.5·H, W)·p/500) px (`PlotRenderer.FontCellHeight`): the label font sets the legend,
scale and marker text, the title font the title and subtitle; at min(1.5·H, W) ≈ 768 px the text is
about its point size. Legend rows per column ≤ clamp(160/p_label, 4, 30).

**FR-PLT-002 (title keywords)** Title, sub-title, user-defined (udef) texts and captions support
keywords (case-insensitive), replaced repeatedly until none remain, each value trimmed and `\`
replaced by `/`:
`[title] [comment] [serialnum] [teststand] [testposition] [testtype] [testnumber]` (header fields of
the reference curve's file), `[date]` (`  Month dd, yyyy`), `[time]` (`h:mmAM|PM`), `[parameter]`
(unique curve names, comma-separated), `[pidname] [pidunits] [piddescription]`, `[dbname]` (first
group name), `[timeslice]` (`start,stop,ref`), `[timestart] [timestop] [timeref]`, `[filename]`
(file name without path and import suffix), `[scriptname]`, `[currentvalue]`, `[currenttime]` (last
point, data/time precision), `[groupname]` (empty: plot group names were removed). An empty result
becomes `" "`.

**FR-PLT-003 (plot creation stamp)** Each plot stores its creation date/time string (`plotDate`) used
in the footer and in SUN export history.

---

## 8.3 Scaling — must be numerically exact

**FR-PLT-010 (auto scale)** For a range [min,max] and pixel extent: (1) unless clock or fixed, round
min and max to 7 significant digits (`_gcvt`); swap if reversed; (2) if exactly [0,1] remember
[−0.1, 1.1] as the data band; (3) if range < 3.4e−37 → ±0.1; (4) if range > 0.8 → floor(min),
ceil(max); else subtract the integer part and scale the fraction by 10^(exp+2) (`fraction()`),
ceil/floor; (5) graduation: for range ≤ 3 choose {0.2, 0.25, 0.25}[clamp(ceil(span)−1,0,2)], else
`scale_factor()`: x = ceil(range/7), base power of 10 of x, first of {1,2,4,5}×10^k ≥ x (3 skipped);
(6) `scale_limits()` rounds min down / max up to multiples of the graduation (integer modulo when
graduation ≥ 1, else fractional part); (7) undo the fraction scaling; (8) up to 3 adjustment steps so
the scale covers the data band; (9) divisions = round(range/grad); if ≥ 8 and even → halve divisions,
double graduation; if < 2 → ×4 divisions, graduation/4; (10) decimals = max(significant decimals of
graduation and of min) (fixed) or exponential digits.
**FR-PLT-011 (fixed scale)** With a fixed increment: divisions = round(range/grad) (+1 if short by
> 1e−4), max = min + divisions·grad.
**FR-PLT-012 (log scale)** min ≤ 0 → 0.001; max ≤ 0 → 1; min/max expanded to decades 10^n
(n ∈ −16..16); divisions = decades; values plotted as natural log; decimals = −minExp (max 7).
**FR-PLT-013 (clock scale)** With clock time and auto X: shift min to a "neat" boundary based on the
range table {0.01,0.1,1,10,60,300,1800,3600,18000,86400,345600,∞ s} with intervals
{1,50,500,1000,5000,60000,300000,600000,900000,3600000,43200000,86400000 ms} and minimum precision of
the GMT format (MIL/SC/MN/HR/DDD); then snap the graduation up to the table {3,5,10,15,30,60,120,300,
600,900,1800,3600,7200,21600,43200,86400,432000,864000,2592000,15552000 s}.
**FR-PLT-014 (zoom stack)** Each axis keeps a 20-level zoom stack (oldest dropped); *Revert*
pops one level; auto-range clears it.
*Acceptance:* TST-PLT-001 — unit tests compare scale outputs (min, max, graduation, divisions,
decimals) with golden reference values for 10,000 random and edge ranges.

**FR-PLT-015 (autoscale sources)** Auto X = union of curve time ranges (end-to-end: total accumulated
duration; *IgnoreStatic* excludes static curves for real-time plots). Auto Y = union of curve
min/max within the visible X range (FR-PLT-017); Home fits Y over all data.

**FR-PLT-017 (autoscale switches)** Each plot has two switches, `PlotConfig.AutoScaleX` and
`AutoScaleY` (defaults for new plots from the session defaults, INI `AutoScaleX`/`AutoScaleY`, both
on; a persistent Fix X / Fix Y range starts the plot with the switch off). Menu items show the
active plot's state with a check mark; `AUTOSCALE X|Y|XY [ON|OFF]` sets them (recorded
`AUTOSCALE X|Y ON|OFF`); `FIXX AUTO` / `FIXY AUTO` = `AUTOSCALE X|Y ON`. Turning a switch on rescales
that axis at once.
* *Auto Scale X* on: the time axis spans all data and, on refresh, extends to the newest data. Off:
  on refresh the visible span is kept and slides so its right edge is the newest data — only if the
  newest data was in view before the refresh; a view of older data does not move.
* *Auto Scale Y* on: the value axes fit the data within the visible time range, after every refresh
  and every change of the time range. Off: the value axes keep their scale.
* Manual ranges switch the axis's autoscale off for that plot: X zoom/pan (keys, wheel, zoom box,
  middle-button pan), Fix X; Y zoom/pan, Fix Y; *Revert Scales* switches both off. A manual X change
  with *Auto Scale Y* on refits Y to the new time range.
* *Auto Range X/Y Once* (Home, `RESCALE`): fit X over all data and Y over all values once, then switch
  both off.
* Plots without a time axis (parameter-versus-parameter, tabular) ignore the switches: a refresh keeps
  their scales (tabular displays still follow the newest time); Home fits them.
* Retired: *Auto Y on Change in X* (`FIXXAUTOY`), *Sliding Scales* (`TIMESLIDE`), one-shot *Auto Range X*
  / *Auto Range Y*; the per-scale "persist" flag no longer decides real-time rescaling.

**FR-PLT-016 (multi-Y)** Multi-Y assigns curves to scales by *Group By*: Independent (one scale per
curve), Name, Units, Description, User-defined (`\GROUP` id); max 10 scales (more → single scale);
disabled when only one curve/scale; logic style forces single scale and no log Y. Clicking in a scale
column selects that axis (`*`); zoom/pan/wheel act on the selected axis, or all axes with **Shift**.

---

## 8.4 Plot styles and curve rendering

**FR-PLT-020 (styles)**
* **2D Line** (default): polyline per curve; *Stair Step* draws horizontal then vertical segments
  (and extends the last value to the right edge); symbols every *SymbolFreq* points (offset per curve
  to avoid stacking).
* **2D Scatter**: point marks (symbol if symbols on, else 1-pixel dots); per-trace scatter flag allows
  mixing.
* **3D Line / 3D Area**: curves offset in depth by the Z angle (`3DZangle`, default 45°; depth ≤ 35 %
  of the plot); each segment drawn as a filled ribbon (area variant also fills to the baseline); width
  divisor from *3D Line Size* (3 thin, 2 medium, 1 wide); data reduced by averaging to *ZaxisFreq*
  segments.
* **Logic**: curve *k* (top to bottom) occupies band [2k+1, 2k+2] on a fixed 0..2N+1 scale; value
  ≠ 0 → high, 0 → low; Y labels show the curve's logic text pair (ON/OFF, OPEN/CLOSED, HIGH/LOW,
  TRUE/FALSE or user labels, FR-SCR-252) in a small font.
* **Tabular**: see §8.7.
Curves are drawn last-to-first (curve 1 on top). Colours: curve colour index (default curve
number mod 100) from the 100-colour table; monochrome mode uses the mono colour.

**FR-PLT-021 (symbols)** Symbol ids 0–12: cross, triangle, square, inverted triangle, left arrow,
right arrow, diamond, wide rectangle, tall rectangle, UL/LR/UR/LL corner triangles; ids ≥ 13 draw a
character: 13–38 → `A`–`Z` (id+52), 39–64 → `a`–`z` (id+58), 65–74 → `0`–`9` (id−17), ≥ 75 → doubled
character (id−10). Size = 0.5 % of min(1.5H, W) (±25 % for large/small).

**FR-PLT-022 (line attributes)** Per trace (Trace Attributes dialog / `SET TRACE`): line size 1–3,
style 1–5 (solid, dash, dot, dash-dot, dash-dot-dot), symbols on/off, symbol id, scatter, colour index,
tab legend, tab format, logic label set.

**FR-PLT-023 (redlines)** Curves from `redline()` are drawn as lines with redline glyphs (MAX: ticks
above, MIN: ticks below) and shown accordingly in the legend.

**FR-PLT-024 (data reduction for display)**
* *Optimized draw* (default): per pixel column keep entry, exit, min, max (and dropouts) of the points
  falling in it, so plotting millions of points is fast and visually exact.
* *Data Rate → Averaged/Sampled*: reduce to *AverageFreq*/*SampleFreq* points across the visible
  range and annotate the footer.
* Full-rate drawing for scatter and parameter-vs-parameter at full rate.
*Acceptance:* NFR-PERF-002 (10 M points redraw < 200 ms).

**FR-PLT-025 (end-to-end)** Groups are laid head-to-tail on the X axis: group *g* is offset by the
sum, over preceding groups, of their longest curve duration plus one sample interval; the X axis label
becomes `Accumulated Time Seconds (<start> to <stop>)` when auto-label is on. Not combinable with
parameter-vs-parameter or clock time.

**FR-PLT-026 (64-bit time)** With *Use 64 Bit Timing* and GMT arrays available, X positions and
expression alignment use double-precision times derived from TimeTags instead of float32 relative times.

---

## 8.5 Mouse and keyboard interaction in plots

**FR-PLT-030 (left drag = zoom box)** In the plot area, drag a rubber-band box (cursor confined to the
plot); release → push current ranges to the zoom stacks, zoom X (unless multi-Y individual-axis zoom)
and Y (single: Y scale; multi: all scales, or only the selected axis without **Shift**) when the box
is larger than 2×2 px and the new range exceeds the minimum (`ZOOMSTOP`); right-click cancels. While
auto refresh is on, zooming makes the scales persistent. Recording writes `fixx a,b` and
`fixy a,b` (multi: `fixy multi a,b,a,…`).
**FR-PLT-031 (left click = curve identification)** A click without drag identifies the curve under the
cursor (by colour) and shows a small popup with `<file>:<curve>` (ParmPopup).
**FR-PLT-032 (right button)** Outside the plot area, in tabular style, with **Alt**, or when the
right-button option is *Popup* (without Shift/Ctrl): show the context menu for the clicked region
(§8.6). Otherwise perform the right-button function (option *RBfunction*, overridden by
Shift = Delta, Ctrl = Slice, Shift+Ctrl = Track):
* **Delta**: rubber line from the press point; status bar shows `(delta) X=…` and `Y=… Slope=…`
  (multi-Y: per curve deltas).
* **Slice Average**: two vertical bars with time labels; on release open the Slice Average dialog for
  the range (FR-XPT-042); a click without movement opens **Data Pick** (values of all curves at that
  time, with options to plot the selection). Not available in end-to-end (`Slice Average not available
  in End-to-End mode`).
* **Track**: a vertical bar following the cursor; status bar `Time: t  name=value …` (N/A outside a
  curve's range).
**FR-PLT-033 (middle drag = pan)** Pan by one graduation per 20 px of movement in X and/or Y (Shift =
all Y axes in multi-Y), using sampled 1000-point drawing while panning.
**FR-PLT-034 (wheel = zoom)** Zoom about the cursor by one graduation (X and Y; multi-Y: selected axis,
Shift = all); disabled for tabular and log plots.
**FR-PLT-035 (coordinates)** With *Show Coordinates*, mouse move shows ` X=%.3f Y=%g` (clock:
`GMT=hh:mm:ss.mmm`; multi-Y: ` name=value` per curve, `CURVEn=` for names > 20 chars) in the status
bar; tabular plots show layout coordinates ` X=n Y=n`.

## 8.6 Context menus (plot regions)

**FR-PLT-036** Regions and menus (`IDR_XHEADING`): **title/legend area** (Set Title, Y Axis
Labeling, Font ▸, Colors ▸, Legend ▸, Presentation Mode, Window ▸,
Properties); **Y scale area** (Set Sub-Title, Font ▸, Multi-Y, Multi-Y Shift Left, Colors ▸, Minimum
Grid Offset, Presentation Mode, Window ▸, Properties); **footer/X area** (X Axis Labeling, Font ▸, Show
Time as GMT, GMT/Clock Format, Test Info (Test Title), Colors ▸, Presentation Mode,
Window ▸, Properties); **plot area 2D** and **3D** (Symbols ▸, Markers ▸, Colors ▸,
End to End, Scales ▸ (Auto X/Y, Auto X, Auto Y, Fix X, Fix Y, Revert), Font ▸, Add Parameter, View
Tabulation, Presentation Mode, Window ▸, plus 3D line widths for 3D); **tabular** (layout, add
parameter, colour text, clock time, …). Popup actions apply to the clicked plot only unless *Popups
Global* and *Apply Globally* are on (never for parameter-vs-parameter or end-to-end plots). The
Window ▸ submenu: Maximize, Minimize, Move, Tile, Set Plot Window Title..., Close.

---

## 8.7 Nomenclature (legend and axis labels)

**FR-PLT-040** The legend entry for a curve (and the X axis label) is built from the selected items in
the configured order (FR-SCR-230): DBNAME (group of the curve's file), FILENAME (file name, import
suffix removed), NAME (`$` removed), ALTNAME, EXTNAME, UNITS, DESC (scale-group suffix removed), REFT
(`(T-Ref: ddd/hh:mm:ss.mmm)` for GMT references, `(T-Ref: %.3f)` for offsets, else `(T-Ref: yy ddd
hh:mm:ss.mmm)` from the header), UDEF (user text with keywords), SEQNUM, CURRENTVALUE (last value, tab
format), CURRENTTIME. Items are separated by two spaces and padded to their fixed widths (character
cells) when lengths are set; fixed-width current values are right-justified in tabular style. The X
label uses the X parameter's nomenclature (or time stamp nomenclature `TIME`/`Seconds`/`Relative Time
in Seconds`). The *Nomenclature* dialog edits items, order (move up/down), lengths, decimal/exponential
format and logic label sets; *Apply all* option.

---

## 8.8 Event markers

**FR-MRK-001 (model)** Markers are event markers only: {timestamp, text, position (top | bottom,
default top)}. Up to 1000 markers per plot (`PlotDocument.Markers`, a list) plus a master set that new
plots copy when *Continue Markers* is on. An identical marker (same timestamp, text and position) is
not added twice. Text, line, box, circle and image markers and the coordinate-based redline markers no
longer exist; value-axis redline markers are FR-MRK-010.

**FR-MRK-002 (timestamps and resolution)** A timestamp is a GMT (`ddd/hh:mm:ss[.mmm]`, `hh:mm:ss[.mmm]`
on the file reference day, `ddd/msec[.usec]`), a number of seconds on the plot's time axis, an
`[events]` name, or `START`/`EOF`/`CUTOFF`, each with an optional `±seconds` offset (FR-LOAD-021). It is
resolved against the plot's time reference on every draw, first in the groups shown by the plot,
then in the other groups (`EventMarkers.Resolve`). Unresolved markers are not drawn and reappear
when they resolve (real-time data reaching a GMT); no message is shown while drawing.

**FR-MRK-003 (file events)** With *Show File Events* (`MARKERSET FILEEVENTS`, INI `ShowFileEvents`,
default on), the `[events]` entries of the files of the plot's groups are drawn as markers with the
event name as text, at the top: SUN files from the comment block `[events]` section, Parquet files
from the `multiplot.events` metadata (both exposed as `IDataFile.Events`). `FILE_REF`, `EOF` and
entries flagged `(invalid)` are not drawn; the same name and GMT is drawn once. Events added by a
refresh of a dynamic file appear at the next draw.

**FR-MRK-004 (drawing)** Each resolved marker inside the visible X range (`GraphLeft..GraphRight`) is
drawn as a vertical line over the full plot-area height in the marker colour (`ColorIndex.Marker`,
FR-MRK-012; mono colour for monochrome plots), with the text right of the line at the top or bottom of the plot area (left of
the line when it would cross the right edge). Markers outside the visible range are not drawn, so
during sliding real-time updates they move with their timestamp and leave the window with it.
Markers are drawn beneath the data unless *Topmost*. Tabular displays, parameter-versus-parameter
plots and end-to-end plots draw no markers.

**FR-MRK-005 (interactive)** *Markers → Add Event Marker...* (Ctrl+D) asks for timestamp (editable
list of the file event names, preset to the centre of the visible X range in seconds), text and
position (the preset is skipped on log-X and end-to-end plots). *Edit...* lists the active plot's markers with Add / Update / Delete / Delete All and
applies the list on OK. When data files are open (and no real-time data is loaded) a timestamp
that does not resolve asks whether to keep the marker. Changes go to the active plot (all plots
with *Apply Changes Globally*) and the master set. *Clear All*, *Save*/*Load* `.markers`
(FR-LOAD-060/061). Recording emits `marker <timestamp>,<text>[,BOTTOM]`, `clear markers all` (Edit
records `clear markers all` followed by the list), `markerset save|load`, `MARKERSET TOPMOST|FILEEVENTS ON|OFF`.
*Clear All* also clears the redline markers (FR-MRK-011).

**FR-MRK-010 (redline markers)** A redline marker is {value (double), text, position (left | right,
default left)} (`RedlineMarker`). Up to 1000 per plot (`PlotDocument.Redlines`) plus a master set
(`Session.MasterRedlines`) that new plots copy when *Continue Markers* is on; an identical redline is
not added twice. Each redline is drawn as a horizontal line over the full plot-area width at the
value, measured on the selected value axis (`YScales[SelectedAxis]`, clamped as for the grid: axis 0
unless multiple-Y is on and another axis was selected by clicking it), with the text above the line
(below when it would cross the top of the plot area) at the left or right end. Mapping as for curves:
`y = GraphBottom − (v − RMin)·Factor`, with `ln v` on log-Y axes (values ≤ 0 not drawn). Redlines
whose value is outside the visible value range are not drawn, so they move with the value scale on
pan, zoom, Fix Y and autoscaling and disappear while the value is out of view; autoscaling does not
include redline values. Colour `ColorIndex.Redline` (FR-MRK-012; mono colour for monochrome plots).
Redlines are drawn with the event markers (beneath the data unless *Topmost*). Tabular, logic and 3D
plots draw no redlines; parameter-versus-parameter and end-to-end plots do.

**FR-MRK-011 (redline commands)** `REDLINE <value>,<text>[,LEFT|RIGHT]` (FR-SCR-241); `CLEAR
REDLINES [ALL]` / `CLEAR REDLINE[S] n[,m…]`; *Markers → Add Redline Marker...* (value preset to the
centre of the visible range of the selected value axis, text, Left/Right) and *Edit Redlines...*
(Add / Update / Delete / Delete All, applied on OK). Changes go to the active plot (all plots with
*Apply Changes Globally*) and the master set. Recording emits `redline <value>,<text>[,RIGHT]` and,
for Edit, `clear redlines all` followed by the list. `.markers` and `.wpd` files hold redlines as
`redline <value>,<text>[,LEFT|RIGHT]` lines (FR-LOAD-060/061, FR-LOAD-040).

**FR-MRK-012 (marker colours)** Event markers and redline markers have their own colour-table entries,
independent of the grid colour: `ColorIndex.Marker` (105, default 0x808080) and `ColorIndex.Redline`
(106, default 0x3C14DC crimson, RGB 220,20,60 — deliberately not a curve palette colour so click identification by colour never mistakes a redline for a curve). Set with *Colors ▸ Marker Color / Redline Color* (Style menu and every
plot context-menu Colors ▸ submenu, also *Markers → Marker Color... / Redline Color...*) or `SET COLOR
MARKER|REDLINE,r,g,b [GLOBAL]`; recorded as `set color MARKER|REDLINE,r,g,b`. Saved in the INI
`Colors` table (FR-CFG); *Restore Colors* and *Load Color Table* include them.

---

## 8.9 Tabular displays

**FR-TAB-001** Tabular style shows, on a 4096×4096 logical layout (origin bottom-left), each curve's
nomenclature + current value (or value only when *tab legend* off) at its tab position; curves
without a position are listed top-down at the left. Optional background image (stretched), markers on
top, and (if *Default Time Stamp*) `TIME : %.3f` or `TIME ddd/hh:mm:ss.mmm` at the bottom right.
Values use each trace's tab format (HEX/OCT/GENERAL/DEC0-7, FR-XPT-001), text in label colour or
curve colour (`coloredtext`).
**FR-TAB-002 (time stepping)** The displayed instant is the latest time while auto refreshing.
Otherwise *Pan Right/Left* (arrow keys, `PAN RIGHT|LEFT`) step the displayed time forward/backward
by *TabStepRate* seconds (Style → Data Rate → Tabular Step), by 1/10 of the X range with `PAGE`, or
to the end/start with `FULL`; the reference curve is the one with the latest end (forward) or
earliest start (backward) and the shown point is the nearest one at/after (forward) or at/before
(backward) the target; each curve shows its last point at or before the displayed time. Switching
into tabular style starts at the end of the previous X range. Left-drag zooms the layout.
**FR-TAB-003 (layout editing)** *Edit → Tabular Display Layout*: alignment grid (spacing prompt;
drawn as dots, red when snapping) and snap-to-grid; *Add Parameter* at the clicked position; Trace
Attributes allows X/Y position, legend and format edits; *Copy to File → as WPD* saves the layout
(FR-LOAD-041). *TabHistory* controls whether real-time refresh keeps history.

---

## 8.10 Dialogs (functional requirements)

| ID | Dialog | Required behaviour (source) |
|---|---|---|
| FR-UI-020 | **Parameter Selection** | Resizable (min size persisted), File combo (`All Files` + each file), X axis combo (`(default)` + history), Y axis combo (history of 64), *Plot* checkbox, list of parameters with columns Name, Name (alt), Units, Description, Seq#, Rate, Type (optional); click column header = sort (toggle) or, if filter text pending, **filter** that column: space-separated terms, each a case-insensitive substring, `!term` excludes; *Restore* clears filter; *Pri/Alt Swap*, *Use Extended* names, *Retain Sort*, *Retain Filter*, *Filter Duplicates*; multi-select builds the Y list (comma-separated); double-click = OK; *X Axis* button opens the dialog in X-selection mode; *Y Axis* button asks `Clear remembered parameters list?`; *Save* parameter list (`Parameter List.csv`, comma or tab; header `Primary, Alternate, Units, Description, Sequence#`). Titles: `Parameter Selection`, `Select X Axis Parameter`, `Add Parameter to Plot`. |
| FR-UI-021 | **Favorites** | FR-LOAD-050 |
| FR-UI-022 | **Filter Helper** | Build filter expressions `[p1] op [p2] = [Y]` from combos of available parameters; list of sub-filters (add/remove; `Missing parameter or operator.`); X axis choice; produces the Y list using `=` filtering (FR-EXP-023). |
| FR-UI-023 | **Time Slice** | Start, Stop, Reference combos (NONE, START, EOF, events of open files); expandable section (state persisted); OK applies to all groups (FR-SCR-131). **Per Database** dialog edits each group's triplet. |
| FR-UI-024 | **Time Alignment Tool** | For the active plot: list curves (`*` prefix = live non-UNCOMPRESSED, cannot shift), spin to shift the selected curve's time by an increment (shift shown `%.3f`, stored in `vTshift`); changing selection asks `Discard shift to current parameter?`; Cancel restores all shifts; OK optionally (`Replace current reference?`) sets the group reference to the shifted GMT (`ddd/msec.usec`) or time. Shifted plots show the footer note. 64-bit time is temporarily disabled. |
| FR-UI-024a | **Align Static to Live** | *TimeSlice → Align Static to Live*, plot popup, Ctrl+L (toggle). For a time plot (not tabular, End-to-End, parameter vs. parameter, log X or 64-bit time) that holds both static and live curves. Every static curve moves together on the time axis while the live curves stay put; refresh is held while the mode is on. Left drag moves the curves (preview, X scale kept). ←/→ moves by one pixel, Shift+←/→ by one X graduation, Ctrl+←/→ by a tenth of a pixel. Snap: a click marks a feature on a static curve (dashed line), a second click marks the same feature on a live curve and the shift moves the first onto the second; right click or Esc cancels a pending snap. Status bar: `Align Static: shift %+.3f s …`. Enter (or the toggle) accepts, Esc cancels and restores the curves. Accept moves the time reference of each static group shown in the plot: a GMT reference (event or GMT) becomes the literal GMT `ddd/msec.usec` moved by −shift; NONE, START, CUTOFF or a constant (offsets from the file reference GMT) becomes the literal GMT of that offset minus the shift, or the constant offset minus the shift when the group's files have different or no reference GMTs; EOF can't be shifted (message, mode stays on). File events are placed against their own group's reference, so the static file's events move with its curves. Numeric start/stop times move by the shift. The change is recorded as `TIME group:start,stop,ref`, and the plots that show those groups reload. Equal shifts left by the Alignment Tool are taken over; different ones are dropped. |
| FR-UI-025 | **Trace Attributes** | Per-trace attributes FR-PLT-022 + tab X/Y position; trace number combo; *Apply all*; controls enabled per style (tabular: position/legend/format; logic: logic label; 3D: no symbols/scatter). Title `Trace Attributes - <name>`. |
| FR-UI-026 | **Nomenclature** | FR-PLT-040 |
| FR-UI-027 | **Fix X / Fix Y** | Min, Max, Increment (blank = best fit), persist; multi-Y per axis; apply all. |
| FR-UI-028 | **Colors** | Line colour table (100), label, title, grid, marker, redline, background, each chosen in the Windows colour dialog (`ColorDialog`, FullOpen, custom colours = curve colours 1–16, COLORREF via `ColorTranslator`); Restore colours; *Load Color Table* (ColorPrinter.wps). Default curve colours: `PlotColors.Palette` (25 colours, no black/white, no duplicates) repeated for 1..100; the shipped `Multiplot.ini` and `ColorPrinter.wps` use the same palette. |
| FR-UI-029 | **Fonts** | Title and label fonts chosen in the Windows font dialog (`FontDialog`: face, regular/bold/italic, size 4–200 pt; no effects/colour). Applied to the active plot (all plots with apply-global) and the defaults; stored as point size ×10 in `PlotConfig.TitlePointSize` / `LabelPointSize`. The screen scaling items (Screen Scaling 1..4, Scale Up/Down, Font Scaling ▸) are removed; `FONTSIZE` is accepted and ignored. |
| FR-UI-030 | **File Properties** | List of files `group (group:g file:f) : name`; details: path, signature `%08X`, type, header reference time, items, points/updates, rate, cutoff, title, comment, serial, stand, position, type, number, comment block, stream events; Remove file (`Plot Windows will be closed` OK/Cancel); Copy, Copy all, Save (`File Details.txt`). |
| FR-UI-031 | **Parameter Properties** | `Plot #n: m parameter(s)`, `Curve i:  <file>name`; details: name, file, units, description, scale ID, reference GMT/TIME/file reference, retrieved points, rate (Unknown/Variable/%g), type id, sequence number (16-bit when rate-packed), min/max value and offset, time shift; remove curve (`Can't remove last parameter`), move up/down, copy, save. |
| FR-UI-032 | **Slice Average** | FR-XPT-042 |
| FR-UI-033 | **Data Pick** | Values of every curve at the picked time (`%g`), sortable list, double-click/plot selected/plot all into a new plot. |
| FR-UI-034 | **Symbolic Names** | List/add/edit/delete/clear user symbols (`Name already exists` confirm). |
| FR-UI-035 | **File Paths** | Data file search path, (DB_DIR search path), script search path, WPD search path, export folder. Each has a *Browse...* button with the Windows folder selection dialog (`Microsoft.Win32.OpenFolderDialog`, starting in the first existing folder of the path). Search paths (`;` lists): multi-select, the chosen folders are appended unless already present (case-insensitive, trailing `\` ignored). Export folder: the choice replaces the text. |
| FR-UI-036 | **External Applications** | Export application, edit/view application, text printer command (stored), Include GMTs. |
| FR-UI-037 | **Precision** | Data format and time format (GENERAL, 0..7, HEX…, OCT…), shared time. |
| FR-UI-038 | **Average Function** | default rate, time-stamp mode (first/last/averaged). |
| FR-UI-039 | **Right Button** | choose Delta / Popup Menus / Slice Average / Data Tracking; Popups Global. |
| FR-UI-040 | **AutoRun / Compact / Discard / Refresh interval** | FR-RT. |
| FR-UI-041 | **Import** | FR-IMP-002. |
| FR-UI-042 | **Manage Plug-ins** | FR-PLG-012. |
| FR-UI-043 | **Window list** | select a plot window from a list. |
| FR-UI-044 | **Input / message dialogs** | generic text input with optional drop-down choices and apply-all checkbox, used for many single-value prompts (legend columns, grid offset, symbol density, GMT format, Z angle, group name, user caption…). |
| FR-UI-045 | **About** | product name, version, build date, data-access and plugin versions. |
| FR-UI-046 | **Appearance** | *View → Appearance → Light / Dark* (checked), `APPEARANCE LIGHT\|DARK`, INI `[interface] Appearance`; default Light. Sets `Application.ThemeMode` (Fluent Light / Dark) for menus, dialogs and frames, and `UiTheme` for the MDI area, plot window chrome, zoom/cross-hair overlays and the identification pop-up. Plot colours are mapped at draw time by `PlotColors.Map` (identity in Light; in Dark a background with luminance ≥ 0.18 is scaled by 30/255, other entries are mixed towards white until their relative luminance reaches 0.55 text/mono, 0.25 marker, 0.18 curves/redline, 0.10 grid); the colour tables are unchanged. Saved/copied pictures follow the appearance; curve identification by pixel colour compares mapped colours. |
| FR-UI-046 | **Help → Window Position** | move the main window back onto the visible desktop. |
