# Scripts

A script (`.wps`) is a text file with one command per line. You can run a script in several ways:

* *Script → Run*, or drag the file onto the window.
* Put it on the command line: `Multiplot.exe test.wps`.
* Call it from another script with `CALL file.wps`.
* Assign it to a user button (`SET USERn label,CALL file.wps`).
* Have it started by an AutoRun event (see [Real-time](realtime.md)).

```
; overnight report
open C:\data\run12.sun
time IGNITION-5,IGNITION+30,IGNITION
xtitle Run 12 - chamber
plot PC1,PC2,[PC1]-[PC2]"Delta P"
savepic PNG C:\reports\run12.png
export C:\reports\run12.csv
exit
```

## Lexical rules

* Command names are not case-sensitive. Arguments are passed on as written.
* `;` and `//` start a comment, except inside commands whose argument is taken literally.
* A line ending in `\` continues on the next line.
* Symbols such as `$NAME` are replaced before the command runs (see below). Substitution stops
  after 1000 replacements, which protects against symbols that refer to themselves.
* ON/OFF arguments: `ON`, or an empty argument where the command allows it, means on.

## Running, pausing and stopping

While a script runs, Space pauses it, Ctrl+Space resumes it and Ctrl+Esc stops it. The *Script* menu has Execute, GoTo Next,
Pause and Abort. `PAUSE` waits for the user; `PAUSE n` waits n seconds while the program stays responsive.
`PAUSEMODE NEXTCOMMAND|NEXTPLOT` sets where Next stops. Commands that would open a dialog run
without it unless `SCRIPTPROMPT ON` is set. `SCRIPTLOG file|ON|OFF` writes a time-stamped log of
script messages.

## Recording

*Script → Record* writes each menu and mouse action that changes the plot to a script as
the command that does the same thing (for example `fixx 10,20` after a zoom). Replaying the
recording reproduces the session.

## Symbols

`SET NAME=value` defines a symbol, and `$NAME` uses it. `INPUT prompt[,default[,caption]][,$SYM]`
asks the user for a value. `CLEAR $NAME` removes one symbol and `CLEAR $$` removes them all.

The program provides these system symbols:

`SCRIPT_NAME`, `SCRIPT_LINE`, `CURRENT_FILE`, `FILE_LIST`, `FILE_REFERENCE`, `EVENT_REFERENCE`,
`GMT_REFERENCE`, `SCALE_XMIN`, `SCALE_XMAX`, `SCALE_YMIN`, `SCALE_YMAX`, `PLOT_X_EXTENTS`,
`PLOT_Y_EXTENTS`, `PLOT_PARMS`, `PLOT_SOURCE`, `SYSTEM_TIME`, `USERNAME`, `USERFOLDER`, `COMPUTER`,
`LAST_FILE_TIME`, `PRINT_APP`, `VIEW_APP`, `EXPORT_APP`, `MAILSLOT_NAME` (always `Auto`).

## Command reference

The exact syntax and error messages for each command are in `docs/requirements/05-script-language.md`.

### Flow and interaction
| Command | Purpose |
|---|---|
| `PAUSE [s]`, `PAUSEMODE` | Wait for the user or for s seconds |
| `CALL file` | Run a nested script |
| `RUN commandline` | Start an external program |
| `EXIT` | End the script and close Multiplot.NET |
| `PROMPT text` | Message box (`\|` starts a new line) |
| `INPUT ...` | Ask the user for a symbol value |
| `SCRIPTPROMPT`, `SCRIPTLOG`, `EMBEDDEDSCRIPT` | Allow dialogs, keep a log, run `[script]` sections found in data files |
| `AUTORUN EVENT,script` / `AUTORUN CLEAR` | Run a script when a real-time event occurs |
| `LIBCOMMAND [0xSIG ]text` | Send a command to the data plugins and the SUN library |

### Files, data and time
| Command | Purpose |
|---|---|
| `OPEN spec[,spec] [ASDATABASE [id]]`, `NEW` | Load files or groups (`NEW` closes the open files first) |
| `CLEAR [PLOTS\|FILES\|LAST\|MARKERS ...\|REDLINES ...\|$sym\|$$]` | Close or clear things |
| `PLOT [list]`, `YAXIS list`, `XAXIS [parm]`, `ADDPARAMETER list` | Create plots and choose parameters |
| `TIME start,stop[,ref]`, `TIME MULTI`, `TIME grp:...` | Time slice and reference |
| `TABULATE`, `FAVORITES file`, `CLEARLIST` | |
| `DATARATE FULL\|AVERAGE n\|SAMPLE n\|3D n\|REBUILD` | Data reduction |
| `CALCEVERYTIME`, `ALIGNCLOSEST`, `FIRSTOCCURRENCE`, `EXTRAPOLATE`, `USEPLACEHOLDER`, `64BITTIME` | Loading options |

### Import and export
| Command | Purpose |
|---|---|
| `IMPORT src [dst] [/In] [/Sn] [/Tn] [/G] [/F] [/R ref]`, `IMPORT CLIPBOARD` | Import ASCII data |
| `EXPORT [file\|CLIPBOARD]` | Export the active plot as CSV |
| `SAVESUN [file]` | Export the active plot as a SUN MULTITIME file |
| `SAVEPIC type [file\|ALL\|CLIPBOARD]`, `COPYBMP`, `COPYJPEG` | Save a picture (BMP, JPEG, GIF or PNG) |
| `DATASTATS [file]` | Statistics for the visible range |
| `SAVEPARMLIST n,file`, `SAVEPARMDETAILS n,file`, `SAVEFILEDETAILS n,file` | Write parameter lists and details |

### Axes and view
`AUTOSCALE X|Y|XY [ON|OFF]`, `FIXX`, `FIXY`, `RESCALE`, `ZOOM`, `PAN`, `MULTIY`, `LOGX`, `LOGY`, `ENDTOEND`,
`SCALEGROUP`, `CLOCKTIME`, `GMTFORMAT`, `INCLUDEGMT`, `FREEZE`. `FIXXAUTOY` and `TIMESLIDE` no longer exist (see
[Scales and autoscaling](plot-windows.md#scales-and-autoscaling)).

### Style
`STYLE`, `GRIDLINES`, `GRIDSTYLE`, `GRIDOFFSET`, `MINORTICS`, `BOLDFRAME`, `LINESIZE`,
`SYMBOLS`, `SYMBOLSIZE`, `STAIRSTEP`, `LEGEND`, `LEGENDSHIFT`, `XNOMEN`, `YNOMEN`, `XUDEF`, `YUDEF`,
`APPEARANCE LIGHT|DARK` (light or dark look of the application and the plots, see
[Colors and appearance](plot-windows.md#colors-and-appearance)),
`XTITLE`, `YTITLE`, `ENABLE`, `DISABLE`, `AUTOLABEL`, `TESTINFO` (test title in the bottom-left
corner), `CAPTION` (`CAPTION USER text` sets the plot window title), `APPLYGLOBAL`, `DROPOUTIGNORE`,
`DROPOUTMARKER` (see [Dropouts](plot-windows.md#dropouts)), `OPTIMIZEDDRAW`, `SHOWCOORD`.

Retired, accepted and ignored so that old scripts still run: `TIMESTAMP`, `SHOWVERSION`,
`SHOWUSERNAME`, `SHOWPAGENO`, `SHOWSCRIPT` (those texts are no longer drawn), `SET GROUPNAME ...`
(plot group names were removed; `TILE name` tiles all plots and the group of `AUTOTAB n group` is
ignored). The title keyword `[groupname]` gives an empty text.

`FONTSIZE n` (the old screen scaling 1–4) is still accepted so that old scripts run, but it does
nothing: the text size comes from the label and title fonts (see [Fonts](plot-windows.md#fonts)).

### Event and redline markers
See [Event and redline markers](markers.md).

| Command | Purpose |
|---|---|
| `MARKER timestamp,text[,TOP\|BOTTOM]` | Add an event marker to the active plot (all plots with `APPLYGLOBAL ON`) |
| `REDLINE value,text[,LEFT\|RIGHT]` | Add a redline marker (horizontal line at a value on the selected value axis) to the active plot (all plots with `APPLYGLOBAL ON`) |
| `MARKERSET TOPMOST [OFF]` | Draw event markers and redlines over the curves |
| `MARKERSET FILEEVENTS [OFF]` | Draw the `[events]` of the data files as markers |
| `MARKERSET SAVE file`, `MARKERSET LOAD file` | Write or read a `.markers` file (event markers and redlines) |
| `CLEAR MARKERS ALL`, `CLEAR MARKER[S] n[,m…]` | Remove all event markers, or event markers by position in the list |
| `CLEAR REDLINES ALL`, `CLEAR REDLINE[S] n[,m…]` | Remove all redlines, or redlines by position in the list |
| `ENABLE MARKERS`, `DISABLE MARKERS` | Copy the current event markers and redlines to new plots (Continue Markers) |
| `SET COLOR MARKER,r,g,b [GLOBAL]`, `SET COLOR REDLINE,r,g,b [GLOBAL]` | Colour of the event markers / redlines |

The commands `TOOLBAR`, `USEINITOOLBAR` and `SCALEREDLINES` no longer exist (there are no toolbars, and
redlines are not part of autoscaling). Scripts that use them, or the old
`MARKER n,type,x1,y1,x2,y2,color,label` form, report an error on that line.

`SET` forms:
* `SET TRACE t,size,style,symbols,symbol,scatter,color,tablegend,tabformat,logic`
* `SET TRACE t, KEY=value, …`
* `SET LOGIC INDEX n`
* `SET LOGIC TEXT n true,false`
* `SET COLOR n|GRID|MARKER|REDLINE|LABEL|TITLE|BACKGROUND,r,g,b [GLOBAL]`
* `SET COLOR PRINTERTABLE|RESTORE`
* `SET PRECISION time data`
* `SET BACKGROUND file`
* `SET USERn label,command`
* `SET NAME=value`

### Windows and settings
`WINDOW`, `TILE`, `TAB`, `AUTOTAB`, `STATUSBAR`, `PRESENTATIONMODE`,
`PRESENTATIONFULL`, `SYSTEMLOAD`, `STREAMCERT`, `TABHISTORY`, `DEFTABTIME`, `TABSTEPRATE`,
`SHAREDTIME`, `LOADSETTINGS`, `SAVESETTINGS`, `FILEPATH`, `SCRIPTPATH`, `EXPORTPATH`, `WPDPATH`,
`DBPATH`, `REFRESH`, `AUTOCOMPACT`, `AUTODISCARD`, `IGNORESTATIC`.

### Accepted for compatibility
These commands are accepted and do nothing, or only store their value for the INI:
`PRINT`, `PRINTERNAME`, `PRINTERMARGIN`, `COLORPRINTER`, `LANDSCAPE`, `EMFWIDE`,
`EXTCMDPOLLING`, `PLAYSOUND`, `MCI` (sets `MCI_RETURN` to empty).

`COPYMETA` and `SAVEPIC EMF` save a PNG instead, and write a note to the script log.

## Running without a window

```
Multiplot.exe /hide /nosave report.wps
```

`/hide` runs the program without showing the main window. Every dialog takes its default answer.
Messages go to the diagnostic log in `%LOCALAPPDATA%\Multiplot\logs`. The script should end with
`EXIT`. The exit code is 0.
