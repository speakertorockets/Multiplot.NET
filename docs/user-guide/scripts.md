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

While a script runs, Space pauses it, Ctrl+Space resumes it and Ctrl+Esc stops it. The toolbar has Run, Pause, Next and Stop
buttons. `PAUSE` waits for the user; `PAUSE n` waits n seconds while the program stays responsive.
`PAUSEMODE NEXTCOMMAND|NEXTPLOT` sets where Next stops. Commands that would open a dialog run
without it unless `SCRIPTPROMPT ON` is set. `SCRIPTLOG file|ON|OFF` writes a time-stamped log of
script messages.

## Recording

*Script → Record* writes each menu, toolbar and mouse action that changes the plot to a script as
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
| `CLEAR [PLOTS\|FILES\|LAST\|MARKERS ...\|$sym\|$$]` | Close or clear things |
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
`FIXX`, `FIXY`, `FIXXAUTOY`, `RESCALE`, `ZOOM`, `PAN`, `MULTIY`, `LOGX`, `LOGY`, `ENDTOEND`,
`SCALEGROUP`, `SCALEREDLINES`, `CLOCKTIME`, `GMTFORMAT`, `INCLUDEGMT`, `TIMESLIDE`, `FREEZE`.

### Style
`STYLE`, `GRIDLINES`, `GRIDSTYLE`, `GRIDOFFSET`, `MINORTICS`, `BOLDFRAME`, `FONTSIZE`, `LINESIZE`,
`SYMBOLS`, `SYMBOLSIZE`, `STAIRSTEP`, `LEGEND`, `LEGENDSHIFT`, `XNOMEN`, `YNOMEN`, `XUDEF`, `YUDEF`,
`XTITLE`, `YTITLE`, `ENABLE`, `DISABLE`, `AUTOLABEL`, `TIMESTAMP`, `TESTINFO`, `SHOWVERSION`,
`SHOWUSERNAME`, `SHOWPAGENO`, `SHOWSCRIPT`, `CAPTION`, `APPLYGLOBAL`, `DROPOUTIGNORE`,
`DROPOUTMARKER`, `MARKER`, `MARKERSET`, `OPTIMIZEDDRAW`, `SHOWCOORD`.

`SET` forms:
* `SET TRACE t,size,style,symbols,symbol,scatter,color,tablegend,tabformat,logic`
* `SET TRACE t, KEY=value, …`
* `SET LOGIC INDEX n`
* `SET LOGIC TEXT n true,false`
* `SET COLOR n|GRID|LABEL|TITLE|BACKGROUND,r,g,b [GLOBAL]`
* `SET COLOR PRINTERTABLE|RESTORE`
* `SET PRECISION time data`
* `SET BACKGROUND file`
* `SET GROUPNAME [next=|all=]name`
* `SET USERn label,command`
* `SET NAME=value`

### Windows and settings
`WINDOW`, `TILE`, `TAB`, `AUTOTAB`, `TOOLBAR`, `STATUSBAR`, `USEINITOOLBAR`, `PRESENTATIONMODE`,
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
