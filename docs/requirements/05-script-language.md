# 05 — Script Language (`.wps`)

Owner decision: existing `.wps` scripts **must run unchanged, including parsing quirks**.
This chapter is the normative reference for the script interpreter.

---

## 5.1 Where commands come from

**FR-SCR-001** One command interpreter (`ExecuteCommand(string line)`) shall serve all of these
sources, with identical semantics:

| Source | Entry point | Notes |
|---|---|---|
| Script file run from menu / toolbar / drag-drop / command line | script runner | `.wps` |
| `CALL file` inside a script | script runner via script stack | |
| AutoRun on event (real-time) | real-time refresh → script runner | see 11-realtime |
| Embedded `[script]` section of a data file's comment block | embedded-script handler | only if `EMBEDDEDSCRIPT ON`; runs after a group finishes loading |
| User toolbar buttons 1–5 (`SET USERn`) | user button handler | runs with `ScriptPrompt` forced on, script name `*internal`; a `CALL x` is queued as a script |
| `SET COLOR PRINTERTABLE` | runs `ColorPrinter.wps` from the user folder, else `%MULTIPLOTPATH%` | |
| `INPUT`, `MCI` | re-enter the interpreter with a synthesized `SET` command | |

(Mailslot external commands are out of scope — see DEV-SCR-002.)

---

## 5.2 Lexical rules

**FR-SCR-010 (file reading)** Script files are read as bytes (Windows-1252), line by line,
maximum **16,383** characters per physical line (`fgets(rdBuf,16383)`). Longer lines are split by
the reader (the remainder becomes the next "line"). Line endings CR/LF or LF.

**FR-SCR-011 (continuation)** After reading a line, the interpreter looks ahead: while the next
physical line, after trimming whitespace, starts with `+` or `&`, it is a continuation:
1. comments are stripped from the accumulated command (FR-SCR-012) *before* appending;
2. the leading `+`/`&` is removed and the trimmed accumulated text gets `" " + rest` appended.
A look-ahead line that starts with `;` (after trim) is skipped and counted as a line but does not end
the continuation. Any other line ends the continuation and is re-read as the next command.
The script line counter counts physical lines (used by `$SCRIPT_LINE` and pause/resume).
*Acceptance:* TST-SCR-002 (continuation fixture) produces the expected command strings.

**FR-SCR-012 (comments)** A command line is trimmed; then loop: find the **last** `;` in the line;
if there is none, or it is at index 0, or it is preceded by `\`, **stop**; otherwise truncate the
line at that `;`, right-trim, and repeat. Afterwards every `\;` is replaced by `;`. A line whose first
character is `;`, or that is empty, is a comment (no-op). Consequences that must be preserved:
* `PLOT a;b ;comment` → `PLOT a` (unescaped semicolons truncate, scanning from the right).
* The scan stops at the first escaped semicolon found from the right, so earlier unescaped ones
  survive: `a;b\;c` → `a;b;c`; `x\;y ;note` → `x;y`.
* `\;` is the only way to keep a literal semicolon after the last comment marker.

**FR-SCR-013 (command word)** The command word is the text before the first **space** (not tab),
trimmed; matching against the keyword table is case-insensitive and exact (no abbreviations). The
argument string `cmdParm` is everything after the first space, left-trimmed (right side untrimmed
unless a command trims it). An unknown word yields the error `"<command line> -> unrecognized
command"`.

**FR-SCR-014 (symbol substitution timing)** If the line is not a comment and contains `$`,
symbol substitution (§5.4) is applied to the **whole line** before dispatch, **except** when the line
begins (case-insensitive) with `clear` (5 chars — this also covers `clearlist`), `plot `, `yaxis `,
`xaxis ` or `set user`. For lines beginning `input ` that contain exactly three commas, the 4th
argument (from the last comma) is excluded from substitution. Symbols in `PLOT`/`XAXIS`/`YAXIS`
are substituted later, per parameter, at load time (late binding), and the curve keeps the
*symbolic* name (FR-EXP-061).

---

## 5.3 Execution model

**FR-SCR-020 (states)** The interpreter shall model these states:
`Idle(0)`, `Running(1)`, `Paused(2)`, `TimedPause(3)`, `Calling(4)`, `Aborting(−1)`, `EndOfFile(−2)`,
`ExitRequested(−3)`. "Interactive" means state ≠ Running. While Running, menu actions invoked
programmatically by commands must not show prompts unless `SCRIPTPROMPT ON` (see FR-SCR-024).

**FR-SCR-021 (script stack)** A stack of `{fileName, byteOffset, lineNo, resume}` frames shall
implement pause/resume and `CALL`:
* `PAUSE` (no argument) or pressing **Space** while Running → push current frame (`resume=false`), go Idle.
* `CALL file` → push current frame (`resume=true`) and a new frame for `file` at offset 0, then start
  the top frame (asynchronously, after the current command completes).
* At natural EOF, if the stack is non-empty and the top frame has `resume=true`, resume it automatically.
* *Resume* (menu, Space while Idle with a paused script, toolbar) pops and continues the top frame.
* *Next* (step) sets "pause after next command" (mode per `PAUSEMODE`) then resumes.
* *Abort* asks "Cancel script execution?" (Running) or "Cancel paused scripts?" (stack non-empty)
  and clears the stack.
* Running a new script while a script is Running is refused ("Can't execute script. Cancel or pause
  current script first."). If a paused script exists, *Run* resumes it unless **Shift** is held.
* Recording and running are mutually exclusive (exact messages).

**FR-SCR-022 (script file lookup)** Running script `name`: strip `"` characters; try to open the name as
given (relative to the process current directory); if that fails, search the file name (without
directory) along the `ScriptPath` list (`;`-separated search list). On failure show
`Failure opening script <name>`. Running a script from the *Run* dialog sets the process current
directory to the script's folder.

**FR-SCR-023 (per-command loop)** After each command the UI shall be refreshed (pending UI
messages processed), so that plots appear progressively. The Space key is polled after each command.
Plugins are notified when a script starts and ends (`SCRIPTRUN_ON/OFF`, FR-PLG).

**FR-SCR-024 (prompting)** A dialog is shown for a command only if
`!HiddenMode && (Idle || ScriptPrompt)` (`SHOW_DIALOGS`). Error message boxes for invalid commands
are always shown (they block until dismissed) — see DEV-SCR-003 for hidden mode.

**FR-SCR-025 (error reporting)** A command that fails parsing shows a modal message
`"<preprocessed command> -> <reason>"`; default reason `invalid syntax`; specific reasons as listed
in §5.6. Execution continues with the next line.

**FR-SCR-026 (pause modes)** `PAUSEMODE NEXTCOMMAND|NEXTPLOT` determines what *Next* does: pause
after the next recognised command (`key > 0`) or after the next `PLOT` command.

**FR-SCR-027 (timed pause)** `PAUSE n` (seconds, float) shows `Paused <remaining>...` in the status
bar when n > 2, keeps the UI responsive, and ends early on *Resume*/*Pause* command or Space.
Granularity 100 ms. `PAUSE` is ignored when not running from a real script file (names starting `*`).

**FR-SCR-028 (exit)** `EXIT` ends the script and closes the application after the script finishes
(normal shutdown path, settings auto-save applies).

**FR-SCR-029 (script log)** `SCRIPTLOG file|ON|OFF` writes a log file; each entry is prefixed with
`h:mm:ssAM|PM mm/dd/yyyy` and two spaces; first line `Multiplot script log started`. The interpreter
and loaders write diagnostic messages there (e.g. load failures).

### 5.3.1 Script recording

**FR-SCR-040** *Scripting → Record* opens a save dialog (`*.wps`), creates the file and writes:
```
;Multiplot Script file (Multiplot generated)
;<version string>
```
Then every user action that has a script equivalent appends the canonical command text followed by
CRLF. *Stop Recording* closes the file. The menu text shows `&Stop Recording [<file>]` while active.

**FR-SCR-041** The canonical recorded forms shall be exactly those produced by
recording calls (they are valid input to the interpreter). Complete list (format strings):
`64BITTIME %s`, `AUTORUN %s`, `AUTORUN clear`, `AlignClosest %s`, `AutoTab %d %s`, `AutoTab %d`,
`AutoTab OFF`, `CAPTION USER %s`, `CLEAR FILES`, `CalcEveryTime %s`, `Caption %s|PARMSET|PLOTNO|SCRIPT|SUBTITLE|TITLE|USER`,
`ClearList`, `ColorPrinter %s`, `DATARATE 3D %d`, `DATARATE REBUILD %s`, `DBpath %s`, `DefTabTime %s`,
`DropoutIgnore %s`, `DropoutMarker %s`, `EMFWIDE %s`, `ExportPath %s`, `FIXXAUTOY %s`, `FilePath %s`,
`FirstOccurrence %s`, `FontSize %d`, `GMTFORMAT %s`, `GridOffset %d`, `INCLUDEGMT %s`, `IgnoreStatic %s`,
`LEGEND COLUMNS %d`, `LandScape %s`, `LegendShift %s`, `LoadSettings %s`, `MARKERSET TOPMOST %s`,
`OPEN %s`, `OptimizedDraw %s`, `PAN DOWN`, `PAN LEFT%s`, `PAN RIGHT%s`, `PAN UP`, `PLOT %s`,
`PresentationMode %s`, `PrinterMargin %.1f`, `SAVESUN %s`, `SCALEGROUP %s`, `SCALEREDLINES %s`,
`SCRIPTLOG %s`, `SCRIPTLOG OFF`, `SET BACKGROUND %s`, `SET COLOR PRINTERTABLE`, `SET COLOR RESTORE`,
`SET GROUPNAME %s%s`, `SET LOGIC INDEX %d`, `SET LOGIC TEXT %d %s,%s`, `SET PRECISION %s %s`,
`SET TRACE %d,LINE_SIZE=%d,LINE_STYLE=%d,USE_SYMBOLS=%d,SYMBOL_ID=%d,SCATTER_PLOT=%d,COLOR_INDEX=%d,TAB_LEGEND=%d,TAB_FORMAT=%s,LOGIC_INDEX=%d`,
`SET USER%d %s,%s`, `SHAREDTIME %s`, `SHOWCOORD %s`, `STATUSBAR %s`, `STREAMCERT %s`, `SYSTEMLOAD %s`,
`ScriptPath %s`, `ShowPageNo %s`, `ShowScript %s`, `ShowVersion %s`, `SymbolSize LARGE|NORMAL|SMALL`,
`Symbols %d`, `TAB NEXT`, `TAB PREVIOUS`, `TABHISTORY %s`, `TOOLBAR %s`, `TabStepRate %g`,
`WINDOW %d,%d,%d,%d`, `WPDpath %s`, `ZOOM IN X`, `ZOOM IN Y`, `ZOOM OUT X`, `ZOOM OUT Y`,
`applyglobal %s`, `autocompact %u %g`, `autodiscard %u`, `autolabel %s`, `boldframe %s`, `clear $$`,
`clear last`, `clear marker %d`, `clear markers all`, `clear plots`, `clocktime %s`, `copybmp CLIPBOARD`,
`datarate AVERAGE %d`, `datarate FULL`, `datarate SAMPLE %d`, `embeddedscript %s`, `endtoend %s`,
`export %s`, `export CLIPBOARD`, `extrapolate %s`, `fixx %g,%g`, `fixx %g,%g,%g`, `fixx AUTO`,
`fixy %g,%g`, `fixy AUTO`, `freeze %s`, `gridlines DASHED|DOTTED|OFF|SOLID`, `import CLIPBOARD`,
`legend %s`, `legend reserve %d`, `linesize %d`, `linesize 3DMEDIUM|3DTHIN|3DWIDE`, `loadsettings %s`,
`logX %s`, `logY %s`, `marker %d,%s,%f,%f,%f,%f,%s,%s%s`, `markerset load %s`, `markerset save %s`,
`minortics %s`, `multiy %s`, `pausemode %s`, `plot`, `rescale`, `refresh %g`, `refresh OFF`,
`savepic %s %s`, `savepic JPEG %s`, `scriptprompt %s`, `set color %s,%d,%d,%d %s`,
`set color LABEL,%d,%d,%d %s`, `set color TITLE,%d,%d,%d %s`, `showUserName %s`, `stairstep %s`,
`style 2DLINE|2DSCATTER|3DAREA|3DLINE|LOGIC|TABULAR`, `symbols %s`, `tabulate`, `testinfo %s`,
`tile %s|cascade|horizontal|stacked|vertical`, `time %s,%s,%s`, `timeslide %s`, `timestamp %s`,
`useINItoolbar %s`, `usePlaceholder %s`, `xaxis %s`, `xtitle %s`, `xtitle`, `yaxis %s`, `ytitle %s`,
`ytitle`, `enable|disable titles|subtitles` (`"%s titles"`). Recording of out-of-scope features
(print, EMF, mailslot) is not required.
*Acceptance:* TST-SCR-010 — record a session exercising every UI action; replaying the recording
reproduces the same plots (image comparison) and settings.

---

## 5.4 Symbols (variables)

**FR-SCR-050 (user symbols)** `SET NAME=value` defines a user symbol. The name is the text before
the first `=`, with all `$` removed, right-trimmed, upper-cased. The value is the text after `=`,
truncated at the first `;` (note: this includes a `;` produced from `\;`), then trimmed. An existing
symbol with the same name (case-insensitive) is replaced. The symbol table is kept sorted by name in
**descending** byte order (so longer names sharing a prefix are tried first).

**FR-SCR-051 (substitution algorithm)** Repeat while the working string contains `$`, taking the
**first** `$` each time:
1. For each user symbol in table order: compare the `len(name)` characters after `$`
   (right-trimmed) case-insensitively with the name; on match replace `$`+those characters by the value.
2. Else test, case-insensitively, whether the text after `$` *starts with* one of the system symbols
   below (in this order) and replace `$`+symbol-length characters by its value.
3. Else report `Undefined symbol: <rest of line from $, spaces removed>` (when reporting is on) and
   replace that `$` by `!`.
Because replacement restarts from the first `$`, a value containing `$` is itself substituted
(a self-referencing value would loop forever — the interpreter shall stop after 1,000 substitutions per
line and report an error; see DEV-SCR-004).

**FR-SCR-052 (system symbols)** in test order, with their values:

| Symbol | Value | Unavailable value |
|---|---|---|
| `PRINT_APP` | INI `[external applications]` text printer command | |
| `VIEW_APP` | edit/view application | |
| `EXPORT_APP` | export application | |
| `MAILSLOT_NAME` | comm name without leading `\`, or `Auto` | (out of scope: always `Auto`) |
| `SCRIPT_NAME` | current script path (`*internal` / `*External` for non-file commands) | |
| `SCRIPT_LINE` | current physical line number | |
| `CURRENT_FILE` | full path of the first open file | `<CURRENT_FILE>` |
| `FILE_LIST` | comma-separated full paths of all open files | `<FILE_LIST>` |
| `FILE_REFERENCE` | per file, reference GMT as `%03u/%02u:%02u:%02u.%03u`, comma-separated | `<FILE_REFERENCE>` |
| `EVENT_REFERENCE` | if > 1 group: each group's reference string, comma-separated; else the global reference | |
| `GMT_REFERENCE` | per group, resolved reference GMT as `ddd/hh:mm:ss.mmm.uuu` | `<GMT_REFERENCE>` per group; global reference if no files |
| `SCALE_XMIN`, `SCALE_XMAX` | active plot X scale min/max (`%g`) | `<SCALE_XMIN>` … |
| `SCALE_YMIN`, `SCALE_YMAX` | active plot Y scale(s) (`%g`); all scales comma-separated if multi-Y | `<SCALE_YMIN>` … |
| `PLOT_X_EXTENTS` | `xmin,xmax` | `<PLOT_X_EXTENTS>` |
| `PLOT_Y_EXTENTS` | first Y scale `ymin,ymax` | `<PLOT_Y_EXTENTS>` |
| `PLOT_PARMS` | active plot curve names, comma-separated | `<PLOT_PARMS>` |
| `PLOT_SOURCE` | `filepath;curvename` per curve, comma-separated | `<PLOT_SOURCE>` |
| `SYSTEM_TIME` | local time `%j/%H:%M:%S.000` | |
| `USERNAME` | Windows user name | `<USERNAME>` |
| `USERFOLDER` | user folder (09-settings) | `<USERFOLDER>` |
| `COMPUTER` | computer name | `<COMPUTER>` |
| `LAST_FILE_TIME` | GMT of last TIME point of first file, `%03u/%02u:%02u:%02u.%03u` | `<LAST_FILE_TIME>` |

`USER_INPUT` and `MCI_RETURN` are ordinary user symbols written by `INPUT` and `MCI`.
Quirk: `GMT_REFERENCE` consumes **16** characters (`$` + 15) although the name is 13
characters long, eating the two characters that follow it — see DEV-SCR-005 (preserve).

**FR-SCR-053** `CLEAR $name` removes one user symbol (all `$` removed from the argument, compared
case-insensitively against the full name); `CLEAR $$` removes all. Symbols persist across scripts
for the session and may be saved/loaded with settings (`[symbolics]`, 09-settings).

---

## 5.5 Common argument conventions

* **ON/OFF flags**: unless noted, `OFF` (case-insensitive, exact) disables; `ON` *or empty* enables;
  anything else is `invalid syntax`. Commands marked **(toggle-if-empty)** toggle when empty.
* **Apply-global**: many style commands affect the active plot only, or all plots when
  `APPLYGLOBAL ON`; the same value also becomes the default for new plots.
* **File names** in arguments may be quoted; relative export names are placed in `ExportPath`
  (`VALIDATE_FILENAME`): empty → `<ExportPath>\plotNNNN.<ext>` (NNNN = plot number, 4 digits);
  no `\` in name → prefixed with ExportPath if that folder exists.
* Numbers are parsed with C `atoi`/`atof` semantics (leading numeric prefix, else 0). The interpreter shall
  replicate this (invariant culture, no exceptions).

---

## 5.6 Command reference

Status column: **S** = supported, **I** = accepted and ignored (out of scope, logged to script log
and status bar; no error), **D** = deprecated (error message shown).

### 5.6.1 Flow control and interaction

| ID | Command | Syntax | Semantics | Status |
|---|---|---|---|---|
| FR-SCR-100 | `PAUSE` | `PAUSE [seconds]` | §5.3 FR-SCR-021/027 | S |
| FR-SCR-101 | `CALL` | `CALL file.wps` | nested script (stack). Empty → `missing filename` | S |
| FR-SCR-102 | `RUN` | `RUN commandline` | start external process (`WinExec`, shown); no wait | S |
| FR-SCR-103 | `EXIT` | `EXIT` | end script, then exit application | S |
| FR-SCR-104 | `PROMPT` | `PROMPT [text]` | modal OK message box (information icon); `|` → newline; empty → `Continue` | S |
| FR-SCR-105 | `INPUT` | `INPUT prompt[,default[,caption]][,$SYM]` | Text input dialog. The symbol name is everything after the first `$` in the argument (default `USER_INPUT`), the argument is cut at that `$` and trailing spaces/commas removed; then split at first `,` → prompt, rest → default; default split at its first `,` → caption. OK → executes `SET <sym>=<text>`. Cancel → nothing. | S |
| FR-SCR-106 | `PAUSEMODE` | `NEXTCOMMAND` \| `NEXTPLOT` | FR-SCR-026 | S |
| FR-SCR-107 | `SCRIPTPROMPT` | ON/OFF | allow dialogs during scripts; plugins notified | S |
| FR-SCR-108 | `SCRIPTLOG` | `file` \| `ON` \| `OFF` | FR-SCR-029. `ON` without a name → `script log name not set`; `ON` when on → `script log already on` | S |
| FR-SCR-109 | `EMBEDDEDSCRIPT` | ON/OFF | run `[script]` sections of loaded files | S |
| FR-SCR-110 | `AUTORUN` | `AUTORUN EVENT,script.wps` \| `AUTORUN CLEAR` | adds an entry (event name upper-cased up to the comma). No comma → error. See FR-RT | S |
| FR-SCR-111 | `LIBCOMMAND` | `LIBCOMMAND [0xSIG ]text` | send text to plugins (all, or the one whose signature = SIG); if SIG = SUN_FORMAT, i.e. `0xf0ffffff` (the mask read as a little-endian uint32; `0xfffffff0` does **not** match) also to the built-in SUN library (§FR-ACC). Empty → `missing command` | S |
| FR-SCR-112 | `EXTCMDPOLLING` | ON/OFF | mailslot polling | I |
| FR-SCR-113 | `PLAYSOUND` | `[file]` | | I |
| FR-SCR-114 | `MCI` | `mci command` | sets `MCI_RETURN` | I (sets `MCI_RETURN` to empty) |

### 5.6.2 Files, data selection and time

| ID | Command | Syntax | Semantics | Status |
|---|---|---|---|---|
| FR-SCR-120 | `OPEN` | `OPEN spec[,spec…] [ASDATABASE [id]]` | Load file(s)/group(s) (FR-LOAD). Without `ASDATABASE` the argument is passed to the group loader (a data file, a `.flt` filter, a group file, or a comma list). With `ASDATABASE`, the comma list before the keyword becomes **one** group named `id` (or derived from the first file name: text between last `\` and first `.`, max 12 chars; or `File Group` if id given empty) via a temporary group file `~id` in the temp folder, deleted after loading. If the list is empty a file dialog opens in group mode. Load failure (−1) aborts the script. Empty argument → Open dialog. | S |
| FR-SCR-121 | `NEW` | as OPEN | closes all files first (`CLEAR FILES`) then OPEN | S |
| FR-SCR-122 | `CLEAR` | `[PLOTS]` \| `FILES` \| `LAST` \| `MARKERS ALL` \| `MARKERS n[,m…]` \| `$name` \| `$$` | close all plot windows / close all files / close last opened file / clear markers (global set and active plot) / symbols. Argument is lower-cased, `markers` matched by substring; marker numbers parsed from char 8 then after each comma. | S |
| FR-SCR-123 | `PLOT` | `PLOT [parmlist]` | sets the Y parameter list (cleaned, FR-SCR-130) and adds it to the remembered list, then creates a plot window with current settings. Empty → plot current list. | S |
| FR-SCR-124 | `YAXIS` | `YAXIS [parmlist]` | set Y list without plotting; empty clears it | S |
| FR-SCR-125 | `XAXIS` | `XAXIS [parm]` | set X parameter (parameter vs parameter); empty → `(default)` i.e. time | S |
| FR-SCR-126 | `ADDPARAMETER` | `ADDPARAMETER parmlist` | add curves to the active plot | S |
| FR-SCR-127 | `TIME` | `TIME` \| `TIME MULTI` \| `TIME start,stop[,ref][,start2,stop2,ref2…]` \| `TIME grp:start,stop,ref` | see FR-SCR-131 | S |
| FR-SCR-128 | `TABULATE` | `TABULATE` | show current Y list as tabular text (View As Tabulation) | S |
| FR-SCR-129 | `FAVORITES` | `FAVORITES file` | set favourites file path | S |
| FR-SCR-132 | `CLEARLIST` | | clear remembered parameter list | S |
| FR-SCR-133 | `DATARATE` | `FULL` \| `AVERAGE[ n]` \| `SAMPLE[ n]` \| `3D n` \| `REBUILD [ON|OFF]` | plot data-reduction mode and densities (FR-PLT); argument upper-cased and prefix-tested; `n` after first space/comma, must be > 0 else keep previous; `3D` without n → `invalid density`; `REBUILD` contains `OFF` → off else on | S |
| FR-SCR-134 | `CALCEVERYTIME` | ON/OFF | expression timeline = union of times (FR-EXP) | S |
| FR-SCR-135 | `ALIGNCLOSEST` | ON/OFF | expression alignment by closest point (FR-EXP) | S |
| FR-SCR-136 | `FIRSTOCCURRENCE` | ON/OFF | event lookup uses first occurrence (FR-LOAD) | S |
| FR-SCR-137 | `EXTRAPOLATE` | ON/OFF | MULTITIME lead-in/trailing values (FR-SUN-071); plugins notified | S |
| FR-SCR-138 | `USEPLACEHOLDER` | ON/OFF | create placeholder plots when no data (FR-LOAD) | S |
| FR-SCR-139 | `64BITTIME` | ON/OFF | use double-precision time axis (FR-PLT) | S |
| FR-SCR-140 | `RETAINMTGMT` | any | error `Command is deprecated\n\nUse 64BITTIME [ON | OFF] instead`, then **also** executes as `64BITTIME` (fall-through) | D |

**FR-SCR-130 (parameter list cleaning)** For `PLOT`, `YAXIS`, `XAXIS`: trim; then repeatedly
replace `" ,"`→`","`, `", "`→`","`, `"[ "`→`"["`, `" ]"`→`"]"`.

**FR-SCR-131 (TIME parsing)** Empty → time-slice dialog (if dialogs allowed). `MULTI` →
per-group time dialog. Otherwise, loop: read `start` up to next comma; then `stop` up to next comma
(or end); then `ref` up to next comma (or end); each trimmed. If `start` contains `:` the text before
the **first** `:` is a group name: that group (matched case-insensitively by name) gets
(ref,start-after-colon,stop) and parsing **stops**. Otherwise triplet *k* (0-based) is applied to
groups *k..last* (so a single triplet applies to all groups, a second triplet overrides groups 1..).
The global defaults (`tstart`,`tstop`,`tref`) become the last triplet. If real-time data is loaded,
all non-frozen plots get the new reference, are flagged for reload, and a refresh is forced if auto
refresh is off.
Note: a clock-format start time such as `12:00:00` is mis-read as a group name (quirk,
preserve; users write times as `ddd/msec` or with a group prefix).

### 5.6.3 Import / export

| ID | Command | Syntax | Semantics | Status |
|---|---|---|---|---|
| FR-SCR-150 | `IMPORT` | `IMPORT CLIPBOARD` \| `IMPORT src [dst] [/In] [/Sn] [/Tn] [/G] [/F] [/R ref]` \| `IMPORT` | ASCII import (10-import-export). Options start at first `/` (case-insensitive): `/I` index column (default 1), `/S` lines to skip, `/T` title record line, `/G` generate TimeTag, `/F` standard format (COMMONTIME instead of MULTITIME64), `/R` reference time (to next `/`). Destination separated by the first `,`, or `" `, or ` "`, or (if no quotes) first space. Quotes removed. Failure → `Import Failed.` Empty → import dialog. | S |
| FR-SCR-151 | `EXPORT` | `EXPORT [file]` \| `EXPORT CLIPBOARD` | export active plot as comma-delimited text (FR-XPT) | S |
| FR-SCR-152 | `SAVESUN` | `SAVESUN [file]` | export active plot to SUN MULTITIME (FR-XPT); no plot → `No existing plot data to save` | S |
| FR-SCR-153 | `SAVEPIC` | `SAVEPIC` \| `SAVEPIC type [file\|CLIPBOARD\|ALL]` | type ∈ {BMP, JPEG (alias JPG), GIF, TIFF, PNG}; `ALL` saves every plot with default names; empty → bitmap to clipboard. Unknown type → `Format type invalid or not supported`; no plot → `Nothing to save`. `EMF` → see DEV-XPT-001 | S |
| FR-SCR-154 | `COPYBMP` / `COPYJPEG` | `[file\|ALL\|CLIPBOARD]` | save active (or all) plot(s) as BMP / JPEG; CLIPBOARD copies bitmap | S |
| FR-SCR-155 | `COPYMETA` | same | EMF | I (CLIPBOARD → copies bitmap instead) |
| FR-SCR-156 | `DATASTATS` | `DATASTATS [file]` | statistics of the visible X range of active plot (FR-XPT-040, FR-XPT-041) | S |
| FR-SCR-157 | `SAVEPARMLIST` | `SAVEPARMLIST n,file` (n = file number 1-based, 0 = all) | writes for each file: path, item count, then `name1,name2,units,description,seqNum` lines. Missing parts → `incomplete command` | S |
| FR-SCR-158 | `SAVEPARMDETAILS` | `SAVEPARMDETAILS n,file` (n = curve number, 0 = list) | writes curve list or details (exact format, `DetailsWriter`) | S |
| FR-SCR-159 | `SAVEFILEDETAILS` | `SAVEFILEDETAILS n,file` (0 = list) | writes file list or file header + comment block (exact format, `DetailsWriter`) | S |

### 5.6.4 Axes, scaling, view

| ID | Command | Syntax | Semantics | Status |
|---|---|---|---|---|
| FR-SCR-170 | `FIXX` / `FIXY` | `lo,hi[,inc][,p]` \| `AUTO` \| (FIXY only) `MULTI lo,hi,inc|a[,lo,hi,inc|a…][ p]` | see FR-SCR-171 | S |
| FR-SCR-172 | `FIXXAUTOY` | ON/OFF | when X is fixed, autoscale Y to visible data | S |
| FR-SCR-173 | `RESCALE` | | reset both axes to auto | S |
| FR-SCR-174 | `ZOOM` | `[IN\|OUT] [X\|Y]` | argument lower-cased; contains `out` → zoom out, else contains `in` → zoom in; then `x`/`y` substring selects the axis, else both; empty → zoom in both | S |
| FR-SCR-175 | `PAN` | `LEFT\|RIGHT [PAGE\|FULL]` \| `UP` \| `DOWN` | shift X by increment / page / to min-max; shift Y | S |
| FR-SCR-176 | `MULTIY` | ON/OFF \| `GROUPBY_NAME\|UNITS\|DESCRIPTION\|UDEF\|INDEPENDENT\|<n>` | multiple Y scales and grouping (any other `GROUPBY_x` → INDEPENDENT; numeric = command id, FR-CFG-040) | S |
| FR-SCR-177 | `LOGX` / `LOGY` | ON/OFF | log axis | S |
| FR-SCR-178 | `ENDTOEND` | ON/OFF | end-to-end display of multiple files (FR-PLT) | S |
| FR-SCR-179 | `SCALEGROUP` | ON/OFF | use `\GROUP` scale ids from descriptions | S |
| FR-SCR-180 | `SCALEREDLINES` | ON/OFF | include redline markers in autoscale | S |
| FR-SCR-181 | `CLOCKTIME` | ON/OFF | X axis in clock (GMT) format | S |
| FR-SCR-182 | `GMTFORMAT` | `[fmt]` | clock format; user tokens `DDD/`, `HR`, `MN`, `SC`, `.MIL`, `DDD` converted to C format (`MAKE_C_FMT`); empty → `%03u/%02u:%02u:%02u.%03u` | S |
| FR-SCR-183 | `INCLUDEGMT` | ON/OFF | include GMT column in tabular/export | S |
| FR-SCR-184 | `TIMESLIDE` | ON/OFF | real-time X window slides with new data | S |
| FR-SCR-185 | `FREEZE` | ON/OFF | freeze active real-time plot; not real-time → `current data not realtime` | S |

**FR-SCR-171 (FIXX/FIXY parsing)** Argument lower-cased.
* `FIXY multi …` (must start with `multi`): persist = argument contains `p` at index > 0; tokens after
  the first space are read in triplets split by `,`: low (`a…` → auto = dropout sentinel), high (same),
  increment (`a…` → best-fit, else |value| fixed). Each complete triplet is assigned to Y axis 0,1,2…;
  an incomplete triplet stops parsing; zero triplets → error.
* starts with `auto` → re-enable autoscale for that axis.
* otherwise needs a comma: low = atof(arg), high = atof(after first comma); swapped if low > high.
  If a third field exists: if its **first character** is not `p` → fixed increment = |atof(field)|;
  else best-fit with the given range; if the field contains `p` anywhere → persist (the range is kept
  for future plots). FIXY applies the same range to all Y axes. No comma → `invalid syntax`.
  Quirk preserved: `fixy 0,100, p` (space before p) yields fixed increment 0 plus persist — the
  scaler must treat increment 0 as best-fit (see DEV-SCR-006).

### 5.6.5 Style and appearance

| ID | Command | Syntax | Semantics | Status |
|---|---|---|---|---|
| FR-SCR-200 | `STYLE` | `2DLINE\|2DSCATTER\|3DLINE\|3DAREA\|LOGIC\|TABULAR` | plot style of active (or all) plot | S |
| FR-SCR-201 | `GRIDLINES` | ON/OFF/`SOLID`/`DOTTED`/`DASHED` | | S |
| FR-SCR-202 | `GRIDSTYLE` | `SOLID\|DASHED\|DOTTED` | also turns grid on | S |
| FR-SCR-203 | `GRIDOFFSET` | `n` | minimum grid offset, |n| clamped to 90 (pixels/percent per FR-PLT) | S |
| FR-SCR-204 | `MINORTICS` | ON/OFF | | S |
| FR-SCR-205 | `BOLDFRAME` | ON/OFF | | S |
| FR-SCR-206 | `FONTSIZE` | `1..4` | else `out of range` | S |
| FR-SCR-207 | `LINESIZE` | `d` (single digit) \| `3DTHIN\|3DMEDIUM\|3DWIDE` | 2D line width / 3D depth (3,2,1) | S |
| FR-SCR-208 | `SYMBOLS` | `ON` \| `OFF` \| `0` \| `n>0` | n sets density (every n-th point) and turns symbols on | S |
| FR-SCR-209 | `SYMBOLSIZE` | `NORMAL\|SMALL\|LARGE` | | S |
| FR-SCR-210 | `STAIRSTEP` | ON/OFF | step rendering | S |
| FR-SCR-211 | `LEGEND` | ON/OFF/`SINGLE`/`DOUBLE`/`COLOR`/`NOCOLOR`/`RESERVE n`/`COLUMNS n` | columns ≤ 64, reserve ≤ 32 (silently ignored if larger) | S |
| FR-SCR-212 | `LEGENDSHIFT` | ON/OFF | | S |
| FR-SCR-213 | `XNOMEN` / `YNOMEN` | item list | legend / axis nomenclature, FR-SCR-230 | S |
| FR-SCR-214 | `XUDEF` / `YUDEF` | text | user-defined nomenclature text (may contain title keywords) | S |
| FR-SCR-215 | `XTITLE` / `YTITLE` | `ON`\|`OFF`\|`CLEAR`\|`[next=]text` | plot title / subtitle; `next=` (case-insensitive, removed from text) sets it only for subsequent plots; otherwise applies to current plot(s) and, unless apply-global, does not change the *continue* flag. Text supports title keywords and `\n`, `\t` (FR-PLT-001, FR-PLT-002) | S |
| FR-SCR-216 | `ENABLE` / `DISABLE` | `MARKERS`\|`TITLES`\|`SUBTITLES`\|`PCGHISTORY`\|`PLUGIN name` | marker usage; title continuation; stream history; plugin block list (registry, FR-PLG) | S |
| FR-SCR-217 | `AUTOLABEL` | ON/OFF | automatic axis labels | S |
| FR-SCR-218 | `TIMESTAMP`, `TESTINFO`, `SHOWVERSION`, `SHOWUSERNAME`, `SHOWPAGENO`, `SHOWSCRIPT` | ON/OFF | plot footer items | S |
| FR-SCR-219 | `CAPTION` | `PARMSET\|PLOTNO\|SUBTITLE\|TITLE\|SCRIPT\|USER [text]\|ON\|OFF` | window caption source; `USER text` sets the caption of the current plot; `OFF`/`ON` hide/show caption bars | S |
| FR-SCR-220 | `SET` (style forms) | see §5.6.7 | | S |
| FR-SCR-221 | `APPLYGLOBAL` | ON/OFF | apply style commands to all plots | S |
| FR-SCR-222 | `DROPOUTIGNORE` / `DROPOUTMARKER` | ON/OFF | dropout drawing (FR-PLT) | S |
| FR-SCR-223 | `MARKER` | §5.6.6 | | S |
| FR-SCR-224 | `MARKERSET` | `TOPMOST [OFF]` \| `SAVE file` \| `LOAD file` | marker draw order; `.markers` file I/O (FR-MRK); argument must be > 5 chars else `incomplete command` | S |
| FR-SCR-225 | `OPTIMIZEDDRAW` | ON/OFF | pixel min/max reduction (FR-PLT) | S |
| FR-SCR-226 | `SHOWCOORD` | ON/OFF | cursor coordinates in status bar | S |
| FR-SCR-227 | `EMFWIDE` | ON/OFF | | I |

**FR-SCR-230 (XNOMEN/YNOMEN item list)** The argument is upper-cased; `" :"`/`": "` become `:`;
items are recognised by substring search: `DBNAME`, `FILE`/`FILENAME`, `NAME` (at start or preceded
by space or `+`), `UNITS`, `DESC`/`DESCRIPTION`, `REFT`, `UDEF`, `ALTNAME`, `EXTNAME`, and on Y only
`SEQNUM`, `CURRENTVALUE`, `CURRENTTIME`. An item may be followed by `:n` (fixed field length 0..256;
outside → `invalid length found (n)` and 0 = variable). The display order is the order of the
items' positions in the text, remaining items keep the default order
`DBNAME, FILENAME, NAME, UNITS, DESC, REFT, UDEF, ALTNAME, EXTNAME, SEQNUM, CURVAL, CURTIME`.
`TITLE` anywhere enables title continuation; `EFORMAT` selects exponential number format.

### 5.6.6 MARKER

**FR-SCR-240** `MARKER n,type,x1,y1,x2,y2,color,label[\g]`
* `\g` or `\G` anywhere → global (apply to all plots); removed before parsing.
* `n` 1..1000 (else error).
* `type`: `TEXT|REDLINE|LINE|BOX|CIRCLE|EVENT|IMAGE` (case-insensitive) or a number 0..6; anything
  else → TEXT.
* x1,y1,x2,y2: floats (data coordinates, or percent when the label carries `\%`, FR-MRK).
* color: `GRID|LABEL|BACKGROUND` or 1..103 (→ index n−1); invalid → LABEL. **The marker is only
  accepted if a color field is present.**
* label: text after the **last** comma, trimmed (empty if absent). Hence labels cannot contain commas.
* REDLINE markers on a non-fixed Y scale trigger an autoscale reset including redlines.
* The master marker set is updated, as are the active plot (or all plots when global).

### 5.6.7 SET

**FR-SCR-250** `SET` dispatch, in this order (first match wins):

| Form | Semantics |
|---|---|
| `SET TRACE …` / `SET LOGIC …` | FR-SCR-251 / FR-SCR-252 |
| `SET COLOR …` | rest upper-cased. Contains `PRINTERTABLE` → run ColorPrinter.wps (saving current colours for RESTORE). Contains `RESTORE` → restore saved colour table. Else target: contains `GRID` / `LABEL` / `TITLE` / `BACKGROUND`, else `atoi−1` in 0..99. Then r,g,b after the 1st,2nd,3rd comma (`atoi % 256`). Missing any → error. `GLOBAL` anywhere (or apply-global on) → all plots. |
| `SET PRECISION t d` | t,d ∈ `HEX2 HEX4 HEX8 OCT3 OCT6 OCT12 GENERAL 0..7` (space or comma separated); sets tabular time and data formats (−7..7). Unknown tokens leave the value unchanged. |
| `SET BACKGROUND file` | background bitmap for tabular display |
| `SET GROUPNAME [next=\|all=]name` | `next=` sets the group name for subsequent plots; `all=` applies to all plots; otherwise current plot and default |
| `SET USERn label,command` | n = 1..5 (else `invalid function number`); label and command required (else `missing parameter`); updates menu text and toolbar button |
| `SET NAME=value` | user symbol (FR-SCR-050) |
| anything else | `invalid syntax` |

**FR-SCR-251 (SET TRACE)** Requires an active plot. Format 2 is used if the argument contains `=`.
* Format 1: `SET TRACE t,lineSize(1-3),lineStyle(1-5),useSymbols(0/1),symbolId(1-100),scatter(0/1),colorIndex(1-100),tabLegend(0/1),tabFormat(-7..7),logicIndex(0-3)`
* Format 2: `SET TRACE t, KEY=v, …` keys `LINE_SIZE, LINE_STYLE, USE_SYMBOLS, SYMBOL_ID, SCATTER_PLOT, COLOR_INDEX, TAB_LEGEND, TAB_FORMAT (HEX2|HEX4|HEX8|OCT3|OCT6|OCT12|GENERAL|DECn), LOGIC_INDEX (1-4)`; spaces removed, upper-cased.
* t is 1-based; out-of-range trace → no effect. Out-of-range values are ignored per field.
  Note the inconsistency: LOGIC index is 0–3 in format 1 but 1–4 in format 2 (preserve).
* With apply-global, the same trace index in every other plot receives line size, style, scatter,
  colour, symbol id, use-symbols and tab format (not tab legend or logic index).

**FR-SCR-252 (SET LOGIC)** `SET LOGIC INDEX n` (1..4) sets the default logic label set and applies
it to all traces of the active plot (and all plots if apply-global). `SET LOGIC TEXT n true,false`
— n is a **single digit** (the character after `TEXT ` is consumed as one digit), then separators
`, ` trimmed, then text up to the first comma = true label, remainder = false label.

### 5.6.8 Windows, layout, presentation

| ID | Command | Syntax | Semantics | Status |
|---|---|---|---|---|
| FR-SCR-260 | `WINDOW` | `MINIMIZE\|MAXIMIZE\|RESTORE\|l,t,r,b` | main window state/position (position requires all four values and t ≠ b) | S |
| FR-SCR-261 | `TILE` | `HORIZONTAL`(default)\|`VERTICAL`\|`CASCADE`\|`STACKED`\|`OFF`\|`groupname` | arrange plot windows; `OFF` maximises active plot; other text tiles plots of that group | S |
| FR-SCR-262 | `TAB` | `NEXT`(default)\|`PREVIOUS` | activate next/previous plot | S |
| FR-SCR-263 | `AUTOTAB` | `seconds[ ,group]` | cycle plots on a timer (0 = off); optional group restriction | S |
| FR-SCR-264 | `TOOLBAR` / `STATUSBAR` | ON/OFF (toggle-if-empty) | | S |
| FR-SCR-265 | `USEINITOOLBAR` | ON/OFF | load toolbar layout from INI | S |
| FR-SCR-266 | `PRESENTATIONMODE` / `PRESENTATIONFULL` | ON/OFF | full-screen presentation modes (FR-UI) | S |
| FR-SCR-267 | `SYSTEMLOAD` | ON/OFF (anything ≠ OFF = ON) | CPU/memory in status bar | S |
| FR-SCR-268 | `STREAMCERT` | ON/OFF (≠ OFF = ON) | stream certification status display (FR-RT) | S |
| FR-SCR-269 | `TABHISTORY` / `DEFTABTIME` / `TABSTEPRATE r` / `SHAREDTIME` | | tabular display options (FR-TAB); `TABSTEPRATE` r ≥ 0 else `out of range` | S |

### 5.6.9 Settings and paths

| ID | Command | Syntax | Semantics | Status |
|---|---|---|---|---|
| FR-SCR-280 | `LOADSETTINGS` | `[file.ini]` | load INI (current file if empty); not found → `Specified INI file not found` | S |
| FR-SCR-281 | `SAVESETTINGS` | `[file.ini]` | save INI | S |
| FR-SCR-282 | `FILEPATH` / `SCRIPTPATH` / `EXPORTPATH` / `WPDPATH` | `path` | default folders (ExportPath trailing `\` removed) | S |
| FR-SCR-283 | `DBPATH` | `path` | DB_DIR folder | I (value stored and persisted) |
| FR-SCR-284 | `REFRESH` | `seconds` \| `OFF`/`0…` \| other | > 0 → start/restart auto refresh; `OFF` or starting with `0` while auto-refresh on → stop; **otherwise** (including `OFF` when not running) → one immediate refresh | S |
| FR-SCR-285 | `AUTOCOMPACT` | `points[ ,interval]` | real-time compaction threshold (interval default 1.0); empty → `missing parameters` | S |
| FR-SCR-286 | `AUTODISCARD` | `seconds` | real-time discard window | S |
| FR-SCR-287 | `IGNORESTATIC` | ON/OFF | skip refreshing non-dynamic files | S |
| FR-SCR-288 | `PRINT`, `PRINTERNAME`, `PRINTERMARGIN`, `COLORPRINTER`, `LANDSCAPE` | | printing | I (values of PRINTERMARGIN 0..10, COLORPRINTER, LANDSCAPE, PRINTERNAME are stored & persisted for INI round-trip) |

---

## 5.7 Command line

**FR-SCR-300** Command-line switches (case-insensitive, removed from the line before file
processing): `/noplugins` (do not load plugins), `/default` (load the INI next to the executable
instead of the user INI, then keep the user INI name for saving), `/hide` (run hidden: main window
hidden, no dialogs), `/nosave` (do not auto-save window position/settings on exit). `/comm/name` is
accepted and ignored (out of scope).
**FR-SCR-301** Remaining arguments: spaces outside double quotes separate arguments; quotes removed.
Each argument is processed in order by extension: `.wps` → run script (synchronously, then state
reset to idle); `.ini` → load settings from it; `.csv` → ASCII import; anything else → load as data
file / group.
*Acceptance:* `Multiplot.exe /hide test.wps` runs a script that saves a PNG and exits, with no window
shown (TST-SCR-020).

---

## 5.8 Interpreter implementation requirements

**NFR-SCR-001** The interpreter shall run on the UI dispatcher but must never block it: waits
(`PAUSE n`, loading, refresh) are `async` and pump the dispatcher; Space/Esc and toolbar Pause/Abort
remain responsive.
**NFR-SCR-002** The parser shall be a separate, unit-testable component (`ScriptLexer`,
`CommandParser`) that converts a raw line into a typed command object without touching UI state, so
that every rule in §5.2 and every syntax in §5.6 is covered by table-driven unit tests (TST-SCR-001).
**NFR-SCR-003** Command handlers shall call the same application services as the corresponding
menu commands (single code path for UI and scripts), so that recording and replay are equivalent.
