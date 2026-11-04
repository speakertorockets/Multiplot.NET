# 08 — User Interface, Plot Windows, Markers and Tabular Displays

Menus, plot popup menus and command prompts are listed in `src/Multiplot.App/MenuDefinitions.cs`
and `src/Multiplot.App/CommandPrompts.cs`.

Rendering need not be pixel-identical between renderers (DEV-UI-001) but layout rules,
content, colours, scale values and interaction semantics listed here are mandatory.

---

## 8.1 Main window

**FR-UI-001 (shell)** A single main window hosting many **plot windows** (MDI children).
The WPF application shall provide an MDI-equivalent document area that supports: overlapping child windows,
maximise/minimise/restore, cascade, tile horizontally, tile vertically, stack horizontally, tile by
group name, tile across *N* monitors (`Monitors` setting), keep-on-top, move, close, close all,
next/previous (Ctrl+Tab / Shift+Tab), select from list (*Window → Select*), auto-tab on a timer (all
plots or one group). New plots open maximised if *Maximized Plots* (`PlotMax`) is on.

**FR-UI-002 (menus)** The main menu shall contain these items (same order, captions and check
states) except out-of-scope items (print, EMF, Query, external command polling, help contents), which
are omitted (DEV-UI-002). Structure (`IDR_MAINFRAME`):
* **File**: New, Add, Import Tabular, Recent Files ▸, Close ▸ (Last, All), Properties, Exit.
* **Edit**: Copy to Clipboard ▸ (as BMP, as Text), Copy to File ▸ (as Picture, as Text, as WPD, as Sun),
  Import from Clipboard, Export to Application, Symbolic Names, Tabular Display Layout ▸ (Alignment
  Grid, Snap To Grid).
* **Script**: Execute, Record, Edit, GoTo Next, Pause, Abort, Pause Any Command (✓), Allow Prompt and
  Error Dialogs (✓), Log Script Errors, Script Placeholder Plot, Enable File Embedded Scripts.
* **Parameter**: Selection, Favorites, Filter Helper, Add to Plot, Select WPD.
* **TimeSlice**: Global Time Slice, Per Database, Alignment Tool, Freeze Plot.
* **View**: New Plot, Tabulation, Parameter Properties, File Properties, Presentation Mode ▸ (Enable, Full Screen).
* **Axis**: Multi Y ▸ (On, Group By ▸ Independent/Name/Units/Description/User Defined), End To End,
  Log X, Log Y, Auto Y on Change in X, Auto Scale REDLINEs, Fix X, Fix Y, Auto Range X, Auto Range Y,
  Auto Range X/Y (Home), Revert Scales (Backspace), Zoom In/Out X (Insert/Delete), Zoom In/Out Y
  (PgUp/PgDn), Pan Left/Right/Up/Down (arrows), 3D Z Axis Angle.
* **Style**: 2D Line, 2D Scatter, 3D Line, 3D Area, Logic, Tabular, Trace Attributes, Data Rate ▸
  (Set Mode, Density ▸ Average/Sample/3D Segments, Tabular Step), Colors ▸ (Line, Label, Title, Grid,
  Background, Restore), 2D Line Size, 3D Line Size, Grid Lines, Minor Tics, Bold Frame, Stair Step,
  Symbols ▸ (Show, Density, Size ▸ Small/Normal/Large), Legend ▸ (Show, Set Columns, Fixed Parameter
  Space, Minimum Grid Offset, Multi-Y Shift Left, Text in Curve Color).
* **Nomenclature**: Fonts ▸ (Labels, Titles, Screen Scaling ▸ 1..4), X Axis/Legend Labeling, Y
  Axis/Legend Labeling, Set Title, Set Sub-Title, Show Time as GMT/Clock, Set GMT Format, Scale Group
  ID, Version, Script Name, User Name, Page Number, Time Stamp, Test Info, Set User Caption Text, Set
  Group Name.
* **Markers**: Draw ▸ (Text, Line, Box, Circle, Event, Redline, Image), Edit, Topmost, Clear All, Save, Load.
* **Options**: File Paths, External Applications ▸ (Assign, Include GMTs), Tabular Output/Exports ▸
  (Data Format, Time Format, Shared Time), Expressions/Calculations ▸ (Align to Closest Point, Perform
  Every Unique Time), Draw Mode ▸ (Optimized, Offscreen), Average Function ▸ (Default Rate, Time Stamp
  Mode), Right Button Click, Show System Load, Show Coordinates, Show Status Bar, Toolbars ▸ (Show,
  Edit), Maximized Plots, Load Symbolic Names, Number of Monitors, Save Window Position, Exit Safety
  Prompt, Continue Titles, Continue Sub-Titles, Continue Markers, X Axis Auto Label, Apply Changes
  Globally, Window Caption ▸ (Disable, Source), Include Toolbars, Save Settings, Load Settings,
  Import WinPlot Settings (FR-CFG-050).
* **User**: 1..5 (user-defined labels), Assign.
* **Data**: Rebuild Full Rate, Extrapolate End Points, Use 64 Bit Timing, Dropouts ▸ (Ignore, Show
  Markers), Real-Time ▸ (Refresh, Auto Refresh, Sliding Scales, Ignore Static Data, First Occurrence,
  Auto Run, Auto Data Compact, Auto Data Discard, Show Stream Certification), Tabular Options ▸
  (Maintain History, Default Time Stamp), plugin commands, Manage Plug-ins.
* **Window**: as FR-UI-001. **Help**: Window Position, About Multiplot.
Every toggle shows its check state for the active plot (or global default when no plot is active);
with *Apply Changes Globally* the action applies to all plots.

**FR-UI-003 (accelerators)** Arrows = pan (Shift/Ctrl variants identical); PgUp/PgDn = zoom Y in/out;
Insert/Delete = zoom X in/out; Home = auto range X/Y; Backspace = revert zoom; Enter = Parameter
Selection; Esc = toggle presentation mode; Ctrl+Esc = break-out (stop current long operation);
Space = script pause/resume, Ctrl+Space = resume; Ctrl+Tab / Shift+Tab = next/previous window;
Ctrl+D = draw markers; Ctrl+J = save JPEG; Ctrl+R = refresh windows; Alt+F1..F5 = user buttons
1..5; Ctrl+Alt+Home = bring main window to the primary screen *(inferred from `ID_WINDOW_BRINGHOME` — verify)*; (F1 help and Ctrl+E/Ctrl+P EMF are out of scope).

**FR-UI-004 (toolbars)** 19 toolbar bands named File, Edit, Script, Parameter, Timeslice, View, Axis,
Pan/Zoom, Style, Style2, Nomenclature, Markers, Options, Options2, User, Window, Data, Realtime, Help,
each showable/hidable, re-orderable (band order persisted), and editable (*Toolbars → Edit*: choose
buttons per band from the command catalogue with their image indices). User toolbar shows 5
user buttons with their labels. Persistence: registry `Toolbars` and optional INI `ButtonSetNN`
(FR-CFG-012, FR-CFG-030). Default button sets are defined in `src/Multiplot.Core/ToolbarDefaults.cs`.

**FR-UI-005 (status bar)** Panes: message text (help/coordinates/progress), CPU load %, memory load %
(updated by a timer when *Show System Load*), current real-time GMT. Messages used by operations
(e.g. `Initializing <file>`, `Retrieving…`, `Calculating expression …`, `calculating spline (%n)`,
`Paused n...`) shall be shown.

**FR-UI-006 (presentation mode)** Presentation mode hides menus, toolbars, status bar and window
borders (Esc toggles); *Full Screen* additionally maximises to the whole monitor (`MaxPresentation`).
Caption bars of plots can be disabled (`NoCaptionBar`).

**FR-UI-007 (window captions)** Plot caption source (`CaptionSource`): parameter set (default), plot
number, title, sub-title, script name, or user text (`CAPTION USER text`).

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
* **Footer** (small font, half the label size): left = stream certification text, `    >> Contains
  time shifted parameters`, `    *Data is Averaged to %d points` / `    *Data is Sampled to %d points`;
  above the plot's top-right = `Script: <script>` and/or version string; top-right corner =
  `page <plot number>` (Show Page No); user name at top-left (or bottom-left when no legend); bottom
  right = plot creation date/time (Time Stamp).
* **Test info** (if on): three rows `Test:`, `Engine:`, `Shutdown:` with one column per group
  (group name, engine serial, cutoff).
Minimum left offset = `GridOffset` % of the width. Font height (screen) = max(5·fontSize,
min(1.5·H, W)/(30 + 10·(4 − fontSize))), min 12 px; title size scales with the title/label font ratio.

**FR-PLT-002 (title keywords)** Title, sub-title, user-defined (udef) texts and captions support
keywords (case-insensitive), replaced repeatedly until none remain, each value trimmed and `\`
replaced by `/`:
`[title] [comment] [serialnum] [teststand] [testposition] [testtype] [testnumber]` (header fields of
the reference curve's file), `[date]` (`  Month dd, yyyy`), `[time]` (`h:mmAM|PM`), `[parameter]`
(unique curve names, comma-separated), `[pidname] [pidunits] [piddescription]`, `[dbname]` (first
group name), `[timeslice]` (`start,stop,ref`), `[timestart] [timestop] [timeref]`, `[filename]`
(file name without path and import suffix), `[scriptname]`, `[currentvalue]`, `[currenttime]` (last
point, data/time precision), `[groupname]` (plot group name). An empty result becomes `" "`.

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
min/max within the visible X range when *FixXautoY* (or zooming), else over all data; redline markers
included when *Scale REDLINEs*. Persistent (fixed `p`) scales survive new data.

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
above, MIN: ticks below) and shown accordingly in the legend; redline markers likewise (FR-MRK).

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
Labeling, Font ▸, Version, Script Name, User Name, Colors ▸, Legend ▸, Presentation Mode, Window ▸,
Properties); **Y scale area** (Set Sub-Title, Font ▸, Multi-Y, Multi-Y Shift Left, Colors ▸, Minimum
Grid Offset, Presentation Mode, Window ▸, Properties); **footer/X area** (X Axis Labeling, Font ▸, Show
Time as GMT, GMT/Clock Format, Time Stamp, Test Info, Font Scaling ▸, Colors ▸, Presentation Mode,
Window ▸, Properties); **plot area 2D** and **3D** (Symbols ▸, Markers ▸, Font Scaling ▸, Colors ▸,
End to End, Scales ▸ (Auto X/Y, Auto X, Auto Y, Fix X, Fix Y, Revert), Font ▸, Add Parameter, View
Tabulation, Presentation Mode, Window ▸, plus 3D line widths for 3D); **tabular** (layout, add
parameter, colour text, clock time, …). Popup actions apply to the clicked plot only unless *Popups
Global* and *Apply Globally* are on (never for parameter-vs-parameter or end-to-end plots). The
Window ▸ submenu: Maximize, Minimize, Move, Tile, Tile By Group, Set Caption, Close.

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

## 8.8 Markers

**FR-MRK-001 (model)** Up to 1000 markers per plot plus a master set (applied to new plots when
*Continue Markers* is on). Marker = {valid, type, x1, y1, x2, y2, colour index (0–99, GRID, LABEL,
BACKGROUND), label}. Types: TEXT, LINE, BOX, CIRCLE, EVENT, REDLINE, IMAGE.

**FR-MRK-002 (coordinates)** Data coordinates on the X and first Y scale (log-transformed when log
axes), or percentages of the plot area when the label contains `\%`. IMAGE: corners normalised; both
x = 0 → full width, both y = 0 → full height; drawn stretched with a border unless colour = background
or full-width. REDLINE: line from (x1,y1) to (x2,y2) with MAX/MIN glyphs every 5 symbol sizes (label
`MAX`/`MIN`). EVENT: label = event name / GMT / time (optionally `name"display text"`); resolved via
FR-LOAD-021 against the plot's reference; drawn as a full-height vertical line; unresolved → `<label>:
event marker GMT not found\nMarker disabled.` (except while real-time data is loaded); invalid
`d/…` syntax → `: Invalid syntax for TIME/GMT\nMarker disabled.`.
**FR-MRK-003 (label flags)** In the label: `\v` keep label visible inside the plot; `\l` left of point;
`\r` right; `\t` above; `\b` / `\a` below; `\h` hide label; `\%` percent coordinates; `\g` (commands
only) global; `"text"` display text override. Defaults: TEXT centred at (x2,y2) (tabular: anchored
like values); vertical markers/events: label above/below the line end; others: right of (x2,y2). A
horizontal line with `\v` and a side label is shortened so it does not overlap the label. Events draw
their line around the label.
**FR-MRK-004 (order)** Markers draw beneath the data unless *Topmost*.
**FR-MRK-005 (interactive)** *Markers → Draw ▸ type* lets the user place a marker with the mouse
(Ctrl+D), *Edit* opens the Marker Edit dialog (marker number spin 1..1000, type radio buttons, X1/Y1/
X2/Y2, as-percent, colour spin (Line n / Grid / Label / None), label combo with type-specific choices:
MIN/MAX for redline, event names from open files for events, `- Browse -` and known bitmaps for
images), *Clear All*, *Save*/*Load* `.markers` (FR-LOAD-060/061). Recording emits `marker …`,
`clear marker n`, `clear markers all`, `markerset save|load`.

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
| FR-UI-025 | **Trace Attributes** | Per-trace attributes FR-PLT-022 + tab X/Y position; trace number combo; *Apply all*; controls enabled per style (tabular: position/legend/format; logic: logic label; 3D: no symbols/scatter). Title `Trace Attributes - <name>`. |
| FR-UI-026 | **Nomenclature** | FR-PLT-040 |
| FR-UI-027 | **Fix X / Fix Y** | Min, Max, Increment (blank = best fit), persist; multi-Y per axis; apply all. |
| FR-UI-028 | **Colors** | Line colour table (100), label, title, grid, background (system colour picker); Restore colours; *Load Color Table* (ColorPrinter.wps). |
| FR-UI-029 | **Fonts** | Title and label font pickers (face, weight, italic, …); screen scaling 1..4; enlarge/reduce. |
| FR-UI-030 | **File Properties** | List of files `group (group:g file:f) : name`; details: path, signature `%08X`, type, header reference time, items, points/updates, rate, cutoff, title, comment, serial, stand, position, type, number, comment block, stream events; Remove file (`Plot Windows will be closed` OK/Cancel); Copy, Copy all, Save (`File Details.txt`). |
| FR-UI-031 | **Parameter Properties** | `Plot #n: m parameter(s)`, `Curve i:  <file>name`; details: name, file, units, description, scale ID, reference GMT/TIME/file reference, retrieved points, rate (Unknown/Variable/%g), type id, sequence number (16-bit when rate-packed), min/max value and offset, time shift; remove curve (`Can't remove last parameter`), move up/down, copy, save. |
| FR-UI-032 | **Slice Average** | FR-XPT-042 |
| FR-UI-033 | **Data Pick** | Values of every curve at the picked time (`%g`), sortable list, double-click/plot selected/plot all into a new plot. |
| FR-UI-034 | **Symbolic Names** | List/add/edit/delete/clear user symbols (`Name already exists` confirm). |
| FR-UI-035 | **File Paths** | Data file search path, (DB_DIR search path), script search path, WPD search path, export folder. |
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
| FR-UI-046 | **Help → Window Position** | move the main window back onto the visible desktop. |
