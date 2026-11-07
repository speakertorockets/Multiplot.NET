# Limitations and notes

## Not supported

| Feature | Notes |
|---|---|
| Printing and print preview | Save a picture and print it from another program. `PRINT*` and `LANDSCAPE` commands are accepted, and their values are kept in the INI |
| EMF / metafile output | `SAVEPIC EMF` and `COPYMETA` save a PNG instead |
| TIFF pictures | Use PNG |
| Mailslot remote control (`/comm/`, `EXTCMDPOLLING`) | Accepted and ignored |
| Query tool, `MCI`, `PLAYSOUND` | Commands accepted; `MCI_RETURN` is empty |
| HTML Help | Use this guide |
| PC GOAL streams, REFPROP, polynomial fitting, external function DLLs | Streams and REFPROP need .NET plugins. `polyfit()` produces no curve. `fn()` is served by `IExpressionFunctionProvider` plugins |
| Native C plugin DLLs | Write a .NET plugin instead ([Plugins](plugins.md)) |

## Parquet files

* Parquet files are archives. They are never dynamic, so auto refresh does not change them.
* The *rebuild data rate* option does not apply to Parquet data.
* Only numeric top-level columns become parameters. Strings, binary, list and nested columns are ignored.
* `multi2parquet` does not export `v1` (variable blob) parameters or parameters whose data type is a time
  tag (`t8` as data). It writes them to the `multiplot.skipped` metadata instead.
* 64-bit integer values above 2^53 are rounded, because the archive stores values as DOUBLE.
* Packed time stamps (`p4`) that were written across a year end decode to days after 366. The SUN
  reader decodes them the same way. `multi2parquet` exports them as decoded and prints a warning.
* Compared with the same data read from the `.multi` file, a Parquet read differs in four ways:
  * UTC is correct on day 366 of a leap year.
  * The EOF hold point is also added to `f4`-indexed parameters.
  * Min/max are computed from the points returned.
  * UTC of a window end point can differ by 1 µs.

## Behaviour notes

* WPD display files: the colour index and decimal (`DECn`) formats of tabular items are applied.
* `IMPORT ... /R ref` honours the reference time.
* An empty boolean value in the INI counts as true.
* Symbol substitution stops after 1000 replacements, so a symbol that refers to itself cannot
  hang the program.
* Hidden mode (`/hide`) answers every dialog with its default and logs messages instead of
  showing them.
* There are no toolbars. The toolbar keys of older INI files (`ToolBar`, `IncludeToolBar`,
  `ButtonSetNN`) and the toolbar values in the registry are ignored. The user buttons are kept in the
  registry; the `UserButtonN` lines that *Save Settings* writes to the INI are not read back.
* Autoscaling is two switches per plot, *Auto Scale X* and *Auto Scale Y*. *Auto Y on Change in X*
  (`FIXXAUTOY`), *Sliding Scales* (`TIMESLIDE`) and the separate *Auto Range X* / *Auto Range Y*
  commands are gone; the INI keys `FixXautoY` and `TimeSlide` are ignored.
* There are two kinds of markers: event markers (timestamp and text) and redline markers (value and
  text). The old text, line, box, circle and image markers and the old coordinate-based redline
  markers (`MARKER n,REDLINE,...`) are not supported, and neither is *Auto Scale REDLINEs*: autoscaling
  fits the curves only, and a redline outside the visible value range is hidden. Redlines are not
  drawn on tabular, logic or 3D plots. Curve redlines made with `redline()` in a parameter list
  still work.
* The Time Alignment tool sets a time shift for each curve. The shift is lost when the curve is
  reloaded. To line up recorded data with live data, use *Align Static to Live* instead (see
  [Real-time operation](realtime.md#aligning-recorded-data-with-live-data)).
* The colour and font dialogs are the Windows common dialogs; they and the message boxes stay light in
  the dark appearance. Both appearances use the WPF Windows 11 (Fluent) style, so menus and dialogs
  look different from the classic Windows style of earlier versions.
* Fonts that are not installed (such as the default "Multiplot" face) are replaced with Arial; the
  font dialog then starts at Arial.
* The plot no longer draws the version number, script name, user name, page number or time stamp,
  and the old multi-line test information (test, engine number, shutdown) is reduced to the test
  title. The commands `TIMESTAMP`, `SHOWVERSION`, `SHOWUSERNAME`, `SHOWPAGENO` and `SHOWSCRIPT` and
  the INI keys `TimeStamp`, `ShowVersion`, `ShowUserName`, `ShowPageNo` and `ShowScript` are accepted
  and ignored.
* Plot group names are gone: *Set Group Name*, `SET GROUPNAME`, *Tile By Group* and *Auto Tab → By
  Group*. `TILE name` tiles all plots, `AUTOTAB n name` tabs through all plots, and `[groupname]` in a
  title is empty. Data-file groups (`TIME grp:...`, file-qualified names) are not affected.
* There is no screen scaling of the text any more (*Screen Scaling 1–4*, *Scale Up/Down*, *Font
  Scaling*): the point sizes chosen in the font dialogs set the text size, which still follows the
  window size. `FONTSIZE` is accepted and ignored, and *Save Settings* no longer writes `FontSize`.
  There is no script command to choose a font.
* SUN files: UTC time tags of `d8`-indexed MULTITIME data on day 366 of a leap year are shown one day
  late. `TimeMath.Utc` wraps day 366 to day 1; see OQ-017.
