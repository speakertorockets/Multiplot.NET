# 09 — Settings, Persistence, User Folder and Compatibility

Owner decision: INI files, registry settings and favourites **must remain compatible** across
versions (an existing `Multiplot.ini` loads with the same effect; a saved INI loads again unchanged).

---

## 9.1 Folders and files

**FR-CFG-001 (user folder)** User folder = `%USERPROFILE%\My Multiplot`, created at start-up if
missing. If it contains no `Multiplot.ini`, the installation's `Multiplot.ini` and `Multiplot.favorites`
(next to the executable) are copied into it. Default INI = `<UserFolder>\Multiplot.ini`; default
favourites = `<UserFolder>\Multiplot.favorites` (overridden by the registry value, FR-CFG-030).
The plugin sub-folder (FR-PLG-010) is created if missing.

**FR-CFG-002** The temp folder is `%TEMP%` without trailing `\`; ExportPath defaults to it. The
environment variable `MULTIPLOTPATH` is set (for child processes and scripts) to the executable
folder. If the ExportPath from the INI does not exist: `Export path is invalid (see Options->File
Paths)` and the temp folder is used.

**FR-CFG-003 (start-up order)** create frame → read window position (registry) → load plugins →
show window (hidden with `/hide`, maximised if saved so) → load INI (or the installation INI with
`/default`; missing → `Multiplot.ini not found\nDefaults used`) → apply toolbar/status bar visibility
→ process command-line arguments (FR-SCR-301).

**FR-CFG-004 (exit)** Exit is refused while a plot popup is open or data is loading
(`Can't exit while data is loading`); if `ExitSafety` → confirm `Are you sure you want to exit?`;
while a script runs → `Script(<name>) is active, exit anyway?` (Yes aborts the script, then exits).
Exit then stops auto refresh (waits for an in-progress refresh), closes all files, stops recording,
saves toolbars, recent parameters, favourites path and (unless `/nosave`) window placement to the
registry, shows a "shutting down" notice while plugins shut down. The INI is **not** saved
automatically.

---

## 9.2 INI file format

**FR-CFG-010 (reader)** Lines are read (max 32,767 chars). For each line, characters **before** the
first `=` are lower-cased; the value is the text after `=` with leading spaces skipped (empty → "no
value"). Section headers and comment lines are **ignored** — keys are matched anywhere by
**prefix** (`strncmp(line, key, len(key))`) in the fixed order of the table below; the first matching
key wins. Unknown lines are ignored. Boolean keys are false iff the value's first character is `0`
(a missing value is treated as true, DEV-CFG-001).

**FR-CFG-011 (writer)** *Save Settings* (dialog unless a script is running; scripts use the current
INI name and do nothing if empty) writes exactly this layout (CRLF as text mode on Windows):
```
;*** Multiplot Initialization File ***

;Saved at h:mmAM|PM  Month dd, yyyy

[version]
;<version string>
;<date string>
;dll = <each library version line>
                                   (blank line)
[workstation]
;OS =  <OS env> <major>.<minor>.<build> <csd>
;memory installed = <n> gigabytes
;owner = <user>

[toolbar]   StatusBar, ToolBar, IncludeToolBar, ButtonSet00..18, UserButton0..4
[symbolics] LoadSymbols, symbolic = NAME,value (one per symbol)
[options]   FileFilters, AutoSave, Monitors, AlignClosest, CalcEveryInstance, OptimizedDraw,
            OffscreenDraw, UsePlaceholder, ExitSafety, RebuildRate, TabHistory, DefaultTabTime,
            MaxPresentation, FirstOccurrence, Extrapolate, ShowSystemLoad, ShowCursorCoords, Use64bitTime
[paths]     FileSpec, DBPATH, ScriptDir, WPDpath, ExportDir, QueryDir
[realtime]  TimeSlide, IgnoreStatic, AutoCompact = n,g, AutoDiscard, AutoRefresh (ms),
            DropoutMarker, DropoutIgnore, PcgCacheSize
[printer]   PrinterName, PrinterMargin (%f), ColorPrinter, Landscape, TextPrinter
[access time] DefaultStartTime, DefaultStopTime, DefaultReference
[functions] AverageRate (%.3f), TimeStampMode
[mouse]     RBfunction, PopupsGlobal
[external applications] ExportApp, EditApp, DecimalPlaces, TimePrecision, SharedTabTime, IncludeGMTs
[scripting] ScriptPrompt, PauseMode, EmbeddedScript
[fonts]     FontSize, TitleFont, LabelFont
[plot style] GroupBy, ExtNomen, AltNames, SymbolFreq, SampleFreq, AverageFreq, ZaxisFreq, TabStepRate,
            Xtitle, Ytitle, Xnomen, Ynomen, Xudef, Yudef, Xautolabel, UseTitle, UseSubTitle, UseMarkers,
            MarkersTopmost, LegendColumns, ReservedLegend, GridOffset, ShowLegend, LegendColor,
            ShowSymbols, SymbolSize, 3DZangle, PlotMax, ApplyAll, LineSize, 3DLineSize, TimeStamp,
            MinorTics, BoldFrame, ScaleGroup, StairStep, ClockTime, GMTformat, GridLines, TestInfo,
            ShowVersion, ShowUserName, ShowPageNo, ShowScript, LegendShift, CaptionSource,
            EMFwideAspect, FixXautoY, ScaleRedlines, NoCaptionBar, LogicTxtIndex,
            LogicTxtLabel = i,true,false (×4), XNomenLen, YNomenLen, XNomenOrder, YNomenOrder,
            GridStyle = solid|dotted|dashed, Colors = 105 decimal COLORREFs
```
Each entry is `Key = value` with formats exactly as `src/Multiplot.Settings/IniFile.cs` writes them. Quirk: `DefaultReference`
is written as the current value only when it is `start` (case-insensitive), otherwise `NONE`
(preserve — DEV-CFG-002). `ZaxisFreq` is written but never read (preserve).
*Acceptance:* TST-CFG-001 — load each fixture INI, save it, diff with the golden saved INI
(ignoring the timestamp/version/workstation comments): identical.

**FR-CFG-012 (keys, read order, meaning, defaults)** Table in reading order. "B" = boolean.

| Key prefix | Type / parse | Setting | Default |
|---|---|---|---|
| `dbpath` | text | DB folder (search path) | "" |
| `filefilters` | text | open-dialog filter string | `All Files(*.*)|*.*|025 Files|*.025|050 Files|*.050|250 Files|*.250|Filter Files (*.flt)|*.flt||` |
| `?nomenlen` (x/y) | 12 ints | nomenclature item lengths | 0… |
| `?nomenorder` (x/y) | 12 ints (flag values) | nomenclature item order | DBNAME(512) FILENAME(32) NAME(1) UNITS(2) DESC(4) REFT(8) UDEF(16) ALTNAME(256) EXTNAME(1024) SEQNUM(64) CURVAL(2048) CURTIME(4096) |
| `xnomen` / `ynomen` | int flags | nomenclature items shown (bits FR-SCR-230; 0x80 EFORMAT) | X = NAME|UNITS (3); Y = FNAM|NAME|UNITS|DESC (39) |
| `yudef` / `xudef` / `xtitle` / `ytitle` | text (empty allowed) | | xtitle `Multiplot`, ytitle `Parameter` |
| `colors` | up to 105 ints | colour table: 0–99 curve colours, 100 grid, 101 label, 102 background, 103 mono, 104 title. Fewer than 105 values = older short colour table: copy entries 25..28 to 100..103 and title = label | curves cycle 0x0000FF,0x00FF00,0xFF0000,0x0000BF,0x00BF00; grid/label/title 0x808080; bg 0xFFFFFF; mono 0 (COLORREF = 0x00BBGGRR) |
| `3dzangle` | int | 3-D perspective angle | 45 |
| `printername`, `textprinter`, `printermargin` (0..10 else 5) | | printing (stored only) | "", `notepad /p`, 5 |
| `fontsize` | int 1..4 | relative label size | 2 |
| `titlefont`, `labelfont` | `pointSz height width esc orient weight italic underline strike charset outPrec clipPrec quality pitchFamily face…` (first value > 0 = point size ×10, else height) | fonts | title 140/45 "Multiplot"; label 100/30 "Multiplot" |
| `gridstyle` | contains solid/dotted/dashed | | dotted |
| `minortics`, `boldframe`, `scalegroup`, `stairstep`, `clocktime` | B | | 0,0,0,0,0 |
| `gmtformat` | C format (empty → default) | | `%03u/%02u:%02u:%02u.%03u` |
| `timeslide`, `ignorestatic`, `dropoutmarker`, `dropoutignore` | B | | 0,0,0,1 |
| `pcgcachesize` | long | stream cache size, also sent as `cachesize n` command to signature 0xFFF0FFFF | 16384 |
| `rbfunction` | 0 delta, 1 popup, 2 slice, 3 track | right-button default | 0 |
| `popupsglobal`, `usetitle`, `usesubtitle`, `usemarkers`, `emfwideaspect`, `legendshift`, `showsymbols` | B | | 0,1,1,0,0,0,1 |
| `symbolsize` | −1..1 | small/normal/large | 0 |
| `testinfo`, `showversion`, `showusername`, `showpageno`, `showscript` | B | | 0,1,0,0,1 |
| `captionsource` | command id (FR-CFG-040) | caption source | `ID_CAPTION_PARMSET` |
| `nocaptionbar` | int | | 0 |
| `groupby` | command id | multi-Y grouping | INDEPENDENT |
| `linesize`, `3dlinesize` | int | | 1, 1 |
| `symbolfreq`, `samplefreq` | int | | 8, 100 |
| `showlegend`, `legendcolor` | B | | 1, 0 |
| `reservedlegend` | int | | 0 |
| `gridoffset` | |n| ≤ 90 | | 0 |
| `legendcolumns` | int ≥ 1 else 2 | | 2 |
| `averagefreq` | int | | 100 |
| `tabsteprate` | float | | 1.0 |
| `timestampmode` | 0 first,1 last,2 averaged | `avg()` default | 1 |
| `averagerate` | float | `avg()`/`quantize()` default dt | 1.0 |
| `timestamp` | B | plot footer clock | 1 |
| `defaultstarttime`, `defaultstoptime`, `defaultreference` | text | global slice | `-3.4e+038`, `+3.4e+038`, `NONE` |
| `editapp`, `exportapp` | text | | "" |
| `decimalplaces`, `timeprecision` | int clamped −1..12 | | −1, −1 |
| `sharedtabtime`, `includegmts` | B | | 0, 0 |
| `monitors` | 0..10 | tiling across monitors | 0 |
| `optimizeddraw`, `offscreendraw` | |int| | | 1, 1 |
| `autosave`, `maxpresentation`, `useplaceholder`, `rebuildrate`, `tabhistory`, `defaulttabtime`, `exitsafety`, `toolbar`, `statusbar` | B | | 1,1,0,1,1,1,0,1,1 |
| `filespec`, `scriptdir`, `wpdpath`, `exportdir`, `querydir` | text (`;`-separated search lists for filespec/scriptdir/dbpath/wpdpath) | | "", "", "", %TEMP%, user folder |
| `plotmax`, `xautolabel`, `applyall`, `extnomen`, `altnames`, `gridlines`, `scriptprompt`, `embeddedscript` | B | | 1,1,0,0,0,1,1,0 |
| `pausemode` | `1` → NEXTCOMMAND else NEXTPLOT | | NEXTPLOT (2) |
| `colorprinter`, `landscape`, `fixxautoy`, `scaleredlines`, `alignclosest`, `calceveryinstance` | B | | 1,1,0,0,1,0 |
| `autocompact` | `n[ ,]interval` | | 0, 0 |
| `autodiscard` | seconds | | 0 |
| `autorefresh` | ms | auto-refresh interval used when refresh is (re)enabled | 0 (→ 2000 when toggled on) |
| `loadsymbols` | B (also clears current symbols) | load `symbolic` lines | 0 |
| `firstoccurrence`, `extrapolate`, `showsystemload`, `showcursorcoords` | B | | 0,0,1,1 |
| `symbolic` | `NAME,value` | user symbol (only if LoadSymbols) | |
| `includetoolbar` | int | apply toolbar keys | 0 |
| `version` | text | INI version (toolbar sets only applied when equal to the app version) | |
| `buttonset` | `ButtonSetNN = enabled id img id img …` | toolbar NN (0..18) contents, only if same version and IncludeToolbar | built-in defaults |
| `userbutton` | `UserButtonN = label,command` | user button N (0..4) | |
| `logictxtindex` | int | | 0 |
| `logictxtlabel` | `i,true,false` | | ON/OFF, OPEN/CLOSED, HIGH/LOW, TRUE/FALSE |
| `markerstopmost` | int | | 0 |
| `use64bittime` | int | | 0 |

Note the second lookup chain: `buttonset`/`userbutton`/`logictxt…`/`markerstopmost`/`use64bittime`
are tested even if an earlier key matched — harmless but preserve.
After loading, the provider options rebuild-rate, extrapolate and script-prompt are broadcast
(FR-ACC-006); symbols are sorted; toolbars rebuilt if changed.

**FR-CFG-013** The *Options* menu provides Load Settings (open dialog `*.ini`, recorded
`LoadSettings`) and Save Settings; the current INI path is not changed by Load Settings.

---

## 9.3 Registry (HKCU\Software\Multiplot)

**FR-CFG-030** The application shall read and write these values with these names and encodings:

| Key | Value | Type | Content |
|---|---|---|---|
| `Recent Files` | `file1`…`file10` | REG_SZ | MRU (file1 = most recent); read in order file10→file1 |
| `Favorites File` | `favorites` | REG_SZ | favourites path |
| `Window` | `position` | REG_SZ | `"left top right bottom maximized"` (normal placement) |
| `Recent Parameters` | `parmset` | REG_MULTI_SZ | remembered Y lists ≤ 64 chars each, most recent first |
| `TimeSlice` | `Expanded` | REG_BINARY (BOOL) | Time Slice dialog expanded state |
| `Toolbars` | one value per toolbar name (`File, Edit, Script, Parameter, Timeslice, View, Axis, Pan/Zoom, Style, Style2, Nomenclature, Markers, Options, Options2, User, Window, Data, Realtime, Help`) | REG_MULTI_SZ | first string = enabled flag, then `"<commandId> <imageIndex>"` strings |
| `Toolbars` | `UserButton 1`..`UserButton 5` | REG_MULTI_SZ | label, command |
| `Toolbars` | `Band Order` | REG_MULTI_SZ | 19 band ids |
| `Plugins\Blocked` | `<file name>` | REG_BINARY 1 | blocked plugins |
| `Plugins\UserFolder` | (default) | REG_DWORD | load plugins from user folder |
Dialog geometry and other implementation-specific values are stored under `NET\…` (DEV-CFG-003).

## 9.4 Command-id mapping

**FR-CFG-040** INI `ButtonSetNN`, registry `Toolbars`, `CaptionSource`, `GroupBy` and
`MULTIY GROUPBY_<n>` store **numeric command ids** (e.g. 57600 = File New). The application
shall contain a versioned mapping table `CommandIds` (id ↔ command name ↔ toolbar image index),
used to read and to write these values so that toolbar layouts round-trip. Unknown ids are ignored (button dropped) with a log entry.
*Acceptance:* TST-CFG-002 — every command id appearing in the default toolbar sets maps to a command.

## 9.5 Importing WinPlot settings

**FR-CFG-050** Users moving from an existing WinPlot installation can import its settings once.
* *Options → Import WinPlot Settings...* (command id `ID_IMPORT_WINPLOT` = 58240) asks for the WinPlot
  INI file (initial folder `%USERPROFILE%\My WinPlot`), then asks *Replace the current Multiplot.NET
  settings with the WinPlot settings?* (Yes/No, default No).
* On the first start-up (the user INI was just created), when not hidden and without `/default`, if
  `%USERPROFILE%\My WinPlot\WinPlot.ini` exists the application asks *Settings of an existing WinPlot
  installation were found in <folder>. Import them into Multiplot.NET?* (Yes/No, default No).
* The import loads the WinPlot INI (§9.2; its `ButtonSetNN` keys are ignored because the version line
  differs). A title beginning with WinPlot's stock `WinPlot <version>\n` is changed to `Multiplot.NET\n`.
* All values and sub-keys of `HKCU\Software\WinPlot` except `ParmSelect461` and `Defaults` are copied
  into `HKCU\Software\Multiplot` (overwriting) and the recent files, recent parameter lists,
  toolbars, user buttons and user-plugin-folder switch are applied at once. Blocked plugins and the
  window position take effect at the next start.
* Favourites: a favourites file in the WinPlot user folder (the registry path, else `WinPlot.favorites`)
  is copied to `<UserFolder>\Multiplot.favorites`, which becomes the favourites file; a favourites file
  elsewhere stays in use where it is.
* The result is saved to the Multiplot.NET INI, menus and toolbars are rebuilt, and the message
  *WinPlot settings imported.* is shown. The WinPlot files and registry key are only read, never changed.
*Acceptance:* unit test of the registry copy (values, sub-keys, skipped keys, missing source); manual
check of the first-run offer.
